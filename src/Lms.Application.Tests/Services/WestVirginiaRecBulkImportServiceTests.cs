using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Lms.Application.Data;
using Lms.Application.Services;
using Lms.Domain.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Lms.Application.Tests.Services;

public sealed class WestVirginiaRecBulkImportServiceTests
{
    private static string[] Row(string name, string licenseNumber, string type, string companyNumber,
        string companyName, string branchNumber = "00", string street = "123 Main Street", string city = "Charleston",
        string state = "WV", string zip = "25301") =>
    [
        name, licenseNumber, type, companyNumber, companyName, branchNumber, "Active", "ignored@example.org",
        street, city, state, zip
    ];

    private static string[] SetCell(string[] cells, int index, string value)
    {
        var result = cells.ToArray();
        result[index] = value;
        return result;
    }

    private static Dictionary<string, IReadOnlyList<string[]>> Files() => new()
    {
        [WestVirginiaRecBulkImportService.SalespersonFile] =
        [
            Row("JANE Q DOE", "WV0001001", "Salesperson", "000123", "Example Realty LLC"),
            Row("JANE Q DOE", "WV0001001", "Salesperson", "000123", "Example Realty LLC",
                "01", "20 Branch Street"),
            Row("AMY RAY", "WV0003003", "Salesperson", "000126", "River Realty")
        ],
        [WestVirginiaRecBulkImportService.AssociateBrokerFile] =
        [
            Row("JANE Q DOE", "WV0001001", "Associate Broker", "000123", "Example Realty LLC"),
            Row("JOHN SMITH", "WV0002002", "Associate Broker", "000125", "Mountain Realty")
        ],
        [WestVirginiaRecBulkImportService.BrokerFile] =
        [
            Row("JANE Q DOE", "WV0001001", "Broker", "000124", "Broker Realty")
        ]
    };

    [Fact]
    public void ReadRecords_ParsesPublishedWorkbookSchemaAndSourceIds()
    {
        using var archive = Archive.Create(Files());
        var records = WestVirginiaRecBulkImportService.ReadRecords(
            archive.Path(WestVirginiaRecBulkImportService.SalespersonFile), WestVirginiaRecBulkImportService.SalespersonFile);

        Assert.Equal(12, WestVirginiaRecBulkImportService.Headers.Count);
        Assert.Equal(3, WestVirginiaRecBulkImportService.CheckFile(
            archive.Path(WestVirginiaRecBulkImportService.SalespersonFile), WestVirginiaRecBulkImportService.SalespersonFile));
        Assert.Equal(("WV0001001", "JANE Q DOE", "Salesperson", "000123", "00",
                "123 Main Street, Charleston, WV 25301"),
            (records[0].LicenseNumber, records[0].Name, records[0].LicenseType, records[0].CompanyNumber,
                records[0].BranchNumber, records[0].Address));
        Assert.Equal(("000123", "01", "20 Branch Street, Charleston, WV 25301"),
            (records[1].CompanyNumber, records[1].BranchNumber, records[1].Address));

        Assert.Collection(WestVirginiaRecBulkImportService.Definitions,
            salesperson => Assert.Equal(("SalespersonRoster.xlsx", "https://rec.wv.gov/media/305/download", 12),
                (salesperson.RelativePath, salesperson.Url, salesperson.Columns)),
            associateBroker => Assert.Equal(("AssociateBrokerRoster.xlsx", "https://rec.wv.gov/media/306/download", 12),
                (associateBroker.RelativePath, associateBroker.Url, associateBroker.Columns)),
            broker => Assert.Equal(("BrokerRoster.xlsx", "https://rec.wv.gov/media/307/download", 12),
                (broker.RelativePath, broker.Url, broker.Columns)));
        Assert.Equal("https://rec.wv.gov/active-licensee-rosters", WestVirginiaRecBulkImportService.SourcePage);
    }

