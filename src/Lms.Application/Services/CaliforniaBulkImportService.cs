using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ExcelDataReader;
using Lms.Application.Data;

namespace Lms.Application.Services;

public sealed record CaliforniaBrokerAssociateRelationship(string AssociateNumber, string ResponsibleNumber,
    string ResponsibleType, string ResponsibleName, DateOnly? StartedAt);

public sealed class CaliforniaBulkImportService(ApplicationDbContext db)
{
    public const string ParserVersion = "ca-dre-bulk-v1";
    public const string SourcePage = "https://dre.ca.gov/Licensees/ExamineeLicenseeListDataFiles.html";
    public static IReadOnlyList<FloridaBulkFileDefinition> Definitions { get; } =
    [
        new("CurrList.zip", "CurrList.zip", 25) { SourceUrl = "https://secure.dre.ca.gov/datafile/CurrList.zip", Encoding = "ZIP containing Windows-1252 CSV (BOM overrides)" },
        new("broker_associates_list.xls", "broker_associates_list.xls", 18)
            { SourceUrl = "https://secure.dre.ca.gov/datafile/broker_associates_list.xls", Encoding = "BIFF binary Excel workbook" }
    ];
    private static readonly string[] AssociateHeaders =
    [
        "rb_license_id", "ba_license_id", "affiliation_start_date", "rb_name", "rb_addresss_line_1",
        "rb_addresss_line_2", "rb_city", "rb_state", "rb_zip_code", "rb_license_type", "rb_expiration_date",
        "ba_name", "ba_addresss_line_1", "ba_addresss_line_2", "ba_city", "ba_state", "ba_zip_code", "ba_expiration_date"
    ];

    public static IReadOnlyList<CaliforniaBrokerAssociateRelationship> ReadAssociateRelationships(
        string archive, IReadOnlySet<string> licenseNumbers, CancellationToken ct = default) =>
        CaliforniaDreCaptureSeed.ReadAssociateRelationships(
            FloridaBulkImportService.ArchiveFile(archive, "broker-associates.csv"), licenseNumbers, ct);

    public static long CheckFile(string path, string name, CancellationToken ct = default, Action<long>? progress = null)
    {
        if (name == "CurrList.zip")
        {
            using var zip = ZipFile.OpenRead(path);
            if (zip.Entries.Count != 1 || zip.Entries[0].FullName != "CurrList.csv"
                || zip.Entries[0].Length is <= 0 or > 1024L * 1024 * 1024)
                throw new InvalidDataException("California current-list ZIP must contain exactly one bounded CurrList.csv.");
            return CaliforniaDreCaptureSeed.ValidateCurrentList(path, ct, progress);
        }
        if (name != "broker_associates_list.xls") throw new InvalidDataException("Unknown California source file.");
        var derived = ConvertAssociates(path, ct);
        return CaliforniaDreCaptureSeed.ValidateAssociates(derived, ct, progress);
    }

