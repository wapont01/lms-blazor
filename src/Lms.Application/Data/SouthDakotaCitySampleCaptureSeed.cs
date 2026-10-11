using System.Globalization;
using System.Text;
using Lms.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualBasic.FileIO;

namespace Lms.Application.Data;

public static class SouthDakotaCitySampleCaptureSeed
{
    private const string State = "South Dakota";
    private static readonly HashSet<string> Cities = new(StringComparer.OrdinalIgnoreCase)
    {
        "Pierre",
        "Sioux Falls"
    };
    private const string SourceName = "South Dakota Real Estate Commission city-based licensee development sample";
    private const string SourceUrl = "https://sdrec.sd.gov/registration/licenseelist.aspx";
    private const string LicenseesFileName = "South_Dakota_Real_Estate_City_Sample_Licensees_20261010.csv";
    private const string FirmsFileName = "South_Dakota_Real_Estate_City_Sample_Firms_20261010.csv";
    private const string ParserVersion = "sdrec-city-real-estate-sample-v4-2026-10";
    private static readonly DateTime RetrievedAtUtc = new(2026, 10, 10, 0, 0, 0, DateTimeKind.Utc);
    private static readonly string[] LicenseTypePriority =
    [
        "Broker",
        "Broker (additional)",
        "Broker Associate",
        "Property Manager",
        "Res. Rental Agent"
    ];

    public static async Task SeedAsync(ApplicationDbContext dbContext, string? workspaceRoot = null)
    {
        ArgumentNullException.ThrowIfNull(dbContext);

        var root = CaptureSeedRoot.Resolve(workspaceRoot);
        if (root is null)
        {
            return;
        }

        var sourceDirectory = Path.Combine(root, "docs", "SOUTH DAKOTA", "XLXS");
        if (!Directory.Exists(sourceDirectory))
        {
            return;
        }

        var licenseesPath = Path.Combine(sourceDirectory, LicenseesFileName);
        var firmsPath = Path.Combine(sourceDirectory, FirmsFileName);
        if (!File.Exists(licenseesPath))
        {
            throw new FileNotFoundException("The dated South Dakota city licensee sample is required.", licenseesPath);
        }

        if (!File.Exists(firmsPath))
        {
            throw new FileNotFoundException("The dated South Dakota city firm sample is required.", firmsPath);
        }

        var existingSnapshot = await dbContext.RegulatoryDataSnapshots.SingleOrDefaultAsync(snapshot =>
                snapshot.State == State
                && snapshot.SourceUrl == SourceUrl
                && snapshot.RetrievedAtUtc == RetrievedAtUtc);
        if (existingSnapshot?.ParserVersion == ParserVersion)
        {
            return;
        }

        if (existingSnapshot is not null)
        {
            if (existingSnapshot.Status != RegulatorySnapshotStatuses.PendingReview)
            {
                throw new InvalidOperationException(
                    "The South Dakota city capture changed after review; the reviewed snapshot was left unchanged.");
            }

            if (await dbContext.UserRegulatoryLicenseLinks.AnyAsync(link =>
                    link.RegulatorySnapshotId == existingSnapshot.Id))
            {
                throw new InvalidOperationException(
                    "The South Dakota city capture is linked to user licenses and cannot be refreshed.");
            }
        }

        var snapshot = existingSnapshot ?? new RegulatoryDataSnapshot
        {
            State = State,
            SourceName = SourceName,
            SourceUrl = SourceUrl,
            RetrievedAtUtc = RetrievedAtUtc,
            Status = RegulatorySnapshotStatuses.PendingReview
        };
        snapshot.SourceName = SourceName;
        snapshot.ParserVersion = ParserVersion;
        snapshot.ImportedAtUtc = DateTime.UtcNow;
        var firms = ReadFirms(firmsPath, snapshot.Id);
        var licensees = new Dictionary<string, SouthDakotaLicenseeBuilder>(StringComparer.OrdinalIgnoreCase);
        var affiliations = new Dictionary<string, RegulatoryFirmLicenseeAffiliation>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in ReadLicensees(licenseesPath))
        {
            if (!string.Equals(row.Status, "Active", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"The South Dakota city sample contains a non-active licensee near line {row.LineNumber}.");
            }

            if (!Cities.Contains(row.City) || !string.Equals(row.State, "SD", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"The South Dakota licensee sample contains a record outside Pierre or Sioux Falls, SD near line {row.LineNumber}.");
            }

            var normalizedLicenseType = NormalizeLicenseType(row.LicenseType);
            if (!LicenseTypePriority.Contains(normalizedLicenseType, StringComparer.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"The South Dakota city sample contains an unsupported license type '{row.LicenseType}' near line {row.LineNumber}.");
            }

            var candidate = new RegulatoryLicensee
            {
                SnapshotId = snapshot.Id,
                State = State,
                LicenseNumber = row.LicenseNumber,
                Name = row.Name,
                LicenseType = normalizedLicenseType,
                LicenseLevel = GetLicenseLevel(normalizedLicenseType),
                LicenseStatus = "Active",
                LicenseIssuedDate = row.IssuedDate,
                OriginalLicenseDate = row.IssuedDate
            };

            if (!licensees.TryGetValue(candidate.LicenseNumber, out var licenseeBuilder))
            {
                licenseeBuilder = new SouthDakotaLicenseeBuilder(candidate);
                licensees.Add(candidate.LicenseNumber, licenseeBuilder);
            }
            else
            {
                licenseeBuilder.Add(candidate, row.LineNumber);
            }

            if (!firms.TryGetValue(row.FirmName, out var firm))
            {
                continue;
            }

            var affiliationKey = $"{candidate.LicenseNumber}\u001f{firm.LicenseNumber}";
            affiliations.TryAdd(affiliationKey, new RegulatoryFirmLicenseeAffiliation
            {
                SnapshotId = snapshot.Id,
                Firm = firm,
                Licensee = licenseeBuilder.Licensee
            });
        }

