using System.Globalization;
using System.Text;
using Lms.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualBasic.FileIO;

namespace Lms.Application.Data;

public static class KansasKrecCaptureSeed
{
    private const string State = "Kansas";
    private const string SourceName = "Kansas Real Estate Commission monthly Licensee Data Report";
    private const string SourceUrl = "https://krec.ks.gov/";
    private const string FileName = "Licensee Data.csv";
    private const string ParserVersion = "krec-licensee-data-active-v1-2026-09";
    private static readonly DateTime RetrievedAtUtc = new(2026, 9, 30, 0, 0, 0, DateTimeKind.Utc);

    public static async Task SeedAsync(ApplicationDbContext dbContext, string? workspaceRoot = null)
    {
        ArgumentNullException.ThrowIfNull(dbContext);

        var root = CaptureSeedRoot.Resolve(workspaceRoot);
        if (root is null)
        {
            return;
        }

        var sourceDirectory = Path.Combine(root, "docs", "KANSAS", "XLXS");
        if (!Directory.Exists(sourceDirectory))
        {
            return;
        }

        var sourcePath = Path.Combine(sourceDirectory, FileName);
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("The dated Kansas KREC Licensee Data Report is required.", sourcePath);
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
        var supervisorsByLicenseNumber = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var firms = new Dictionary<string, KansasFirmBuilder>(StringComparer.OrdinalIgnoreCase);
        var affiliations = new Dictionary<(string LicenseNumber, string FirmLicenseNumber), KansasAffiliationBuilder>();
        var activeRowsWithUnnamedCompany = 0;

        foreach (var row in ReadActiveRecords(sourcePath))
        {
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
                LicenseRenewalDate = row.ExpirationDate
            };

            if (licensees.TryGetValue(licensee.LicenseNumber, out var existingLicensee))
            {
                EnsureConsistentLicense(existingLicensee, licensee);
            }
            else
            {
                licensees.Add(licensee.LicenseNumber, licensee);
            }

            if (row.SupervisingBrokerNumber is not null)
            {
                if (!supervisorsByLicenseNumber.TryGetValue(row.LicenseNumber, out var supervisors))
                {
                    supervisors = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    supervisorsByLicenseNumber.Add(row.LicenseNumber, supervisors);
                }

                supervisors.Add(row.SupervisingBrokerNumber);
            }

            if (row.CompanyNumber is null)
            {
                if (row.CompanyName is not null)
                {
                    licensees[row.LicenseNumber].RelatedLicenseName ??= row.CompanyName;
                    licensees[row.LicenseNumber].RelatedLicenseType ??= "Company";
                }

                continue;
            }

            if (row.CompanyName is null)
            {
                activeRowsWithUnnamedCompany++;
                continue;
            }

            if (!firms.TryGetValue(row.CompanyNumber, out var firmBuilder))
            {
                firmBuilder = new KansasFirmBuilder(snapshot.Id, row.CompanyNumber, row.CompanyName);
                firms.Add(row.CompanyNumber, firmBuilder);
            }
            else if (!string.Equals(firmBuilder.LegalName, row.CompanyName, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"The Kansas KREC CSV maps company number '{row.CompanyNumber}' to conflicting names.");
            }

            if (row.BusinessAddress is not null)
            {
                firmBuilder.AddAddress(row.BusinessAddress);
            }

            var affiliationKey = (row.LicenseNumber, row.CompanyNumber);
            if (!affiliations.TryGetValue(affiliationKey, out var affiliationBuilder))
            {
                affiliationBuilder = new KansasAffiliationBuilder(
                    licensees[row.LicenseNumber],
                    firmBuilder.Firm,
                    row.SupervisingBrokerNumber);
                affiliations.Add(affiliationKey, affiliationBuilder);
            }
            else if (!string.Equals(
                         affiliationBuilder.SupervisingBrokerNumber,
                         row.SupervisingBrokerNumber,
                         StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"The Kansas KREC CSV has conflicting supervising brokers for license '{row.LicenseNumber}' and company '{row.CompanyNumber}'.");
            }
        }

