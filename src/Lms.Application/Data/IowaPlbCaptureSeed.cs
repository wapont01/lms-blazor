using System.Globalization;
using System.Text;
using Lms.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualBasic.FileIO;

namespace Lms.Application.Data;

public static class IowaPlbCaptureSeed
{
    private const string State = "Iowa";
    private const string City = "Des Moines";
    private const string ZipCode = "50309";
    private const string LastNameGroup = "A-K";
    private const string SourceName = "Iowa PLB Des Moines 50309 active real estate license sample";
    private const string SourceUrl = "https://ia-plb.my.site.com/LicenseSearchPage";
    private const string LicenseesFileName = "Iowa_Des_Moines_50309_AK_Real_Estate_Licensees_20261010.csv";
    private const string FirmsFileName = "Iowa_Des_Moines_50309_AK_Real_Estate_Firms_20261010.csv";
    private const string ParserVersion = "iowa-plb-des-moines-50309-ak-pages1-2-v3-2026-10-10";
    private static readonly DateTime RetrievedAtUtc = new(2026, 10, 10, 20, 17, 37, DateTimeKind.Utc);

    private static readonly HashSet<string> LicenseeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Real Estate Salesperson",
        "Real Estate Broker Associate",
        "Real Estate Broker Officer"
    };

    private static readonly HashSet<string> FirmTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Real Estate Firm",
        "Real Estate Branch",
        "Real Estate Trade Name",
        "Real Estate Broker Sole Proprietor"
    };

    public static async Task SeedAsync(ApplicationDbContext dbContext, string? workspaceRoot = null)
    {
        ArgumentNullException.ThrowIfNull(dbContext);

        var root = CaptureSeedRoot.Resolve(workspaceRoot);
        if (root is null)
        {
            return;
        }

        var sourceDirectory = Path.Combine(root, "docs", "IOWA", "XLXS");
        if (!Directory.Exists(sourceDirectory))
        {
            return;
        }

        var licenseesPath = Path.Combine(sourceDirectory, LicenseesFileName);
        var firmsPath = Path.Combine(sourceDirectory, FirmsFileName);
        if (!File.Exists(licenseesPath) || !File.Exists(firmsPath))
        {
            throw new FileNotFoundException(
                "Both dated Iowa Des Moines licensee and firm sample files are required.",
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
                    "The Iowa capture changed after review; the reviewed snapshot was left unchanged.");
            }

            if (await dbContext.UserRegulatoryLicenseLinks.AnyAsync(link =>
                    link.RegulatorySnapshotId == existingSnapshot.Id))
            {
                throw new InvalidOperationException(
                    "The Iowa capture is linked to user licenses and cannot be refreshed.");
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

        var firmRecords = ReadFirms(firmsPath);
        var firms = new Dictionary<string, RegulatoryFirm>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in firmRecords)
        {
            ValidateLocation(row.InSearchResults, row.City, row.State, row.ZipCode, row.LastNameGroup, row.ResultPage, row.LineNumber);
            if (!FirmTypes.Contains(row.LicenseType))
            {
                throw new InvalidDataException(
                    $"The Iowa capture contains unsupported business license type '{row.LicenseType}' near line {row.LineNumber}.");
            }
            ValidateLicensePrefix(row.LicenseNumber, row.LicenseType, row.LineNumber);

            var firm = new RegulatoryFirm
            {
                SnapshotId = snapshot.Id,
                State = State,
                LicenseNumber = row.LicenseNumber,
                LegalName = row.Name,
                TradeName = string.Equals(row.LicenseType, "Real Estate Trade Name", StringComparison.OrdinalIgnoreCase)
                    ? row.Name
                    : null,
                LicenseStatus = row.Status,
                LicenseIssuedDate = row.OriginalCreatedDate,
                LicenseRenewalDate = row.ExpirationDate,
                QualifyingBrokerName = row.ManagingLicenseName
            };
            if (!firms.TryAdd(firm.LicenseNumber, firm))
            {
                throw new InvalidDataException(
                    $"The Iowa firm capture repeats license number '{firm.LicenseNumber}' near line {row.LineNumber}.");
            }
        }

        var licensees = new Dictionary<string, RegulatoryLicensee>(StringComparer.OrdinalIgnoreCase);
        var affiliations = new Dictionary<string, RegulatoryFirmLicenseeAffiliation>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in ReadLicensees(licenseesPath))
        {
            ValidateLocation(true, row.City, row.State, row.ZipCode, row.LastNameGroup, row.ResultPage, row.LineNumber);
            if (!LicenseeTypes.Contains(row.LicenseType))
            {
                throw new InvalidDataException(
                    $"The Iowa capture contains unsupported individual license type '{row.LicenseType}' near line {row.LineNumber}.");
            }
            ValidateLicensePrefix(row.LicenseNumber, row.LicenseType, row.LineNumber);

            if (!string.Equals(row.Status, "Active", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"The Iowa Des Moines sample contains a non-active licensee near line {row.LineNumber}.");
            }

            var hasRelatedLicense = !string.IsNullOrWhiteSpace(row.RelatedLicenseNumber);
            var hasAllRelatedFields = hasRelatedLicense
                && !string.IsNullOrWhiteSpace(row.RelatedLicenseName)
                && !string.IsNullOrWhiteSpace(row.RelatedLicenseType);
            if (hasRelatedLicense != hasAllRelatedFields)
            {
                throw new InvalidDataException(
                    $"The Iowa licensee capture has incomplete related-license details near line {row.LineNumber}.");
            }

            RegulatoryFirm? firm = null;
            IowaFirmRecord? firmRecord = null;
            if (hasRelatedLicense
                && (!firms.TryGetValue(row.RelatedLicenseNumber, out firm)
                    || !string.Equals(firm.LegalName, row.RelatedLicenseName, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidDataException(
                    $"The Iowa licensee capture has no matching related license '{row.RelatedLicenseNumber}' near line {row.LineNumber}.");
            }

            if (hasRelatedLicense)
            {
                firmRecord = firmRecords.Single(candidate =>
                    string.Equals(candidate.LicenseNumber, row.RelatedLicenseNumber, StringComparison.OrdinalIgnoreCase));
            }

            if (firmRecord is not null
                && !string.Equals(row.RelatedLicenseType, firmRecord.LicenseType, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"The Iowa related license type does not match license '{row.RelatedLicenseNumber}' near line {row.LineNumber}.");
            }

            var licensee = new RegulatoryLicensee
            {
                SnapshotId = snapshot.Id,
                State = State,
                LicenseNumber = row.LicenseNumber,
                Name = row.Name,
                LicenseType = row.LicenseType,
                LicenseLevel = row.LicenseType switch
                {
                    "Real Estate Salesperson" => "Salesperson",
                    "Real Estate Broker Associate" => "Broker",
                    "Real Estate Broker Officer" => "Managing Broker",
                    _ => throw new InvalidDataException(
                        $"The Iowa capture contains unsupported individual license type '{row.LicenseType}' near line {row.LineNumber}.")
                },
                LicenseStatus = row.Status,
                LicenseIssuedDate = row.OriginalCreatedDate,
                OriginalLicenseDate = row.OriginalCreatedDate,
                LicenseRenewalDate = row.ExpirationDate,
                RelatedLicenseNumber = EmptyToNull(row.RelatedLicenseNumber),
                RelatedLicenseType = EmptyToNull(row.RelatedLicenseType),
                RelatedLicenseName = EmptyToNull(row.RelatedLicenseName)
            };
            if (!licensees.TryAdd(licensee.LicenseNumber, licensee))
            {
                throw new InvalidDataException(
                    $"The Iowa licensee capture repeats license number '{licensee.LicenseNumber}' near line {row.LineNumber}.");
            }

            if (firm is null || firmRecord is null)
            {
                continue;
            }

            var affiliationKey = $"{licensee.LicenseNumber}\u001f{firm.LicenseNumber}";
            var isBrokerInCharge = string.Equals(
                    row.LicenseType,
                    "Real Estate Broker Officer",
                    StringComparison.OrdinalIgnoreCase)
                && string.Equals(
                    firmRecord.ManagingLicenseNumber,
                    licensee.LicenseNumber,
                    StringComparison.OrdinalIgnoreCase);
            affiliations.Add(affiliationKey, new RegulatoryFirmLicenseeAffiliation
            {
                SnapshotId = snapshot.Id,
                Firm = firm,
                Licensee = licensee,
                IsBrokerInCharge = isBrokerInCharge,
                BrokerInChargeName = isBrokerInCharge ? firmRecord.ManagingLicenseName : null
            });
        }

        if (licensees.Count == 0 || firms.Count == 0 || affiliations.Count > licensees.Count)
        {
            throw new InvalidDataException("The Iowa Des Moines sample contained no usable licensees, firms, or affiliations.");
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

    private static List<IowaFirmRecord> ReadFirms(string sourcePath)
    {
        using var parser = OpenCsv(sourcePath);
        var columns = ReadColumns(
            parser,
            sourcePath,
            "LicenseNumber",
            "Name",
            "LicenseType",
            "LicenseStatus",
            "OriginalCreatedDate",
            "ExpirationDate",
            "ManagingLicenseName",
            "ManagingLicenseNumber",
            "InSearchResults",
            "QueryCity",
            "QueryState",
            "QueryZip",
            "LastNameGroup",
            "ResultPage");
        var records = new List<IowaFirmRecord>();

        while (!parser.EndOfData)
        {
            var fields = ReadFields(parser, columns, sourcePath);
            var lineNumber = parser.LineNumber;
            var inSearchResults = ParseBoolean(
                GetRequiredField(fields, columns, "InSearchResults", sourcePath, lineNumber),
                sourcePath,
                lineNumber);
            records.Add(new IowaFirmRecord(
                GetRequiredField(fields, columns, "LicenseNumber", sourcePath, lineNumber),
                GetRequiredField(fields, columns, "Name", sourcePath, lineNumber),
                GetRequiredField(fields, columns, "LicenseType", sourcePath, lineNumber),
                GetRequiredField(fields, columns, "LicenseStatus", sourcePath, lineNumber),
                ParseDate(GetRequiredField(fields, columns, "OriginalCreatedDate", sourcePath, lineNumber), sourcePath, lineNumber),
                ParseDate(GetRequiredField(fields, columns, "ExpirationDate", sourcePath, lineNumber), sourcePath, lineNumber),
                GetOptionalField(fields, columns, "ManagingLicenseName"),
                GetOptionalField(fields, columns, "ManagingLicenseNumber"),
                inSearchResults,
                GetOptionalField(fields, columns, "QueryCity"),
                GetOptionalField(fields, columns, "QueryState"),
                GetOptionalField(fields, columns, "QueryZip"),
                GetOptionalField(fields, columns, "LastNameGroup"),
                GetOptionalField(fields, columns, "ResultPage"),
                lineNumber));
        }

        return records;
    }

    private static IEnumerable<IowaLicenseeRecord> ReadLicensees(string sourcePath)
    {
        using var parser = OpenCsv(sourcePath);
        var columns = ReadColumns(
            parser,
            sourcePath,
            "LicenseNumber",
            "Name",
            "LicenseType",
            "LicenseStatus",
            "OriginalCreatedDate",
            "ExpirationDate",
            "RelatedLicenseNumber",
            "RelatedLicenseName",
            "RelatedLicenseType",
            "QueryCity",
            "QueryState",
            "QueryZip",
            "LastNameGroup",
            "ResultPage");

        while (!parser.EndOfData)
        {
            var fields = ReadFields(parser, columns, sourcePath);
            var lineNumber = parser.LineNumber;
            yield return new IowaLicenseeRecord(
                GetRequiredField(fields, columns, "LicenseNumber", sourcePath, lineNumber),
                GetRequiredField(fields, columns, "Name", sourcePath, lineNumber),
                GetRequiredField(fields, columns, "LicenseType", sourcePath, lineNumber),
                GetRequiredField(fields, columns, "LicenseStatus", sourcePath, lineNumber),
                ParseDate(GetRequiredField(fields, columns, "OriginalCreatedDate", sourcePath, lineNumber), sourcePath, lineNumber),
                ParseDate(GetRequiredField(fields, columns, "ExpirationDate", sourcePath, lineNumber), sourcePath, lineNumber),
                GetOptionalField(fields, columns, "RelatedLicenseNumber"),
                GetOptionalField(fields, columns, "RelatedLicenseName"),
                GetOptionalField(fields, columns, "RelatedLicenseType"),
                GetRequiredField(fields, columns, "QueryCity", sourcePath, lineNumber),
                GetRequiredField(fields, columns, "QueryState", sourcePath, lineNumber),
                GetRequiredField(fields, columns, "QueryZip", sourcePath, lineNumber),
                GetRequiredField(fields, columns, "LastNameGroup", sourcePath, lineNumber),
                GetRequiredField(fields, columns, "ResultPage", sourcePath, lineNumber),
                lineNumber);
        }
    }

    private static void ValidateLocation(
        bool inSearchResults,
        string city,
        string state,
        string zipCode,
        string lastNameGroup,
        string resultPage,
        long lineNumber)
    {
        if (!inSearchResults)
        {
            if (city.Length > 0 || state.Length > 0 || zipCode.Length > 0 || lastNameGroup.Length > 0 || resultPage.Length > 0)
            {
                throw new InvalidDataException(
                    $"An Iowa related business record is marked as a query result without matching query metadata near line {lineNumber}.");
            }

            return;
        }

        if (!string.Equals(city, City, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(state, State, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(zipCode, ZipCode, StringComparison.Ordinal)
            || !string.Equals(lastNameGroup, LastNameGroup, StringComparison.Ordinal)
            || resultPage is not ("1" or "2"))
        {
            throw new InvalidDataException(
                $"The Iowa capture contains a search result outside Des Moines, IA {ZipCode}, A-K pages 1-2 near line {lineNumber}.");
        }
    }

    private static void ValidateLicensePrefix(string licenseNumber, string licenseType, long lineNumber)
    {
        var expectedPrefix = licenseType switch
        {
            "Real Estate Salesperson" => 'S',
            "Real Estate Broker Associate" => 'B',
            "Real Estate Broker Officer" => 'B',
            "Real Estate Firm" => 'F',
            "Real Estate Branch" => 'F',
            "Real Estate Trade Name" => 'T',
            "Real Estate Broker Sole Proprietor" => 'B',
            _ => throw new InvalidDataException(
                $"The Iowa capture contains unsupported license type '{licenseType}' near line {lineNumber}.")
        };

        if (licenseNumber.Length == 0 || char.ToUpperInvariant(licenseNumber[0]) != expectedPrefix)
        {
            throw new InvalidDataException(
                $"Iowa license number '{licenseNumber}' does not have the expected '{expectedPrefix}' prefix for '{licenseType}' near line {lineNumber}.");
        }
    }

    private static TextFieldParser OpenCsv(string sourcePath) => new(sourcePath, Encoding.UTF8, detectEncoding: true)
    {
        TextFieldType = FieldType.Delimited,
        HasFieldsEnclosedInQuotes = true,
        TrimWhiteSpace = false
    };

    private static Dictionary<string, int> ReadColumns(
        TextFieldParser parser,
        string sourcePath,
        params string[] requiredColumns)
    {
        parser.SetDelimiters(",");
        var headerFields = parser.ReadFields()
            ?? throw new InvalidDataException($"The Iowa CSV '{Path.GetFileName(sourcePath)}' is empty.");
        var columns = headerFields
            .Select((name, index) => (Name: name.Trim(), Index: index))
            .ToDictionary(column => column.Name, column => column.Index, StringComparer.OrdinalIgnoreCase);
        var missingColumns = requiredColumns.Where(column => !columns.ContainsKey(column)).ToArray();
        if (missingColumns.Length > 0)
        {
            throw new InvalidDataException(
                $"The Iowa CSV '{Path.GetFileName(sourcePath)}' is missing columns: {string.Join(", ", missingColumns)}.");
        }

        return columns;
    }

    private static string[] ReadFields(
        TextFieldParser parser,
        IReadOnlyDictionary<string, int> columns,
        string sourcePath)
    {
        var fields = parser.ReadFields()
            ?? throw new InvalidDataException(
                $"The Iowa CSV '{Path.GetFileName(sourcePath)}' contains an empty row near line {parser.LineNumber}.");
        if (fields.Length <= columns.Values.Max())
        {
            throw new InvalidDataException(
                $"The Iowa CSV '{Path.GetFileName(sourcePath)}' has an incomplete row near line {parser.LineNumber}.");
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
                $"The Iowa CSV '{Path.GetFileName(sourcePath)}' has an empty '{name}' value near line {lineNumber}.");
        }

        return value;
    }

    private static string GetOptionalField(string[] fields, IReadOnlyDictionary<string, int> columns, string name) =>
        fields[columns[name]].Trim();

    private static string? EmptyToNull(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    private static bool ParseBoolean(string value, string sourcePath, long lineNumber)
    {
        if (bool.TryParse(value, out var result))
        {
            return result;
        }

        throw new InvalidDataException(
            $"The Iowa CSV '{Path.GetFileName(sourcePath)}' has an invalid boolean '{value}' near line {lineNumber}.");
    }

    private static DateOnly ParseDate(string value, string sourcePath, long lineNumber)
    {
        if (DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return date;
        }

        throw new InvalidDataException(
            $"The Iowa CSV '{Path.GetFileName(sourcePath)}' has an invalid date '{value}' near line {lineNumber}.");
    }

    private sealed record IowaLicenseeRecord(
        string LicenseNumber,
        string Name,
        string LicenseType,
        string Status,
        DateOnly OriginalCreatedDate,
        DateOnly ExpirationDate,
        string RelatedLicenseNumber,
        string RelatedLicenseName,
        string RelatedLicenseType,
        string City,
        string State,
        string ZipCode,
        string LastNameGroup,
        string ResultPage,
        long LineNumber);

    private sealed record IowaFirmRecord(
        string LicenseNumber,
        string Name,
        string LicenseType,
        string Status,
        DateOnly OriginalCreatedDate,
        DateOnly ExpirationDate,
        string ManagingLicenseName,
        string ManagingLicenseNumber,
        bool InSearchResults,
        string City,
        string State,
        string ZipCode,
        string LastNameGroup,
        string ResultPage,
        long LineNumber);
}
