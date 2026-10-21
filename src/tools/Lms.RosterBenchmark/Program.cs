using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Lms.Application.Data;
using Lms.Application.Services;
using Lms.Domain.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualBasic.FileIO;

if (args.Length is < 2 or > 3 || (args.Length == 3 && args[2] != "--include-california"))
{
    Console.Error.WriteLine("Usage: Lms.RosterBenchmark <repository-root> <new-output-directory> [--include-california]");
    return 2;
}

var repositoryRoot = Path.GetFullPath(args[0]);
var outputDirectory = Path.GetFullPath(args[1]);
if (Directory.Exists(outputDirectory) || File.Exists(outputDirectory))
{
    throw new IOException("Use a new output directory. Existing databases/results are never overwritten.");
}

var docsRoot = Path.Combine(repositoryRoot, "src", "docs");
var inputPath = Path.Combine(docsRoot, "ENTITY_CAMPAIGN", "Full_License_Development_Data_20261011.csv");
var manifestPath = Path.ChangeExtension(inputPath, ".json");
var relationshipPath = Path.Combine(docsRoot, "ENTITY_CAMPAIGN", "Full_License_Development_Relationships_20261011.csv");
using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
foreach (var file in manifest.RootElement.GetProperty("Files").EnumerateArray())
{
    var name = file.GetProperty("Name").GetString() ?? throw new InvalidDataException("Missing manifest filename.");
    var path = name.Contains('\\') ? Path.Combine(docsRoot, name) : Path.Combine(Path.GetDirectoryName(inputPath)!, name);
    if (Hash(path) != file.GetProperty("Sha256").GetString())
    {
        throw new InvalidDataException($"Source hash mismatch: {name}");
    }
}

var rows = ReadCsv(inputPath).ToList();
if (rows.Count != manifest.RootElement.GetProperty("UniqueLicenses").GetInt32())
{
    throw new InvalidDataException("Full-license input count differs from manifest.");
}

var identities = new HashSet<string>(StringComparer.Ordinal);
foreach (var row in rows)
{
    if (!identities.Add(Key(row)))
    {
        throw new InvalidDataException($"Duplicate full-license identity: {Key(row)}");
    }
}

Directory.CreateDirectory(outputDirectory);
var databasePath = Path.Combine(outputDirectory, "roster-benchmark.db");
var options = new DbContextOptionsBuilder<ApplicationDbContext>()
    .UseSqlite(new SqliteConnectionStringBuilder { DataSource = databasePath, Pooling = false }.ToString())
    .Options;
await using var db = new ApplicationDbContext(options);
await db.Database.EnsureCreatedAsync();
await db.Database.ExecuteSqlRawAsync("""
    CREATE TABLE BenchmarkSourceRecords (
        State TEXT NOT NULL, LicenseType TEXT NOT NULL, LicenseNumber TEXT NOT NULL,
        RecordKind TEXT NOT NULL, EntityId TEXT NOT NULL, SourceJson TEXT NOT NULL,
        PRIMARY KEY (State, LicenseType, LicenseNumber));
    """);

var stages = new List<object>();
foreach (var group in rows.GroupBy(row => row["State"]).OrderBy(group => group.Key, StringComparer.Ordinal))
{
    var stateRows = group.ToList();
    var timer = Stopwatch.StartNew();
    var imported = await ImportState(stateRows);
    timer.Stop();
    var counts = await Counts();
    var snapshot = await db.RegulatoryDataSnapshots.AsNoTracking().SingleAsync(s => s.State == group.Key);
    var expectedIndividuals = stateRows.Count(r => r["RecordKind"] == "Individual");
    var expectedBusinesses = stateRows.Count(r => r["RecordKind"] == "Business license");
    if (await db.RegulatoryLicensees.CountAsync(l => l.SnapshotId == snapshot.Id) != expectedIndividuals ||
        await db.RegulatoryFirms.CountAsync(f => f.SnapshotId == snapshot.Id) != expectedBusinesses)
    {
        throw new InvalidDataException($"Application table counts differ from source for {group.Key}.");
    }
    if (await ImportState(stateRows))
    {
        throw new InvalidOperationException("Idempotence check unexpectedly imported another snapshot.");
    }
    stages.Add(new { State = group.Key, Imported = imported, InputRecords = stateRows.Count,
        ImportMilliseconds = timer.Elapsed.TotalMilliseconds, DatabaseBytes = new FileInfo(databasePath).Length,
        Counts = counts, IdempotentRepeat = true });
    Console.WriteLine($"{group.Key}: {stateRows.Count:N0} records, {timer.Elapsed.TotalSeconds:F2}s, {new FileInfo(databasePath).Length:N0} database bytes");
}

