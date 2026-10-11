using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;
using Lms.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Lms.Application.Data;

public static class NevadaNredCaptureSeed
{
    private const string State = "Nevada";
    private const string SourceName = "Nevada NRED monthly active real-estate licensee lists";
    private const string SourceUrl = "https://red.nv.gov/";
    private const string ParserVersion = "nred-active-licensee-lists-v1-2026-09";
    private static readonly DateTime RetrievedAtUtc = new(2026, 9, 26, 0, 0, 0, DateTimeKind.Utc);
    private static readonly (string FileName, string LicenseType)[] SourceFiles =
    [
        ("NRED-ACTIVE-BROKERS.xlsx", "Broker"),
        ("NRED-ACTIVE-BROKER-SALESPERSONS.xlsx", "Broker-Salesperson"),
        ("NRED-ACTIVE-SALESPERSONS.xlsx", "Salesperson")
    ];

    private static readonly XNamespace SpreadsheetNamespace =
        "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    public static async Task SeedAsync(ApplicationDbContext dbContext, string? workspaceRoot = null)
    {
        ArgumentNullException.ThrowIfNull(dbContext);

        var root = CaptureSeedRoot.Resolve(workspaceRoot);
        if (root is null)
        {
            return;
        }

        var sourceDirectory = Path.Combine(root, "docs", "NEVADA", "XLXS");
        if (!Directory.Exists(sourceDirectory))
        {
            return;
        }

        var sourcePaths = SourceFiles
            .Select(source => (source.FileName, source.LicenseType, Path: Path.Combine(sourceDirectory, source.FileName)))
            .ToArray();
        var missingSource = sourcePaths.FirstOrDefault(source => !File.Exists(source.Path));
        if (missingSource.Path is not null)
        {
            throw new FileNotFoundException(
                $"The dated Nevada NRED active licensee workbook '{missingSource.FileName}' is required.",
                missingSource.Path);
        }

        if (await dbContext.RegulatoryDataSnapshots.AnyAsync(snapshot =>
                snapshot.State == State
                && snapshot.SourceUrl == SourceUrl
                && snapshot.RetrievedAtUtc == RetrievedAtUtc))
        {
            return;
        }

        var snapshot = new RegulatoryDataSnapshot
        {
            State = State,
            SourceName = SourceName,
            SourceUrl = SourceUrl,
            ParserVersion = ParserVersion,
            RetrievedAtUtc = RetrievedAtUtc,
            Status = RegulatorySnapshotStatuses.PendingReview
        };
        var licensees = new Dictionary<string, RegulatoryLicensee>(StringComparer.OrdinalIgnoreCase);

        foreach (var (fileName, licenseType, sourcePath) in sourcePaths)
        {
            foreach (var row in ReadRecords(sourcePath))
            {
                var licensee = new RegulatoryLicensee
                {
                    SnapshotId = snapshot.Id,
                    State = State,
                    LicenseNumber = row.LicenseNumber,
                    Name = row.Name,
                    LicenseType = licenseType,
                    LicenseLevel = licenseType,
                    LicenseStatus = "Active",
                    LicenseIssuedDate = row.FirstIssuedDate,
                    OriginalLicenseDate = row.FirstIssuedDate,
                    LicenseRenewalDate = row.ExpirationDate,
                    RelatedLicenseName = row.Company,
                    RelatedLicenseType = row.Company is null ? null : "Company"
                };
                if (!licensees.TryGetValue(licensee.LicenseNumber, out var existingLicensee))
                {
                    licensees.Add(licensee.LicenseNumber, licensee);
                    continue;
                }

                if (existingLicensee.LicenseType != licensee.LicenseType)
                {
                    throw new InvalidDataException(
                        $"The Nevada NRED active lists assign license number '{licensee.LicenseNumber}' to multiple license types, including '{fileName}'.");
                }

                var renewalDateComparison = Nullable.Compare(
                    licensee.LicenseRenewalDate,
                    existingLicensee.LicenseRenewalDate);
                if (renewalDateComparison > 0)
                {
                    licensees[licensee.LicenseNumber] = licensee;
                    continue;
                }

                if (renewalDateComparison < 0)
                {
                    continue;
                }

                if (licensee.Name == existingLicensee.Name
                    && licensee.LicenseIssuedDate == existingLicensee.LicenseIssuedDate
                    && licensee.RelatedLicenseName == existingLicensee.RelatedLicenseName)
                {
                    continue;
                }

                throw new InvalidDataException(
                    $"The Nevada NRED active lists have conflicting rows for license number '{licensee.LicenseNumber}' with the same renewal date.");
            }
        }

        if (licensees.Count == 0)
        {
            throw new InvalidDataException("The Nevada NRED active licensee workbooks contained no licensees.");
        }

        dbContext.RegulatoryDataSnapshots.Add(snapshot);
        dbContext.RegulatoryLicensees.AddRange(licensees.Values);
        await dbContext.SaveChangesAsync();
    }

