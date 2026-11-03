using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lms.Application.Data;
using Lms.Application.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Lms.Application.Tests.Services;

public sealed class ArizonaAdreBulkImportServiceTests
{
    private static readonly string IndividualsCsv = Csv(ArizonaAdreBulkImportService.IndividualHeaders,
        Person("DOE", "JANE", "A", "BR000001000   ", "Real Estate", "Broker", "Active", "Designated Broker", "ACME REALTY LLC", "LC100000000   ", "10/1/2026 8:00:00 AM"),
        Person("SMITH", "JOHN", "", "SA000002000   ", "Real Estate", "Salesperson", "Active", "Salesperson", "ACME REALTY LLC", "LC100000000   ", "10/2/2026 8:23:03 AM"),
        Person("SMITH", "JOHN", "", "SA000002000   ", "Real Estate", "Salesperson", "Active", "Salesperson", "ACME REALTY LLC", "LC100000001   ", "10/2/2026 8:23:03 AM"),
        Person("HASTINGS", "ROBERT", "", "SA000003000   ", "Real Estate", "Salesperson", "Terminated", "", "", "", "2/20/2002 9:36:09 AM"),
        Person("HISERMAN", "JOHN", "", "SA000003000   ", "Real Estate", "Salesperson", "Active", "Salesperson", "ACME REALTY LLC", "LC100000000   ", "4/25/2025 8:09:03 AM"),
        Person("GRAVE", "TOM", "", "CS000004000   ", "Cemetery", "Salesperson", "Active", "Salesperson", "RESTFUL CEMETERY", "CM200000000   ", "1/1/2026 1:00:00 PM"),
        Person("", "", "", "SA000005000   ", "Real Estate", "Salesperson", "Eligible", "", "", "", ""));

    private static readonly string EntitiesCsv = Csv(ArizonaAdreBulkImportService.EntityHeaders,
        ["LC100000000   ", "ACME REALTY LLC", " ", "1/1/2010 12:00:00 AM", "1/31/2027 12:00:00 AM", "Active", "Limited Liability", "Real Estate", "", "", "1 MAIN ST", "", "PHOENIX", "AZ", "85001", "Maricopa", "BR000001000   ", "1/30/2025 7:12:38 PM"],
        ["LC100000001   ", "ACME REALTY LLC", "ACME TUCSON", "1/1/2012 12:00:00 AM", "1/31/2027 12:00:00 AM", "Active", "Limited Liability Branch", "Real Estate", "", "", "2 ELM ST", "SUITE 5", "TUCSON", "AZ", "85701", "Pima", "BR000001000   ", "1/30/2025 7:12:38 PM"],
        ["CM200000000   ", "RESTFUL CEMETERY", "", "1/1/1990 12:00:00 AM", "", "Active", "Corporation", "Cemetery", "", "", "3 OAK ST", "", "MESA", "AZ", "85201", "Maricopa", "", "1/30/2025 7:12:38 PM"]);

    [Fact]
    public void ReadIndividuals_KeepsRealEstateRowsWithTrimmedNumbersNamesAndDates()
    {
        using var archive = Archive.Create(IndividualsCsv, EntitiesCsv);

        var records = ArizonaAdreBulkImportService.ReadIndividuals(archive.IndividualsPath);

        Assert.Equal(6, records.Count);
        var broker = records[0];
        Assert.Equal("BR000001000", broker.LicenseNumber);
        Assert.Equal("JANE A DOE", broker.Name);
        Assert.Equal("LC100000000", broker.EmployerLicenseNumber);
        Assert.Equal(new DateOnly(2000, 1, 2), broker.OriginalDate);
        Assert.Equal(new DateOnly(2027, 12, 31), broker.ExpirationDate);
        Assert.Equal("Name not published (SA000005000)", records[^1].Name);
        Assert.DoesNotContain(records, record => record.LicenseNumber == "CS000004000");
        Assert.Equal(7, ArizonaAdreBulkImportService.CheckFile(archive.IndividualsPath, ArizonaAdreBulkImportService.IndividualsFile));
    }

    [Fact]
    public void SelectLicensees_KeepsMostRecentRowForReusedNumbers()
    {
        using var archive = Archive.Create(IndividualsCsv, EntitiesCsv);

        var selected = ArizonaAdreBulkImportService.SelectLicensees(ArizonaAdreBulkImportService.ReadIndividuals(archive.IndividualsPath));

        Assert.Equal(4, selected.Count);
        Assert.Equal("JOHN HISERMAN", selected.Single(record => record.LicenseNumber == "SA000003000").Name);
    }

    [Fact]
    public void ReadIndividuals_RejectsChangedSchema()
    {
        using var archive = Archive.Create(IndividualsCsv.Replace("\"LicCategory\"", "\"Category\"", StringComparison.Ordinal), EntitiesCsv);

        Assert.Throws<InvalidDataException>(() => ArizonaAdreBulkImportService.ReadIndividuals(archive.IndividualsPath));
    }