var relationshipRows = ReadCsv(relationshipPath).ToList();
if (relationshipRows.Count != manifest.RootElement.GetProperty("Relationships").GetInt32())
{
    throw new InvalidDataException("Relationship count differs from manifest.");
}
foreach (var edge in relationshipRows)
{
    if (edge["RelationshipType"] != "Principal Broker")
    {
        throw new InvalidDataException("Unsupported relationship semantics; do not infer a broker-in-charge flag.");
    }
    var firm = await db.RegulatoryFirms.SingleAsync(f => f.State == edge["State"] && f.LicenseNumber == edge["FromLicenseNumber"]);
    var licensee = await db.RegulatoryLicensees.SingleAsync(l => l.State == edge["State"] &&
        l.LicenseNumber == edge["ToLicenseNumber"] && l.LicenseType == edge["ToLicenseType"]);
    if (firm.SnapshotId != licensee.SnapshotId)
    {
        throw new InvalidDataException("Relationship endpoints belong to different snapshots.");
    }
    firm.QualifyingBrokerName = licensee.Name;
    db.RegulatoryFirmLicenseeAffiliations.Add(new RegulatoryFirmLicenseeAffiliation
    {
        SnapshotId = firm.SnapshotId, FirmId = firm.Id, LicenseeId = licensee.Id
    });
}
await db.SaveChangesAsync();
db.ChangeTracker.Clear();

// Full source payloads retain fields the application model cannot yet represent.
await using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = databasePath, Pooling = false }.ToString()))
{
    await connection.OpenAsync();
    using var command = connection.CreateCommand();
    command.CommandText = "SELECT State, LicenseType, LicenseNumber, RecordKind, EntityId, SourceJson FROM BenchmarkSourceRecords";
    using var reader = await command.ExecuteReaderAsync();
    var source = rows.ToDictionary(Key, StringComparer.Ordinal);
    var verified = 0;
    while (await reader.ReadAsync())
    {
        var key = $"{reader.GetString(0)}|{reader.GetString(1)}|{reader.GetString(2)}";
        if (!source.TryGetValue(key, out var row) || reader.GetString(3) != row["RecordKind"] ||
            reader.GetString(5) != row["SourceRecordJson"] || !Guid.TryParse(reader.GetString(4), out _))
        {
            throw new InvalidDataException($"Source evidence did not round-trip: {key}");
        }
        verified++;
    }
    if (verified != rows.Count) { throw new InvalidDataException("Evidence row count mismatch."); }
}

