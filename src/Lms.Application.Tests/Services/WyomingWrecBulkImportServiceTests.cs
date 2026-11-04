using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lms.Application.Data;
using Lms.Application.Services;
using Lms.Domain.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Lms.Application.Tests.Services;

public sealed class WyomingWrecBulkImportServiceTests
{
    [Fact]
    public void HistoricalArrays_PreserveAllStatusesTypesDatesIdentifiersAndIncompleteCredentialsAsEvidence()
    {
        using var archive = Archive.Create();
        var rows = WyomingWrecBulkImportService.ReadRecords(archive.Path(WyomingWrecBulkImportService.IndividualFile),
            WyomingWrecBulkImportService.IndividualFile);
        Assert.Equal(5, rows.Count);
        var broker = rows[0];
        Assert.Equal("Responsible Broker", broker.LicenseType);
        Assert.Equal("Responsible Broker License", broker.SourceLicenseType);
        Assert.Equal(new DateOnly(2001, 3, 8), broker.OriginalLicenseDate);
        Assert.Equal(new DateOnly(2025, 3, 8), broker.IssueDate);
        Assert.Equal(new DateOnly(2027, 3, 8), broker.ExpirationDate);
        Assert.Equal(11, broker.IndividualId);
        Assert.Equal(0, broker.ProviderId);
        Assert.Null(broker.CapturedAtUtc);
        Assert.Null(broker.Page);
        Assert.Null(broker.Query);
        Assert.Equal(67, broker.Raw.EnumerateObject().Count());
        Assert.False(broker.Raw.GetProperty("IsActive").GetBoolean());
        Assert.Equal("Active", broker.Status);
        Assert.Contains(rows, row => row.Status == "Inactive" && row.LicenseType == "Associate Broker");
        Assert.Contains(rows, row => row.LicenseType == "Certified Residential Appraiser");
        Assert.Contains(rows, row => row.LicenseNumber.Length == 0);
        Assert.Equal("10 Main St, Cheyenne, CO, 82001, United States", broker.Address);
    }

    [Theory]
    [InlineData(1, "Real Estate Salesperson License", "Salesperson")]
    [InlineData(2, "Associate Broker License", "Associate Broker")]
    [InlineData(3, "Responsible Broker License", "Responsible Broker")]
    [InlineData(4, "Certified Residential Appraiser Permit", "Certified Residential Appraiser")]
    [InlineData(5, "Certified General Appraiser Permit", "Certified General Appraiser")]
    [InlineData(6, "Certified Appraiser Trainee Permit", "Certified Appraiser Trainee")]
    public void Reader_NormalizesOnlyPublishedTypesAndDoesNotDeriveStatusFromExpiryOrIsActive(int id, string sourceType, string normalized)
    {
        using var archive = Archive.Create();
        var path = archive.Path(WyomingWrecBulkImportService.IndividualFile);
        var json = JsonNode.Parse(File.ReadAllText(path))!;
        json[0]!["LicenseTypeId"] = id; json[0]!["LicenseTypeName"] = sourceType;
        json[0]!["LicenseExpirationDate"] = "2016-03-08T00:00:00";
        json[0]!["OriginalLicenseDate"] = "0001-01-01T00:00:00";
        json[0]!["LicenseStatusTypeName"] = "Pending Renewal";
        File.WriteAllText(path, json.ToJsonString());
        var record = WyomingWrecBulkImportService.ReadRecords(path, WyomingWrecBulkImportService.IndividualFile)[0];
        Assert.Equal(normalized, record.LicenseType);
        Assert.Equal(sourceType, record.SourceLicenseType);
        Assert.Equal("Pending Renewal", record.Status);
        Assert.Equal(new DateOnly(2016, 3, 8), record.ExpirationDate);
        Assert.Null(record.OriginalLicenseDate);
        Assert.Equal("0001-01-01T00:00:00", record.Raw.GetProperty("OriginalLicenseDate").GetString());
    }

    [Theory]
    [InlineData("schema")]
    [InlineData("duplicate-property")]
    [InlineData("type")]
    [InlineData("identifier")]
    [InlineData("date")]
    [InlineData("relations")]
    [InlineData("duplicate")]
    [InlineData("conflict")]
    [InlineData("empty")]
    public void Reader_RejectsMalformedSchemaDuplicatesAndConflictingPublishedIdentities(string change)
    {
        using var archive = Archive.Create(change);
        Assert.Throws<InvalidDataException>(() => WyomingWrecBulkImportService.CheckFile(
            archive.Path(WyomingWrecBulkImportService.IndividualFile), WyomingWrecBulkImportService.IndividualFile));
    }

