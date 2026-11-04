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

public sealed class KentuckyOopBulkImportServiceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Reader_SupportsRawHtmlExcelAndQuotedMultilineCsvWithoutReinterpretingAddresses(bool html)
    {
        using var archive = Archive.Create(html: html);
        var row = Assert.Single(KentuckyOopBulkImportService.ReadRecords(archive.Path(KentuckyOopBulkImportService.RegisteredFirmBranchFile),
            KentuckyOopBulkImportService.RegisteredFirmBranchFile));
        Assert.Equal("Published, \"Realty\" & Branch", row.Name);
        Assert.Equal("000300", row.LicenseNumber);
        Assert.Equal(new DateOnly(2008, 6, 5), row.IssueDate);
        Assert.Equal(new DateOnly(2026, 7, 9), row.StatusDate);
        Assert.Equal(new DateOnly(2027, 3, 8), row.RenewalDate);
        Assert.Equal(new DateOnly(2028, 3, 31), row.ExpirationDate);
        Assert.Equal(13, row.Fields.Length);
        Assert.Equal("Original export business\n68 Mill St, MA", row.Fields[11]);
        Assert.Equal("Original export mailing\nNOT PUBLIC INFORMATION", row.Fields[12]);
    }

    [Theory]
    [InlineData("header", false)]
    [InlineData("columns", false)]
    [InlineData("board", false)]
    [InlineData("status", false)]
    [InlineData("broker-probation", false)]
    [InlineData("broker-cancellation", false)]
    [InlineData("type", false)]
    [InlineData("id", false)]
    [InlineData("date", false)]
    [InlineData("malformed", false)]
    [InlineData("conflict", false)]
    [InlineData("empty", false)]
    [InlineData("header", true)]
    [InlineData("columns", true)]
    [InlineData("board", true)]
    [InlineData("status", true)]
    [InlineData("type", true)]
    [InlineData("date", true)]
    [InlineData("truncated", true)]
    [InlineData("script", true)]
    [InlineData("doctype", true)]
    [InlineData("merged", true)]
    [InlineData("second-table", true)]
    [InlineData("empty", true)]
    public void Reader_RejectsChangedSchemaUnsupportedFactsAndMalformedExports(string change, bool html)
    {
        using var archive = Archive.Create(change, html);
        Assert.Throws<InvalidDataException>(() => KentuckyOopBulkImportService.CheckFile(
            archive.Path(KentuckyOopBulkImportService.BrokerFile), KentuckyOopBulkImportService.BrokerFile));
    }

    [Theory]
    [InlineData("Probation")]
    [InlineData("Active - Requested Cancellation")]
    public async Task Import_PreservesExceptionalStatusesActuallyPublishedByTheActiveDirectory(string status)
    {
        using var archive = Archive.Create();
        var path = archive.Path(KentuckyOopBulkImportService.SalesAssociateFile);
        File.WriteAllText(path, File.ReadAllText(path).Replace("\"Active\"", "\"" + status + "\""));
        archive.RefreshManifest();
        var record = Assert.Single(KentuckyOopBulkImportService.ReadRecords(path, KentuckyOopBulkImportService.SalesAssociateFile));
        Assert.Equal(status, record.Status);
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = Context(connection);
        await db.Database.EnsureCreatedAsync();
        await new KentuckyOopBulkImportService(db).ImportArchiveAsync(archive.Root);
        Assert.Equal(status, (await db.RegulatoryLicensees.SingleAsync(row => row.LicenseNumber == "200")).LicenseStatus);
    }

    [Fact]
    public async Task Import_PreservesRawRowsAndLicensedFirmCredentialsWithoutGuessingRelationships()
    {
        using var archive = Archive.Create(html: true);
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = Context(connection);
        await db.Database.EnsureCreatedAsync();
        await RegulatoryLicenseeNameIndex.EnsureAsync(db);
        var importer = new KentuckyOopBulkImportService(db);
        var id = await importer.ImportArchiveAsync(archive.Root);
        Assert.Equal(id, await importer.ImportArchiveAsync(archive.Root));
        Assert.Single(await db.RegulatoryDataSnapshots.ToListAsync());
        Assert.Equal(4, await db.RegulatorySourceRecords.CountAsync());
        Assert.Equal(2, await db.RegulatoryLicensees.CountAsync());
        var firm = await db.RegulatoryFirms.SingleAsync();
        Assert.Equal("000300", firm.LicenseNumber);
        Assert.Equal("Published, \"Realty\" & Branch", firm.LegalName);
        Assert.Null(firm.Address);
        Assert.Null(firm.TradeName);
        Assert.Null(firm.QualifyingBrokerName);
        Assert.Equal(RegulatoryIndustries.RealEstate, firm.Industry);
        Assert.Equal(new DateOnly(2027, 3, 8), firm.LicenseRenewalDate);
        Assert.Empty(await db.RegulatoryFirmLocations.ToListAsync());
        Assert.Empty(await db.RegulatoryFirmLicenseeAffiliations.ToListAsync());
        var broker = await db.RegulatoryLicensees.SingleAsync(row => row.LicenseNumber == "100");
        Assert.Equal("Broker", broker.LicenseType);
        Assert.Equal("Broker", broker.SourceLicenseType);
        Assert.Equal(new DateOnly(2008, 6, 5), broker.LicenseIssuedDate);
        Assert.Equal(new DateOnly(2027, 3, 8), broker.LicenseRenewalDate);
        Assert.False(broker.IsBicEligible);
        Assert.Null(broker.RelatedLicenseNumber);
        var match = RegulatoryLicenseeNameIndex.BuildMatchExpression("Jane Smi", id);
        var result = await db.RegulatoryLicensees.FromSqlInterpolated($"""
            SELECT l.* FROM RegulatoryLicenseeNameSearch f CROSS JOIN RegulatoryLicensees l ON l.rowid=f.rowid
            WHERE f.RegulatoryLicenseeNameSearch MATCH {match}
            """).ToListAsync();
        Assert.Equal(id, Assert.Single(result).SnapshotId);
        var evidence = await db.RegulatorySourceRecords.SingleAsync(row => row.LicenseNumber == "000300");
        using var json = JsonDocument.Parse(evidence.SourceRecordJson);
        var fields = json.RootElement.GetProperty("fields");
        Assert.Equal("Registered Firm Branch", fields.GetProperty("License Type").GetString());
        Assert.Equal("Original export business\n68 Mill St, MA", fields.GetProperty("Business Address").GetString());
        Assert.Equal("Original export mailing\nNOT PUBLIC INFORMATION", fields.GetProperty("Mailing Address").GetString());
    }

    [Theory]
    [InlineData("cross-file")]
    [InlineData("conflict")]
    public async Task Import_RejectsConflictingCredentialsBeforePublishing(string change)
    {
        using var archive = Archive.Create(change);
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = Context(connection);
        await db.Database.EnsureCreatedAsync();
        await Assert.ThrowsAsync<InvalidDataException>(() => new KentuckyOopBulkImportService(db).ImportArchiveAsync(archive.Root));
        Assert.Empty(await db.RegulatoryDataSnapshots.ToListAsync());
        Assert.Empty(await db.RegulatorySourceRecords.ToListAsync());
    }

    [Theory]
    [InlineData(4)]
    [InlineData(6)]
    [InlineData(7)]
    public async Task Import_RollsBackAllStagedRowsAndNameSearchWhenCancelled(long cancelAfter)
    {
        using var archive = Archive.Create();
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = Context(connection);
        await db.Database.EnsureCreatedAsync();
        await RegulatoryLicenseeNameIndex.EnsureAsync(db);
        using var cancellation = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new KentuckyOopBulkImportService(db)
            .ImportArchiveAsync(archive.Root, cancellation.Token, staged: rows =>
            {
                if (rows >= cancelAfter) cancellation.Cancel();
            }));
        Assert.Empty(await db.RegulatoryDataSnapshots.ToListAsync());
        Assert.Empty(await db.RegulatorySourceRecords.ToListAsync());
        Assert.Empty(await db.RegulatoryLicensees.ToListAsync());
        Assert.Empty(await db.RegulatoryFirms.ToListAsync());
        Assert.Empty(await db.Database.SqlQueryRaw<string>("SELECT Name AS Value FROM RegulatoryLicenseeNameSearch").ToListAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LastRun_IdempotentAndChangedHashArchivesReportUnchangedCredentialsWithScopedSearch(bool changedHash)
    {
        using var first = Archive.Create();
        using var second = Archive.Create();
        if (changedHash)
        {
            File.AppendAllText(second.Path(KentuckyOopBulkImportService.BrokerFile), "\r\n");
            second.RefreshManifest();
        }
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = Context(connection);
        await db.Database.EnsureCreatedAsync();
        await RegulatoryLicenseeNameIndex.EnsureAsync(db);
        var importer = new KentuckyOopBulkImportService(db);
        var previous = await importer.ImportArchiveAsync(first.Root);
        var incoming = await importer.ImportArchiveAsync(second.Root);
        Assert.Equal(changedHash, previous != incoming);
        var statistics = await new RegulatoryStatisticsService(db, lastRuns: new LastRunSource(previous, incoming))
            .GetAsync("Kentucky", bulkOnly: true);
        Assert.Equal(previous, statistics.Baseline!.Id);
        Assert.Equal(incoming, statistics.Incoming!.Id);
        Assert.Equal(3, statistics.Professions.Sum(row => row.Collected));
        Assert.Equal(3, statistics.Records.Count);
        Assert.All(statistics.Records, row => Assert.Equal("Unchanged", row.Classification));
        Assert.All(statistics.Professions, row =>
        {
            Assert.Equal(row.Current, row.Collected);
            Assert.Equal(0, row.New);
            Assert.Equal(0, row.Changed);
            Assert.Equal(row.Collected, row.Unchanged);
        });
        Assert.Equal(previous, await importer.ImportArchiveAsync(first.Root));
        Assert.Equal(previous, await RegulatoryRoster.Published(db.RegulatoryDataSnapshots.Where(row => row.State == "Kentucky"))
            .Select(row => row.Id).FirstAsync());
        foreach (var snapshot in new[] { previous, incoming }.Distinct())
        {
            var match = RegulatoryLicenseeNameIndex.BuildMatchExpression("Jane Smi", snapshot);
            var rows = await db.RegulatoryLicensees.FromSqlInterpolated($"""
                SELECT l.* FROM RegulatoryLicenseeNameSearch f CROSS JOIN RegulatoryLicensees l ON l.rowid=f.rowid
                WHERE f.RegulatoryLicenseeNameSearch MATCH {match}
                """).ToListAsync();
            Assert.Equal(snapshot, Assert.Single(rows).SnapshotId);
        }
        await db.RegulatoryDataSnapshots.Where(row => row.Id == incoming).ExecuteDeleteAsync();
        var deletedMatch = RegulatoryLicenseeNameIndex.BuildMatchExpression("Jane Smi", incoming);
        Assert.Empty(await db.Database.SqlQueryRaw<string>(
            "SELECT Name AS Value FROM RegulatoryLicenseeNameSearch WHERE RegulatoryLicenseeNameSearch MATCH {0}", deletedMatch!)
            .ToListAsync());
        if (changedHash)
        {
            var retainedMatch = RegulatoryLicenseeNameIndex.BuildMatchExpression("Jane Smi", previous);
            Assert.Single(await db.Database.SqlQueryRaw<string>(
                "SELECT Name AS Value FROM RegulatoryLicenseeNameSearch WHERE RegulatoryLicenseeNameSearch MATCH {0}", retainedMatch!)
                .ToListAsync());
        }
    }

    [Fact]
    public async Task Import_RejectsAnIncompleteExistingSnapshot()
    {
        using var archive = Archive.Create();
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = Context(connection);
        await db.Database.EnsureCreatedAsync();
        var importer = new KentuckyOopBulkImportService(db);
        var id = await importer.ImportArchiveAsync(archive.Root);
        await db.RegulatorySourceRecords.Where(row => row.SnapshotId == id).Take(1).ExecuteDeleteAsync();
        await Assert.ThrowsAsync<InvalidDataException>(() => importer.ImportArchiveAsync(archive.Root));
    }

    [Theory]
    [InlineData("hash")]
    [InlineData("bytes")]
    [InlineData("rows")]
    [InlineData("url")]
    [InlineData("missing")]
    [InlineData("duplicate")]
    [InlineData("columns")]
    [InlineData("published")]
    [InlineData("verified")]
    public async Task Archive_RejectsChangedOrIncompleteManifestEvidence(string change)
    {
        using var archive = Archive.Create();
        var entries = JsonSerializer.Deserialize<List<FloridaBulkManifestEntry>>(File.ReadAllText(archive.Path("manifest.json")))!;
        entries[0] = change switch
        {
            "hash" => entries[0] with { Sha256 = new string('0', 64) },
            "bytes" => entries[0] with { Bytes = entries[0].Bytes + 1 },
            "rows" => entries[0] with { Rows = 99, Columns = new() { ["13"] = 99 }, SourceRowCount = 99 },
            "url" => entries[0] with { SourceUrl = "https://example.invalid/" },
            "columns" => entries[0] with { Columns = new() { ["12"] = 2 } },
            "published" => entries[0] with { SourceRowCount = 99 },
            "verified" => entries[0] with { VerifiedAtUtc = default },
            _ => entries[0]
        };
        if (change == "missing") entries.RemoveAt(1);
        if (change == "duplicate") entries[1] = entries[0];
        File.WriteAllText(archive.Path("manifest.json"), JsonSerializer.Serialize(entries));
        await Assert.ThrowsAsync<InvalidDataException>(() => KentuckyOopBulkImportService.ValidateArchiveAsync(archive.Root));
    }

    private static ApplicationDbContext Context(SqliteConnection connection) =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);

    private sealed class LastRunSource(Guid previous, Guid incoming) : IRegulatoryLastRunSource
    {
        public Task<RegulatoryLastRun?> GetAsync(string state, CancellationToken cancellationToken = default) =>
            Task.FromResult<RegulatoryLastRun?>(state == "Kentucky" ? new(previous, incoming) : null);
    }

    private sealed class Archive : IDisposable
    {
        public string Root { get; } = System.IO.Path.Combine(Directory.GetCurrentDirectory(), ".kentucky-tests-" + Guid.NewGuid().ToString("N"));
        public string Path(string name) => System.IO.Path.Combine(Root, name);
        private readonly Dictionary<string, long> counts = new();

        public static Archive Create(string? change = null, bool html = false)
        {
            var archive = new Archive();
            Directory.CreateDirectory(archive.Root);
            foreach (var file in KentuckyOopBulkImportService.Definitions)
            {
                var isBroker = file.Name == KentuckyOopBulkImportService.BrokerFile;
                var row = new[]
                {
                    isBroker ? "Smith, Jane" : file.Name == KentuckyOopBulkImportService.SalesAssociateFile
                        ? "Jones, John" : "Published, \"Realty\" & Branch",
                    "Published, \"Realty\" & Branch", "Kentucky Real Estate Commission",
                    KentuckyOopBulkImportService.LicenseTypeForFile(file.Name),
                    isBroker ? "100" : file.Name == KentuckyOopBulkImportService.SalesAssociateFile ? "200" : "000300",
                    "Active", "6/5/2008", "7/9/2026", "03/8/2027", "3/31/2028", "NOT AVAILABLE",
                    "Original export business\n68 Mill St, MA", "Original export mailing\nNOT PUBLIC INFORMATION"
                };
                var rows = new List<string[]> { row };
                if (isBroker) rows.Add((string[])row.Clone());
                if (isBroker && change == "columns") rows[0] = row[..12];
                if (isBroker && change == "board") row[2] = "Other Board";
                if (isBroker && change == "status") row[5] = "Inactive";
                if (isBroker && change == "broker-probation") row[5] = "Probation";
                if (isBroker && change == "broker-cancellation") row[5] = "Active - Requested Cancellation";
                if (isBroker && change == "type") row[3] = "Sales Associate";
                if (isBroker && change == "id") row[4] = "BR-100";
                if (isBroker && change == "date") row[6] = "2/30/2026";
                if (isBroker && change == "conflict") rows[1][0] = "Different Person";
                if (!isBroker && change == "cross-file") row[4] = "100";
                if (isBroker && change == "empty") rows.Clear();
                var header = (string[])KentuckyOopBulkImportService.Headers.Clone();
                if (isBroker && change == "header") header[11] = "Corrected Address";
                static string Csv(string[] fields) => string.Join(",", fields.Select(field => "\"" + field.Replace("\"", "\"\"") + "\""));
                static string Html(string[] fields) => "<tr>" + string.Join("", fields.Select(field =>
                    "<td>" + System.Net.WebUtility.HtmlEncode(field).Replace("\n", "<br />") + "</td>")) + "</tr>";
                var text = html ? "<table>" + Html(header) + string.Join("", rows.Select(Html)) + "</table>"
                    : Csv(header) + "\r\n" + string.Join("\r\n", rows.Select(Csv)) + "\r\n";
                if (isBroker && change == "malformed") text += "\"unterminated";
                if (isBroker && change == "truncated") text = text[..^8];
                if (isBroker && change == "script") text = text.Replace("</table>", "<script>alert(1)</script></table>");
                if (isBroker && change == "doctype") text = "<!DOCTYPE table SYSTEM \"https://example.invalid/\">" + text;
                if (isBroker && change == "merged") text = text.Replace("<td>", "<td colspan=\"2\">");
                if (isBroker && change == "second-table") text += "<table></table>";
                File.WriteAllText(archive.Path(file.Name), text, new UTF8Encoding(false));
                archive.counts[file.Name] = rows.Count;
            }
            archive.RefreshManifest();
            return archive;
        }

        public void RefreshManifest()
        {
            var entries = KentuckyOopBulkImportService.Definitions.Select(file =>
            {
                var bytes = File.ReadAllBytes(Path(file.Name));
                var count = counts[file.Name];
                return new FloridaBulkManifestEntry(file.Name, count, bytes.Length, Convert.ToHexString(SHA256.HashData(bytes)),
                    file.Url, DateTimeOffset.UtcNow, "Verified official complete query", new() { ["13"] = count })
                    { SourceRowCount = count };
            }).ToList();
            File.WriteAllText(Path("manifest.json"), JsonSerializer.Serialize(entries));
        }

        public void Dispose() => Directory.Delete(Root, true);
    }
}
