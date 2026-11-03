using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lms.Application.Data;
using Lms.Application.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Lms.Application.Tests.Services;

public sealed class ConnecticutDcpBulkImportServiceTests
{
    private static readonly string SalespersonsCsv = Csv(ConnecticutDcpBulkImportService.CredentialHeaders,
        Credential("1", "JANE DOE", "INDIVIDUAL", "", "RES", "0000001", "REAL ESTATE SALESPERSON", "ACTIVE", "2011-07-26T00:00:00.000", "2024-11-01T00:00:00.000", "2026-10-31T00:00:00.000"),
        Credential("2", "JOHN ROE", "INDIVIDUAL", "", "RES", "0000002", "REAL ESTATE SALESPERSON", "INACTIVE", "2015-01-05T00:00:00.000", "", "2020-10-31T00:00:00.000"),
        Credential("3", "PENDING APPLICANT", "INDIVIDUAL", "", "RES", "", "REAL ESTATE SALESPERSON", "EXPIRED APPLICATION", "", "", ""),
        Credential("4", "TEAM MEMBER", "INDIVIDUAL", "", "RES", "0000004", "REAL ESTATE SALESPERSON", "ACTIVE", "2020-03-01T00:00:00.000", "2024-11-01T00:00:00.000", "2026-10-31T00:00:00.000"));

    private static readonly string BrokersCsv = Csv(ConnecticutDcpBulkImportService.CredentialHeaders,
        Credential("10", "ALMA GAYLE", "INDIVIDUAL", "", "REB", "0000010", "REAL ESTATE BROKER", "ACTIVE IN RENEWAL", "2005-04-01T00:00:00.000", "2025-04-01T00:00:00.000", "2027-03-31T00:00:00.000"),
        Credential("20", "ACME REALTY LLC", "LLC", "ACME", "REB", "0000020", "REAL ESTATE BROKER", "ACTIVE", "2010-01-01T00:00:00.000", "2024-11-01T00:00:00.000", "2026-10-31T00:00:00.000"),
        Credential("30", "BIG CORP", "CORPORATION", "", "REB", "0000030", "REAL ESTATE BROKER", "ACTIVE", "2001-01-01T00:00:00.000", "2024-11-01T00:00:00.000", "2026-10-31T00:00:00.000"),
        Credential("40", "SOLO BROKER", "INDIVIDUAL", "", "REB", "0000040", "REAL ESTATE BROKER", "EXPIRED", "1999-01-01T00:00:00.000", "", "2009-10-31T00:00:00.000"));

    private static readonly string AssociationsCsv = Csv(ConnecticutDcpBulkImportService.AssociationHeaders,
        ["JANE DOE", "RES.0000001", "ALMA GAYLE", "REB.0000010"],
        ["JOHN ROE", "RES.0000002", "ACME REALTY LLC", "REB.0000020"],
        ["TEAM MEMBER", "RES.0000004", "ROB ROSA TEAM", "RTM.0000099"]);

    [Fact]
    public void ReadCredentials_SkipsRowsWithoutNumbersAndFormatsAddressAndDates()
    {
        using var archive = Archive.Create(SalespersonsCsv, BrokersCsv, AssociationsCsv);

        var records = ConnecticutDcpBulkImportService.ReadCredentials(archive.Path(0), "RES");

        Assert.Equal(["RES.0000001", "RES.0000002", "RES.0000004"], records.Select(record => record.LicenseNumber));
        var jane = records[0];
        Assert.Equal("HARTFORD, CT 06120-1120", jane.Address);
        Assert.Equal(new DateOnly(2011, 7, 26), jane.IssueDate);
        Assert.Equal(new DateOnly(2024, 11, 1), jane.EffectiveDate);
        Assert.Equal(4, ConnecticutDcpBulkImportService.CheckFile(archive.Path(0), ConnecticutDcpBulkImportService.SalespersonsFile));
    }