if (args.Length == 3)
{
    var timer = Stopwatch.StartNew();
    await CaliforniaDreCaptureSeed.SeedAsync(db, Path.Combine(repositoryRoot, "src"));
    timer.Stop();
    db.ChangeTracker.Clear();
    var snapshot = await db.RegulatoryDataSnapshots.SingleAsync(s => s.State == "California");
    var before = await Counts();
    if (await db.RegulatoryLicensees.CountAsync(l => l.SnapshotId == snapshot.Id) != 427990 ||
        await db.RegulatoryFirms.CountAsync(f => f.SnapshotId == snapshot.Id) != 35006 ||
        await db.RegulatoryFirmLicenseeAffiliations.CountAsync(a => a.SnapshotId == snapshot.Id) != 268151)
    {
        throw new InvalidDataException("California import totals differ from the previously verified dated archive.");
    }
    await CaliforniaDreCaptureSeed.SeedAsync(db, Path.Combine(repositoryRoot, "src"));
    db.ChangeTracker.Clear();
    if (before != await Counts()) { throw new InvalidDataException("California import is not idempotent."); }
    // Approval is limited to this isolated benchmark so existing admin services can be measured.
    snapshot = await db.RegulatoryDataSnapshots.SingleAsync(s => s.State == "California");
    snapshot.Status = RegulatorySnapshotStatuses.Approved;
    await db.SaveChangesAsync();
    db.ChangeTracker.Clear();
    stages.Add(new { State = "California", ImportMilliseconds = timer.Elapsed.TotalMilliseconds,
        DatabaseBytes = new FileInfo(databasePath).Length, Counts = before, IdempotentRepeat = true });
}

var service = new RegulatoryDirectoryService(db, new AuditLogService(db));
var measurements = new List<object>();
foreach (var state in rows.Select(row => row["State"]).Distinct().Order(StringComparer.Ordinal).Prepend((string?)null)
    .Concat(args.Length == 3 ? new[] { "California" } : Array.Empty<string>()))
{
    measurements.Add(await Measure($"Admin license list / {state ?? "all"} / take 250",
        async () => (await service.GetApprovedLicenseesAsync(state, 250)).Count, 250));
    measurements.Add(await Measure($"Admin firm targets / {state ?? "all"} / take 100",
        async () => (await service.GetCampaignFirmTargetsAsync(state, 100)).Count, 100));
}
measurements.Add(await Measure("Admin snapshot summary", async () => (await service.GetRecentSnapshotsAsync()).Count, 25));
var example = await db.RegulatoryLicensees.AsNoTracking().OrderBy(l => l.State).ThenBy(l => l.LicenseNumber).FirstAsync();
measurements.Add(await Measure("Indexed state/license-number lookup", async () =>
    await db.RegulatoryLicensees.AsNoTracking().Where(l => l.State == example.State && l.LicenseNumber == example.LicenseNumber).CountAsync(), null));
measurements.Add(await Measure("Verified firm relationship detail", async () =>
    await db.RegulatoryFirmLicenseeAffiliations.AsNoTracking().Where(a => a.Firm!.State == "Kentucky" &&
        a.Firm.LicenseNumber == "251286").Select(a => a.Licensee!.Name).CountAsync(), 1));

