using System.Text.Json;
using Lms.Application.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

if (args.Length is < 3 or > 5 || args.Skip(3).Any(arg => arg is not ("--apply" or "--approve-interim"))
    || (args.Contains("--approve-interim") && !args.Contains("--apply")))
{
    Console.Error.WriteLine("Usage: Lms.RosterImport <roster-root> <existing-db> <new-report.json> [--apply] [--approve-interim]");
    return 2;
}
var root = Path.GetFullPath(args[0]);
var dbPath = Path.GetFullPath(args[1]);
var reportPath = Path.GetFullPath(args[2]);
if (!File.Exists(dbPath)) throw new FileNotFoundException("Import requires an existing migrated application database.", dbPath);
if (File.Exists(reportPath)) throw new IOException("Use a new report path; existing reports are never overwritten.");
var batches = RosterSourceImport.Prepare(root);
foreach (var batch in batches)
{
    Console.WriteLine($"{batch.State}: {batch.Records.Count(record => !record.Business):N0} individuals, {batch.Records.Count(record => record.Business):N0} firms");
    foreach (var file in batch.Evidence)
    {
        foreach (var exclusion in file.Exclusions)
            Console.WriteLine($"  EXCLUDED {exclusion.Value:N0}: {exclusion.Key} ({Path.GetFileName(file.File)})");
        foreach (var warning in file.Warnings)
            Console.WriteLine($"  WARNING {warning.Value:N0}: {warning.Key} ({Path.GetFileName(file.File)})");
    }
}
var json = new JsonSerializerOptions { WriteIndented = true };
if (!args.Contains("--apply"))
{
    File.WriteAllText(reportPath, JsonSerializer.Serialize(batches.Select(batch => new
    {
        batch.State, batch.SourceUrl, batch.CapturedAt, batch.Fingerprint,
        Individuals = batch.Records.Count(record => !record.Business), Firms = batch.Records.Count(record => record.Business),
        Sources = batch.Evidence
    }), json));
    Console.WriteLine("Preflight complete; database unchanged.");
    return 0;
}
var connectionString = new SqliteConnectionStringBuilder { DataSource = dbPath, Mode = SqliteOpenMode.ReadWrite, Pooling = false }.ToString();
await using var connection = new SqliteConnection(connectionString);
await connection.OpenAsync();
var backupPath = dbPath + ".before-roster-import-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmss") + "-" + Guid.NewGuid().ToString("N") + ".db";
await using (var backup = new SqliteConnection(new SqliteConnectionStringBuilder
    { DataSource = backupPath, Mode = SqliteOpenMode.ReadWriteCreate, Pooling = false }.ToString()))
{
    await backup.OpenAsync();
    connection.BackupDatabase(backup);
}
Console.WriteLine($"SQLite backup: {backupPath}");
await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
var before = await ProtectedCounts();
var results = new List<RosterImportResult>();
foreach (var batch in batches)
{
    var result = await RosterSourceImport.ApplyAsync(db, batch, args.Contains("--approve-interim"));
    results.Add(result);
    File.WriteAllText(reportPath, JsonSerializer.Serialize(new { Backup = backupPath, Complete = false, Results = results }, json));
    Console.WriteLine($"{batch.State}: {(result.Imported ? "imported" : "already present")} ({result.Status})");
    var repeat = await RosterSourceImport.ApplyAsync(db, batch);
    if (repeat.Imported || repeat.SnapshotId != result.SnapshotId)
        throw new InvalidDataException("Import idempotence verification failed.");
}
var after = await ProtectedCounts();
if (before != after) throw new InvalidDataException("User, enrollment, certificate or license-link counts changed during roster import.");
File.WriteAllText(reportPath, JsonSerializer.Serialize(new { Backup = backupPath, Complete = true, UnrelatedCountsUnchanged = true, IdempotentRepeat = true, Results = results }, json));
Console.WriteLine("Import complete; repeat safety and unchanged unrelated table counts verified.");
return 0;

async Task<string> ProtectedCounts() => string.Join("|", await db.UserAccounts.CountAsync(),
    await db.Enrollments.CountAsync(), await db.CompletionCertificates.CountAsync(), await db.UserRegulatoryLicenseLinks.CountAsync(),
    await db.RosterAccountRequests.CountAsync());
