using System.Data.Common;
using System.Text;
using Microsoft.EntityFrameworkCore;

namespace Lms.Application.Data;

/// <summary>
/// SQLite FTS5 word index over RegulatoryLicensees.Name so roster name search avoids full scans.
/// Each row also carries a Scope token for its snapshot, so FTS narrows to one roster before touching the licensee table.
/// Kept in sync by triggers, so raw bulk inserts and cascade deletes are covered.
/// </summary>
/// <remarks>
/// Rows are keyed by the licensee table's implicit rowid. A manual VACUUM can renumber those; afterwards drop
/// RegulatoryLicenseeNameSearch and restart so it is rebuilt.
/// </remarks>
public static class RegulatoryLicenseeNameIndex
{
    public const string TableName = "RegulatoryLicenseeNameSearch";
    private const string ScopeSql = "'S' || replace(upper({0}.SnapshotId), '-', '')";

    private static readonly string[] Triggers =
    [
        "RegulatoryLicensees_NameSearch_AfterInsert", "RegulatoryLicensees_NameSearch_AfterDelete",
        "RegulatoryLicensees_NameSearch_AfterUpdate"
    ];

    private static readonly string[] SchemaStatements =
    [
        $"""
        CREATE VIRTUAL TABLE IF NOT EXISTS "{TableName}" USING fts5(
            Name, Scope, tokenize='unicode61 remove_diacritics 2');
        """,
        $"""
        CREATE TRIGGER IF NOT EXISTS "{Triggers[0]}" AFTER INSERT ON "RegulatoryLicensees" BEGIN
            INSERT INTO "{TableName}"(rowid, Name, Scope) VALUES (new.rowid, new.Name, {string.Format(ScopeSql, "new")});
        END;
        """,
        $"""
        CREATE TRIGGER IF NOT EXISTS "{Triggers[1]}" AFTER DELETE ON "RegulatoryLicensees" BEGIN
            DELETE FROM "{TableName}" WHERE rowid = old.rowid;
        END;
        """,
        $"""
        CREATE TRIGGER IF NOT EXISTS "{Triggers[2]}" AFTER UPDATE OF Name, SnapshotId ON "RegulatoryLicensees" BEGIN
            DELETE FROM "{TableName}" WHERE rowid = old.rowid;
            INSERT INTO "{TableName}"(rowid, Name, Scope) VALUES (new.rowid, new.Name, {string.Format(ScopeSql, "new")});
        END;
        """
    ];

    /// <summary>Creates the index and triggers if missing; backfills existing rows once. Returns true when it backfilled.</summary>
    public static async Task<bool> EnsureAsync(ApplicationDbContext db, CancellationToken ct = default)
    {
        if (!IsSqlite(db)) return false;
        var connection = db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open) await connection.OpenAsync(ct);
        if (!await TableExistsAsync(connection, "RegulatoryLicensees", ct)) return false;
        var definition = await TableSqlAsync(connection, TableName, ct);
        if (definition is not null && definition.Contains("Scope", StringComparison.Ordinal)) return false;
        await using var transaction = await connection.BeginTransactionAsync(ct);
        foreach (var trigger in Triggers)
            await ExecuteAsync(connection, transaction, $"DROP TRIGGER IF EXISTS \"{trigger}\";", ct);
        await ExecuteAsync(connection, transaction, $"DROP TABLE IF EXISTS \"{TableName}\";", ct);
        foreach (var statement in SchemaStatements)
            await ExecuteAsync(connection, transaction, statement, ct);
        await ExecuteAsync(connection, transaction, $"""
            INSERT INTO "{TableName}"(rowid, Name, Scope)
            SELECT l.rowid, l.Name, {string.Format(ScopeSql, "l")} FROM "RegulatoryLicensees" AS l;
            """, ct);
        await transaction.CommitAsync(ct);
        return true;
    }
    public static async Task<bool> ExistsAsync(ApplicationDbContext db, CancellationToken ct = default)
    {
        if (!IsSqlite(db)) return false;
        var connection = db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open) await connection.OpenAsync(ct);
        var definition = await TableSqlAsync(connection, TableName, ct);
        return definition is not null && definition.Contains("Scope", StringComparison.Ordinal);
    }

    /// <summary>
    /// Builds an FTS5 query requiring every word of the input as a name-word prefix within one snapshot
    /// ("nona lis" → Name:"NONA"* Name:"LIS"* Scope:"S…"). Returns null when the input has no letters or digits.
    /// </summary>
    public static string? BuildMatchExpression(string name, Guid snapshotId)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();
        foreach (var character in name)
        {
            if (char.IsLetterOrDigit(character)) current.Append(char.ToUpperInvariant(character));
            else if (current.Length > 0) { tokens.Add(current.ToString()); current.Clear(); }
        }
        if (current.Length > 0) tokens.Add(current.ToString());
        if (tokens.Count == 0) return null;
        var scope = "S" + snapshotId.ToString("N").ToUpperInvariant();
        return string.Join(" ", tokens.Distinct().Select(token => $"Name:\"{token}\"*")) + $" Scope:\"{scope}\"";
    }

    private static bool IsSqlite(ApplicationDbContext db) =>
        db.Database.ProviderName?.EndsWith(".Sqlite", StringComparison.Ordinal) == true;

    private static async Task<string?> TableSqlAsync(DbConnection connection, string table, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT sql FROM sqlite_master WHERE type = 'table' AND name = @name;";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "@name";
        parameter.Value = table;
        command.Parameters.Add(parameter);
        return await command.ExecuteScalarAsync(ct) as string;
    }

    private static async Task<bool> TableExistsAsync(DbConnection connection, string table, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE name = @name;";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "@name";
        parameter.Value = table;
        command.Parameters.Add(parameter);
        return Convert.ToInt64(await command.ExecuteScalarAsync(ct)) > 0;
    }

    private static async Task ExecuteAsync(DbConnection connection, DbTransaction transaction, string sql, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = 0;
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(ct);
    }
}
