using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;
using Lms.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Lms.Application.Data;

public static class NewHampshireOplcCaptureSeed
{
    private const string State = "New Hampshire";
    private const string SourceName = "New Hampshire OPLC Real Estate Commission licensee roster";
    private const string SourceUrl = "https://www.oplc.nh.gov/list-oplc-licensees-and-their-license-types";
    private const string FileName = "real-estate-verification09212026.xlsx";
    private const string ParserVersion = "nh-oplc-real-estate-roster-v1-2026-09";
    private static readonly DateTime RetrievedAtUtc = new(2026, 9, 21, 0, 0, 0, DateTimeKind.Utc);
    private static readonly HashSet<string> IndividualLicenseTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Associate Broker",
        "Managing Broker",
        "Principal Broker",
        "Salesperson"
    };
    private static readonly HashSet<string> EntityLicenseTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Firm",
        "Firm Branch",
        "Trade Name"
    };
    private static readonly HashSet<string> Professions = new(StringComparer.OrdinalIgnoreCase)
    {
        "OPLC Real Estate Commission",
        "Real Estate"
    };
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

        var sourceDirectory = Path.Combine(root, "docs", "NEW HAMPSHIRE", "XLXS");
        if (!Directory.Exists(sourceDirectory))
        {
            return;
        }

        var sourcePath = Path.Combine(sourceDirectory, FileName);
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("The dated New Hampshire OPLC real-estate roster is required.", sourcePath);
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
        var licensees = new Dictionary<string, NewHampshireLicenseeBuilder>(StringComparer.OrdinalIgnoreCase);
        var firms = new Dictionary<string, NewHampshireFirmBuilder>(StringComparer.OrdinalIgnoreCase);
        var activeRowsMissingLicenseNumber = 0;
        var activeRowsMissingName = 0;

        foreach (var row in ReadRecords(sourcePath))
        {
            if (!row.LicenseStatus.StartsWith("Active", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!IndividualLicenseTypes.Contains(row.LicenseType)
                && !EntityLicenseTypes.Contains(row.LicenseType))
            {
                continue;
            }

            if (row.LicenseNumber.Length == 0)
            {
                if (IndividualLicenseTypes.Contains(row.LicenseType))
                {
                    activeRowsMissingLicenseNumber++;
                    continue;
                }

                throw new InvalidDataException(
                    $"The New Hampshire OPLC workbook has an active {row.LicenseType} record without a license number at row {row.RowNumber}.");
            }

            if (IndividualLicenseTypes.Contains(row.LicenseType))
            {
                if (row.Name.Length == 0)
                {
                    activeRowsMissingName++;
                    continue;
                }

                var licensee = new RegulatoryLicensee
                {
                    SnapshotId = snapshot.Id,
                    State = State,
                    LicenseNumber = row.LicenseNumber,
                    Name = row.Name,
                    LicenseType = row.LicenseType,
                    LicenseLevel = row.LicenseType,
                    LicenseStatus = row.LicenseStatus,
                    LicenseIssuedDate = row.IssueDate,
                    OriginalLicenseDate = row.IssueDate,
                    LicenseRenewalDate = row.ExpirationDate,
                    IsRestricted = row.LicenseStatus.Contains("Restricted", StringComparison.OrdinalIgnoreCase)
                };

                if (!licensees.TryGetValue(row.LicenseNumber, out var builder))
                {
                    builder = new NewHampshireLicenseeBuilder(licensee.LicenseNumber, licensee.Name);
                    licensees.Add(row.LicenseNumber, builder);
                }

                builder.Add(licensee, row.RowNumber);

                continue;
            }

            if (row.Name.Length == 0)
            {
                throw new InvalidDataException(
                    $"The New Hampshire OPLC workbook has an active {row.LicenseType} record without a name at row {row.RowNumber}.");
            }

            var firm = new RegulatoryFirm
            {
                SnapshotId = snapshot.Id,
                State = State,
                LicenseNumber = row.LicenseNumber,
                LegalName = row.Name,
                LicenseStatus = row.LicenseStatus,
                LicenseIssuedDate = row.IssueDate,
                LicenseRenewalDate = row.ExpirationDate
            };

            if (!firms.TryGetValue(row.LicenseNumber, out var firmBuilder))
            {
                firms.Add(row.LicenseNumber, new NewHampshireFirmBuilder(firm, row.LicenseType));
            }
            else
            {
                firmBuilder.Merge(firm, row.LicenseType, row.RowNumber);
            }
        }

        if (licensees.Count == 0 && firms.Count == 0)
        {
            throw new InvalidDataException("The New Hampshire OPLC roster contained no active real-estate licenses.");
        }

        if (activeRowsMissingLicenseNumber > 0 || activeRowsMissingName > 0)
        {
            snapshot.SourceName = $"{SourceName} ({activeRowsMissingLicenseNumber} missing license number; {activeRowsMissingName} missing name)";
        }

        dbContext.RegulatoryDataSnapshots.Add(snapshot);
        dbContext.RegulatoryLicensees.AddRange(licensees.Values.Select(builder => builder.Build()));
        dbContext.RegulatoryFirms.AddRange(firms.Values.Select(builder => builder.Firm));
        await dbContext.SaveChangesAsync();
    }

    private static IEnumerable<NewHampshireLicenseRecord> ReadRecords(string sourcePath)
    {
        using var archive = ZipFile.OpenRead(sourcePath);
        var sharedStrings = ReadSharedStrings(archive, sourcePath);
        var sheetEntry = archive.GetEntry("xl/worksheets/sheet1.xml")
            ?? throw new InvalidDataException($"The New Hampshire OPLC workbook '{Path.GetFileName(sourcePath)}' has no first worksheet.");
        using var sheetStream = sheetEntry.Open();
        var sheet = XDocument.Load(sheetStream);
        var rows = sheet.Root?
            .Element(SpreadsheetNamespace + "sheetData")?
            .Elements(SpreadsheetNamespace + "row")
            .ToArray();
        if (rows is null || rows.Length < 2)
        {
            throw new InvalidDataException($"The New Hampshire OPLC workbook '{Path.GetFileName(sourcePath)}' has no data rows.");
        }

        var header = ReadRow(rows[0], sharedStrings, sourcePath);
        var columns = header
            .Select((value, index) => (Name: value.Trim(), Index: index))
            .Where(column => column.Name.Length > 0)
            .ToDictionary(column => column.Name, column => column.Index, StringComparer.OrdinalIgnoreCase);
        var requiredColumns = new[]
        {
            "Profession", "License Type", "License Number", "Issue Date", "Expiration Date", "License Status", "License Holder Name"
        };
        var missingColumns = requiredColumns.Where(column => !columns.ContainsKey(column)).ToArray();
        if (missingColumns.Length > 0)
        {
            throw new InvalidDataException(
                $"The New Hampshire OPLC workbook '{Path.GetFileName(sourcePath)}' is missing columns: {string.Join(", ", missingColumns)}.");
        }

        foreach (var row in rows.Skip(1))
        {
            var values = ReadRow(row, sharedStrings, sourcePath);
            if (values.All(string.IsNullOrWhiteSpace))
            {
                continue;
            }

            string GetValue(string column)
            {
                var index = columns[column];
                return index < values.Length ? values[index].Trim() : string.Empty;
            }

            if (!Professions.Contains(GetValue("Profession")))
            {
                throw new InvalidDataException(
                    $"The New Hampshire OPLC workbook contains a row from an unexpected profession at row {row.Attribute("r")?.Value ?? "?"}.");
            }

            var rowNumber = row.Attribute("r")?.Value ?? "?";
            yield return new NewHampshireLicenseRecord(
                GetValue("License Type"),
                GetValue("License Number"),
                GetValue("License Status"),
                GetValue("License Holder Name"),
                ParseExcelDate(GetValue("Issue Date"), "Issue Date", sourcePath, rowNumber),
                ParseExcelDate(GetValue("Expiration Date"), "Expiration Date", sourcePath, rowNumber),
                rowNumber);
        }
    }

    private static string[] ReadRow(XElement row, IReadOnlyList<string> sharedStrings, string sourcePath)
    {
        var cells = new Dictionary<int, string>();
        foreach (var cell in row.Elements(SpreadsheetNamespace + "c"))
        {
            var reference = cell.Attribute("r")?.Value
                ?? throw new InvalidDataException($"A cell in the New Hampshire OPLC workbook '{Path.GetFileName(sourcePath)}' has no reference.");
            var columnIndex = GetColumnIndex(reference, sourcePath);
            var cellType = cell.Attribute("t")?.Value;
            string value;
            if (cellType == "s")
            {
                var stringIndexText = cell.Element(SpreadsheetNamespace + "v")?.Value;
                if (!int.TryParse(stringIndexText, NumberStyles.None, CultureInfo.InvariantCulture, out var stringIndex)
                    || stringIndex < 0
                    || stringIndex >= sharedStrings.Count)
                {
                    throw new InvalidDataException(
                        $"The New Hampshire OPLC workbook has an invalid shared-string index in cell '{reference}'.");
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
            ?? throw new InvalidDataException(
                $"The New Hampshire OPLC workbook '{Path.GetFileName(sourcePath)}' has no shared strings table.");
        using var stream = entry.Open();
        var document = XDocument.Load(stream);
        return document
            .Root?
            .Elements(SpreadsheetNamespace + "si")
            .Select(item => string.Concat(item.Descendants(SpreadsheetNamespace + "t").Select(text => text.Value)))
            .ToList()
            ?? throw new InvalidDataException(
                $"The New Hampshire OPLC workbook '{Path.GetFileName(sourcePath)}' has an invalid shared strings table.");
    }

    private static int GetColumnIndex(string cellReference, string sourcePath)
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
            throw new InvalidDataException(
                $"The New Hampshire OPLC workbook has an invalid cell reference '{cellReference}' in '{Path.GetFileName(sourcePath)}'.");
        }

        return index - 1;
    }

    private static DateOnly? ParseExcelDate(string value, string column, string sourcePath, string rowNumber)
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
                    $"The New Hampshire OPLC workbook '{Path.GetFileName(sourcePath)}' has an invalid {column} value '{value}' at row {rowNumber}.",
                    exception);
            }
        }

        if (DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return date;
        }

        throw new InvalidDataException(
            $"The New Hampshire OPLC workbook '{Path.GetFileName(sourcePath)}' has an invalid {column} value '{value}' at row {rowNumber}.");
    }

    private sealed record NewHampshireLicenseRecord(
        string LicenseType,
        string LicenseNumber,
        string LicenseStatus,
        string Name,
        DateOnly? IssueDate,
        DateOnly? ExpirationDate,
        string RowNumber);

    private sealed class NewHampshireLicenseeBuilder(string licenseNumber, string name)
    {
        private static readonly string[] LicenseLevelPriority =
        [
            "Principal Broker",
            "Managing Broker",
            "Associate Broker",
            "Salesperson"
        ];

        private readonly Dictionary<string, RegulatoryLicensee> _licensesByType = new(StringComparer.OrdinalIgnoreCase);

        public void Add(RegulatoryLicensee candidate, string rowNumber)
        {
            if (!string.Equals(name, candidate.Name, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"The New Hampshire OPLC roster has conflicting current identities for license number '{candidate.LicenseNumber}' at row {rowNumber}.");
            }

            if (!_licensesByType.TryGetValue(candidate.LicenseType!, out var existing))
            {
                _licensesByType.Add(candidate.LicenseType!, candidate);
                return;
            }

            var expirationComparison = Nullable.Compare(candidate.LicenseRenewalDate, existing.LicenseRenewalDate);
            if (expirationComparison > 0)
            {
                _licensesByType[candidate.LicenseType!] = candidate;
                return;
            }

            var issueDateComparison = Nullable.Compare(candidate.LicenseIssuedDate, existing.LicenseIssuedDate);
            if (expirationComparison == 0 && issueDateComparison > 0)
            {
                _licensesByType[candidate.LicenseType!] = candidate;
                return;
            }

            if (expirationComparison == 0
                && issueDateComparison == 0
                && !string.Equals(candidate.LicenseStatus, existing.LicenseStatus, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"The New Hampshire OPLC roster has conflicting current rows for license number '{candidate.LicenseNumber}' with the same issue and expiration dates at row {rowNumber}.");
            }
        }

        public RegulatoryLicensee Build()
        {
            var categories = LicenseLevelPriority
                .Where(_licensesByType.ContainsKey)
                .ToArray();
            var latestLicense = _licensesByType.Values
                .OrderByDescending(licensee => licensee.LicenseRenewalDate)
                .First();
            var earliestIssuedDate = _licensesByType.Values
                .Where(licensee => licensee.LicenseIssuedDate is not null)
                .Min(licensee => licensee.LicenseIssuedDate);
            var earliestRenewalDate = _licensesByType.Values
                .Where(licensee => licensee.LicenseRenewalDate is not null)
                .Min(licensee => licensee.LicenseRenewalDate);
            var licenseType = string.Join(", ", categories);
            if (licenseType.Length > 40)
            {
                throw new InvalidDataException(
                    $"The New Hampshire OPLC roster has too many license types to store for license number '{licenseNumber}'.");
            }

            return new RegulatoryLicensee
            {
                SnapshotId = latestLicense.SnapshotId,
                State = latestLicense.State,
                LicenseNumber = licenseNumber,
                Name = name,
                LicenseType = licenseType,
                LicenseLevel = categories[0],
                LicenseStatus = string.Join(
                    ", ",
                    _licensesByType.Values
                        .Select(licensee => licensee.LicenseStatus!)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(status => status, StringComparer.OrdinalIgnoreCase)),
                LicenseIssuedDate = earliestIssuedDate,
                OriginalLicenseDate = earliestIssuedDate,
                LicenseRenewalDate = earliestRenewalDate,
                IsRestricted = _licensesByType.Values.Any(licensee => licensee.IsRestricted)
            };
        }
    }

    private sealed class NewHampshireFirmBuilder(RegulatoryFirm firm, string licenseType)
    {
        public RegulatoryFirm Firm { get; private set; } = firm;
        private string LicenseType { get; } = licenseType;

        public void Merge(RegulatoryFirm candidate, string candidateLicenseType, string rowNumber)
        {
            if (!string.Equals(Firm.LegalName, candidate.LegalName, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(LicenseType, candidateLicenseType, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"The New Hampshire OPLC roster has conflicting current entities for license number '{candidate.LicenseNumber}' at row {rowNumber}.");
            }

            var expirationComparison = Nullable.Compare(candidate.LicenseRenewalDate, Firm.LicenseRenewalDate);
            if (expirationComparison > 0)
            {
                Firm = candidate;
                return;
            }

            var issueDateComparison = Nullable.Compare(candidate.LicenseIssuedDate, Firm.LicenseIssuedDate);
            if (expirationComparison == 0 && issueDateComparison > 0)
            {
                Firm = candidate;
                return;
            }

            if (expirationComparison == 0
                && issueDateComparison == 0
                && !string.Equals(candidate.LicenseStatus, Firm.LicenseStatus, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"The New Hampshire OPLC roster has conflicting current entity rows for license number '{candidate.LicenseNumber}' with the same issue and expiration dates at row {rowNumber}.");
            }
        }
    }
}