await using var planConnection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = databasePath, Pooling = false }.ToString());
await planConnection.OpenAsync();
var plans = new List<object>();
foreach (var sql in new[]
{
    "SELECT Id FROM RegulatoryLicensees WHERE State = 'Idaho' AND LicenseNumber = '1881926'",
    "SELECT Id FROM RegulatoryLicensees WHERE State = 'Idaho' ORDER BY Name LIMIT 250",
    "SELECT Id FROM RegulatoryFirms WHERE State = 'Kentucky'"
})
{
    using var command = planConnection.CreateCommand();
    command.CommandText = "EXPLAIN QUERY PLAN " + sql;
    using var reader = await command.ExecuteReaderAsync();
    var details = new List<string>();
    while (await reader.ReadAsync()) { details.Add(reader.GetString(3)); }
    plans.Add(new { Sql = sql, Details = details });
}
var finalCounts = await Counts();
var report = new
{
    RunAtUtc = DateTime.UtcNow, DatabasePath = databasePath, DatabaseBytes = new FileInfo(databasePath).Length,
    Runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
    OperatingSystem = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
    LogicalProcessors = Environment.ProcessorCount,
    Schema = "ApplicationDbContext current model via EnsureCreated; not a migration-upgrade test",
    Sources = new { InputPath = inputPath, Sha256 = Hash(inputPath), UniqueSourceCredentials = rows.Count,
        CaliforniaIncluded = args.Length == 3 },
    Stages = stages, FinalCounts = finalCounts, Measurements = measurements, RepresentativeQueryPlans = plans,
    Validation = "All four source payloads round-tripped; state counts matched; repeat imports idempotent; relationship endpoints resolved",
    Limitations = new[]
    {
        "Isolated benchmark only. No live application database or production approval modified.",
        "Four registered recent datasets; optional full California importer. Older roster registration remains incomplete.",
        "RegulatoryFirm has no license-type field; exact business types retained in BenchmarkSourceRecords. Cross-regulator firm identity still needs application schema work before live ingestion.",
        "Raw source statuses, restrictions, discipline flags and coverage retained as JSON evidence; not all mapped into application fields.",
        "Only the one registered recent relationship plus optional California importer affiliations; other relationships unknown.",
        "One warm-up then 20 sequential measurements per operation. Warm local DB/service time, not browser/network latency, cold disk, concurrent admins or 22-million-record performance.",
        "GetCampaignFirmTargetsAsync loads all matching firms before its in-memory limit. Measurements expose existing behavior; this tool does not change it.",
        "Database bytes include the source evidence table and indexes; not a per-state production storage prediction."
    }
};
var reportPath = Path.Combine(outputDirectory, "benchmark-results.json");
await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"Verified benchmark complete: {finalCounts}; report {reportPath}");
return 0;