    [Fact]
    public void ReadRecords_RejectsChangedHeadersAndMalformedActiveRows()
    {
        var files = Files();
        using var wrongHeader = Archive.Create(files, changedHeader: true);
        Assert.Throws<InvalidDataException>(() => WestVirginiaRecBulkImportService.ReadRecords(
            wrongHeader.Path(WestVirginiaRecBulkImportService.SalespersonFile), WestVirginiaRecBulkImportService.SalespersonFile));

        files[WestVirginiaRecBulkImportService.SalespersonFile] =
        [
            SetCell(Row("JANE DOE", "WV0001001", "Salesperson", "000123", "Example Realty LLC"), 6, "Inactive")
        ];
        using var malformed = Archive.Create(files);
        Assert.Throws<InvalidDataException>(() => WestVirginiaRecBulkImportService.ReadRecords(
            malformed.Path(WestVirginiaRecBulkImportService.SalespersonFile), WestVirginiaRecBulkImportService.SalespersonFile));
        Assert.Throws<InvalidDataException>(() => WestVirginiaRecBulkImportService.CheckFile(
            malformed.Path(WestVirginiaRecBulkImportService.SalespersonFile), WestVirginiaRecBulkImportService.SalespersonFile));

        Assert.Throws<InvalidDataException>(() => WestVirginiaRecBulkImportService.ReadRecords(
            malformed.Path(WestVirginiaRecBulkImportService.SalespersonFile), "unknown.xlsx"));
    }

    [Fact]
    public void SelectLicensees_DeduplicatesAffiliationsAndOrdersTypesDeterministically()
    {
        using var archive = Archive.Create(Files());
        var records = WestVirginiaRecBulkImportService.Definitions
            .SelectMany(definition => WestVirginiaRecBulkImportService.ReadRecords(
                archive.Path(definition.RelativePath), definition.Name))
            .ToList();

        var first = WestVirginiaRecBulkImportService.SelectLicensees(records);
        var reordered = WestVirginiaRecBulkImportService.SelectLicensees(records.AsEnumerable().Reverse());
        var jane = Assert.Single(first, item => item.Record.LicenseNumber == "WV0001001");

        Assert.Equal(("Broker", "Broker, Associate Broker, Salesperson", "JANE Q DOE"),
            (jane.Record.LicenseType, jane.SourceLicenseType, jane.Record.Name));
        Assert.Equal(first.Select(item => (item.Record.LicenseNumber, item.Record.LicenseType, item.SourceLicenseType)),
            reordered.Select(item => (item.Record.LicenseNumber, item.Record.LicenseType, item.SourceLicenseType)));
        Assert.Equal(
            [("WV0001001", "000123"), ("WV0001001", "000124"), ("WV0002002", "000125"), ("WV0003003", "000126")],
            WestVirginiaRecBulkImportService.SelectAffiliations(records)
                .Select(item => (item.LicenseNumber, item.CompanyNumber)));
    }