    [Fact]
    public void ReadCredentials_RejectsChangedSchemaAndUnexpectedCredentialCodes()
    {
        using var changed = Archive.Create(SalespersonsCsv.Replace("\"fullcredentialcode\"", "\"credentialcode\"", StringComparison.Ordinal),
            BrokersCsv, AssociationsCsv);
        Assert.Throws<InvalidDataException>(() => ConnecticutDcpBulkImportService.ReadCredentials(changed.Path(0), "RES"));

        using var archive = Archive.Create(SalespersonsCsv, BrokersCsv, AssociationsCsv);
        Assert.Throws<InvalidDataException>(() => ConnecticutDcpBulkImportService.ReadCredentials(archive.Path(1), "RES"));
    }

    [Theory]
    [InlineData("LLC", true)]
    [InlineData("LLP", true)]
    [InlineData("CORPORATION", true)]
    [InlineData("CORPORATION  LTD (LIMITED)", true)]
    [InlineData("PARTNERSHIP", true)]
    [InlineData("LIMITED LIABILITY COMPANY", true)]
    [InlineData("BUSINESS", true)]
    [InlineData("INDIVIDUAL", false)]
    [InlineData("EMPLOYEE ONLY", false)]
    public void IsCompanyType_SeparatesCompanyBrokersFromPeople(string type, bool expected) =>
        Assert.Equal(expected, ConnecticutDcpBulkImportService.IsCompanyType(type));

    [Fact]
    public async Task ImportArchive_StoresFirmsPeopleTeamsAndAffiliationsIdempotently()
    {
        using var archive = Archive.Create(SalespersonsCsv, BrokersCsv, AssociationsCsv);
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var importer = new ConnecticutDcpBulkImportService(db);

        var id = await importer.ImportArchiveAsync(archive.Root);

        Assert.Equal(id, await importer.ImportArchiveAsync(archive.Root));
        var snapshot = await db.RegulatoryDataSnapshots.SingleAsync();
        Assert.Equal("Connecticut", snapshot.State);
        Assert.Equal(ConnecticutDcpBulkImportService.ParserVersion, snapshot.ParserVersion);
        Assert.NotNull(snapshot.ActivatedAtUtc);

        var firms = await db.RegulatoryFirms.OrderBy(firm => firm.LicenseNumber).ToListAsync();
        Assert.Equal(["REB.0000010", "REB.0000020", "REB.0000030", "RTM.0000099"], firms.Select(firm => firm.LicenseNumber));
        Assert.Equal("ALMA GAYLE", firms[0].QualifyingBrokerName);
        Assert.Equal("Active In Renewal", firms[0].LicenseStatus);
        Assert.Equal("ACME", firms[1].TradeName);
        Assert.Null(firms[1].QualifyingBrokerName);
        Assert.Equal("HARTFORD, CT 06120-1120", firms[1].Address);
        Assert.Equal("ROB ROSA TEAM", firms[3].LegalName);
        Assert.Null(firms[3].LicenseStatus);

        var people = await db.RegulatoryLicensees.ToListAsync();
        Assert.Equal(["REB.0000010", "REB.0000040", "RES.0000001", "RES.0000002", "RES.0000004"],
            people.Select(person => person.LicenseNumber).Order());
        var jane = people.Single(person => person.LicenseNumber == "RES.0000001");
        Assert.Equal("Real Estate Salesperson", jane.LicenseType);
        Assert.Equal("Real Estate Salesperson - Individual", jane.SourceLicenseType);
        Assert.Equal("Active", jane.LicenseStatus);
        Assert.Equal(new DateOnly(2011, 7, 26), jane.OriginalLicenseDate);
        Assert.Equal(new DateOnly(2024, 11, 1), jane.LicenseIssuedDate);
        Assert.Equal("REB.0000010", jane.RelatedLicenseNumber);
        Assert.Equal("Real Estate Broker", jane.RelatedLicenseType);
        Assert.Equal("ALMA GAYLE", jane.RelatedLicenseName);
        var john = people.Single(person => person.LicenseNumber == "RES.0000002");
        Assert.Equal(new DateOnly(2015, 1, 5), john.LicenseIssuedDate);
        Assert.Equal(ConnecticutDcpBulkImportService.TeamLicenseType,
            people.Single(person => person.LicenseNumber == "RES.0000004").RelatedLicenseType);
        Assert.Null(people.Single(person => person.LicenseNumber == "REB.0000040").RelatedLicenseNumber);

        var affiliations = await db.RegulatoryFirmLicenseeAffiliations.ToListAsync();
        Assert.Equal(4, affiliations.Count);
        var alma = people.Single(person => person.LicenseNumber == "REB.0000010").Id;
        Assert.Single(affiliations, affiliation => affiliation.LicenseeId == alma && affiliation.IsBrokerInCharge
            && affiliation.FirmId == firms[0].Id);
        Assert.Single(affiliations, affiliation => affiliation.LicenseeId == jane.Id && affiliation.FirmId == firms[0].Id
            && !affiliation.IsBrokerInCharge);

        Assert.True(RegulatoryRoster.HasBulkProvider("Connecticut"));
        Assert.Equal(ConnecticutDcpBulkImportService.ParserVersion, RegulatoryRoster.BulkParser("Connecticut"));
    }

