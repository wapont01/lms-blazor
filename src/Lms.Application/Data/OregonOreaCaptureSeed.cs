using System.Globalization;
using System.Text;
using Lms.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualBasic.FileIO;

namespace Lms.Application.Data;

public static class OregonOreaCaptureSeed
{
    private const string State = "Oregon";
    private const string SourceName = "Oregon OREA active individual and business license rosters";
    private const string SourceUrl = "https://orea.elicense.micropact.com/Lookup/GenerateRoster.aspx";
    private const string IndividualsFileName = "Oregon_Active_Individuals_20261010.csv";
    private const string BusinessesFileName = "Oregon_Active_Businesses_20261010.csv";
    private const string ParserVersion = "oregon-orea-active-rosters-v1-2026-10";
    private static readonly DateTime RetrievedAtUtc = new(2026, 10, 10, 0, 0, 0, DateTimeKind.Utc);
    private static readonly string[] LicenseTypePriority = ["Principal Broker", "Broker", "Property Manager"];
    private static readonly IReadOnlyDictionary<string, string> LicenseTypes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["B"] = "Broker",
            ["PB"] = "Principal Broker",
            ["PM"] = "Property Manager"
        };
    private static readonly HashSet<string> BusinessRegistrationTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "EA",
        "RBN",
        "RBO"
    };
    private static readonly (int Index, string Name)[] IndividualHeader =
    [
        (0, "Last Name"),
        (2, "First Name"),
        (4, "Middle"),
        (17, "License Type"),
        (18, "License Number"),
        (20, "License Expiration"),
        (22, "License Status"),
        (25, "Business License"),
        (27, "Licensee Associated to RBN Date"),
        (28, "Key Manager"),
        (30, "Key Manager Credential Number")
    ];
    private static readonly (int Index, string Name)[] BusinessHeader =
    [
        (0, "Business"),
        (1, "Name"),
        (2, "Address"),
        (6, "Name of"),
        (8, "State"),
        (11, "County"),
        (14, "Registration Type"),
        (15, "Business License"),
        (17, "Expiration Date")
    ];

    public static async Task SeedAsync(ApplicationDbContext dbContext, string? workspaceRoot = null)
    {
        ArgumentNullException.ThrowIfNull(dbContext);

        var root = CaptureSeedRoot.Resolve(workspaceRoot);
        if (root is null)
        {
            return;
        }

        var sourceDirectory = Path.Combine(root, "docs", "OREGON", "XLXS");
        if (!Directory.Exists(sourceDirectory))
        {
            return;
        }

        var individualsPath = Path.Combine(sourceDirectory, IndividualsFileName);
        var businessesPath = Path.Combine(sourceDirectory, BusinessesFileName);
        if (!File.Exists(individualsPath))
        {
            throw new FileNotFoundException("The dated Oregon OREA active-individual roster is required.", individualsPath);
        }

        if (!File.Exists(businessesPath))
        {
            throw new FileNotFoundException("The dated Oregon OREA active-business roster is required.", businessesPath);
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
        var firms = ReadBusinesses(businessesPath, snapshot.Id);
        var licensees = new Dictionary<string, OregonLicenseeBuilder>(StringComparer.OrdinalIgnoreCase);
        var affiliations = new Dictionary<string, OregonAffiliationBuilder>(StringComparer.OrdinalIgnoreCase);
        var unnamedIndividualRows = 0;

        foreach (var row in ReadIndividuals(individualsPath))
        {
            if (row.LicenseNumber.Length == 0)
            {
                throw new InvalidDataException(
                    $"The Oregon OREA active-individual roster has no license number near line {row.LineNumber}.");
            }

            if (!string.Equals(row.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"The Oregon OREA active-individual roster has an unexpected status '{row.Status}' near line {row.LineNumber}.");
            }

            if (!LicenseTypes.TryGetValue(row.LicenseTypeCode, out var licenseType))
            {
                throw new InvalidDataException(
                    $"The Oregon OREA active-individual roster has an unsupported license type '{row.LicenseTypeCode}' near line {row.LineNumber}.");
            }

            if (row.Name.Length == 0)
            {
                unnamedIndividualRows++;
                continue;
            }

            var candidate = new RegulatoryLicensee
            {
                SnapshotId = snapshot.Id,
                State = State,
                LicenseNumber = row.LicenseNumber,
                Name = row.Name,
                LicenseType = licenseType,
                LicenseLevel = licenseType,
                LicenseStatus = "Active",
                LicenseRenewalDate = row.ExpirationDate
            };

            if (!licensees.TryGetValue(candidate.LicenseNumber, out var licenseeBuilder))
            {
                licenseeBuilder = new OregonLicenseeBuilder(candidate);
                licensees.Add(candidate.LicenseNumber, licenseeBuilder);
            }
            else
            {
                licenseeBuilder.Add(candidate, row.LineNumber);
            }

            if (row.BusinessLicenseNumber.Length == 0
                || !firms.TryGetValue(row.BusinessLicenseNumber, out var firmBuilder))
            {
                throw new InvalidDataException(
                    $"The Oregon OREA active-individual roster has no matching business license '{row.BusinessLicenseNumber}' near line {row.LineNumber}.");
            }

            var affiliationKey = $"{candidate.LicenseNumber}\u001f{firmBuilder.Firm.LicenseNumber}";
            if (!affiliations.TryGetValue(affiliationKey, out var affiliationBuilder))
            {
                affiliationBuilder = new OregonAffiliationBuilder(licenseeBuilder, firmBuilder);
                affiliations.Add(affiliationKey, affiliationBuilder);
            }

            affiliationBuilder.Add(row);
            firmBuilder.AddKeyManager(row.KeyManagerNumber);
        }

        if (licensees.Count == 0 || firms.Count == 0)
        {
            throw new InvalidDataException("The Oregon OREA active rosters contained no usable real-estate records.");
        }

        foreach (var firmBuilder in firms.Values)
        {
            firmBuilder.SetQualifyingBrokerName(licensees);
        }

        if (unnamedIndividualRows > 0)
        {
            snapshot.SourceName = $"{SourceName} ({unnamedIndividualRows} unnamed individual records excluded)";
        }

        dbContext.RegulatoryDataSnapshots.Add(snapshot);
        dbContext.RegulatoryFirms.AddRange(firms.Values.Select(builder => builder.Firm));
        dbContext.RegulatoryLicensees.AddRange(licensees.Values.Select(builder => builder.Build()));
        dbContext.RegulatoryFirmLicenseeAffiliations.AddRange(affiliations.Values.Select(builder => builder.Build(snapshot.Id)));
        await dbContext.SaveChangesAsync();
    }

    private static Dictionary<string, OregonFirmBuilder> ReadBusinesses(string sourcePath, Guid snapshotId)
    {
        var firms = new Dictionary<string, OregonFirmBuilder>(StringComparer.OrdinalIgnoreCase);
        using var parser = OpenCsv(sourcePath);
        ValidateHeader(parser, sourcePath, BusinessHeader, minimumColumnCount: 18);

        while (!parser.EndOfData)
        {
            var fields = ReadFields(parser, sourcePath, minimumColumnCount: 18);
            var lineNumber = parser.LineNumber;
            var licenseNumber = GetField(fields, 15);
            var name = JoinFields(fields, 0, 1);
            var registrationType = GetField(fields, 14);
            if (licenseNumber.Length == 0 || name.Length == 0)
            {
                throw new InvalidDataException(
                    $"The Oregon OREA active-business roster has a missing license number or name near line {lineNumber}.");
            }

            if (!BusinessRegistrationTypes.Contains(registrationType))
            {
                throw new InvalidDataException(
                    $"The Oregon OREA active-business roster has an unsupported registration type '{registrationType}' near line {lineNumber}.");
            }

            var firm = new RegulatoryFirm
            {
                SnapshotId = snapshotId,
                State = State,
                LicenseNumber = licenseNumber,
                LegalName = name,
                LicenseStatus = "Active",
                LicenseRenewalDate = ParseDate(GetField(fields, 17), "business expiration date", sourcePath, lineNumber),
                Address = string.Join(", ", new[] { JoinFields(fields, 2, 3, 4, 5), JoinFields(fields, 6, 7), JoinFields(fields, 8, 9, 10, 11) }
                    .Where(value => value.Length > 0))
            };

            if (!firms.TryAdd(licenseNumber, new OregonFirmBuilder(firm)))
            {
                throw new InvalidDataException(
                    $"The Oregon OREA active-business roster repeats business license '{licenseNumber}' near line {lineNumber}.");
            }
        }

        return firms;
    }

    private static IEnumerable<OregonIndividualRecord> ReadIndividuals(string sourcePath)
    {
        using var parser = OpenCsv(sourcePath);
        ValidateHeader(parser, sourcePath, IndividualHeader, minimumColumnCount: 31);

        while (!parser.EndOfData)
        {
            var fields = ReadFields(parser, sourcePath, minimumColumnCount: 31);
            if (fields.All(string.IsNullOrWhiteSpace))
            {
                continue;
            }

            yield return new OregonIndividualRecord(
                JoinFields(fields, 2, 3, 4, 5, 0, 1),
                GetField(fields, 17),
                GetField(fields, 18),
                ParseDate(GetField(fields, 20), "individual expiration date", sourcePath, parser.LineNumber),
                GetField(fields, 22),
                GetField(fields, 25),
                ParseDate(GetField(fields, 27), "RBN association date", sourcePath, parser.LineNumber),
                GetField(fields, 28),
                GetField(fields, 30),
                parser.LineNumber);
        }
    }

    private static TextFieldParser OpenCsv(string sourcePath) => new(sourcePath, Encoding.UTF8, detectEncoding: true)
    {
        TextFieldType = FieldType.Delimited,
        HasFieldsEnclosedInQuotes = true,
        TrimWhiteSpace = false
    };

    private static void ValidateHeader(
        TextFieldParser parser,
        string sourcePath,
        IReadOnlyList<(int Index, string Name)> requiredHeaders,
        int minimumColumnCount)
    {
        parser.SetDelimiters(",");
        var header = parser.ReadFields()
            ?? throw new InvalidDataException($"The Oregon OREA roster '{Path.GetFileName(sourcePath)}' is empty.");
        if (header.Length < minimumColumnCount)
        {
            throw new InvalidDataException(
                $"The Oregon OREA roster '{Path.GetFileName(sourcePath)}' has an incomplete header.");
        }

        foreach (var (index, name) in requiredHeaders)
        {
            if (!string.Equals(header[index].Trim(), name, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"The Oregon OREA roster '{Path.GetFileName(sourcePath)}' has an unexpected column at position {index + 1}.");
            }
        }
    }

    private static string[] ReadFields(TextFieldParser parser, string sourcePath, int minimumColumnCount)
    {
        var fields = parser.ReadFields()
            ?? throw new InvalidDataException(
                $"The Oregon OREA roster '{Path.GetFileName(sourcePath)}' contains an empty record near line {parser.LineNumber}.");
        if (fields.Length < minimumColumnCount)
        {
            throw new InvalidDataException(
                $"The Oregon OREA roster '{Path.GetFileName(sourcePath)}' has an incomplete record near line {parser.LineNumber}.");
        }

        return fields;
    }

    private static string GetField(string[] fields, int index) =>
        index < fields.Length ? NormalizeText(fields[index]) : string.Empty;

    private static string JoinFields(string[] fields, params int[] indexes) =>
        string.Join(' ', indexes
            .Select(index => GetField(fields, index))
            .Where(value => value.Length > 0));

    private static string NormalizeText(string value)
    {
        var normalized = value.Trim();
        return normalized == "-" ? string.Empty : normalized;
    }

    private static DateOnly? ParseDate(string value, string column, string sourcePath, long lineNumber)
    {
        if (value.Length == 0)
        {
            return null;
        }

        string[] formats = ["M-d-yyyy", "MM-dd-yyyy", "M/d/yyyy", "MM/dd/yyyy"];
        if (DateOnly.TryParseExact(value, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return date;
        }

        throw new InvalidDataException(
            $"The Oregon OREA roster '{Path.GetFileName(sourcePath)}' has an invalid {column} value '{value}' near line {lineNumber}.");
    }

    private sealed record OregonIndividualRecord(
        string Name,
        string LicenseTypeCode,
        string LicenseNumber,
        DateOnly? ExpirationDate,
        string Status,
        string BusinessLicenseNumber,
        DateOnly? AssociatedDate,
        string KeyManagerName,
        string KeyManagerNumber,
        long LineNumber);

    private sealed class OregonLicenseeBuilder(RegulatoryLicensee licensee)
    {
        private readonly Dictionary<string, RegulatoryLicensee> _licensesByType =
            new(StringComparer.OrdinalIgnoreCase)
            {
                [licensee.LicenseType!] = licensee
            };

        public RegulatoryLicensee Licensee { get; } = licensee;
        public bool HasLicenseType(string licenseType) => _licensesByType.ContainsKey(licenseType);

        public void Add(RegulatoryLicensee candidate, long lineNumber)
        {
            if (!string.Equals(Licensee.Name, candidate.Name, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"The Oregon OREA roster has conflicting names for license '{candidate.LicenseNumber}' near line {lineNumber}.");
            }

            if (!string.Equals(Licensee.LicenseStatus, candidate.LicenseStatus, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"The Oregon OREA roster has conflicting statuses for license '{candidate.LicenseNumber}' near line {lineNumber}.");
            }

            if (!_licensesByType.TryGetValue(candidate.LicenseType!, out var existing))
            {
                _licensesByType.Add(candidate.LicenseType!, candidate);
                return;
            }

            if (Nullable.Compare(candidate.LicenseRenewalDate, existing.LicenseRenewalDate) > 0)
            {
                _licensesByType[candidate.LicenseType!] = candidate;
            }
        }

        public RegulatoryLicensee Build()
        {
            var orderedTypes = LicenseTypePriority.Where(_licensesByType.ContainsKey).ToArray();
            var preferredLicense = _licensesByType[orderedTypes[0]];
            var renewalDate = _licensesByType.Values
                .Where(candidate => candidate.LicenseRenewalDate is not null)
                .Select(candidate => candidate.LicenseRenewalDate!.Value)
                .DefaultIfEmpty()
                .Max();

            Licensee.LicenseType = string.Join(", ", orderedTypes);
            Licensee.LicenseLevel = preferredLicense.LicenseLevel;
            Licensee.LicenseRenewalDate = renewalDate == default ? null : renewalDate;
            return Licensee;
        }
    }

    private sealed class OregonFirmBuilder(RegulatoryFirm firm)
    {
        private readonly HashSet<string> _keyManagerNumbers = new(StringComparer.OrdinalIgnoreCase);

        public RegulatoryFirm Firm { get; } = firm;

        public void AddKeyManager(string licenseNumber)
        {
            if (licenseNumber.Length > 0)
            {
                _keyManagerNumbers.Add(licenseNumber);
            }
        }

        public void SetQualifyingBrokerName(IReadOnlyDictionary<string, OregonLicenseeBuilder> licensees)
        {
            if (_keyManagerNumbers.Count == 1
                && licensees.TryGetValue(_keyManagerNumbers.Single(), out var manager)
                && manager.HasLicenseType("Principal Broker"))
            {
                Firm.QualifyingBrokerName = manager.Licensee.Name;
            }
        }
    }

    private sealed class OregonAffiliationBuilder(OregonLicenseeBuilder licensee, OregonFirmBuilder firm)
    {
        private readonly HashSet<string> _keyManagerNumbers = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _keyManagerNames = new(StringComparer.OrdinalIgnoreCase);
        private DateOnly? _relationshipEffectiveDate;

        public RegulatoryLicensee Licensee { get; } = licensee.Licensee;
        public OregonFirmBuilder Firm { get; } = firm;

        public void Add(OregonIndividualRecord row)
        {
            if (row.KeyManagerNumber.Length > 0)
            {
                _keyManagerNumbers.Add(row.KeyManagerNumber);
            }

            if (row.KeyManagerName.Length > 0)
            {
                _keyManagerNames.Add(row.KeyManagerName);
            }

            var effectiveDate = row.AssociatedDate;
            if (effectiveDate is not null
                && (_relationshipEffectiveDate is null || effectiveDate.Value < _relationshipEffectiveDate.Value))
            {
                _relationshipEffectiveDate = effectiveDate;
            }
        }

        public RegulatoryFirmLicenseeAffiliation Build(Guid snapshotId)
        {
            var singleManager = _keyManagerNumbers.Count == 1;
            return new RegulatoryFirmLicenseeAffiliation
            {
                SnapshotId = snapshotId,
                Firm = Firm.Firm,
                Licensee = Licensee,
                RelationshipEffectiveDate = _relationshipEffectiveDate,
                IsBrokerInCharge = singleManager
                    && string.Equals(_keyManagerNumbers.Single(), Licensee.LicenseNumber, StringComparison.OrdinalIgnoreCase),
                BrokerInChargeName = singleManager && _keyManagerNames.Count == 1
                    ? _keyManagerNames.Single()
                    : null
            };
        }
    }

}
