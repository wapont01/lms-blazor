using System.Globalization;
using System.Text;
using Lms.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualBasic.FileIO;

namespace Lms.Application.Data;

public static class NorthDakotaBismarckCaptureSeed
{
    private const string State = "North Dakota";
    private const string City = "Bismarck";
    private const string ZipCode = "58501";
    private const string SourceName = "North Dakota Real Estate Commission Bismarck 58501 licensee development sample";
    private const string SourceUrl = "https://services.realestatend.org/directory/index.asp";
    private const string LicenseesFileName = "North_Dakota_Bismarck_58501_Real_Estate_Licensees_20261010.csv";
    private const string FirmsFileName = "North_Dakota_Bismarck_58501_Real_Estate_Firms_20261010.csv";
    private const string ParserVersion = "ndrec-bismarck-58501-directory-sample-v2-2026-10-10";
    private static readonly DateTime RetrievedAtUtc = new(2026, 10, 10, 19, 55, 0, DateTimeKind.Utc);
    private static readonly HashSet<string> LicenseTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Broker",
        "Broker Associate",
        "Salesperson"
    };

    public static async Task SeedAsync(ApplicationDbContext dbContext, string? workspaceRoot = null)
    {
        ArgumentNullException.ThrowIfNull(dbContext);

        var root = CaptureSeedRoot.Resolve(workspaceRoot);
        if (root is null)
        {
            return;
        }

        var sourceDirectory = Path.Combine(root, "docs", "NORTH DAKOTA", "XLXS");
        if (!Directory.Exists(sourceDirectory))
        {
            return;
        }

        var licenseesPath = Path.Combine(sourceDirectory, LicenseesFileName);
        var firmsPath = Path.Combine(sourceDirectory, FirmsFileName);
        if (!File.Exists(licenseesPath) || !File.Exists(firmsPath))
        {
            throw new FileNotFoundException(
                "Both dated North Dakota Bismarck 58501 licensee and firm sample files are required.",
                !File.Exists(licenseesPath) ? licenseesPath : firmsPath);
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
                    "The North Dakota Bismarck capture changed after review; the reviewed snapshot was left unchanged.");
            }

            if (await dbContext.UserRegulatoryLicenseLinks.AnyAsync(link =>
                    link.RegulatorySnapshotId == existingSnapshot.Id))
            {
                throw new InvalidOperationException(
                    "The North Dakota Bismarck capture is linked to user licenses and cannot be refreshed.");
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
        var licensees = new Dictionary<string, RegulatoryLicensee>(StringComparer.OrdinalIgnoreCase);
        var affiliations = new Dictionary<(string LicenseeNumber, string FirmNumber), RegulatoryFirmLicenseeAffiliation>();

        foreach (var row in ReadLicensees(licenseesPath))
        {
            if (!string.Equals(row.City, City, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(row.State, "ND", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(row.ZipCode, ZipCode, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"The North Dakota licensee sample contains a record outside Bismarck, ND {ZipCode} near line {row.LineNumber}.");
            }

            if (!string.Equals(row.Status, "Active", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"The North Dakota Bismarck sample contains a non-active licensee near line {row.LineNumber}.");
            }

            if (!LicenseTypes.Contains(row.LicenseType))
            {
                throw new InvalidDataException(
                    $"The North Dakota Bismarck sample contains unsupported license type '{row.LicenseType}' near line {row.LineNumber}.");
            }

            if (!firms.TryGetValue(row.FirmLicenseNumber, out var firm))
            {
                throw new InvalidDataException(
                    $"The North Dakota licensee sample has no matching firm record for license '{row.LicenseNumber}' near line {row.LineNumber}.");
            }

            var licensee = new RegulatoryLicensee
            {
                SnapshotId = snapshot.Id,
                State = State,
                LicenseNumber = row.LicenseNumber,
                Name = row.Name,
                LicenseType = row.LicenseType,
                LicenseLevel = row.LicenseType,
                LicenseStatus = row.Status,
                LicenseIssuedDate = row.IssuedDate,
                OriginalLicenseDate = row.IssuedDate,
                LicenseRenewalDate = row.ExpirationDate,
                RelatedLicenseNumber = row.FirmLicenseNumber,
                RelatedLicenseType = "Firm",
                RelatedLicenseName = row.Company
            };

            if (!licensees.TryAdd(licensee.LicenseNumber, licensee))
            {
                throw new InvalidDataException(
                    $"The North Dakota Bismarck sample repeats license number '{licensee.LicenseNumber}' near line {row.LineNumber}.");
            }

            affiliations.Add((licensee.LicenseNumber, firm.LicenseNumber), new RegulatoryFirmLicenseeAffiliation
            {
                SnapshotId = snapshot.Id,
                Firm = firm,
                Licensee = licensee
            });
        }

        if (licensees.Count == 0 || firms.Count == 0)
        {
            throw new InvalidDataException("The North Dakota Bismarck sample contained no usable licensees or firms.");
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
        dbContext.RegulatoryLicensees.AddRange(licensees.Values);
        dbContext.RegulatoryFirmLicenseeAffiliations.AddRange(affiliations.Values);
        await dbContext.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    private static Dictionary<string, RegulatoryFirm> ReadFirms(string sourcePath, Guid snapshotId)
    {
        using var parser = OpenCsv(sourcePath);
        var columns = ReadColumns(parser, sourcePath, "Company", "FirmLicenseNumber", "City", "State", "ZipCode");
        var firms = new Dictionary<string, RegulatoryFirm>(StringComparer.OrdinalIgnoreCase);

        while (!parser.EndOfData)
        {
            var fields = ReadFields(parser, columns, sourcePath);
            var lineNumber = parser.LineNumber;
            var company = GetRequiredField(fields, columns, "Company", sourcePath, lineNumber);
            var licenseNumber = GetRequiredField(fields, columns, "FirmLicenseNumber", sourcePath, lineNumber);
            var city = GetRequiredField(fields, columns, "City", sourcePath, lineNumber);
            var state = GetRequiredField(fields, columns, "State", sourcePath, lineNumber);
            var zipCode = GetRequiredField(fields, columns, "ZipCode", sourcePath, lineNumber);
            if (!string.Equals(city, City, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(state, "ND", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(zipCode, ZipCode, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"The North Dakota firm sample contains a record outside Bismarck, ND {ZipCode} near line {lineNumber}.");
            }

            var firm = new RegulatoryFirm
            {
                SnapshotId = snapshotId,
                State = State,
                LicenseNumber = licenseNumber,
                LegalName = company,
                LicenseStatus = "Listed"
            };
            if (!firms.TryAdd(licenseNumber, firm)
                && !string.Equals(firms[licenseNumber].LegalName, company, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"The North Dakota firm sample maps license number '{licenseNumber}' to conflicting names near line {lineNumber}.");
            }
        }

        return firms;
    }

    private static IEnumerable<NorthDakotaLicenseeRecord> ReadLicensees(string sourcePath)
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
            "ZipCode",
            "Company",
            "FirmLicenseNumber",
            "LicenseNumber",
            "LicenseIssued",
            "LicenseExpiration");

        while (!parser.EndOfData)
        {
            var fields = ReadFields(parser, columns, sourcePath);
            var lineNumber = parser.LineNumber;
            yield return new NorthDakotaLicenseeRecord(
                GetRequiredField(fields, columns, "Name", sourcePath, lineNumber),
                GetRequiredField(fields, columns, "LicenseType", sourcePath, lineNumber),
                GetRequiredField(fields, columns, "LicenseStatus", sourcePath, lineNumber),
                GetRequiredField(fields, columns, "City", sourcePath, lineNumber),
                GetRequiredField(fields, columns, "State", sourcePath, lineNumber),
                GetRequiredField(fields, columns, "ZipCode", sourcePath, lineNumber),
                GetRequiredField(fields, columns, "Company", sourcePath, lineNumber),
                GetRequiredField(fields, columns, "FirmLicenseNumber", sourcePath, lineNumber),
                GetRequiredField(fields, columns, "LicenseNumber", sourcePath, lineNumber),
                ParseDate(GetRequiredField(fields, columns, "LicenseIssued", sourcePath, lineNumber), sourcePath, lineNumber),
                ParseDate(GetRequiredField(fields, columns, "LicenseExpiration", sourcePath, lineNumber), sourcePath, lineNumber),
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
            ?? throw new InvalidDataException($"The North Dakota CSV '{Path.GetFileName(sourcePath)}' is empty.");
        var columns = headerFields
            .Select((name, index) => (Name: name.Trim(), Index: index))
            .ToDictionary(column => column.Name, column => column.Index, StringComparer.OrdinalIgnoreCase);
        var missingColumns = requiredColumns.Where(column => !columns.ContainsKey(column)).ToArray();
        if (missingColumns.Length > 0)
        {
            throw new InvalidDataException(
                $"The North Dakota CSV '{Path.GetFileName(sourcePath)}' is missing columns: {string.Join(", ", missingColumns)}.");
        }

        return columns;
    }

    private static string[] ReadFields(TextFieldParser parser, IReadOnlyDictionary<string, int> columns, string sourcePath)
    {
        var fields = parser.ReadFields()
            ?? throw new InvalidDataException(
                $"The North Dakota CSV '{Path.GetFileName(sourcePath)}' contains an empty row near line {parser.LineNumber}.");
        if (fields.Length < columns.Count)
        {
            throw new InvalidDataException(
                $"The North Dakota CSV '{Path.GetFileName(sourcePath)}' has an incomplete row near line {parser.LineNumber}.");
        }

        return fields;
    }

    private static string GetRequiredField(
        string[] fields,
        IReadOnlyDictionary<string, int> columns,
        string name,
        string sourcePath,
        long lineNumber)
    {
        var value = fields[columns[name]].Trim();
        if (value.Length == 0)
        {
            throw new InvalidDataException(
                $"The North Dakota CSV '{Path.GetFileName(sourcePath)}' has an empty '{name}' value near line {lineNumber}.");
        }

        return value;
    }

    private static DateOnly ParseDate(string value, string sourcePath, long lineNumber)
    {
        string[] formats = ["M/d/yyyy", "MM/dd/yyyy"];
        if (DateOnly.TryParseExact(value, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return date;
        }

        throw new InvalidDataException(
            $"The North Dakota CSV '{Path.GetFileName(sourcePath)}' has an invalid date '{value}' near line {lineNumber}.");
    }

    private sealed record NorthDakotaLicenseeRecord(
        string Name,
        string LicenseType,
        string Status,
        string City,
        string State,
        string ZipCode,
        string Company,
        string FirmLicenseNumber,
        string LicenseNumber,
        DateOnly IssuedDate,
        DateOnly ExpirationDate,
        long LineNumber);
}