    [Fact]
    public async Task Import_RetainsRawEvidenceAllCredentialTypesAndOnlyIdentifierVerifiedRelationships()
    {
        using var archive = Archive.Create();
        await using var database = await Database.CreateAsync();
        var importer = new WyomingWrecBulkImportService(database.Db);
        var id = await importer.ImportArchiveAsync(archive.Root);
        Assert.Equal(id, await importer.ImportArchiveAsync(archive.Root));
        Assert.Single(await database.Db.RegulatoryDataSnapshots.ToListAsync());
        Assert.Equal(11, await database.Db.RegulatorySourceRecords.CountAsync());
        Assert.Equal(4, await database.Db.RegulatoryLicensees.CountAsync());
        Assert.Equal(3, await database.Db.RegulatoryFirms.CountAsync());
        Assert.Empty(await database.Db.RegulatoryFirmLocations.ToListAsync());
        var firm = await database.Db.RegulatoryFirms.SingleAsync(row => row.LicenseNumber == "F1");
        Assert.Equal("Jane Smith", firm.QualifyingBrokerName);
        Assert.Equal(RegulatoryIndustries.RealEstate, firm.Industry);
        var unresolved = await database.Db.RegulatoryFirms.SingleAsync(row => row.LicenseNumber == "F2");
        Assert.Null(unresolved.QualifyingBrokerName);
        Assert.Equal(RegulatoryIndustries.Appraisal,
            (await database.Db.RegulatoryFirms.SingleAsync(row => row.LicenseNumber == "AMC1")).Industry);
        Assert.DoesNotContain(await database.Db.RegulatoryFirms.ToListAsync(), row => row.LicenseNumber is "DUP" or "B1");
        var links = await database.Db.RegulatoryFirmLicenseeAffiliations.Include(row => row.Firm).Include(row => row.Licensee).ToListAsync();
        Assert.Equal(3, links.Count);
        Assert.All(links, row => Assert.False(row.IsBrokerInCharge));
        Assert.All(links, row => Assert.Equal(new DateOnly(2025, 4, 1), row.RelationshipEffectiveDate));
        Assert.Contains(links, row => row.Firm!.LicenseNumber == "F1" && row.Licensee!.LicenseType == "Salesperson");
        Assert.Contains(links, row => row.Firm!.LicenseNumber == "AMC1" && row.Licensee!.LicenseType == "Certified Residential Appraiser");
        Assert.DoesNotContain(links, row => row.Firm!.LicenseNumber == "F2" || row.Licensee!.LicenseType == "Associate Broker");
        var evidence = await database.Db.RegulatorySourceRecords.SingleAsync(row => row.LicenseNumber == "B1");
        Assert.Contains("\"ChildProviderId\":101", evidence.SourceRecordJson);
        Assert.Contains("\"BranchId\":55", evidence.SourceRecordJson);
        Assert.DoesNotContain("captured_at_utc", evidence.SourceRecordJson);
        var match = RegulatoryLicenseeNameIndex.BuildMatchExpression("Jane Smi", id);
        Assert.Single(await database.Db.RegulatoryLicensees.FromSqlInterpolated($"""
            SELECT l.* FROM RegulatoryLicenseeNameSearch f CROSS JOIN RegulatoryLicensees l ON l.rowid=f.rowid
            WHERE f.RegulatoryLicenseeNameSearch MATCH {match}
            """).ToListAsync());
        Assert.Equal(3, WyomingWrecBulkImportService.AppraiserLicenseTypes.Count);
    }