async Task<bool> ImportState(List<Dictionary<string, string>> stateRows)
{
    var state = stateRows[0]["State"];
    var parserVersion = "isolated-full-development-v1-" + Hash(inputPath)[..12];
    if (await db.RegulatoryDataSnapshots.AnyAsync(s => s.State == state && s.ParserVersion == parserVersion))
    {
        return false;
    }
    await using var transaction = await db.Database.BeginTransactionAsync();
    var snapshot = new RegulatoryDataSnapshot
    {
        State = state, SourceName = "Isolated full-size development benchmark; not production-approved",
        SourceUrl = stateRows[0]["OfficialSourceUrl"], ParserVersion = parserVersion,
        RetrievedAtUtc = DateTime.ParseExact(stateRows[0]["CaptureDate"], "yyyy-MM-dd", CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal),
        Status = RegulatorySnapshotStatuses.Approved
    };
    db.RegulatoryDataSnapshots.Add(snapshot);
    await db.SaveChangesAsync();
    db.ChangeTracker.Clear();
    foreach (var batch in stateRows.Chunk(1000))
    {
        foreach (var row in batch)
        {
            using var payload = JsonDocument.Parse(row["SourceRecordJson"]);
            var source = payload.RootElement;
            var id = Guid.NewGuid();
            if (row["RecordKind"] == "Individual")
            {
                db.RegulatoryLicensees.Add(new RegulatoryLicensee
                {
                    Id = id, SnapshotId = snapshot.Id, State = state, LicenseNumber = row["LicenseNumber"],
                    Name = row["PublishedName"], LicenseType = row["SourceLicenseType"], LicenseStatus = row["SourceStatus"],
                    LicenseIssuedDate = Date(source, "Issue Date") ?? Date(source, "OriginalIssueDate"),
                    OriginalLicenseDate = Date(source, "OriginalIssueDate"),
                    LicenseRenewalDate = Date(source, "Expiration Date") ?? Date(source, "Expiration")
                });
            }
            else if (row["RecordKind"] == "Business license")
            {
                db.RegulatoryFirms.Add(new RegulatoryFirm
                {
                    Id = id, SnapshotId = snapshot.Id, State = state, LicenseNumber = row["LicenseNumber"],
                    LegalName = row["PublishedName"], LicenseStatus = row["SourceStatus"],
                    LicenseIssuedDate = Date(source, "Issue Date") ?? Date(source, "OriginalIssueDate"),
                    LicenseRenewalDate = Date(source, "Expiration Date") ?? Date(source, "Expiration")
                });
            }
            else { throw new InvalidDataException($"Unsupported record kind: {row["RecordKind"]}"); }
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO BenchmarkSourceRecords (State, LicenseType, LicenseNumber, RecordKind, EntityId, SourceJson)
                VALUES ({state}, {row["SourceLicenseType"]}, {row["LicenseNumber"]}, {row["RecordKind"]}, {id.ToString()}, {row["SourceRecordJson"]})
                """);
        }
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
    }
    await transaction.CommitAsync();
    return true;
}

async Task<DatabaseCounts> Counts() =>
    new(await db.RegulatoryDataSnapshots.CountAsync(), await db.RegulatoryLicensees.CountAsync(),
        await db.RegulatoryFirms.CountAsync(), await db.RegulatoryFirmLicenseeAffiliations.CountAsync());

async Task<object> Measure(string name, Func<Task<int>> operation, int? maximumRows)
{
    await operation();
    var timings = new List<double>();
    int? expectedRows = null;
    for (var i = 0; i < 20; i++)
    {
        db.ChangeTracker.Clear();
        var timer = Stopwatch.StartNew();
        var count = await operation();
        timer.Stop();
        if (maximumRows.HasValue && count > maximumRows || expectedRows.HasValue && count != expectedRows)
        {
            throw new InvalidDataException($"Unstable or unbounded benchmark output: {name}");
        }
        expectedRows = count;
        timings.Add(timer.Elapsed.TotalMilliseconds);
    }
    timings.Sort();
    var result = new { Operation = name, Iterations = timings.Count, ReturnedRows = expectedRows,
        P50Milliseconds = timings[9], P95Milliseconds = timings[18], MaximumMilliseconds = timings[^1] };
    Console.WriteLine($"{name}: p50 {result.P50Milliseconds:F2}ms, p95 {result.P95Milliseconds:F2}ms, rows {expectedRows}");
    return result;
}

static DateOnly? Date(JsonElement payload, string field)
{
    if (!payload.TryGetProperty(field, out var value) || string.IsNullOrWhiteSpace(value.GetString())) { return null; }
    var text = value.GetString();
    if (DateOnly.TryParseExact(text, new[] { "M/d/yyyy", "MM/dd/yyyy", "yyyy-MM-dd", "d-MMM-yyyy", "dd-MMM-yyyy", "M/d/yyyy H:mm:ss" },
        CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) { return date; }
    throw new InvalidDataException($"Unrecognized published date in {field}: {text}");
}

static string Key(Dictionary<string, string> row) => $"{row["State"]}|{row["SourceLicenseType"]}|{row["LicenseNumber"]}";

static string Hash(string path)
{
    using var stream = File.OpenRead(path);
    return Convert.ToHexString(SHA256.HashData(stream));
}

static IEnumerable<Dictionary<string, string>> ReadCsv(string path)
{
    using var parser = new TextFieldParser(path, System.Text.Encoding.UTF8);
    parser.SetDelimiters(",");
    parser.HasFieldsEnclosedInQuotes = true;
    parser.TrimWhiteSpace = false;
    var headers = parser.ReadFields() ?? throw new InvalidDataException($"Missing CSV header: {path}");
    if (headers.Distinct(StringComparer.Ordinal).Count() != headers.Length) { throw new InvalidDataException("Duplicate CSV columns."); }
    while (!parser.EndOfData)
    {
        var fields = parser.ReadFields() ?? throw new InvalidDataException($"Missing CSV row: {path}");
        if (fields.Length != headers.Length) { throw new InvalidDataException($"CSV field count mismatch: {path}"); }
        var row = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < fields.Length; i++) { row.Add(headers[i], fields[i]); }
        yield return row;
    }
}

internal sealed record DatabaseCounts(int Snapshots, int LicenseeRecords, int Firms, int Affiliations);