    [Fact]
    public async Task ValidateArchive_RejectsArchiveHashMismatch()
    {
        using var archive = Archive.Create(Files());
        var path = archive.Path(WestVirginiaRecBulkImportService.SalespersonFile);
        File.WriteAllBytes(path, File.ReadAllBytes(path).Concat(new byte[] { 0 }).ToArray());

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            WestVirginiaRecBulkImportService.ValidateArchiveAsync(archive.Root));
    }

    [Fact]
    public async Task ValidateArchive_RejectsManifestRowCountMismatch()
    {
        using var archive = Archive.Create(Files());
        var manifestPath = archive.Path("manifest.json");
        var entries = JsonSerializer.Deserialize<List<FloridaBulkManifestEntry>>(File.ReadAllText(manifestPath))!;
        var index = entries.FindIndex(entry => entry.File == WestVirginiaRecBulkImportService.SalespersonFile);
        var entry = entries[index];
        var rows = entry.Rows + 1;
        entries[index] = entry with
        {
            Rows = rows,
            Columns = new() { [WestVirginiaRecBulkImportService.Definitions[0].Columns.ToString(CultureInfo.InvariantCulture)] = rows }
        };
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(entries));

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            WestVirginiaRecBulkImportService.ValidateArchiveAsync(archive.Root));
    }

    [Fact]
    public async Task ImportArchive_ReusesSnapshotsAndReplacesPublishedCoverageWithRawFtsIndexedRows()
    {
        using var firstArchive = Archive.Create(Files(), new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero));
        var replacementFiles = Files();
        replacementFiles[WestVirginiaRecBulkImportService.SalespersonFile] =
        [
            .. replacementFiles[WestVirginiaRecBulkImportService.SalespersonFile],
            Row("NEW LICENSEE", "WV0004004", "Salesperson", "000127", "New River Realty")
        ];
        using var replacementArchive = Archive.Create(replacementFiles,
            new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero));

        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        await RegulatoryLicenseeNameIndex.EnsureAsync(db);
        var importer = new WestVirginiaRecBulkImportService(db);

        var firstId = await importer.ImportArchiveAsync(firstArchive.Root);
        Assert.Equal(firstId, await importer.ImportArchiveAsync(firstArchive.Root));
        var replacedId = await importer.ImportArchiveAsync(replacementArchive.Root);

        Assert.NotEqual(firstId, replacedId);
        Assert.Equal(2, await db.RegulatoryDataSnapshots.CountAsync(snapshot => snapshot.State == WestVirginiaRecBulkImportService.State));
        Assert.Equal(firstId, await db.RegulatoryDataSnapshots.Where(snapshot => snapshot.Id == firstId)
            .Select(snapshot => snapshot.Id).SingleAsync());
        Assert.Equal(3, await db.RegulatoryLicensees.CountAsync(item => item.SnapshotId == firstId));
        Assert.Equal(4, await db.RegulatoryFirms.CountAsync(item => item.SnapshotId == firstId));
        Assert.Equal(1, await db.RegulatoryFirmLocations.CountAsync(item => item.SnapshotId == firstId));
        Assert.Equal(4, await db.RegulatoryFirmLicenseeAffiliations.CountAsync(item => item.SnapshotId == firstId));
        Assert.Equal(4, await db.RegulatoryLicensees.CountAsync(item => item.SnapshotId == replacedId));
        Assert.Equal(5, await db.RegulatoryFirms.CountAsync(item => item.SnapshotId == replacedId));
        Assert.Equal(5, await db.RegulatoryFirmLicenseeAffiliations.CountAsync(item => item.SnapshotId == replacedId));

        var current = await RegulatoryRoster.Published(
                db.RegulatoryDataSnapshots.Where(snapshot => snapshot.State == WestVirginiaRecBulkImportService.State))
            .Select(snapshot => snapshot.Id).FirstAsync();
        Assert.Equal(replacedId, current);
        var snapshot = await db.RegulatoryDataSnapshots.SingleAsync(item => item.Id == replacedId);
        Assert.Contains("active-only", snapshot.SourceName, StringComparison.OrdinalIgnoreCase);

        var jane = await db.RegulatoryLicensees.SingleAsync(item =>
            item.SnapshotId == firstId && item.LicenseNumber == "WV0001001");
        Assert.Equal(("Broker", "Broker, Associate Broker, Salesperson", "Active"),
            (jane.LicenseType, jane.SourceLicenseType, jane.LicenseStatus));
        var firms = await db.RegulatoryFirms.Where(item => item.SnapshotId == firstId)
            .OrderBy(item => item.LicenseNumber).ToListAsync();
        var exampleFirm = Assert.Single(firms, item => item.LicenseNumber == "000123");
        Assert.Equal("123 Main Street, Charleston, WV 25301", exampleFirm.Address);
        Assert.DoesNotContain(firms, firm => firm.LicenseNumber == "WV0001001");
        var branch = await db.RegulatoryFirmLocations.SingleAsync(item => item.SnapshotId == firstId);
        Assert.Equal(("000123:01", exampleFirm.Id, "20 Branch Street, Charleston, WV 25301"),
            (branch.RegistrationNumber, branch.FirmId, branch.Address));
        Assert.All(firms, firm =>
        {
            Assert.Null(firm.LicenseStatus);
            Assert.Null(firm.LicenseIssuedDate);
            Assert.Null(firm.LicenseRenewalDate);
            Assert.Null(firm.QualifyingBrokerName);
        });
        Assert.All(await db.RegulatoryFirmLicenseeAffiliations.ToListAsync(), affiliation =>
        {
            Assert.False(affiliation.IsBrokerInCharge);
            Assert.Null(affiliation.RelationshipEffectiveDate);
        });

        var janeAffiliations = await db.RegulatoryFirmLicenseeAffiliations
            .Where(item => item.SnapshotId == firstId && item.LicenseeId == jane.Id)
            .Select(item => item.Firm!.LicenseNumber).OrderBy(number => number).ToListAsync();
        Assert.Equal(["000123", "000124"], janeAffiliations);

        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM \"{RegulatoryLicenseeNameIndex.TableName}\" WHERE \"{RegulatoryLicenseeNameIndex.TableName}\" MATCH @query;";
        var query = command.CreateParameter();
        query.ParameterName = "@query";
        query.Value = RegulatoryLicenseeNameIndex.BuildMatchExpression("Jane", firstId);
        command.Parameters.Add(query);
        Assert.Equal(1L, Convert.ToInt64(await command.ExecuteScalarAsync()));
    }

    [Fact]
    public async Task ImportArchive_ScopesRepeatedBranchNumbersToPublishedCompanyIds()
    {
        var files = Files();
        files[WestVirginiaRecBulkImportService.BrokerFile] =
        [
            .. files[WestVirginiaRecBulkImportService.BrokerFile],
            Row("JANE Q DOE", "WV0001001", "Broker", "000124", "Broker Realty", "01", "30 Branch Street")
        ];
        using var archive = Archive.Create(files);
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var importer = new WestVirginiaRecBulkImportService(db);

        var id = await importer.ImportArchiveAsync(archive.Root);

        Assert.Equal(["000123:01", "000124:01"], await db.RegulatoryFirmLocations
            .OrderBy(location => location.RegistrationNumber).Select(location => location.RegistrationNumber).ToListAsync());
        Assert.Equal(id, await importer.ImportArchiveAsync(archive.Root));
        var location = await db.RegulatoryFirmLocations.OrderBy(item => item.RegistrationNumber).FirstAsync();
        db.RegulatoryFirmLocations.Remove(location);
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidDataException>(() => importer.ImportArchiveAsync(archive.Root));
    }

    private sealed class Archive : IDisposable
    {
        private Archive(string root) => Root = root;
        public string Root { get; }
        public string Path(string file) => System.IO.Path.Combine(Root, file);

        public static Archive Create(Dictionary<string, IReadOnlyList<string[]>> files, DateTimeOffset? verifiedAt = null,
            bool changedHeader = false)
        {
            var root = System.IO.Path.Combine(Environment.CurrentDirectory, $"wv-rec-archive-{Guid.NewGuid():N}");
            Directory.CreateDirectory(root);
            var manifest = new List<FloridaBulkManifestEntry>();
            foreach (var definition in WestVirginiaRecBulkImportService.Definitions)
            {
                var header = WestVirginiaRecBulkImportService.Headers.ToArray();
                if (changedHeader && definition.Name == WestVirginiaRecBulkImportService.SalespersonFile)
                    header[0] = "Licensee";
                var bytes = Workbook(header, files[definition.Name]);
                File.WriteAllBytes(System.IO.Path.Combine(root, definition.RelativePath), bytes);
                var rows = files[definition.Name].Count;
                manifest.Add(new(definition.RelativePath, rows, bytes.Length, Convert.ToHexString(SHA256.HashData(bytes)),
                    definition.Url, verifiedAt ?? new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero), "test",
                    new() { [definition.Columns.ToString(CultureInfo.InvariantCulture)] = rows }));
            }
            File.WriteAllText(System.IO.Path.Combine(root, "manifest.json"), JsonSerializer.Serialize(manifest));
            return new Archive(root);
        }

        private static byte[] Workbook(IReadOnlyList<string> headers, IReadOnlyList<string[]> rows)
        {
            XNamespace main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            XNamespace rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
            XNamespace packageRel = "http://schemas.openxmlformats.org/package/2006/relationships";
            XNamespace contentTypes = "http://schemas.openxmlformats.org/package/2006/content-types";
            var values = new List<string>();
            var indices = new Dictionary<string, int>(StringComparer.Ordinal);
            int Index(string value)
            {
                if (indices.TryGetValue(value, out var index)) return index;
                index = values.Count;
                values.Add(value);
                indices.Add(value, index);
                return index;
            }

            var allRows = new[] { headers.ToArray() }.Concat(rows).ToArray();
            var sheetRows = allRows.Select((cells, rowIndex) =>
            {
                if (cells.Length != headers.Count)
                    throw new InvalidDataException("Test workbook rows must match the published schema.");
                var rowNumber = rowIndex + 1;
                return new XElement(main + "row", new XAttribute("r", rowNumber),
                    cells.Select((value, columnIndex) => new XElement(main + "c",
                        new XAttribute("r", $"{(char)('A' + columnIndex)}{rowNumber}"),
                        new XAttribute("t", "s"), new XElement(main + "v", Index(value)))));
            }).ToArray();
            using var memory = new MemoryStream();
            using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
            {
                WriteXml(zip, "[Content_Types].xml", new XDocument(new XElement(contentTypes + "Types",
                    new XElement(contentTypes + "Default", new XAttribute("Extension", "rels"),
                        new XAttribute("ContentType", "application/vnd.openxmlformats-package.relationships+xml")),
                    new XElement(contentTypes + "Default", new XAttribute("Extension", "xml"), new XAttribute("ContentType", "application/xml")),
                    new XElement(contentTypes + "Override", new XAttribute("PartName", "/xl/workbook.xml"),
                        new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml")),
                    new XElement(contentTypes + "Override", new XAttribute("PartName", "/xl/worksheets/sheet1.xml"),
                        new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml")),
                    new XElement(contentTypes + "Override", new XAttribute("PartName", "/xl/sharedStrings.xml"),
                        new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.sharedStrings+xml")))));
                WriteXml(zip, "_rels/.rels", new XDocument(new XElement(packageRel + "Relationships",
                    new XElement(packageRel + "Relationship", new XAttribute("Id", "rId1"),
                        new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument"),
                        new XAttribute("Target", "xl/workbook.xml")))));
                WriteXml(zip, "xl/workbook.xml", new XDocument(new XElement(main + "workbook",
                    new XAttribute(XNamespace.Xmlns + "r", rel),
                    new XElement(main + "sheets",
                        new XElement(main + "sheet", new XAttribute("name", "Roster"), new XAttribute("sheetId", 1),
                            new XAttribute(rel + "id", "rId1"))))));
                WriteXml(zip, "xl/_rels/workbook.xml.rels", new XDocument(new XElement(packageRel + "Relationships",
                    new XElement(packageRel + "Relationship", new XAttribute("Id", "rId1"),
                        new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet"),
                        new XAttribute("Target", "worksheets/sheet1.xml")),
                    new XElement(packageRel + "Relationship", new XAttribute("Id", "rId2"),
                        new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/sharedStrings"),
                        new XAttribute("Target", "sharedStrings.xml")))));
                WriteXml(zip, "xl/sharedStrings.xml", new XDocument(new XElement(main + "sst",
                    new XAttribute("count", allRows.Sum(row => row.Length)),
                    new XAttribute("uniqueCount", values.Count),
                    values.Select(value => new XElement(main + "si", new XElement(main + "t",
                        new XAttribute(XNamespace.Xml + "space", "preserve"), value))))));
                WriteXml(zip, "xl/worksheets/sheet1.xml", new XDocument(new XElement(main + "worksheet",
                    new XElement(main + "dimension", new XAttribute("ref", $"A1:L{allRows.Length}")),
                    new XElement(main + "sheetData", sheetRows))));
            }
            return memory.ToArray();
        }

        private static void WriteXml(ZipArchive zip, string path, XDocument document)
        {
            var entry = zip.CreateEntry(path, CompressionLevel.Optimal);
            using var stream = entry.Open();
            document.Save(stream);
        }

        public void Dispose()
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
    }
}
