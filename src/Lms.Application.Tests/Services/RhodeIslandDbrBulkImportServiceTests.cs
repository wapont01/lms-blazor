using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lms.Application.Data;
using Lms.Application.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Lms.Application.Tests.Services;

public sealed class RhodeIslandDbrBulkImportServiceTests
{
    [Fact]
    public void RawArrays_PreserveSourceIdentitiesHistoricalStatusesAndExactLicenseNumbers()
    {
        using var archive = new Archive();
        archive.Write(RhodeIslandDbrBulkImportService.BrokerFile,
            JsonSerializer.Serialize(new[] { Card(1, "REB.0000001", RhodeIslandDbrBulkImportService.Broker),
                Card(2, "REB.0000001", RhodeIslandDbrBulkImportService.Broker, status: "INACTIVE", expires: "") }));
        var rows = RhodeIslandDbrBulkImportService.ReadRecords(archive.Path(RhodeIslandDbrBulkImportService.BrokerFile),
            RhodeIslandDbrBulkImportService.BrokerFile);
        Assert.Equal(2, rows.Count);
        Assert.Equal("REB.0000001", rows[1].LicenseNumber);
        Assert.Equal("INACTIVE", rows[1].Status);
        Assert.Null(rows[1].IssueDate);
        Assert.Null(rows[1].ExpirationDate);
        Assert.Contains("\"id\":\"C2\"", rows[1].SourceRecordJson);
        Assert.Equal("Broker", RhodeIslandDbrBulkImportService.NormalizeLicenseType(rows[0].LicenseType));
        Assert.Equal("Salesperson", RhodeIslandDbrBulkImportService.NormalizeLicenseType(RhodeIslandDbrBulkImportService.Salesperson));
        Assert.True(RegulatoryLicenseTypes.IsRealEstate("Rhode Island", "Broker"));
        Assert.True(RegulatoryLicenseTypes.IsRealEstate("Rhode Island", "Salesperson"));
    }

    [Fact]
    public void Parser_PreservesPublishedCompoundLicenseNumbersAndMissingExpirationCards()
    {
        using var compound = JsonDocument.Parse(JsonSerializer.Serialize(
            Card(1, "REB.0010922.10923", RhodeIslandDbrBulkImportService.Broker)));
        Assert.Equal("REB.0010922.10923", RhodeIslandDbrBulkImportService.ParseRecord(
            compound.RootElement, RhodeIslandDbrBulkImportService.Broker).LicenseNumber);
        using var missingDate = JsonDocument.Parse("""
            {"id":"C15959","title":"Sabatino M Giordano","details":[{"label":"Credential Details",
            "values":["Real Estate Broker","REB.0004597","INACTIVE","00015959"]}]}
            """);
        var row = RhodeIslandDbrBulkImportService.ParseRecord(missingDate.RootElement, RhodeIslandDbrBulkImportService.Broker);
        Assert.Null(row.ExpirationDate);
        Assert.Equal("INACTIVE", row.Status);
    }

