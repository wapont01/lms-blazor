using System.Globalization;
using System.Text;
using Lms.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualBasic.FileIO;

namespace Lms.Application.Data;

public static class UtahDreCaptureSeed
{
    private const string State = "Utah";
    private const string SourceName = "Utah DRE active real-estate licensee and company lists";
    private const string SourceUrl = "https://secure.utah.gov/rer/relv/search.html";
    private const string PropertyManagementDesignation = " - Property Mgnt Sales Agent Designation";
    private const string ParserVersion = "utah-dre-active-license-lists-v1-2026-09";
    private static readonly DateTime RetrievedAtUtc = new(2026, 9, 22, 0, 0, 0, DateTimeKind.Utc);
    private static readonly HashSet<string> RealEstateLicenseTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Sales Agent",
        "Associate Broker",
        "Principal Broker",
        "Branch Broker",
        "Dual Broker"
    };

    public static async Task SeedAsync(ApplicationDbContext dbContext, string? workspaceRoot = null)
    {
        ArgumentNullException.ThrowIfNull(dbContext);

        var root = CaptureSeedRoot.Resolve(workspaceRoot);
        if (root is null)
        {
            return;
        }

        var sourceDirectory = Path.Combine(root, "docs", "UTAH", "XLXS");
        if (!Directory.Exists(sourceDirectory))
        {
            return;
        }

        var licenseePath = Path.Combine(sourceDirectory, "Real_Estate_Agents_and_Brokers_Summary.csv");
        var companyPath = Path.Combine(sourceDirectory, "Real_Estate_Company_Summary.csv");
        if (!File.Exists(licenseePath))
        {
            throw new FileNotFoundException("The staged Utah DRE agent and broker list is required.", licenseePath);
        }

        if (!File.Exists(companyPath))
        {
            throw new FileNotFoundException("The staged Utah DRE company list is required.", companyPath);
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

        var firms = ReadCompanies(companyPath, snapshot.Id);
        var licensees = new Dictionary<string, UtahLicenseeBuilder>(StringComparer.OrdinalIgnoreCase);
        var affiliations = new Dictionary<string, UtahAffiliationBuilder>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in ReadCurrentLicensees(licenseePath))
        {
            var candidate = new RegulatoryLicensee
            {
                SnapshotId = snapshot.Id,
                State = State,
                LicenseNumber = row.LicenseNumber,
                Name = row.Name,
                LicenseType = row.LicenseType,
                LicenseLevel = row.LicenseLevel,
                LicenseStatus = row.Status,
                LicenseIssuedDate = row.IssuedDate,
                OriginalLicenseDate = row.IssuedDate,
                LicenseRenewalDate = row.ExpirationDate
            };

            if (!licensees.TryGetValue(candidate.LicenseNumber, out var licenseeBuilder))
            {
                licenseeBuilder = new UtahLicenseeBuilder(candidate);
                licensees.Add(candidate.LicenseNumber, licenseeBuilder);
            }
            else
            {
                licenseeBuilder.EnsureConsistent(candidate);
            }

            if (row.AssociationLicenseNumber is null
                || !firms.TryGetValue(row.AssociationLicenseNumber, out var firmBuilder))
            {
                continue;
            }

            var affiliationKey = $"{candidate.LicenseNumber}\u001f{firmBuilder.Firm.LicenseNumber}";
            if (!affiliations.TryGetValue(affiliationKey, out var affiliationBuilder))
            {
                affiliationBuilder = new UtahAffiliationBuilder(licenseeBuilder.Licensee, firmBuilder.Firm);
                affiliations.Add(affiliationKey, affiliationBuilder);
            }

            affiliationBuilder.AddSupervisingBroker(row.BrokerLicenseNumber);
        }

        if (licensees.Count == 0)
        {
            throw new InvalidDataException("The Utah DRE agent and broker list contained no current real-estate licensees.");
        }

        if (firms.Count == 0)
        {
            throw new InvalidDataException("The Utah DRE company list contained no active real-estate firms.");
        }

        var brokerNames = licensees.Values
            .Where(builder => RealEstateLicenseTypes.Contains(builder.Licensee.LicenseLevel ?? string.Empty)
                && builder.Licensee.LicenseLevel != "Sales Agent")
            .ToDictionary(
                builder => builder.Licensee.LicenseNumber,
                builder => builder.Licensee.Name,
                StringComparer.OrdinalIgnoreCase);

        var regulatoryAffiliations = affiliations.Values.Select(affiliation =>
        {
            var brokerNumber = affiliation.SupervisingBrokerNumbers.Count == 1
                ? affiliation.SupervisingBrokerNumbers.Single()
                : null;
            return new RegulatoryFirmLicenseeAffiliation
            {
                SnapshotId = snapshot.Id,
                Firm = affiliation.Firm,
                Licensee = affiliation.Licensee,
                IsBrokerInCharge = brokerNumber is not null
                    && string.Equals(brokerNumber, affiliation.Licensee.LicenseNumber, StringComparison.OrdinalIgnoreCase),
                BrokerInChargeName = brokerNumber is not null
                    ? brokerNames.GetValueOrDefault(brokerNumber)
                    : null
            };
        }).ToList();

        foreach (var firm in firms.Values)
        {
            firm.SetQualifyingBrokerName(brokerNames);
        }

        dbContext.RegulatoryDataSnapshots.Add(snapshot);
        dbContext.RegulatoryFirms.AddRange(firms.Values.Select(builder => builder.Firm));
        dbContext.RegulatoryLicensees.AddRange(licensees.Values.Select(builder => builder.Licensee));
        dbContext.RegulatoryFirmLicenseeAffiliations.AddRange(regulatoryAffiliations);
        await dbContext.SaveChangesAsync();
    }

    private static Dictionary<string, UtahFirmBuilder> ReadCompanies(string sourcePath, Guid snapshotId)
    {
        var firms = new Dictionary<string, UtahFirmBuilder>(StringComparer.OrdinalIgnoreCase);
        using var parser = OpenCsv(sourcePath);
        var columns = ReadColumns(parser, sourcePath, "License #", "License Type", "Status", "Issue Date", "Expiration Date",
            "Association", "Association License Number", "Broker License #");

        while (!parser.EndOfData)
        {
            var fields = ReadFields(parser, columns, sourcePath);
            var status = GetField(fields, columns, "Status");
            if (!string.Equals(status, "Active", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var licenseNumber = RequireField(fields, columns, "License #", sourcePath, parser.LineNumber);
            var licenseType = RequireField(fields, columns, "License Type", sourcePath, parser.LineNumber);
            var legalName = RequireField(fields, columns, "Association", sourcePath, parser.LineNumber);
            var associationLicenseNumber = RequireField(fields, columns, "Association License Number", sourcePath, parser.LineNumber);
            if (!string.Equals(licenseNumber, associationLicenseNumber, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"The Utah DRE company list has inconsistent license numbers near line {parser.LineNumber}.");
            }

            var firm = new RegulatoryFirm
            {
                SnapshotId = snapshotId,
                State = State,
                LicenseNumber = licenseNumber,
                LegalName = legalName,
                LicenseStatus = status,
                LicenseIssuedDate = ParseDate(GetField(fields, columns, "Issue Date"), "Issue Date", sourcePath, parser.LineNumber),
                LicenseRenewalDate = ParseDate(GetField(fields, columns, "Expiration Date"), "Expiration Date", sourcePath, parser.LineNumber)
            };

            if (!firms.TryGetValue(licenseNumber, out var firmBuilder))
            {
                firmBuilder = new UtahFirmBuilder(firm, licenseType);
                firms.Add(licenseNumber, firmBuilder);
            }
            else
            {
                firmBuilder.EnsureConsistent(firm, licenseType, sourcePath, parser.LineNumber);
            }

            firmBuilder.AddSupervisingBroker(GetOptionalField(fields, columns, "Broker License #"));
        }

        return firms;
    }

    private static IEnumerable<UtahLicenseRecord> ReadCurrentLicensees(string sourcePath)
    {
        using var parser = OpenCsv(sourcePath);
        var columns = ReadColumns(parser, sourcePath, "License #", "License Type", "Last Name", "First Name", "Middle Name",
            "Status", "Issue Date", "Expiration Date", "Association", "Association License Number", "Broker License #");

        while (!parser.EndOfData)
        {
            var fields = ReadFields(parser, columns, sourcePath);
            var status = GetField(fields, columns, "Status");
            if (!string.Equals(status, "Active", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(status, "Active On Probation", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var sourceLicenseType = RequireField(fields, columns, "License Type", sourcePath, parser.LineNumber);
            var baseLicenseType = sourceLicenseType.EndsWith(PropertyManagementDesignation, StringComparison.OrdinalIgnoreCase)
                ? sourceLicenseType[..^PropertyManagementDesignation.Length].Trim()
                : sourceLicenseType;
            if (!RealEstateLicenseTypes.Contains(baseLicenseType))
            {
                continue;
            }

            var licenseNumber = RequireField(fields, columns, "License #", sourcePath, parser.LineNumber);
            var name = string.Join(' ', new[]
                {
                    GetOptionalField(fields, columns, "First Name"),
                    GetOptionalField(fields, columns, "Middle Name"),
                    GetOptionalField(fields, columns, "Last Name")
                }
                .Where(value => value.Length > 0));
            if (name.Length == 0)
            {
                throw new InvalidDataException(
                    $"The Utah DRE list has a current license without a name near line {parser.LineNumber}.");
            }

            var licenseType = sourceLicenseType.EndsWith(PropertyManagementDesignation, StringComparison.OrdinalIgnoreCase)
                ? $"{baseLicenseType} - Property Management"
                : baseLicenseType;

            yield return new UtahLicenseRecord(
                licenseNumber,
                name,
                licenseType,
                baseLicenseType,
                status,
                ParseDate(GetField(fields, columns, "Issue Date"), "Issue Date", sourcePath, parser.LineNumber),
                ParseDate(GetField(fields, columns, "Expiration Date"), "Expiration Date", sourcePath, parser.LineNumber),
                NormalizeSourceValue(GetField(fields, columns, "Association License Number")),
                NormalizeSourceValue(GetField(fields, columns, "Broker License #")));
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
            ?? throw new InvalidDataException($"The Utah DRE CSV '{Path.GetFileName(sourcePath)}' is empty.");
        var columns = headerFields
            .Select((name, index) => (Name: name.Trim(), Index: index))
            .ToDictionary(column => column.Name, column => column.Index, StringComparer.OrdinalIgnoreCase);
        var missingColumns = requiredColumns.Where(column => !columns.ContainsKey(column)).ToArray();
        if (missingColumns.Length > 0)
        {
            throw new InvalidDataException(
                $"The Utah DRE CSV '{Path.GetFileName(sourcePath)}' is missing required columns: {string.Join(", ", missingColumns)}.");
        }

        return columns;
    }

    private static string[] ReadFields(TextFieldParser parser, IReadOnlyDictionary<string, int> columns, string sourcePath)
    {
        var fields = parser.ReadFields()
            ?? throw new InvalidDataException(
                $"The Utah DRE CSV '{Path.GetFileName(sourcePath)}' contains an empty record near line {parser.LineNumber}.");
        if (fields.Length < columns.Count)
        {
            throw new InvalidDataException(
                $"The Utah DRE CSV '{Path.GetFileName(sourcePath)}' has an incomplete record near line {parser.LineNumber}.");
        }

        return fields;
    }

    private static string GetField(string[] fields, IReadOnlyDictionary<string, int> columns, string name) =>
        fields[columns[name]].Trim();

    private static string GetOptionalField(string[] fields, IReadOnlyDictionary<string, int> columns, string name) =>
        NormalizeSourceValue(GetField(fields, columns, name)) ?? string.Empty;

    private static string RequireField(
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
                $"The Utah DRE CSV '{Path.GetFileName(sourcePath)}' has an empty '{name}' value near line {lineNumber}.");
        }

        return value;
    }

    private static string? NormalizeSourceValue(string value)
    {
        var trimmed = value.Trim();
        return trimmed.Length == 0 || trimmed == "-" ? null : trimmed;
    }

    private static DateOnly? ParseDate(string value, string column, string sourcePath, long lineNumber)
    {
        var normalizedValue = NormalizeSourceValue(value);
        if (normalizedValue is null)
        {
            return null;
        }

        if (normalizedValue.StartsWith("=\"", StringComparison.Ordinal) && normalizedValue.EndsWith('"'))
        {
            normalizedValue = normalizedValue[2..^1];
        }

        string[] formats = ["M/d/yyyy", "MM/dd/yyyy", "M/d/yy", "MM/dd/yy"];
        if (DateOnly.TryParseExact(
                normalizedValue,
                formats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var date))
        {
            return date;
        }

        throw new InvalidDataException(
            $"The Utah DRE CSV '{Path.GetFileName(sourcePath)}' has an invalid {column} value '{normalizedValue}' near line {lineNumber}.");
    }

    private sealed record UtahLicenseRecord(
        string LicenseNumber,
        string Name,
        string LicenseType,
        string LicenseLevel,
        string Status,
        DateOnly? IssuedDate,
        DateOnly? ExpirationDate,
        string? AssociationLicenseNumber,
        string? BrokerLicenseNumber);

    private sealed class UtahLicenseeBuilder(RegulatoryLicensee licensee)
    {
        public RegulatoryLicensee Licensee { get; } = licensee;

        public void EnsureConsistent(RegulatoryLicensee candidate)
        {
            if (!string.Equals(Licensee.Name, candidate.Name, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(Licensee.LicenseType, candidate.LicenseType, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(Licensee.LicenseLevel, candidate.LicenseLevel, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(Licensee.LicenseStatus, candidate.LicenseStatus, StringComparison.OrdinalIgnoreCase)
                || Licensee.LicenseIssuedDate != candidate.LicenseIssuedDate
                || Licensee.LicenseRenewalDate != candidate.LicenseRenewalDate)
            {
                throw new InvalidDataException(
                    $"The Utah DRE list has conflicting current records for license number '{candidate.LicenseNumber}'.");
            }
        }
    }

    private sealed class UtahFirmBuilder(RegulatoryFirm firm, string licenseType)
    {
        private readonly HashSet<string> _supervisingBrokerNumbers = new(StringComparer.OrdinalIgnoreCase);

        public RegulatoryFirm Firm { get; } = firm;

        public void EnsureConsistent(RegulatoryFirm candidate, string candidateLicenseType, string sourcePath, long lineNumber)
        {
            if (!string.Equals(Firm.LegalName, candidate.LegalName, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(Firm.LicenseStatus, candidate.LicenseStatus, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(licenseType, candidateLicenseType, StringComparison.OrdinalIgnoreCase)
                || Firm.LicenseIssuedDate != candidate.LicenseIssuedDate
                || Firm.LicenseRenewalDate != candidate.LicenseRenewalDate)
            {
                throw new InvalidDataException(
                    $"The Utah DRE company list has conflicting records for license number '{candidate.LicenseNumber}' near line {lineNumber}.");
            }
        }

        public void AddSupervisingBroker(string? licenseNumber)
        {
            if (licenseNumber is not null)
            {
                _supervisingBrokerNumbers.Add(licenseNumber);
            }
        }

        public void SetQualifyingBrokerName(IReadOnlyDictionary<string, string> brokerNames)
        {
            if (_supervisingBrokerNumbers.Count == 1
                && brokerNames.TryGetValue(_supervisingBrokerNumbers.Single(), out var brokerName))
            {
                Firm.QualifyingBrokerName = brokerName;
            }
        }
    }

    private sealed class UtahAffiliationBuilder(RegulatoryLicensee licensee, RegulatoryFirm firm)
    {
        public RegulatoryLicensee Licensee { get; } = licensee;
        public RegulatoryFirm Firm { get; } = firm;
        public HashSet<string> SupervisingBrokerNumbers { get; } = new(StringComparer.OrdinalIgnoreCase);

        public void AddSupervisingBroker(string? licenseNumber)
        {
            if (licenseNumber is not null)
            {
                SupervisingBrokerNumbers.Add(licenseNumber);
            }
        }
    }
}