    private static IEnumerable<NevadaLicenseRecord> ReadRecords(string sourcePath)
    {
        using var archive = ZipFile.OpenRead(sourcePath);
        var sharedStrings = ReadSharedStrings(archive, sourcePath);
        var sheetEntry = archive.GetEntry("xl/worksheets/sheet1.xml")
            ?? throw new InvalidDataException($"The Nevada NRED workbook '{Path.GetFileName(sourcePath)}' has no first worksheet.");
        using var sheetStream = sheetEntry.Open();
        var sheet = XDocument.Load(sheetStream);
        var rows = sheet.Root?
            .Element(SpreadsheetNamespace + "sheetData")?
            .Elements(SpreadsheetNamespace + "row")
            .ToArray();
        if (rows is null || rows.Length < 2)
        {
            throw new InvalidDataException($"The Nevada NRED workbook '{Path.GetFileName(sourcePath)}' has no data rows.");
        }

        var header = ReadRow(rows[0], sharedStrings);
        var columns = header
            .Select((value, index) => (Name: value.Trim(), Index: index))
            .Where(column => column.Name.Length > 0)
            .ToDictionary(column => column.Name, column => column.Index, StringComparer.OrdinalIgnoreCase);
        var requiredColumns = new[]
        {
            "License No", "Expiration Date", "First Issuance Date", "Name", "Company"
        };
        var missingColumns = requiredColumns.Where(column => !columns.ContainsKey(column)).ToArray();
        if (missingColumns.Length > 0)
        {
            throw new InvalidDataException(
                $"The Nevada NRED workbook '{Path.GetFileName(sourcePath)}' is missing columns: {string.Join(", ", missingColumns)}.");
        }

        foreach (var row in rows.Skip(1))
        {
            var values = ReadRow(row, sharedStrings);
            if (values.All(string.IsNullOrWhiteSpace))
            {
                continue;
            }

            string GetValue(string column)
            {
                var index = columns[column];
                return index < values.Length ? values[index].Trim() : string.Empty;
            }

            var licenseNumber = GetValue("License No");
            var name = GetValue("Name");
            if (licenseNumber.Length == 0 || name.Length == 0)
            {
                throw new InvalidDataException(
                    $"The Nevada NRED workbook '{Path.GetFileName(sourcePath)}' has a row without a license number or name at row {row.Attribute("r")?.Value ?? "?"}.");
            }

            yield return new NevadaLicenseRecord(
                licenseNumber,
                name,
                GetValue("Company") is { Length: > 0 } company ? company : null,
                ParseExcelDate(GetValue("First Issuance Date"), "First Issuance Date", sourcePath, row),
                ParseExcelDate(GetValue("Expiration Date"), "Expiration Date", sourcePath, row));
        }
    }