        if (licensees.Count == 0 || firms.Count == 0)
        {
            throw new InvalidDataException("The South Dakota city sample contained no usable licensees or firms.");
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        if (existingSnapshot is null)
        {
            dbContext.RegulatoryDataSnapshots.Add(snapshot);
        }
        else
        {
            var oldAffiliations = await dbContext.RegulatoryFirmLicenseeAffiliations
                .Where(affiliation => affiliation.SnapshotId == snapshot.Id)
                .ToListAsync();
            var oldFirms = await dbContext.RegulatoryFirms
                .Where(firm => firm.SnapshotId == snapshot.Id)
                .ToListAsync();
            var oldLicensees = await dbContext.RegulatoryLicensees
                .Where(licensee => licensee.SnapshotId == snapshot.Id)
                .ToListAsync();
            dbContext.RegulatoryFirmLicenseeAffiliations.RemoveRange(oldAffiliations);
            dbContext.RegulatoryFirms.RemoveRange(oldFirms);
            dbContext.RegulatoryLicensees.RemoveRange(oldLicensees);
            await dbContext.SaveChangesAsync();
        }

        dbContext.RegulatoryFirms.AddRange(firms.Values);
        dbContext.RegulatoryLicensees.AddRange(licensees.Values.Select(builder => builder.Build()));
        dbContext.RegulatoryFirmLicenseeAffiliations.AddRange(affiliations.Values);
        await dbContext.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    private static Dictionary<string, RegulatoryFirm> ReadFirms(string sourcePath, Guid snapshotId)
    {
        using var parser = OpenCsv(sourcePath);
        var columns = ReadColumns(
            parser,
            sourcePath,
            "FirmName",
            "Address",
            "City",
            "State",
            "Phone",
            "LicenseNumber",
            "RenewalYear",
            "LicenseIssued");
        var firms = new Dictionary<string, RegulatoryFirm>(StringComparer.OrdinalIgnoreCase);

        while (!parser.EndOfData)
        {
            var fields = ReadFields(parser, columns, sourcePath);
            var lineNumber = parser.LineNumber;
            var licenseNumber = GetRequiredField(fields, columns, "LicenseNumber", sourcePath, lineNumber);
            var firmName = GetRequiredField(fields, columns, "FirmName", sourcePath, lineNumber);
            var city = GetRequiredField(fields, columns, "City", sourcePath, lineNumber);
            var state = GetRequiredField(fields, columns, "State", sourcePath, lineNumber);
            var renewalYear = GetRequiredField(fields, columns, "RenewalYear", sourcePath, lineNumber);
            if (!Cities.Contains(city)
                || !string.Equals(state, "SD", StringComparison.OrdinalIgnoreCase)
                || licenseNumber == "0"
                || !int.TryParse(renewalYear, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedYear)
                || parsedYear < RetrievedAtUtc.Year)
            {
                throw new InvalidDataException(
                    $"The South Dakota firm sample contains an unsupported firm record near line {lineNumber}.");
            }

            var firm = new RegulatoryFirm
            {
                SnapshotId = snapshotId,
                State = State,
                LicenseNumber = licenseNumber,
                LegalName = firmName,
                LicenseStatus = "Listed",
                LicenseIssuedDate = ParseDate(GetField(fields, columns, "LicenseIssued"), sourcePath, lineNumber),
                Address = GetOptionalField(fields, columns, "Address")
            };
            if (!firms.TryAdd(firmName, firm))
            {
                if (firms[firmName].LicenseNumber == firm.LicenseNumber)
                {
                    continue;
                }

                throw new InvalidDataException(
                    $"The South Dakota city firm sample repeats the firm name '{firmName}' with conflicting license numbers near line {lineNumber}.");
            }
        }

        return firms;
    }

    private static IEnumerable<SouthDakotaLicenseeRecord> ReadLicensees(string sourcePath)
    {
        using var parser = OpenCsv(sourcePath);
        var columns = ReadColumns(
            parser,
            sourcePath,
            "Name",
            "LicenseType",
            "LicenseStatus",
            "City",
            "State",
            "FirmName",
            "Phone",
            "LicenseNumber",
            "RenewalYear",
            "LicenseIssued");

        while (!parser.EndOfData)
        {
            var fields = ReadFields(parser, columns, sourcePath);
            var lineNumber = parser.LineNumber;
            var licenseNumber = GetRequiredField(fields, columns, "LicenseNumber", sourcePath, lineNumber);
            var name = GetRequiredField(fields, columns, "Name", sourcePath, lineNumber);
            yield return new SouthDakotaLicenseeRecord(
                name,
                GetRequiredField(fields, columns, "LicenseType", sourcePath, lineNumber),
                GetRequiredField(fields, columns, "LicenseStatus", sourcePath, lineNumber),
                GetRequiredField(fields, columns, "City", sourcePath, lineNumber),
                GetRequiredField(fields, columns, "State", sourcePath, lineNumber),
                GetOptionalField(fields, columns, "FirmName"),
                licenseNumber,
                ParseDate(GetField(fields, columns, "LicenseIssued"), sourcePath, lineNumber),
                lineNumber);
        }
    }

    private static TextFieldParser OpenCsv(string sourcePath) => new(sourcePath, Encoding.UTF8, detectEncoding: true)
    {
        TextFieldType = FieldType.Delimited,
        HasFieldsEnclosedInQuotes = true,
        TrimWhiteSpace = false
    };

    private static Dictionary<string, int> ReadColumns(TextFieldParser parser, string sourcePath, params string[] requiredColumns)
    {
        parser.SetDelimiters(",");
        var headerFields = parser.ReadFields()
            ?? throw new InvalidDataException($"The South Dakota CSV '{Path.GetFileName(sourcePath)}' is empty.");
        var columns = headerFields
            .Select((name, index) => (Name: name.Trim(), Index: index))
            .ToDictionary(column => column.Name, column => column.Index, StringComparer.OrdinalIgnoreCase);
        var missingColumns = requiredColumns.Where(column => !columns.ContainsKey(column)).ToArray();
        if (missingColumns.Length > 0)
        {
            throw new InvalidDataException(
                $"The South Dakota CSV '{Path.GetFileName(sourcePath)}' is missing columns: {string.Join(", ", missingColumns)}.");
        }

        return columns;
    }

    private static string[] ReadFields(TextFieldParser parser, IReadOnlyDictionary<string, int> columns, string sourcePath)
    {
        var fields = parser.ReadFields()
            ?? throw new InvalidDataException(
                $"The South Dakota CSV '{Path.GetFileName(sourcePath)}' contains an empty row near line {parser.LineNumber}.");
        if (fields.Length < columns.Count)
        {
            throw new InvalidDataException(
                $"The South Dakota CSV '{Path.GetFileName(sourcePath)}' has an incomplete row near line {parser.LineNumber}.");
        }

        return fields;
    }

    private static string GetField(string[] fields, IReadOnlyDictionary<string, int> columns, string name) =>
        fields[columns[name]].Trim();

    private static string GetOptionalField(string[] fields, IReadOnlyDictionary<string, int> columns, string name)
    {
        var value = GetField(fields, columns, name);
        return value == "-" ? string.Empty : value;
    }

    private static string GetRequiredField(
        string[] fields,
        IReadOnlyDictionary<string, int> columns,
        string name,
        string sourcePath,
        long lineNumber)
    {
        var value = GetOptionalField(fields, columns, name);
        if (value.Length == 0)
        {
            throw new InvalidDataException(
                $"The South Dakota CSV '{Path.GetFileName(sourcePath)}' has an empty '{name}' value near line {lineNumber}.");
        }

        return value;
    }

    private static DateOnly? ParseDate(string value, string sourcePath, long lineNumber)
    {
        if (value.Length == 0)
        {
            return null;
        }

        string[] formats = ["M/d/yyyy", "MM/dd/yyyy"];
        if (DateOnly.TryParseExact(value, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return date;
        }

        throw new InvalidDataException(
            $"The South Dakota CSV '{Path.GetFileName(sourcePath)}' has an invalid license issue date '{value}' near line {lineNumber}.");
    }

    private static string NormalizeLicenseType(string licenseType) =>
        licenseType.StartsWith("Salesperson", StringComparison.OrdinalIgnoreCase)
            ? "Broker Associate"
            : licenseType;

    private static string GetLicenseLevel(string licenseType) =>
        licenseType.StartsWith("Broker", StringComparison.OrdinalIgnoreCase)
            ? "Broker"
            : licenseType;

    private sealed record SouthDakotaLicenseeRecord(
        string Name,
        string LicenseType,
        string Status,
        string City,
        string State,
        string FirmName,
        string LicenseNumber,
        DateOnly? IssuedDate,
        long LineNumber);

    private sealed class SouthDakotaLicenseeBuilder(RegulatoryLicensee licensee)
    {
        private readonly HashSet<string> _licenseTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            licensee.LicenseType!
        };

        public RegulatoryLicensee Licensee { get; } = licensee;

        public void Add(RegulatoryLicensee candidate, long lineNumber)
        {
            if (!string.Equals(Licensee.Name, candidate.Name, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(Licensee.LicenseStatus, candidate.LicenseStatus, StringComparison.OrdinalIgnoreCase)
                || Licensee.LicenseIssuedDate != candidate.LicenseIssuedDate)
            {
                throw new InvalidDataException(
                    $"The South Dakota roster has conflicting current records for license '{candidate.LicenseNumber}' near line {lineNumber}.");
            }

            _licenseTypes.Add(candidate.LicenseType!);
        }

        public RegulatoryLicensee Build()
        {
            var orderedTypes = LicenseTypePriority.Where(_licenseTypes.Contains).ToArray();
            Licensee.LicenseType = string.Join(", ", orderedTypes);
            Licensee.LicenseLevel = GetLicenseLevel(orderedTypes[0]);
            return Licensee;
        }
    }
}
