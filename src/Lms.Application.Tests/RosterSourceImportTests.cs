using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Lms.Application.Data;
using Lms.Domain.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Lms.Application.Tests;

public sealed class RosterSourceImportTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "lms-roster-import-" + Guid.NewGuid().ToString("N"));
    private readonly SqliteConnection connection = new("Data Source=:memory:");
    private readonly RosterImportSource source = new("Maine", "people.csv", "Individual", "Synthetic fixture only");

    public RosterSourceImportTests()
    {
        Directory.CreateDirectory(root);
        connection.Open();
    }

    [Fact]
    public void PreparingPreservesZerosTypesAndReconcilesEveryRow()
    {
        CreateSource("LicenseNumber,PublishedName,SourceLicenseType,SourceStatus,Expiration\n" +
            "0012,Synthetic Student,Broker,Inactive,2027-12-31\n" +
            "0012,Synthetic Student,Broker,Inactive,2027-12-31\n" +
            "0012,Synthetic Student,Salesperson,Active,2027-12-31\n" +
            "0013,,Broker,Active,\n");
        var batch = RosterSourceImport.Prepare(root, [source]).Single();
        Assert.Equal(2, batch.Records.Count);
        Assert.All(batch.Records, record => Assert.Equal("0012", record.Number));
        Assert.Equal(new DateOnly(2027, 12, 31), batch.Records[0].Expires);
        var evidence = batch.Evidence.Single();
        Assert.Equal(4, evidence.SourceRows);
        Assert.Equal(2, evidence.ImportedRows);
        Assert.Equal(1, evidence.DuplicateRows);
        Assert.Equal(1, evidence.Exclusions.Values.Sum());
        Assert.Equal([4], evidence.RejectedDataRows);
    }

    [Fact]
    public void OhioUsesPublishedAccountNameAndDoesNotMistakeRenewalEffectiveDateForOriginalIssue()
    {
        CreateSource("Name,Legacy First Name,Legacy Last Name,Organization: Account Name,Effective Date,Expiration Date,Status,Primary License: Name,Regulatory Authorization Type: Name\n" +
            "SAL.0012,,,Synthetic Student,46000,47000,Active,REC.001,Salesperson\n" +
            "APP.0013,Synthetic,Appraiser,,46000,47000,Active,,Appraiser\n");
        var batch = RosterSourceImport.Prepare(root, [source with { Kind = "Ohio" }]).Single();
        var person = Assert.Single(batch.Records);
        Assert.Equal("Synthetic Student", person.Name);
        Assert.Null(person.Issued);
        Assert.NotNull(person.Expires);
        Assert.Equal("REC.001", person.RelatedNumber);
        Assert.Equal(1, batch.Evidence.Single().Exclusions.Values.Sum());
    }

    [Fact]
    public void BusinessQueryRecoversSingleNamesButKeepsUnverifiedAliasesUnresolved()
    {
        CreateSource("LicenseNumber,LicenseeName,SourceLicenseType,SourceStatus\n" +
            "001,Synthetic Company One,Company,Active\n" +
            "002,Synthetic Alias One,Company,Active\n" +
            "002,Synthetic Alias Two,Company,Active\n");
        var batch = RosterSourceImport.Prepare(root, [source with { Kind = "BusinessQuery" }]).Single();
        var firm = Assert.Single(batch.Records);
        Assert.True(firm.Business);
        Assert.Equal("001", firm.Number);
        var evidence = batch.Evidence.Single();
        Assert.Equal(2, evidence.Exclusions.Values.Sum());
        Assert.Equal(new[] { 2, 3 }, evidence.RejectedDataRows);
        Assert.Equal(3, evidence.ImportedRows + evidence.DuplicateRows + evidence.Exclusions.Values.Sum());
    }

    [Fact]
    public void BusinessQueryPreservesVerifiedProfileInsteadOfCreatingAliasFirms()
    {
        CreateSource("LicenseNumber,LicenseeName,SourceLicenseType,SourceStatus\n" +
            "001,Synthetic Firm,Company,Active\n");
        var batch = RosterSourceImport.Prepare(root,
            [source with { Kind = "Business license" }, source with { Kind = "BusinessQuery" }]).Single();
        Assert.Single(batch.Records);
        Assert.Equal(1, batch.Evidence[1].DuplicateRows);
        Assert.Empty(batch.Evidence[1].Exclusions);
    }

    [Fact]
    public void UnclassifiedHolderNeverInfersPersonhoodFromAPersonLookingName()
    {
        CreateSource("LicenseNumber,LicenseeName,SourceLicenseType,SourceStatus\n001,Synthetic Person,Broker,Active\n");
        var batch = RosterSourceImport.Prepare(root, [source with { Kind = "UnclassifiedHolder" }]).Single();
        Assert.Empty(batch.Records);
        Assert.Equal([1], batch.Evidence.Single().RejectedDataRows);
        Assert.Contains("Holder kind", batch.Evidence.Single().Exclusions.Single().Key);
    }

    [Theory]
    [InlineData("Synthetic Firm\nAKA: Synthetic Firm\nSynthetic Firm", "Synthetic Firm", true)]
    [InlineData("Synthetic Firm\nAKA: Different Firm", "Synthetic Firm\nAKA: Different Firm", false)]
    public void LouisianaCollapsesOnlyIdenticalNameLines(string published, string expected, bool warning)
    {
        CreateSource("LicenseNumber,PublishedName,SourceLicenseType,SourceStatus\n" +
            $"001,\"{published}\",Company,Active\n", "Louisiana");
        var batch = RosterSourceImport.Prepare(root, [source with { Jurisdiction = "Louisiana", Kind = "Business license" }]).Single();
        Assert.Equal(expected, Assert.Single(batch.Records).Name);
        Assert.Equal(warning, batch.Evidence.Single().Warnings.Count == 1);
    }

    [Fact]
    public void ModifiedSourceFailsBeforeAnyImport()
    {
        var path = CreateSource("LicenseNumber,PublishedName,SourceLicenseType\n0012,Synthetic Student,Broker\n");
        File.AppendAllText(path, "0013,Synthetic Other,Broker\n");
        Assert.Contains("hash mismatch", Assert.Throws<InvalidDataException>(() =>
            RosterSourceImport.Prepare(root, [source])).Message);
    }

    [Fact]
    public void ConflictingFirmsAreRejectedDuringPreflight()
    {
        CreateSource("LicenseNumber,PublishedName,SourceLicenseType\n0012,Synthetic Firm One,Firm\n0012,Synthetic Firm Two,Firm\n");
        Assert.Contains("Conflicting business", Assert.Throws<InvalidDataException>(() =>
            RosterSourceImport.Prepare(root, [source with { Kind = "Business license" }])).Message);
    }

    [Fact]
    public async Task ApplyingIsTransactionalIdempotentAndDefaultsToReview()
    {
        CreateSource("LicenseNumber,PublishedName,SourceLicenseType\n0012,Synthetic Student,Broker\n");
        var batch = RosterSourceImport.Prepare(root, [source]).Single();
        await using var db = Context();
        await db.Database.EnsureCreatedAsync();
        var first = await RosterSourceImport.ApplyAsync(db, batch);
        var second = await RosterSourceImport.ApplyAsync(db, batch);
        Assert.True(first.Imported);
        Assert.False(second.Imported);
        Assert.Equal(first.SnapshotId, second.SnapshotId);
        Assert.Equal(RegulatorySnapshotStatuses.PendingReview, first.Status);
        Assert.Single(await db.RegulatoryLicensees.ToListAsync());
        Assert.Single(await db.AuditLogs.ToListAsync());
        Assert.Empty(await db.UserRegulatoryLicenseLinks.ToListAsync());
        Assert.Empty(await db.UserAccounts.ToListAsync());
    }

    [Fact]
    public async Task ExplicitInterimApprovalAuditsAuthorizationWithoutImpersonatingAnAdmin()
    {
        CreateSource("LicenseNumber,PublishedName,SourceLicenseType\n0012,Synthetic Student,Broker\n");
        await using var db = Context();
        await db.Database.EnsureCreatedAsync();
        var result = await RosterSourceImport.ApplyAsync(db, RosterSourceImport.Prepare(root, [source]).Single(), true);
        var snapshot = await db.RegulatoryDataSnapshots.SingleAsync();
        var audit = await db.AuditLogs.SingleAsync();
        Assert.Equal(RegulatorySnapshotStatuses.Approved, result.Status);
        Assert.NotNull(snapshot.ReviewedAtUtc);
        Assert.Null(snapshot.ReviewedByUserId);
        Assert.Null(audit.ActorUserId);
        Assert.Contains("Workspace owner", audit.Details);
        Assert.Contains("Not license ownership", audit.Details);
    }

    [Fact]
    public async Task InvalidLateRecordRollsBackSnapshotAndEarlierChunk()
    {
        await using var db = Context();
        await db.Database.EnsureCreatedAsync();
        var records = Enumerable.Range(1, 1001).Select(index =>
            new RosterImportRecord("Maine", index.ToString(), index == 1001 ? new string('x', 161) : "Synthetic Student",
                "Broker", "Active", false, null, null, "", "")).ToList();
        var batch = new RosterImportBatch("Maine", "https://example.test/commission", DateTime.UtcNow, new string('A', 64), records, []);
        await Assert.ThrowsAsync<System.ComponentModel.DataAnnotations.ValidationException>(() =>
            RosterSourceImport.ApplyAsync(db, batch));
        db.ChangeTracker.Clear();
        Assert.Empty(await db.RegulatoryDataSnapshots.ToListAsync());
        Assert.Empty(await db.RegulatoryLicensees.ToListAsync());
        Assert.Empty(await db.AuditLogs.ToListAsync());
    }

    [Fact]
    public async Task AffiliationsUseExactEndpointsWithoutInferringBicOrUserOwnership()
    {
        await using var db = Context();
        await db.Database.EnsureCreatedAsync();
        var records = new[]
        {
            new RosterImportRecord("Maine", "F001", "Synthetic Firm", "Firm", "Active", true, null, null, "", ""),
            new RosterImportRecord("Maine", "0012", "Synthetic Student", "Broker", "Active", false, null, null, "F001", "Synthetic Firm")
        };
        var effective = new DateOnly(2027, 1, 1);
        var batch = new RosterImportBatch("Maine", "https://example.test/commission", DateTime.UtcNow,
            new string('B', 64), records, [], [new("F001", "0012", "Broker", effective)]);
        var result = await RosterSourceImport.ApplyAsync(db, batch);
        var edge = await db.RegulatoryFirmLicenseeAffiliations.SingleAsync();
        Assert.Equal(1, result.Affiliations);
        Assert.Equal(effective, edge.RelationshipEffectiveDate);
        Assert.False(edge.IsBrokerInCharge);
        Assert.False((await RosterSourceImport.ApplyAsync(db, batch)).Imported);
        Assert.Empty(await db.UserRegulatoryLicenseLinks.ToListAsync());
    }

    [Fact]
    public void CsvDialectIsStrictUnlessExplicitlyEnabled()
    {
        var path = Path.Combine(root, "quotes.csv");
        File.WriteAllText(path, "Name,DBA,Type\nSynthetic,\"Quoted\" suffix,Broker\n");
        Assert.Throws<InvalidDataException>(() => RosterSourceTable.Read(path).ToList());
        Assert.Equal("Quoted suffix", RosterSourceTable.Read(path, true).Single()["DBA"]);
        File.WriteAllText(path, "A,B\n\"multi\nline\",\"comma, value\"\n");
        var row = RosterSourceTable.Read(path).Single();
        Assert.Contains("line", row["A"]);
        Assert.Equal("comma, value", row["B"]);
        File.WriteAllText(path, "A,A\n1,2\n");
        Assert.Throws<InvalidDataException>(() => RosterSourceTable.Read(path).ToList());
    }

    [Fact]
    public void WorkbookReaderHandlesAsOfRowSparseCellsAndSharedStrings()
    {
        var path = Path.Combine(root, "source.xlsx");
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            WriteEntry(zip, "xl/sharedStrings.xml", """
                <sst xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><si><t>As of 9/4/2026</t></si><si><t>LicenseNumber</t></si><si><t>Name</t></si><si><t>Status</t></si><si><t>0012</t></si><si><t>Active</t></si></sst>
                """);
            WriteEntry(zip, "xl/worksheets/sheet1.xml", """
                <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><sheetData>
                <row r="1"><c r="A1" t="s"><v>0</v></c></row>
                <row r="2"><c r="A2" t="s"><v>1</v></c><c r="B2" t="s"><v>2</v></c><c r="C2" t="s"><v>3</v></c></row>
                <row r="3"><c r="A3" t="s"><v>4</v></c><c r="C3" t="s"><v>5</v></c></row>
                </sheetData></worksheet>
                """);
        }
        var row = RosterSourceTable.Read(path).Single();
        Assert.Equal("0012", row["LicenseNumber"]);
        Assert.Equal("", row["Name"]);
        Assert.Equal("Active", row["Status"]);
    }

    private string CreateSource(string contents, string jurisdiction = "Maine")
    {
        var capture = Path.Combine(root, jurisdiction.ToLowerInvariant(), "2026-10-12");
        Directory.CreateDirectory(Path.Combine(capture, "prepared", "XLXS"));
        var path = Path.Combine(capture, "prepared", "XLXS", "people.csv");
        File.WriteAllText(path, contents);
        File.WriteAllText(Path.Combine(root, "source-catalog.csv"),
            "Jurisdiction,LegacyRelativePath,CaptureDate,Role,OfficialSourceUrl,Coverage\n" +
            $"{jurisdiction},{jurisdiction.ToUpperInvariant()}\\XLXS\\people.csv,2026-10-12,prepared,https://example.test/commission,Synthetic\n");
        File.WriteAllText(Path.Combine(capture, "manifest.json"), JsonSerializer.Serialize(new
        {
            Files = new[] { new { LegacyRelativePath = jurisdiction.ToUpperInvariant() + @"\XLXS\people.csv",
                Sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))) } }
        }));
        return path;
    }
    private ApplicationDbContext Context() => new(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
    private static void WriteEntry(ZipArchive zip, string name, string contents)
    {
        using var writer = new StreamWriter(zip.CreateEntry(name).Open());
        writer.Write(contents);
    }
    public void Dispose()
    {
        connection.Dispose();
        Directory.Delete(root, true);
    }
}