    [Fact]
    public async Task Import_CancellationRollsBackAndLeavesThePreviouslyPublishedSnapshotCurrent()
    {
        using var first = Archive.Create();
        using var second = Archive.Create();
        second.ChangeFormatting();
        await using var database = await Database.CreateAsync();
        var importer = new WyomingWrecBulkImportService(database.Db);
        var baseline = await importer.ImportArchiveAsync(first.Root);
        using var cancellation = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => importer.ImportArchiveAsync(second.Root,
            cancellation.Token, staged: _ => cancellation.Cancel()));
        Assert.Single(await database.Db.RegulatoryDataSnapshots.ToListAsync());
        Assert.Equal(baseline, await CurrentAsync(database.Db));
        Assert.All(await database.Db.RegulatorySourceRecords.ToListAsync(), row => Assert.Equal(baseline, row.SnapshotId));
        Assert.All(await database.Db.RegulatoryLicensees.ToListAsync(), row => Assert.Equal(baseline, row.SnapshotId));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Snapshot_IdentityActivationStatisticsAndFtsAreStableForRepeatedSemanticData(bool differentHash)
    {
        using var first = Archive.Create();
        using var second = Archive.Create();
        if (differentHash) second.ChangeFormatting();
        await using var database = await Database.CreateAsync();
        var importer = new WyomingWrecBulkImportService(database.Db);
        var previous = await importer.ImportArchiveAsync(first.Root);
        var incoming = await importer.ImportArchiveAsync(second.Root);
        Assert.Equal(differentHash, previous != incoming);
        Assert.Equal(differentHash ? 2 : 1, await database.Db.RegulatoryDataSnapshots.CountAsync());
        Assert.Equal(incoming, await CurrentAsync(database.Db));
        var statistics = await new RegulatoryStatisticsService(database.Db, lastRuns: new LastRunSource(previous, incoming))
            .GetAsync("Wyoming", bulkOnly: true);
        Assert.Equal(previous, statistics.Baseline!.Id);
        Assert.Equal(incoming, statistics.Incoming!.Id);
        Assert.NotEmpty(statistics.Records);
        Assert.All(statistics.Records, row => Assert.Equal("Unchanged", row.Classification));
        Assert.All(statistics.Professions, row =>
        {
            Assert.Equal(row.Current, row.Collected);
            Assert.Equal(0, row.New); Assert.Equal(0, row.Changed);
            Assert.Equal(row.Collected, row.Unchanged);
        });
        Assert.Equal(previous, await importer.ImportArchiveAsync(first.Root));
        Assert.Equal(previous, await CurrentAsync(database.Db));
        var match = RegulatoryLicenseeNameIndex.BuildMatchExpression("Jane Smi", incoming);
        Assert.Single(await database.Db.Database.SqlQueryRaw<string>(
            "SELECT Name AS Value FROM RegulatoryLicenseeNameSearch WHERE RegulatoryLicenseeNameSearch MATCH {0}", match!).ToListAsync());
        await database.Db.RegulatoryDataSnapshots.Where(row => row.Id == incoming).ExecuteDeleteAsync();
        Assert.Empty(await database.Db.Database.SqlQueryRaw<string>(
            "SELECT Name AS Value FROM RegulatoryLicenseeNameSearch WHERE RegulatoryLicenseeNameSearch MATCH {0}", match!).ToListAsync());
        if (differentHash)
        {
            var retained = RegulatoryLicenseeNameIndex.BuildMatchExpression("Jane Smi", previous);
            Assert.Single(await database.Db.Database.SqlQueryRaw<string>(
                "SELECT Name AS Value FROM RegulatoryLicenseeNameSearch WHERE RegulatoryLicenseeNameSearch MATCH {0}", retained!).ToListAsync());
        }
    }

    [Fact]
    public async Task Import_RejectsIncompleteExistingSnapshotBeforeReactivation()
    {
        using var archive = Archive.Create();
        await using var database = await Database.CreateAsync();
        var importer = new WyomingWrecBulkImportService(database.Db);
        var id = await importer.ImportArchiveAsync(archive.Root);
        await database.Db.RegulatorySourceRecords.Where(row => row.SnapshotId == id).Take(1).ExecuteDeleteAsync();
        await Assert.ThrowsAsync<InvalidDataException>(() => importer.ImportArchiveAsync(archive.Root));
    }

