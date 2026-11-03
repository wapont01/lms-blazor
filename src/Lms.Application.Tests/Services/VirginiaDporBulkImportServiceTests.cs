using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lms.Application.Data;
using Lms.Application.Services;
using Lms.Domain.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Lms.Application.Tests.Services;

public sealed class VirginiaDporBulkImportServiceTests
{
    private static readonly string Header = string.Join('\t', VirginiaDporBulkImportService.Headers) + "\n";

    private static string Row(string occupation, string certificate, string individual, string business, string street, string city,
        string zip, string expiration, string certification, string rank) =>
        string.Join('\t', "02", occupation, certificate, individual, business, street, "", "", city, "VA", zip, "0000", "", "", "",
            expiration, certification, rank, "", "someone@example.com") + "\n";

    // Rows modelled on DPOR regulant lists: tab-separated, padded names, no quoting.
    private static Dictionary<string, string> Files() => new()
    {
        ["0225s_act.txt"] = Header
            + Row("25", "250860", "DALH M  BROWN ", "", "6655 STONELYN COTTAGE CT", "ROANOKE", "24019", "02/28/2027", "02/25/2021", "SALP")
            + Row("25", "250861", "JO \"JJ\" SMITH", "", "1 MAIN ST", "RICHMOND", "23220", "03/31/2027", "03/01/2022", "SALP"),
        ["0225s_inact.txt"] = Header
            + Row("25", "250860", "DALH M BROWN", "", "6655 STONELYN COTTAGE CT", "ROANOKE", "24019", "02/28/2025", "02/25/2021", "SALP"),
        ["0225p_act.txt"] = Header
            + Row("25", "225000", "PAT PRINCIPAL", "", "2 MAIN ST", "RICHMOND", "23220", "08/31/2028", "08/23/2010", "BRKR"),
        ["0226f__crnt.txt"] = Header
            + Row("26", "029721", "", "THE STEELE GROUP INC", "1531 W MAIN STREET", "RICHMOND", "23220", "08/31/2028", "08/23/2018", "FIRM")
            + string.Join('\t', "02", "26", "029800", "", "BLUE", "RIDGE REALTY", "9 OAK ST", "", "", "ROANOKE", "VA", "24011", "1234", "", "", "",
                "05/31/2027", "05/01/2019", "FIRM", "", "") + "\n",
        ["0226b__crnt.txt"] = Header
            + Row("26", "023051", "", "THE STEELE GROUP, INC.", "10 BRANCH RD", "CHARLOTTESVILLE", "22901", "08/31/2028", "04/06/2011", "BRNH")
    };

    [Fact]
    public void ReadRecords_RejoinsTabsInBusinessNamesAndBuildsLicenseNumbers()
    {
        using var archive = Archive.Create(Files());

        var people = VirginiaDporBulkImportService.ReadRecords(archive.Path("0225s_act.txt"), "0225s_act.txt");
        var firms = VirginiaDporBulkImportService.ReadRecords(archive.Path("0226f__crnt.txt"), "0226f__crnt.txt");

        Assert.Equal(("0225250860", "DALH M BROWN", "Salesperson", "Active"),
            (people[0].LicenseNumber, people[0].Name, people[0].LicenseType, people[0].LicenseStatus));
        Assert.Equal("6655 STONELYN COTTAGE CT, ROANOKE, VA 24019", people[0].Address);
        Assert.Equal(new DateOnly(2021, 2, 25), people[0].CertificationDate);
        Assert.Equal("JO \"JJ\" SMITH", people[1].Name);
        Assert.Equal(("0226029800", "BLUE RIDGE REALTY", "9 OAK ST, ROANOKE, VA 24011-1234"),
            (firms[1].LicenseNumber, firms[1].Name, firms[1].Address));
        Assert.Empty(VirginiaDporBulkImportService.ReadRecords(archive.Path("0227__crnt.txt"), "0227__crnt.txt"));
    }

    [Fact]
    public void ReadRecords_RejectsUnexpectedRankChangedHeaderAndEmptyRequiredList()
    {
        var files = Files();
        files["0225p_act.txt"] = files["0225p_act.txt"].Replace("\tBRKR\t", "\tSALP\t", StringComparison.Ordinal);
        using var rank = Archive.Create(files);
        Assert.Throws<InvalidDataException>(() => VirginiaDporBulkImportService.ReadRecords(rank.Path("0225p_act.txt"), "0225p_act.txt"));

        using var archive = Archive.Create(Files());
        File.WriteAllText(archive.Path("0225a_act.txt"), Header);
        Assert.Throws<InvalidDataException>(() => VirginiaDporBulkImportService.ReadRecords(archive.Path("0225a_act.txt"), "0225a_act.txt"));
        File.WriteAllText(archive.Path("0225s_act.txt"), Header.Replace("BUSINESS NAME", "COMPANY", StringComparison.Ordinal));
        Assert.Throws<InvalidDataException>(() => VirginiaDporBulkImportService.ReadRecords(archive.Path("0225s_act.txt"), "0225s_act.txt"));
    }