    [Theory]
    [InlineData("REB.15477-1")]
    [InlineData("REB.001800X")]
    public void Parser_PreservesVerifiedHistoricalNumberVariantsLiterally(string number)
    {
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(Card(1, number, RhodeIslandDbrBulkImportService.Broker)));
        Assert.Equal(number, RhodeIslandDbrBulkImportService.ParseRecord(json.RootElement, RhodeIslandDbrBulkImportService.Broker).LicenseNumber);
    }

    [Theory]
    [InlineData("multiple", "", null)]
    [InlineData("missing", "", null)]
    [InlineData("single-null-line", "J.J. Properties Inc.", "41 Longfellow Terrace, Providence, Rhode Island 02907, United States")]
    [InlineData("single-null-name", "", "41 Longfellow Terrace, Providence, Rhode Island 02907, United States")]
    public void CorporationDetail_KeepsMissingOrHistoricalBusinessAddressesSourceOnlyWithoutNameGuess(
        string shape, string expectedName, string? expectedAddress)
    {
        // Mirrors verified DBR C28931: two historical Business Address entries with null line slots and no current marker.
        var addresses = shape switch
        {
            "multiple" => """
                {"id":"30728","label":"Business Address","type":null,"value":["J.J. Properties  Inc.","41 Longfellow Terrace",null,"Providence, Rhode Island 02907","United States"]},
                {"id":"36346","label":"Business Address","type":null,"value":["JJ Properties  Inc.","237 Melrose Street",null,"Providence, Rhode Island 02907","United States"]},
                """,
            "single-null-line" => """
                {"id":"30728","label":"Business Address","type":null,"value":["J.J. Properties Inc.","41 Longfellow Terrace",null,"Providence, Rhode Island 02907","United States"]},
                """,
            "single-null-name" => """
                {"id":"30728","label":"Business Address","type":null,"value":[null,"41 Longfellow Terrace","","Providence, Rhode Island 02907","United States"]},
                """,
            _ => ""
        };
        using var search = JsonDocument.Parse(JsonSerializer.Serialize(
            Card(28931, "REC.0010001", RhodeIslandDbrBulkImportService.Corporation, name: "Qualifier Person")));
        using var detail = JsonDocument.Parse("""
            {"id":"C28931","tiles":[{"steps":[{"contents":[{"data":[
            {"label":"Credential","value":["REC.0010001","Real Estate Corporation","REAL ESTATE"]},
            {"label":"Status","value":["ACTIVE"]},
            """ + addresses + """
            {"label":"Expiration Date","value":["04/30/2028"]}]}]}]}]}
            """);
        var row = RhodeIslandDbrBulkImportService.ParseRecord(search.RootElement, RhodeIslandDbrBulkImportService.Corporation, detail.RootElement);
        Assert.Equal(expectedName, row.Name);
        Assert.Equal(expectedAddress, row.Address);
        Assert.Null(row.IssueDate);
        if (shape == "multiple") Assert.Contains("Melrose Street", row.SourceRecordJson);
        Assert.DoesNotContain("Qualifier Person", row.Name);
    }

    [Theory]
    [InlineData("[\"Realty LLC\",\"1 Main St\",\"Providence, Rhode Island 02903\",\"United States\"]")]
    [InlineData("[\"Realty LLC\",\"1 Main St\",7,\"Providence, Rhode Island 02903\",\"United States\"]")]
    [InlineData("\"Realty LLC, 1 Main St\"")]
    public void CorporationDetail_RejectsChangedBusinessAddressLayout(string value)
    {
        using var search = JsonDocument.Parse(JsonSerializer.Serialize(
            Card(1, "REC.0010001", RhodeIslandDbrBulkImportService.Corporation)));
        using var detail = JsonDocument.Parse("""
            {"id":"C1","tiles":[{"steps":[{"contents":[{"data":[
            {"label":"Credential","value":["REC.0010001","Real Estate Corporation","REAL ESTATE"]},
            {"label":"Status","value":["ACTIVE"]},{"label":"Expiration Date","value":["04/30/2028"]},
            {"label":"Business Address","value":
            """ + value + "}]}]}]}]}");
        Assert.Throws<InvalidDataException>(() => RhodeIslandDbrBulkImportService.ParseRecord(
            search.RootElement, RhodeIslandDbrBulkImportService.Corporation, detail.RootElement));
    }
    [Fact]
    public void BaselineCsv_RetainsMisplacedRecordNumbersAsSourceOnlyRatherThanGuessingDates()
    {
        var fields = RhodeIslandDbrBulkImportService.BaselinePersonHeaders.ToDictionary(field => field, _ => "");
        fields["InternalId"] = "C15959";
        fields["LicenseNumber"] = "REB.0004597";
        fields["CredentialType"] = RhodeIslandDbrBulkImportService.Broker;
        fields["Board"] = "REAL ESTATE";
        fields["FullName"] = "Sabatino M Giordano";
        fields["Status"] = "INACTIVE";
        fields["ExpirationDate"] = "00015959";
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            source_file = RhodeIslandDbrBulkImportService.BaselinePersonCsv, source_row = 1, fields
        }));
        var row = RhodeIslandDbrBulkImportService.ParseRecord(json.RootElement, RhodeIslandDbrBulkImportService.Broker);
        Assert.False(row.CanPublish);
        Assert.Null(row.ExpirationDate);
        Assert.Contains("\"ExpirationDate\":\"00015959\"", row.SourceRecordJson);
    }

    [Theory]
    [InlineData("REAL ESTATE BROKER", "REB.0000001", "ACTIVE", "04/30/2028")]
    [InlineData("Real Estate Broker", "RES.0000001", "ACTIVE", "04/30/2028")]
    [InlineData("Real Estate Broker", "REB.1", "ACTIVE", "04/30/2028")]
    [InlineData("Real Estate Broker", "REB.0000001", "", "04/30/2028")]
    [InlineData("Real Estate Broker", "REB.0000001", "Active", "04/30/2028")]
    [InlineData("Real Estate Broker", "REB.0000001", "UNKNOWN", "04/30/2028")]
    [InlineData("Real Estate Broker", "REB.0000001", "ACTIVE", "02/30/2028")]
    [InlineData("Real Estate Broker", "REB.0000001", "ACTIVE", "tomorrow")]
    public void Parser_RejectsChangedTypesNumbersMissingStatusesAndInferredDates(string type, string number, string status, string expires)
    {
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(Card(1, number, type, status: status, expires: expires)));
        Assert.Throws<InvalidDataException>(() =>
            RhodeIslandDbrBulkImportService.ParseRecord(json.RootElement, RhodeIslandDbrBulkImportService.Broker));
    }

    [Fact]
    public void Csv_AcceptsOnlyEmbeddedUntouchedSourceJson()
    {
        using var archive = new Archive();
        var raw = JsonSerializer.Serialize(Card(1, "REB.0000001", RhodeIslandDbrBulkImportService.Broker));
        archive.Write(RhodeIslandDbrBulkImportService.BrokerFile, "FullName,SourceRecordJson\r\n\"Do not use this name\",\""
            + raw.Replace("\"", "\"\"") + "\"\r\n");
        var row = Assert.Single(RhodeIslandDbrBulkImportService.ReadRecords(archive.Path(RhodeIslandDbrBulkImportService.BrokerFile),
            RhodeIslandDbrBulkImportService.BrokerFile));
        Assert.Equal("Jane Q Smith", row.Name);
        Assert.Equal(raw, row.SourceRecordJson);
        archive.Write(RhodeIslandDbrBulkImportService.BrokerFile, "LicenseNumber,FullName,Status\r\nREB.0000001,Jane Q Smith,ACTIVE\r\n");
        Assert.Throws<InvalidDataException>(() => RhodeIslandDbrBulkImportService.CheckFile(
            archive.Path(RhodeIslandDbrBulkImportService.BrokerFile), RhodeIslandDbrBulkImportService.BrokerFile));
    }

    [Theory]
    [InlineData("count")]
    [InlineData("filter")]
    [InlineData("missing")]
    [InlineData("duplicate")]
    [InlineData("offset")]
    public void Pages_RejectChangedCountWrongTypeMissingRepeatedAndOutOfOrderPages(string change)
    {
        using var archive = new Archive();
        var card = Card(1, "REB.0000001", RhodeIslandDbrBulkImportService.Broker);
        var second = change == "duplicate" ? card : Card(2, "REB.0000002", RhodeIslandDbrBulkImportService.Broker);
        var type = change == "filter" ? RhodeIslandDbrBulkImportService.Salesperson : RhodeIslandDbrBulkImportService.Broker;
        var pages = new List<object>
        {
            new { start = 0, response = Page(RhodeIslandDbrBulkImportService.Broker, 2, card) }
        };
        if (change != "missing")
            pages.Add(new { start = change == "offset" ? 0 : 1, response = Page(type, change == "count" ? 3 : 2, second) });
        archive.Write(RhodeIslandDbrBulkImportService.BrokerFile,
            JsonSerializer.Serialize(new { licenseType = RhodeIslandDbrBulkImportService.Broker, pageSize = 1, pages }));
        Assert.Throws<InvalidDataException>(() => RhodeIslandDbrBulkImportService.CheckFile(
            archive.Path(RhodeIslandDbrBulkImportService.BrokerFile), RhodeIslandDbrBulkImportService.BrokerFile));
    }

    [Fact]
    public async Task Import_IsTransactionalIndexedIdempotentAndDoesNotGuessQualifierAffiliations()
    {
        using var archive = new Archive();
        archive.Write(RhodeIslandDbrBulkImportService.CorporationFile, JsonSerializer.Serialize(new[]
        {
            new
            {
                search = Card(3, "REC.0000003", RhodeIslandDbrBulkImportService.Corporation, "Jane Q Smith"),
                detail = Detail(3, "REC.0000003", qualifier: "Jane Q Smith")
            }
        }));
        archive.Manifest();
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var importer = new RhodeIslandDbrBulkImportService(db);
        var id = await importer.ImportArchiveAsync(archive.Root);
        Assert.Equal(id, await importer.ImportArchiveAsync(archive.Root));
        Assert.Single(await db.RegulatoryDataSnapshots.ToListAsync());
        Assert.Equal(3, await db.RegulatorySourceRecords.CountAsync());
        Assert.Equal(2, await db.RegulatoryLicensees.CountAsync());
        var firm = await db.RegulatoryFirms.SingleAsync();
        Assert.Equal("Published Realty LLC", firm.LegalName);
        Assert.Equal("Jane Q Smith", firm.QualifyingBrokerName);
        Assert.Equal(new DateOnly(2024, 1, 1), firm.LicenseIssuedDate);
        Assert.Empty(await db.RegulatoryFirmLicenseeAffiliations.ToListAsync());
        Assert.Empty(await db.RegulatoryFirmLocations.ToListAsync());
        var match = RegulatoryLicenseeNameIndex.BuildMatchExpression("Jane Smi", id);
        var found = await db.RegulatoryLicensees.FromSqlInterpolated($"""
            SELECT l.* FROM RegulatoryLicenseeNameSearch f CROSS JOIN RegulatoryLicensees l ON l.rowid=f.rowid
            WHERE f.RegulatoryLicenseeNameSearch MATCH {match}
            """).ToListAsync();
        Assert.Equal(2, found.Count);
        Assert.Equal(new[] { "Broker", "Salesperson" }, found.Select(row => row.LicenseType).Order().ToArray());
        Assert.All(found, row => Assert.StartsWith("Real Estate ", row.SourceLicenseType));
        Assert.Contains("\"detail\"", (await db.RegulatorySourceRecords.SingleAsync(row => row.LicenseNumber == "REC.0000003")).SourceRecordJson);
    }

    [Fact]
    public async Task Import_LinksOnlyPublishedSupervisedLicenseReferencesNotNamesOrEndedRelationships()
    {
        using var archive = new Archive();
        archive.Write(RhodeIslandDbrBulkImportService.CorporationFile, JsonSerializer.Serialize(new[]
        {
            new
            {
                search = Card(3, "REC.0000003", RhodeIslandDbrBulkImportService.Corporation, "Jane Q Smith"),
                detail = Detail(3, "REC.0000003", reference: "Real Estate Salesperson - RES.0000002")
            }
        }));
        archive.Manifest();
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var importer = new RhodeIslandDbrBulkImportService(db);
        var id = await importer.ImportArchiveAsync(archive.Root);
        Assert.Equal(id, await importer.ImportArchiveAsync(archive.Root));
        var affiliation = await db.RegulatoryFirmLicenseeAffiliations.Include(row => row.Licensee).SingleAsync();
        Assert.Equal("RES.0000002", affiliation.Licensee!.LicenseNumber);
        Assert.Equal(new DateOnly(2026, 2, 1), affiliation.RelationshipEffectiveDate);
        Assert.False(affiliation.IsBrokerInCharge);
        using var ended = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            search = Card(3, "REC.0000003", RhodeIslandDbrBulkImportService.Corporation),
            detail = Detail(3, "REC.0000003", reference: "Real Estate Salesperson - RES.0000002", ended: true)
        }));
        Assert.NotNull(Assert.Single(RhodeIslandDbrBulkImportService.ParseRecord(ended.RootElement,
            RhodeIslandDbrBulkImportService.Corporation).Relationships).EndDate);
        using var nameOnly = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            search = Card(3, "REC.0000003", RhodeIslandDbrBulkImportService.Corporation),
            detail = Detail(3, "REC.0000003", reference: "Jane Q Smith")
        }));
        Assert.Empty(RhodeIslandDbrBulkImportService.ParseRecord(nameOnly.RootElement, RhodeIslandDbrBulkImportService.Corporation).Relationships);
    }

    [Theory]
    [InlineData("hash")]
    [InlineData("rows")]
    [InlineData("source-count")]
    [InlineData("url")]
    [InlineData("missing")]
    [InlineData("truncated")]
    public async Task Validation_RejectsUnverifiedPartialArchives(string change)
    {
        using var archive = new Archive();
        var entries = archive.Manifest();
        entries[0] = change switch
        {
            "hash" => entries[0] with { Sha256 = new string('0', 64) },
            "rows" => entries[0] with { Rows = 2, Columns = new() { ["5"] = 2 } },
            "source-count" => entries[0] with { SourceRowCount = 2 },
            "url" => entries[0] with { SourceUrl = "https://example.invalid/roster" },
            _ => entries[0]
        };
        if (change == "missing") entries.RemoveAt(1);
        if (change == "truncated") archive.Write(RhodeIslandDbrBulkImportService.BrokerFile, "[");
        archive.Write("manifest.json", JsonSerializer.Serialize(entries));
        await Assert.ThrowsAsync<InvalidDataException>(() => RhodeIslandDbrBulkImportService.ValidateArchiveAsync(archive.Root));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Import_CancellationAndStagingFailureRollBackNormalizedSourceAndFtsRows(bool cancel)
    {
        using var archive = new Archive();
        archive.Manifest();
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        await RegulatoryLicenseeNameIndex.EnsureAsync(db);
        using var cancellation = new CancellationTokenSource();
        var importer = new RhodeIslandDbrBulkImportService(db);
        var task = importer.ImportArchiveAsync(archive.Root, cancellation.Token, staged: total =>
        {
            if (total < 6) return;
            if (cancel) cancellation.Cancel();
            else throw new InvalidOperationException("Synthetic staging failure");
        });
        if (cancel) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        else await Assert.ThrowsAsync<InvalidOperationException>(() => task);
        Assert.Equal(0, await db.RegulatoryDataSnapshots.CountAsync());
        Assert.Equal(0, await db.RegulatorySourceRecords.CountAsync());
        Assert.Equal(0, await db.RegulatoryLicensees.CountAsync());
        Assert.Equal(0, await db.RegulatoryFirms.CountAsync());
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM RegulatoryLicenseeNameSearch";
        Assert.Equal(0L, (long)(await command.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task Statistics_IgnoreJsonFormattingButDetectPublishedStatusChangesAndScopeReactivation()
    {
        using var first = new Archive();
        using var same = new Archive(indented: true);
        using var changed = new Archive(status: "INACTIVE");
        first.Manifest(); same.Manifest(); changed.Manifest();
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var importer = new RhodeIslandDbrBulkImportService(db);
        var firstId = await importer.ImportArchiveAsync(first.Root);
        var sameId = await importer.ImportArchiveAsync(same.Root);
        Assert.NotEqual(firstId, sameId);
        var statistics = await new RegulatoryStatisticsService(db).GetAsync("Rhode Island", bulkOnly: true);
        Assert.All(statistics.Professions, row =>
        {
            Assert.Equal(0, row.New);
            Assert.Equal(0, row.Changed);
            Assert.Equal(row.Collected, row.Unchanged);
        });
        await importer.ImportArchiveAsync(changed.Root);
        statistics = await new RegulatoryStatisticsService(db).GetAsync("Rhode Island", bulkOnly: true);
        Assert.Contains(statistics.Professions, row => row.Changed == 1);
        Assert.Equal(firstId, await importer.ImportArchiveAsync(first.Root));
        Assert.Equal(firstId, await RegulatoryRoster.Published(db.RegulatoryDataSnapshots.Where(row => row.State == "Rhode Island"))
            .Select(row => row.Id).FirstAsync());
        var match = RegulatoryLicenseeNameIndex.BuildMatchExpression("Jane", firstId);
        Assert.Equal(2, (await db.RegulatoryLicensees.FromSqlInterpolated($"""
            SELECT l.* FROM RegulatoryLicenseeNameSearch f CROSS JOIN RegulatoryLicensees l ON l.rowid=f.rowid
            WHERE f.RegulatoryLicenseeNameSearch MATCH {match}
            """).ToListAsync()).Count);
    }

    [Fact]
    public async Task Statistics_RetainConflictingHistoricalIdentitiesAsEvidenceWithoutPublishingAStatusWinner()
    {
        using var first = new Archive();
        using var second = new Archive(indented: true);
        var rows = new[]
        {
            Card(1, "REB.0000001", RhodeIslandDbrBulkImportService.Broker),
            Card(4, "REB.0000001", RhodeIslandDbrBulkImportService.Broker, status: "INACTIVE", expires: "04/30/2022")
        };
        first.Write(RhodeIslandDbrBulkImportService.BrokerFile, JsonSerializer.Serialize(rows));
        second.Write(RhodeIslandDbrBulkImportService.BrokerFile, JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true }));
        first.Manifest(); second.Manifest();
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var importer = new RhodeIslandDbrBulkImportService(db);
        await importer.ImportArchiveAsync(first.Root);
        var id = await importer.ImportArchiveAsync(second.Root);
        Assert.Equal(1, await db.RegulatoryLicensees.CountAsync(row => row.SnapshotId == id));
        Assert.Equal(4, await db.RegulatorySourceRecords.CountAsync(row => row.SnapshotId == id));
        Assert.False(await db.RegulatoryLicensees.AnyAsync(row => row.SnapshotId == id && row.LicenseNumber == "REB.0000001"));
        var statistics = await new RegulatoryStatisticsService(db).GetAsync("Rhode Island", bulkOnly: true);
        Assert.Equal(2, statistics.Professions.Sum(row => row.Collected));
        Assert.Equal(2, statistics.Professions.Sum(row => row.Unchanged));
        Assert.All(statistics.Professions, row => Assert.Equal(0, row.Changed));
    }

    private static object Card(int id, string number, string type, string name = "Jane Q Smith", string status = "ACTIVE", string expires = "04/30/2028") => new
    {
        id = "C" + id, title = name, subtitle = "INDIVIDUAL",
        details = new[] { new { label = "Credential Details", values = new[] { type, number, status, expires, id.ToString("D8") } } }
    };
    private static object Page(string type, int count, params object[] rows) => new
    {
        resultCount = count, results = rows,
        filters = new[]
        {
            new { key = "board", items = new[] { new { label = "REAL ESTATE", count } } },
            new { key = "licenseType", items = new[] { new { label = type, count } } }
        }
    };
    private static object Detail(int id, string number, string? qualifier = null, string? reference = null, bool ended = false)
    {
        var data = new List<object>
        {
            new { label = "Credential", value = new[] { number, RhodeIslandDbrBulkImportService.Corporation, "REAL ESTATE" } },
            new { label = "Status", value = new[] { "ACTIVE" } },
            new { label = "Initial Credential Date", value = new[] { "01/01/2024" } },
            new { label = "Expiration Date", value = new[] { "04/30/2028" } },
            new { label = "Business Address", value = new[] { "Published Realty LLC", "1 Main St", "", "Providence, Rhode Island 02903", "United States" } }
        };
        if (qualifier is not null) data.Add(new { label = "Broker Qualifier", value = new[] { qualifier } });
        if (reference is not null) data.Add(new
        {
            relationshipName = "Supervision of RES", credentialReference = reference, name = "Jane Q Smith",
            startDate = "02/01/2026", endDate = ended ? "03/01/2026" : null
        });
        return new { id = "C" + id, tiles = new[] { new { steps = new[] { new { contents = new[] { new { data } } } } } } };
    }

    private sealed class Archive : IDisposable
    {
        public string Root { get; } = System.IO.Path.Combine(Environment.CurrentDirectory, "ri-import-test-" + Guid.NewGuid().ToString("N"));
        public string Path(string name) => System.IO.Path.Combine(Root, name);
        public Archive(bool indented = false, string status = "ACTIVE")
        {
            Directory.CreateDirectory(Root);
            var options = new JsonSerializerOptions { WriteIndented = indented };
            Write(RhodeIslandDbrBulkImportService.BrokerFile, JsonSerializer.Serialize(new[] { Card(1, "REB.0000001", RhodeIslandDbrBulkImportService.Broker, status: status) }, options));
            Write(RhodeIslandDbrBulkImportService.SalespersonFile, JsonSerializer.Serialize(new[] { Card(2, "RES.0000002", RhodeIslandDbrBulkImportService.Salesperson) }, options));
            Write(RhodeIslandDbrBulkImportService.CorporationFile, JsonSerializer.Serialize(new[]
            {
                new { search = Card(3, "REC.0000003", RhodeIslandDbrBulkImportService.Corporation, "Not a legal business name"),
                    detail = Detail(3, "REC.0000003") }
            }, options));
        }
        public void Write(string name, string value) => File.WriteAllText(Path(name), value, new UTF8Encoding(false));
        public List<FloridaBulkManifestEntry> Manifest()
        {
            var entries = RhodeIslandDbrBulkImportService.Definitions.Select(file =>
            {
                var bytes = File.ReadAllBytes(Path(file.RelativePath));
                var rows = RhodeIslandDbrBulkImportService.CheckFile(Path(file.RelativePath), file.Name);
                return new FloridaBulkManifestEntry(file.RelativePath, rows, bytes.Length,
                    Convert.ToHexString(SHA256.HashData(bytes)), file.Url, DateTimeOffset.UtcNow,
                    "Synthetic source evidence", new() { ["5"] = rows }) { SourceRowCount = rows };
            }).ToList();
            Write("manifest.json", JsonSerializer.Serialize(entries));
            return entries;
        }
        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