    public static string ConvertAssociates(string path, CancellationToken ct = default)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        using var stream = File.OpenRead(path);
        using var reader = ExcelReaderFactory.CreateBinaryReader(stream);
        if (reader.ResultsCount != 1 || !reader.Read() || reader.FieldCount != AssociateHeaders.Length)
            throw new InvalidDataException("California broker-associate workbook layout changed.");
        for (var i = 0; i < AssociateHeaders.Length; i++)
            if (!string.Equals(reader.GetValue(i)?.ToString()?.TrimEnd('\0').Trim(), AssociateHeaders[i], StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("California broker-associate workbook header changed.");
        var derived = FloridaBulkImportService.ArchiveFile(Path.GetDirectoryName(path)!, "broker-associates.csv");
        var pending = FloridaBulkImportService.ArchiveFile(Path.GetDirectoryName(path)!, "broker-associates.csv.pending");
        try
        {
            using (var writer = new StreamWriter(pending, false, new UTF8Encoding(false)))
            {
                writer.WriteLine(string.Join(",", AssociateHeaders));
                while (reader.Read())
                {
                    ct.ThrowIfCancellationRequested();
                    var fields = new string[AssociateHeaders.Length];
                    var any = false;
                    for (var i = 0; i < fields.Length; i++)
                    {
                        var value = reader.GetValue(i);
                        any |= value is not null;
                        var text = value switch
                        {
                            null => "",
                            DateTime date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                            IFormattable formatted => formatted.ToString(null, CultureInfo.InvariantCulture),
                            _ => value.ToString() ?? ""
                        };
                        text = text.TrimEnd('\0').Trim();
                        if (i is 0 or 1 && text.Length > 0)
                        {
                            if (!text.All(char.IsAsciiDigit) || text.Length > 8)
                                throw new InvalidDataException($"Invalid DRE workbook license identity in column {AssociateHeaders[i]}: '{text}'.");
                            text = text.PadLeft(8, '0');
                        }
                        fields[i] = "\"" + text.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
                    }
                    if (any) writer.WriteLine(string.Join(",", fields));
                }
            }
            File.Move(pending, derived, true);
            return derived;
        }
        finally
        {
            if (File.Exists(pending)) File.Delete(pending);
        }
    }

    public static async Task<IReadOnlyList<FloridaBulkManifestEntry>> ValidateArchiveAsync(string root,
        CancellationToken ct = default, Action<string, long, long>? progress = null)
    {
        await using var manifest = File.OpenRead(FloridaBulkImportService.ArchiveFile(root, "manifest.json"));
        var entries = await JsonSerializer.DeserializeAsync<List<FloridaBulkManifestEntry>>(manifest, cancellationToken: ct)
            ?? throw new InvalidDataException("Missing California acquisition metadata.");
        if (entries.Count != Definitions.Count || entries.Select(e => e.File).Distinct(StringComparer.Ordinal).Count() != Definitions.Count)
            throw new InvalidDataException("California archive requires both official current-list and broker-associate sources.");
        foreach (var definition in Definitions)
        {
            var entry = entries.SingleOrDefault(e => e.File == definition.RelativePath)
                ?? throw new InvalidDataException("California official source file is missing.");
            if (entry.SourceUrl != definition.Url || entry.Rows <= 0 || entry.VerifiedAtUtc == default
                || entry.Columns is null || entry.Columns.Count != 1
                || !entry.Columns.TryGetValue(definition.Columns.ToString(CultureInfo.InvariantCulture), out var rows) || rows != entry.Rows)
                throw new InvalidDataException("Invalid California source provenance or row layout.");
            var path = FloridaBulkImportService.ArchiveFile(root, entry.File);
            await using (var file = File.OpenRead(path))
                if (file.Length != entry.Bytes || !Convert.ToHexString(await SHA256.HashDataAsync(file, ct)).Equals(entry.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("California archive size/hash mismatch.");
            var parsed = CheckFile(path, definition.Name, ct, count => progress?.Invoke(definition.Name, count, entry.Bytes));
            if (parsed != entry.Rows) throw new InvalidDataException("California archive row count mismatch.");
            progress?.Invoke(definition.Name, parsed, entry.Bytes);
        }
        if (entries.All(e => e.SourceLastModifiedUtc.HasValue)
            && entries.Select(e => e.SourceLastModifiedUtc!.Value.UtcDateTime.Date).Distinct().Count() != 1)
            throw new InvalidDataException("DRE source files have different publication days; retry after both daily sources update.");
        return entries;
    }

    public async Task<Guid> ImportArchiveAsync(string root, CancellationToken ct = default,
        Action<string, long, long>? progress = null, Action<long>? staged = null)
    {
        var entries = await ValidateArchiveAsync(root, ct, progress);
        var identity = ParserVersion + string.Join("", Definitions.Select(d =>
            entries.Single(e => e.File == d.RelativePath).Sha256.ToUpperInvariant()));
        var id = new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(identity)).AsSpan(0, 16));
        return await CaliforniaDreCaptureSeed.ImportFilesAsync(db,
            FloridaBulkImportService.ArchiveFile(root, "CurrList.zip"),
            FloridaBulkImportService.ArchiveFile(root, "broker-associates.csv"), id, ParserVersion,
            entries.Max(e => e.VerifiedAtUtc).UtcDateTime, ct, staged, officialBulk: true);
    }
}