    [Theory]
    [InlineData("hash")]
    [InlineData("bytes")]
    [InlineData("rows")]
    [InlineData("columns")]
    [InlineData("url")]
    [InlineData("missing")]
    [InlineData("published")]
    [InlineData("timestamp")]
    public async Task Archive_RejectsTamperedOrIncompleteAcquisitionManifest(string change)
    {
        using var archive = Archive.Create();
        var entries = JsonSerializer.Deserialize<List<FloridaBulkManifestEntry>>(File.ReadAllText(archive.Path("manifest.json")))!;
        entries[0] = change switch
        {
            "hash" => entries[0] with { Sha256 = new string('0', 64) },
            "bytes" => entries[0] with { Bytes = 1 },
            "rows" => entries[0] with { Rows = 99, Columns = new() { ["67"] = 99 } },
            "columns" => entries[0] with { Columns = new() { ["66"] = 5 } },
            "url" => entries[0] with { SourceUrl = "https://example.invalid/" },
            "published" => entries[0] with { SourceRowCount = 99 },
            "timestamp" => entries[0] with { VerifiedAtUtc = default },
            _ => entries[0]
        };
        if (change == "missing") entries.RemoveAt(2);
        File.WriteAllText(archive.Path("manifest.json"), JsonSerializer.Serialize(entries));
        await using var database = await Database.CreateAsync();
        await Assert.ThrowsAsync<InvalidDataException>(() => new WyomingWrecBulkImportService(database.Db).ImportArchiveAsync(archive.Root));
        Assert.Empty(await database.Db.RegulatoryDataSnapshots.ToListAsync());
    }

    private static Task<Guid> CurrentAsync(ApplicationDbContext db) =>
        RegulatoryRoster.Published(db.RegulatoryDataSnapshots.Where(row => row.State == "Wyoming")).Select(row => row.Id).FirstAsync();

    private sealed class LastRunSource(Guid previous, Guid incoming) : IRegulatoryLastRunSource
    {
        public Task<RegulatoryLastRun?> GetAsync(string state, CancellationToken cancellationToken = default) =>
            Task.FromResult<RegulatoryLastRun?>(state == "Wyoming" ? new(previous, incoming) : null);
    }