    [Fact]
    public async Task ImportArchive_StoresLicenseesFirmsAndBranchLocationsIdempotently()
    {
        using var archive = Archive.Create(Files());
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var importer = new VirginiaDporBulkImportService(db);

        var id = await importer.ImportArchiveAsync(archive.Root);

        Assert.Equal(id, await importer.ImportArchiveAsync(archive.Root));
        var snapshot = await db.RegulatoryDataSnapshots.SingleAsync();
        Assert.Equal(("Virginia", VirginiaDporBulkImportService.ParserVersion), (snapshot.State, snapshot.ParserVersion));

        var people = await db.RegulatoryLicensees.Where(person => !person.Name.StartsWith("FILL")).OrderBy(person => person.LicenseNumber).ToListAsync();
        Assert.Equal(["0225225000", "0225250860", "0225250861"], people.Select(person => person.LicenseNumber));
        Assert.Equal(8, await db.RegulatoryLicensees.CountAsync());
        Assert.Equal(("Active", new DateOnly(2027, 2, 28), new DateOnly(2021, 2, 25)),
            (people[1].LicenseStatus, people[1].LicenseRenewalDate, people[1].OriginalLicenseDate));
        Assert.Equal("Principal Broker", people[0].LicenseType);

        var firms = await db.RegulatoryFirms.Where(firm => !firm.LegalName.StartsWith("FILL")).OrderBy(firm => firm.LicenseNumber).ToListAsync();
        Assert.Equal(["0226029721", "0226029800"], firms.Select(firm => firm.LicenseNumber));
        Assert.Equal(4, await db.RegulatoryFirms.CountAsync());
        Assert.All(firms, firm => Assert.Equal(RegulatoryIndustries.RealEstate, firm.Industry));
        Assert.Equal(new DateOnly(2018, 8, 23), firms[0].LicenseIssuedDate);

        var branch = await db.RegulatoryFirmLocations.SingleAsync();
        Assert.Equal(("0226023051", firms[0].Id, VirginiaDporBulkImportService.BranchOfficeKind, RegulatoryLocationMatchMethods.NormalizedName),
            (branch.RegistrationNumber, branch.FirmId, branch.Kind, branch.MatchMethod));

        Assert.True(RegulatoryRoster.HasBulkProvider("Virginia"));
        Assert.Equal(VirginiaDporBulkImportService.ParserVersion, RegulatoryRoster.BulkParser("Virginia"));
    }

    [Fact]
    public async Task ValidateArchive_RejectsMissingListAndRowCountMismatch()
    {
        using var mismatch = Archive.Create(Files(), declaredRows: ("0225s_act.txt", 3));
        await Assert.ThrowsAsync<InvalidDataException>(() => VirginiaDporBulkImportService.ValidateArchiveAsync(mismatch.Root));

        using var missing = Archive.Create(Files(), skip: "0226b__crnt.txt");
        await Assert.ThrowsAsync<InvalidDataException>(() => VirginiaDporBulkImportService.ValidateArchiveAsync(missing.Root));
    }

    private sealed class Archive : IDisposable
    {
        private Archive(string root) => Root = root;
        public string Root { get; }
        public string Path(string file) => System.IO.Path.Combine(Root, file);

        // Lists not supplied are written with a header only; every list other than settlement agents then needs a row.
        public static Archive Create(Dictionary<string, string> files, (string File, long Rows)? declaredRows = null, string? skip = null)
        {
            var archive = new Archive(System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"va-dpor-test-{Guid.NewGuid():N}"));
            Directory.CreateDirectory(archive.Root);
            var manifest = new List<FloridaBulkManifestEntry>();
            foreach (var definition in VirginiaDporBulkImportService.Definitions)
            {
                var text = files.TryGetValue(definition.Name, out var content)
                    ? content
                    : Header + (definition.Name == "0227__crnt.txt" ? "" : Row(
                        VirginiaDporBulkImportService.List(definition.Name).Occupation, (800000 + manifest.Count).ToString(),
                        "FILL ER", "FILLER BIZ " + definition.Name[..5], "1 ST", "NORFOLK", "23510", "01/31/2027", "01/01/2020",
                        VirginiaDporBulkImportService.List(definition.Name).Rank));
                var bytes = Encoding.Latin1.GetBytes(text);
                File.WriteAllBytes(archive.Path(definition.RelativePath), bytes);
                if (definition.Name == skip) continue;
                var rows = declaredRows is { } declared && declared.File == definition.Name
                    ? declared.Rows
                    : text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length - 1;
                manifest.Add(new(definition.RelativePath, rows, bytes.Length, Convert.ToHexString(SHA256.HashData(bytes)), definition.Url,
                    DateTimeOffset.UtcNow, "test", new() { [definition.Columns.ToString()] = rows }));
            }
            File.WriteAllText(archive.Path("manifest.json"), JsonSerializer.Serialize(manifest));
            return archive;
        }

        public void Dispose()
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
    }
}