        if (licensees.Count == 0)
        {
            throw new InvalidDataException("The Kansas KREC CSV contained no active real-estate licensees.");
        }

        var brokerNamesByLicenseNumber = licensees.Values
            .Where(licensee => licensee.LicenseType == "Broker")
            .ToDictionary(licensee => licensee.LicenseNumber, licensee => licensee.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var licensee in licensees.Values)
        {
            if (!supervisorsByLicenseNumber.TryGetValue(licensee.LicenseNumber, out var supervisors)
                || supervisors.Count != 1)
            {
                continue;
            }

            var supervisingBrokerNumber = supervisors.Single();
            licensee.RelatedLicenseNumber = supervisingBrokerNumber;
            licensee.RelatedLicenseType = "Supervising Broker";
            if (brokerNamesByLicenseNumber.TryGetValue(supervisingBrokerNumber, out var brokerName))
            {
                licensee.RelatedLicenseName = brokerName;
            }
        }

        if (activeRowsWithUnnamedCompany > 0)
        {
            snapshot.SourceName = $"{SourceName} ({activeRowsWithUnnamedCompany} active company links lack a name)";
        }

        var regulatoryAffiliations = affiliations.Values.Select(affiliation =>
        {
            var brokerNumber = affiliation.SupervisingBrokerNumber;
            var isBrokerInCharge = brokerNumber is not null
                && string.Equals(brokerNumber, affiliation.Licensee.LicenseNumber, StringComparison.OrdinalIgnoreCase);
            return new RegulatoryFirmLicenseeAffiliation
            {
                SnapshotId = snapshot.Id,
                Firm = affiliation.Firm,
                Licensee = affiliation.Licensee,
                IsBrokerInCharge = isBrokerInCharge,
                BrokerInChargeName = brokerNumber is not null
                    ? brokerNamesByLicenseNumber.GetValueOrDefault(brokerNumber)
                    : null
            };
        }).ToList();