    private sealed class Database(SqliteConnection connection, ApplicationDbContext db) : IAsyncDisposable
    {
        public ApplicationDbContext Db { get; } = db;
        public static async Task<Database> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
            await db.Database.EnsureCreatedAsync();
            return new(connection, db);
        }
        public async ValueTask DisposeAsync() { await Db.DisposeAsync(); await connection.DisposeAsync(); }
    }

    private sealed class Archive : IDisposable
    {
        public string Root { get; } = System.IO.Path.Combine(Directory.GetCurrentDirectory(), ".wyoming-tests-" + Guid.NewGuid().ToString("N"));
        public string Path(string file) => System.IO.Path.Combine(Root, file);
        public static Archive Create(string? change = null)
        {
            var archive = new Archive();
            Directory.CreateDirectory(archive.Root);
            var people = new List<Dictionary<string, object?>>
            {
                Row("P1", 11, 3, "Responsible Broker License", true),
                Row("P2", 12, 1, "Real Estate Salesperson License", true),
                Row("P2", 12, 2, "Associate Broker License", true),
                Row("A1", 13, 4, "Certified Residential Appraiser Permit", true),
                Row("", 14, 1, "Real Estate Salesperson License", true)
            };
            people[2]["LicenseStatusTypeName"] = "Inactive";
            var firm = Row("F1", 101, 2, "Real Estate License", false);
            firm["EntityRelationList"] = new[]
            {
                Relation(101, 11, "P1", 3, true),
                Relation(101, 12, "P2", 1, false),
                Relation(101, 999, "P1", 3, true),
                Relation(101, 13, "A1", 4, false)
            };
            var unresolved = Row("F2", 102, 2, "Real Estate License", false);
            var deleted = Relation(102, 11, "P1", 3, true); deleted["IsDeleted"] = true;
            var released = Relation(102, 11, "P1", 3, true); released["ReleasedDate"] = "2025-06-01T00:00:00";
            var wrongNumber = Relation(102, 11, "WRONG", 3, true);
            unresolved["EntityRelationList"] = new[] { deleted, released, wrongNumber, Relation(102, 12, "P2", null, false) };
            var branch = Row("B1", 105, 2, "Real Estate License", false);
            branch["BranchTypeName"] = "Published branch";
            var branchRelation = Relation(101, 11, "P1", 3, true);
            branchRelation["BranchId"] = 55; branchRelation["ChildProviderId"] = 101;
            branch["EntityRelationList"] = new[] { branchRelation };
            var amc = Row("AMC1", 201, 1, "Appraisal Management Company", false);
            amc["EntityRelationList"] = new[] { Relation(201, 13, "A1", 4, false) };
            if (change == "schema") people[0].Remove("Zip");
            if (change == "type") people[0]["LicenseTypeName"] = "Unknown license";
            if (change == "identifier") people[0]["IndividualId"] = 0;
            if (change == "date") people[0]["LicenseExpirationDate"] = "yesterday";
            if (change == "relations") people[0]["EntityRelationList"] = "not a list";
            if (change is "duplicate" or "conflict")
            {
                var duplicate = new Dictionary<string, object?>(people[0]);
                if (change == "conflict") duplicate["LastName"] = "Conflicting name";
                people.Add(duplicate);
            }
            if (change == "empty") people.Clear();
            File.WriteAllText(archive.Path(WyomingWrecBulkImportService.IndividualFile), JsonSerializer.Serialize(people));
            if (change == "duplicate-property")
            {
                var json = File.ReadAllText(archive.Path(WyomingWrecBulkImportService.IndividualFile));
                File.WriteAllText(archive.Path(WyomingWrecBulkImportService.IndividualFile), json.Replace("\"Zip\":", "\"Zip\":\"fake\",\"Zip\":", StringComparison.Ordinal));
            }
            File.WriteAllText(archive.Path(WyomingWrecBulkImportService.FirmFile),
                JsonSerializer.Serialize(new[] { firm, unresolved, Row("DUP", 103, 2, "Real Estate License", false),
                    Row("DUP", 104, 2, "Real Estate License", false), branch }));
            File.WriteAllText(archive.Path(WyomingWrecBulkImportService.AmcFile), JsonSerializer.Serialize(new[] { amc }));
            archive.Manifest();
            return archive;
        }
        public void ChangeFormatting()
        {
            File.AppendAllText(Path(WyomingWrecBulkImportService.FirmFile), "\r\n");
            Manifest();
        }
        private void Manifest()
        {
            var entries = WyomingWrecBulkImportService.Definitions.Select(file =>
            {
                var bytes = File.ReadAllBytes(Path(file.Name));
                using var json = JsonDocument.Parse(bytes);
                long count = json.RootElement.GetArrayLength();
                return new FloridaBulkManifestEntry(file.Name, count, bytes.Length, Convert.ToHexString(SHA256.HashData(bytes)),
                    file.Url, DateTimeOffset.Parse("2026-10-12T12:00:00Z"), "Historical official raw array",
                    new() { ["67"] = count }) { SourceRowCount = count };
            });
            File.WriteAllText(Path("manifest.json"), JsonSerializer.Serialize(entries));
        }
        private static Dictionary<string, object?> Row(string number, long id, int type, string sourceType, bool individual)
        {
            var row = WyomingWrecBulkImportService.Headers.ToDictionary(key => key, _ => (object?)null);
            row["LicenseNumber"] = number; row["ProviderId"] = individual ? 0L : id; row["IndividualId"] = id;
            row["LicenseTypeId"] = type; row["LicenseTypeName"] = sourceType;
            row["FirstName"] = id == 11 ? "Jane" : "John"; row["LastName"] = id == 11 ? "Smith" : "Jones";
            row["CorporationName"] = "Same Published Realty"; row["IsActive"] = false;
            row["LicenseStatusTypeName"] = "Active"; row["OriginalLicenseDate"] = "2001-03-08T00:00:00";
            row["LicenseEffectiveDate"] = "2025-03-08T00:00:00"; row["LicenseExpirationDate"] = "2027-03-08T00:00:00";
            row["Address1"] = "10 Main St"; row["City"] = "Cheyenne"; row["State"] = "CO";
            row["Zip"] = "82001"; row["CountryName"] = "United States"; row["TotalRecord"] = 0;
            row["EntityRelationList"] = Array.Empty<object>();
            return row;
        }
        private static Dictionary<string, object?> Relation(long provider, long individual, string number, int? type, bool responsible) =>
            new()
            {
                ["ProviderId"] = provider, ["IndividualId"] = individual, ["IndividualLicenseNumber"] = number,
                ["IndividualLicenseTypeId"] = type, ["Responsible"] = responsible, ["EntityRelationStatusId"] = 1,
                ["EmailAddress"] = "published@example.test", ["StartDate"] = "2025-04-01T00:00:00",
                ["IndividualFirstName"] = "Jane", ["IndividualLastName"] = "Smith"
            };
        public void Dispose() { if (Directory.Exists(Root)) Directory.Delete(Root, true); }
    }
}