    [Fact]
    public async Task ImportArchive_StoresFirmsPeopleAndAffiliationsIdempotently()
    {
        using var archive = Archive.Create(IndividualsCsv, EntitiesCsv);
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var importer = new ArizonaAdreBulkImportService(db);

        var id = await importer.ImportArchiveAsync(archive.Root);

        Assert.Equal(id, await importer.ImportArchiveAsync(archive.Root));
        var snapshot = await db.RegulatoryDataSnapshots.SingleAsync();
        Assert.Equal("Arizona", snapshot.State);
        Assert.Equal(ArizonaAdreBulkImportService.ParserVersion, snapshot.ParserVersion);
        Assert.NotNull(snapshot.ActivatedAtUtc);

        var firms = await db.RegulatoryFirms.OrderBy(firm => firm.LicenseNumber).ToListAsync();
        Assert.Equal(["LC100000000", "LC100000001"], firms.Select(firm => firm.LicenseNumber));
        Assert.Null(firms[0].TradeName);
        Assert.Equal("ACME TUCSON", firms[1].TradeName);
        Assert.Equal("1 MAIN ST, PHOENIX, AZ 85001", firms[0].Address);
        Assert.Equal("2 ELM ST, SUITE 5, TUCSON, AZ 85701", firms[1].Address);
        Assert.All(firms, firm => Assert.Equal("JANE A DOE", firm.QualifyingBrokerName));

        var people = await db.RegulatoryLicensees.ToListAsync();
        Assert.Equal(4, people.Count);
        var hiserman = people.Single(person => person.LicenseNumber == "SA000003000");
        Assert.Equal("JOHN HISERMAN", hiserman.Name);
        Assert.Equal("Active", hiserman.LicenseStatus);
        Assert.Equal("Salesperson", hiserman.LicenseType);
        Assert.Equal("LC100000000", hiserman.RelatedLicenseNumber);
        Assert.Equal("Eligible", people.Single(person => person.LicenseNumber == "SA000005000").LicenseStatus);

        var affiliations = await db.RegulatoryFirmLicenseeAffiliations.ToListAsync();
        Assert.Equal(5, affiliations.Count);
        var brokerId = people.Single(person => person.LicenseNumber == "BR000001000").Id;
        Assert.Equal(2, affiliations.Count(affiliation => affiliation.LicenseeId == brokerId && affiliation.IsBrokerInCharge));
        var smithId = people.Single(person => person.LicenseNumber == "SA000002000").Id;
        Assert.Equal(2, affiliations.Count(affiliation => affiliation.LicenseeId == smithId && !affiliation.IsBrokerInCharge));

        Assert.True(RegulatoryRoster.HasBulkProvider("Arizona"));
        Assert.Equal(ArizonaAdreBulkImportService.ParserVersion, RegulatoryRoster.BulkParser("Arizona"));
    }

    [Fact]
    public async Task ValidateArchive_RejectsManifestRowCountMismatch()
    {
        using var archive = Archive.Create(IndividualsCsv, EntitiesCsv, individualRows: 6);

        await Assert.ThrowsAsync<InvalidDataException>(() => ArizonaAdreBulkImportService.ValidateArchiveAsync(archive.Root));
    }

    private static string[] Person(string last, string first, string middle, string number, string category, string type,
        string status, string employment, string employer, string employerNumber, string modified) =>
        [last, first, middle, "1/2/2000 12:00:00 AM", "12/31/2027 12:00:00 AM", number, category, type, status, employment,
            employer, "", employerNumber, "", "", "PO BOX 1", "", "PHOENIX", "AZ", "85001", "Maricopa", modified];

    private static string Csv(IReadOnlyList<string> headers, params string[][] rows) =>
        string.Concat(new[] { headers.ToArray() }.Concat(rows)
            .Select(fields => string.Join(',', fields.Select(field => "\"" + field.Replace("\"", "\"\"") + "\"")) + "\r\n"));

    private sealed class Archive : IDisposable
    {
        private Archive(string root) => Root = root;
        public string Root { get; }
        public string IndividualsPath => Path.Combine(Root, ArizonaAdreBulkImportService.IndividualsFile);

        public static Archive Create(string individuals, string entities, long? individualRows = null)
        {
            var archive = new Archive(Path.Combine(Path.GetTempPath(), $"az-adre-test-{Guid.NewGuid():N}"));
            Directory.CreateDirectory(archive.Root);
            var manifest = new[]
            {
                Write(archive.Root, ArizonaAdreBulkImportService.Definitions[0], individuals, individualRows),
                Write(archive.Root, ArizonaAdreBulkImportService.Definitions[1], entities, null)
            };
            File.WriteAllText(Path.Combine(archive.Root, "manifest.json"), JsonSerializer.Serialize(manifest));
            return archive;
        }

        private static FloridaBulkManifestEntry Write(string root, FloridaBulkFileDefinition definition, string csv, long? declaredRows)
        {
            var bytes = new UTF8Encoding(false).GetBytes(csv);
            File.WriteAllBytes(Path.Combine(root, definition.RelativePath), bytes);
            var rows = declaredRows ?? csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries).Length - 1;
            return new(definition.RelativePath, rows, bytes.Length, Convert.ToHexString(SHA256.HashData(bytes)), definition.Url,
                DateTimeOffset.UtcNow, "test", new() { [definition.Columns.ToString()] = rows });
        }

        public void Dispose()
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
    }
}