        dbContext.RegulatoryDataSnapshots.Add(snapshot);
        dbContext.RegulatoryFirms.AddRange(firms.Values.Select(builder => builder.Build()));
        dbContext.RegulatoryLicensees.AddRange(licensees.Values);
        dbContext.RegulatoryFirmLicenseeAffiliations.AddRange(regulatoryAffiliations);
        await dbContext.SaveChangesAsync();
    }

    private static IEnumerable<KansasLicenseRecord> ReadActiveRecords(string sourcePath)
    {
        using var parser = new TextFieldParser(sourcePath, Encoding.UTF8, detectEncoding: true)
        {
            TextFieldType = FieldType.Delimited,
            HasFieldsEnclosedInQuotes = true,
            TrimWhiteSpace = false
        };
        parser.SetDelimiters(",");

        var headerFields = parser.ReadFields()
            ?? throw new InvalidDataException("The Kansas KREC CSV is empty.");
        var columns = headerFields
            .Select((name, index) => (Name: name.Trim(), Index: index))
            .ToDictionary(column => column.Name, column => column.Index, StringComparer.OrdinalIgnoreCase);
        var requiredColumns = new[]
        {
            "last_name", "first_name", "license_no", "license_type", "issue_date2", "expiration_date",
            "license_status", "company_number", "company_name", "business_address", "business_city",
            "business_state", "business_zip", "superBroker"
        };
        var missingColumns = requiredColumns.Where(column => !columns.ContainsKey(column)).ToArray();
        if (missingColumns.Length > 0)
        {
            throw new InvalidDataException(
                $"The Kansas KREC CSV is missing required columns: {string.Join(", ", missingColumns)}.");
        }

        while (!parser.EndOfData)
        {
            var fields = parser.ReadFields()
                ?? throw new InvalidDataException($"The Kansas KREC CSV contains an empty record near line {parser.LineNumber}.");

            string GetField(string name)
            {
                var index = columns[name];
                if (index >= fields.Length)
                {
                    throw new InvalidDataException($"The Kansas KREC CSV row does not contain the '{name}' column near line {parser.LineNumber}.");
                }

                return fields[index].Trim();
            }

            if (!string.Equals(GetField("license_status"), "Active", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var firstName = GetField("first_name");
            var lastName = GetField("last_name");
            var licenseNumber = GetField("license_no");
            var licenseType = GetField("license_type");
            if (firstName.Length == 0 || lastName.Length == 0 || licenseNumber.Length == 0 || licenseType.Length == 0)
            {
                throw new InvalidDataException(
                    $"The Kansas KREC CSV has an active license row without a name, license number, or type near line {parser.LineNumber}.");
            }

            var companyNumber = NormalizeSourceValue(GetField("company_number"));
            var companyName = NormalizeSourceValue(GetField("company_name"));
            var supervisingBrokerNumber = NormalizeSourceValue(GetField("superBroker"));
            var addressParts = new[]
            {
                GetField("business_address"),
                GetField("business_city"),
                GetField("business_state"),
                GetField("business_zip")
            }.Where(part => NormalizeSourceValue(part) is not null);
            var businessAddress = string.Join(", ", addressParts);

            yield return new KansasLicenseRecord(
                licenseNumber,
                $"{firstName} {lastName}",
                licenseType,
                GetField("license_status"),
                ParseDate(GetField("issue_date2"), "issue_date2", parser.LineNumber),
                ParseDate(GetField("expiration_date"), "expiration_date", parser.LineNumber),
                companyNumber,
                companyName,
                businessAddress.Length == 0 ? null : businessAddress,
                supervisingBrokerNumber,
                parser.LineNumber);
        }
    }

    private static void EnsureConsistentLicense(RegulatoryLicensee existing, RegulatoryLicensee candidate)
    {
        if (!string.Equals(existing.Name, candidate.Name, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(existing.LicenseType, candidate.LicenseType, StringComparison.OrdinalIgnoreCase)
            || existing.LicenseIssuedDate != candidate.LicenseIssuedDate
            || existing.LicenseRenewalDate != candidate.LicenseRenewalDate
            || !string.Equals(existing.LicenseStatus, candidate.LicenseStatus, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"The Kansas KREC CSV has conflicting active records for license number '{candidate.LicenseNumber}'.");
        }
    }

    private static string? NormalizeSourceValue(string value)
    {
        var trimmed = value.Trim();
        return trimmed.Length == 0 || trimmed == "-" ? null : trimmed;
    }

    private static DateOnly? ParseDate(string value, string column, long lineNumber)
    {
        var normalizedValue = NormalizeSourceValue(value);
        if (normalizedValue is null)
        {
            return null;
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
            $"The Kansas KREC CSV has an invalid {column} value '{normalizedValue}' near line {lineNumber}.");
    }

    private sealed record KansasLicenseRecord(
        string LicenseNumber,
        string Name,
        string LicenseType,
        string Status,
        DateOnly? IssuedDate,
        DateOnly? ExpirationDate,
        string? CompanyNumber,
        string? CompanyName,
        string? BusinessAddress,
        string? SupervisingBrokerNumber,
        long LineNumber);

    private sealed class KansasFirmBuilder(Guid snapshotId, string licenseNumber, string legalName)
    {
        private readonly HashSet<string> _addresses = new(StringComparer.OrdinalIgnoreCase);

        public RegulatoryFirm Firm { get; } = new()
        {
            SnapshotId = snapshotId,
            State = State,
            LicenseNumber = licenseNumber,
            LegalName = legalName
        };

        public string LegalName => Firm.LegalName;

        public void AddAddress(string address) => _addresses.Add(address);

        public RegulatoryFirm Build()
        {
            if (_addresses.Count == 1)
            {
                Firm.Address = _addresses.Single();
            }

            return Firm;
        }
    }

    private sealed record KansasAffiliationBuilder(
        RegulatoryLicensee Licensee,
        RegulatoryFirm Firm,
        string? SupervisingBrokerNumber);
}