    private static string[] ReadRow(XElement row, IReadOnlyList<string> sharedStrings)
    {
        var cells = new Dictionary<int, string>();
        foreach (var cell in row.Elements(SpreadsheetNamespace + "c"))
        {
            var reference = cell.Attribute("r")?.Value
                ?? throw new InvalidDataException("A cell in a Nevada NRED workbook has no reference.");
            var columnIndex = GetColumnIndex(reference);
            var cellType = cell.Attribute("t")?.Value;
            string value;
            if (cellType == "s")
            {
                var stringIndexText = cell.Element(SpreadsheetNamespace + "v")?.Value;
                if (!int.TryParse(stringIndexText, NumberStyles.None, CultureInfo.InvariantCulture, out var stringIndex)
                    || stringIndex < 0
                    || stringIndex >= sharedStrings.Count)
                {
                    throw new InvalidDataException($"The Nevada NRED workbook has an invalid shared-string index in cell '{reference}'.");
                }

                value = sharedStrings[stringIndex];
            }
            else if (cellType == "inlineStr")
            {
                value = string.Concat(cell.Descendants(SpreadsheetNamespace + "t").Select(text => text.Value));
            }
            else
            {
                value = cell.Element(SpreadsheetNamespace + "v")?.Value ?? string.Empty;
            }

            cells[columnIndex] = value;
        }

        if (cells.Count == 0)
        {
            return [];
        }

        var values = Enumerable.Repeat(string.Empty, cells.Keys.Max() + 1).ToArray();
        foreach (var (index, value) in cells)
        {
            values[index] = value;
        }

        return values;
    }

    private static List<string> ReadSharedStrings(ZipArchive archive, string sourcePath)
    {
        var entry = archive.GetEntry("xl/sharedStrings.xml")
            ?? throw new InvalidDataException($"The Nevada NRED workbook '{Path.GetFileName(sourcePath)}' has no shared strings table.");
        using var stream = entry.Open();
        var document = XDocument.Load(stream);
        return document
            .Root?
            .Elements(SpreadsheetNamespace + "si")
            .Select(item => string.Concat(item.Descendants(SpreadsheetNamespace + "t").Select(text => text.Value)))
            .ToList()
            ?? throw new InvalidDataException($"The Nevada NRED workbook '{Path.GetFileName(sourcePath)}' has an invalid shared strings table.");
    }

    private static int GetColumnIndex(string cellReference)
    {
        var index = 0;
        var foundLetters = false;
        foreach (var character in cellReference)
        {
            var upperCharacter = char.ToUpperInvariant(character);
            if (upperCharacter is < 'A' or > 'Z')
            {
                break;
            }

            foundLetters = true;
            index = checked(index * 26 + upperCharacter - 'A' + 1);
        }

        if (!foundLetters)
        {
            throw new InvalidDataException($"The Nevada NRED workbook has an invalid cell reference '{cellReference}'.");
        }

        return index - 1;
    }

    private static DateOnly? ParseExcelDate(string value, string column, string sourcePath, XElement row)
    {
        if (value.Length == 0 || string.Equals(value, "N/A", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var serial))
        {
            try
            {
                return DateOnly.FromDateTime(DateTime.FromOADate(serial));
            }
            catch (ArgumentException exception)
            {
                throw new InvalidDataException(
                    $"The Nevada NRED workbook '{Path.GetFileName(sourcePath)}' has an invalid {column} value '{value}' at row {row.Attribute("r")?.Value ?? "?"}.",
                    exception);
            }
        }

        if (DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return date;
        }

        throw new InvalidDataException(
            $"The Nevada NRED workbook '{Path.GetFileName(sourcePath)}' has an invalid {column} value '{value}' at row {row.Attribute("r")?.Value ?? "?"}.");
    }

    private sealed record NevadaLicenseRecord(
        string LicenseNumber,
        string Name,
        string? Company,
        DateOnly? FirstIssuedDate,
        DateOnly? ExpirationDate);
}