    [Fact]
    public async Task ValidateArchive_RejectsManifestRowCountMismatch()
    {
        using var archive = Archive.Create(SalespersonsCsv, BrokersCsv, AssociationsCsv, associationRows: 2);

        await Assert.ThrowsAsync<InvalidDataException>(() => ConnecticutDcpBulkImportService.ValidateArchiveAsync(archive.Root));
    }

    private static string[] Credential(string id, string name, string type, string dba, string prefix, string number,
        string credential, string status, string issued, string effective, string expires) =>
        [id, name, type, type == "INDIVIDUAL" ? "" : name, dba, number.Length == 0 ? prefix : prefix + "." + number, prefix, number,
            "", credential, status, "CURRENT", status == "ACTIVE" ? "1" : "0", issued, effective, expires,
            "HARTFORD", "CT", "061201120", "2026-11-02T00:00:00.000"];

    private static string Csv(IReadOnlyList<string> headers, params string[][] rows) =>
        string.Concat(new[] { headers.ToArray() }.Concat(rows)
            .Select(fields => string.Join(',', fields.Select(field => "\"" + field.Replace("\"", "\"\"") + "\"")) + "\n"));

    private sealed class Archive : IDisposable
    {
        private Archive(string root) => Root = root;
        public string Root { get; }
        public string Path(int index) => System.IO.Path.Combine(Root, ConnecticutDcpBulkImportService.Definitions[index].RelativePath);

        public static Archive Create(string salespersons, string brokers, string associations, long? associationRows = null)
        {
            var archive = new Archive(System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"ct-dcp-test-{Guid.NewGuid():N}"));
            Directory.CreateDirectory(archive.Root);
            var manifest = new[]
            {
                Write(archive.Root, ConnecticutDcpBulkImportService.Definitions[0], salespersons, null),
                Write(archive.Root, ConnecticutDcpBulkImportService.Definitions[1], brokers, null),
                Write(archive.Root, ConnecticutDcpBulkImportService.Definitions[2], associations, associationRows)
            };
            File.WriteAllText(System.IO.Path.Combine(archive.Root, "manifest.json"), JsonSerializer.Serialize(manifest));
            return archive;
        }

        private static FloridaBulkManifestEntry Write(string root, FloridaBulkFileDefinition definition, string csv, long? declaredRows)
        {
            var bytes = new UTF8Encoding(false).GetBytes(csv);
            File.WriteAllBytes(System.IO.Path.Combine(root, definition.RelativePath), bytes);
            var rows = declaredRows ?? csv.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length - 1;
            return new(definition.RelativePath, rows, bytes.Length, Convert.ToHexString(SHA256.HashData(bytes)), definition.Url,
                DateTimeOffset.UtcNow, "test", new() { [definition.Columns.ToString()] = rows });
        }

        public void Dispose()
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
    }
}
