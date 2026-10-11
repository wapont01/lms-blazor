using System.Globalization;
using Microsoft.VisualBasic.FileIO;
using Lms.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Lms.Application.Data;

public static class MinnesotaPulseCaptureSeed
{
    private const string State = "Minnesota";
    private const string SourceName = "Minnesota PULSE Saint Paul real estate license sample";
    private const string SourceUrl = "https://www.pulseportal.com/Inquiry/searchEntityForProducerInfoSimple.do?method=menuInit&criteriaNextAction=producerInformationSimple&moduleCode=PRDCR_LIC&serviceCode=RQST_CNSMR&accessCode=MN&functionCode=RE";
    private const string LicenseesFileName = "Minnesota_Saint_Paul_Pulse_Real_Estate_Licensees_20261010.csv";
    private const string FirmsFileName = "Minnesota_Saint_Paul_Pulse_Real_Estate_Firms_20261010.csv";
    private const string ParserVersion = "mn-pulse-saint-paul-page1-sample-v1-2026-10-10";
    private static readonly DateTime RetrievedAtUtc = new(2026, 10, 10, 21, 12, 0, DateTimeKind.Utc);

    public static async Task SeedAsync(ApplicationDbContext dbContext, string? workspaceRoot = null)
    {
        ArgumentNullException.ThrowIfNull(dbContext);

        var root = CaptureSeedRoot.Resolve(workspaceRoot);
        if (root is null)
        {
            return;
        }

        var sourceDirectory = Path.Combine(root, "docs", "MINNESOTA", "XLXS");
        if (!Directory.Exists(sourceDirectory))
        {
            return;
        }

        var licenseesPath = Path.Combine(sourceDirectory, LicenseesFileName);
        var firmsPath = Path.Combine(sourceDirectory, FirmsFileName);
        if (!File.Exists(licenseesPath) || !File.Exists(firmsPath))
        {
            throw new FileNotFoundException(
                "Both dated Minnesota PULSE licensee and firm sample files are required.",
                !File.Exists(licenseesPath) ? licenseesPath : firmsPath);
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

        var firms = ReadCsv(
            firmsPath,
            "LicenseNumber",
            "LegalName",
            "TradeName",
            "LicenseType",
            "LicenseStatus",
            "OriginalIssueDate",
            "ExpirationDate",
            "Address");
        var firmsByLicenseNumber = new Dictionary<string, RegulatoryFirm>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in firms)
        {
            if (!string.Equals(row["LicenseType"], "Real Estate Company", StringComparison.Ordinal)
                || !string.Equals(row["LicenseStatus"], "Active", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("The Minnesota firm sample contains an unsupported type or non-active record.");
            }

            var firm = new RegulatoryFirm
            {
                Snapshot = snapshot,
                State = State,
                LicenseNumber = row["LicenseNumber"],
                LegalName = row["LegalName"],
                TradeName = EmptyToNull(row["TradeName"]),
                LicenseStatus = row["LicenseStatus"],
                LicenseIssuedDate = ParseDate(row["OriginalIssueDate"]),
                LicenseRenewalDate = ParseDate(row["ExpirationDate"]),
                Address = EmptyToNull(row["Address"])
            };
            if (!firmsByLicenseNumber.TryAdd(firm.LicenseNumber, firm))
            {
                throw new InvalidDataException($"The Minnesota firm sample repeats license number '{firm.LicenseNumber}'.");
            }
        }

        var licensees = new Dictionary<string, RegulatoryLicensee>(StringComparer.OrdinalIgnoreCase);
        var affiliations = new Dictionary<string, RegulatoryFirmLicenseeAffiliation>(StringComparer.OrdinalIgnoreCase);
        var licenseeRows = ReadCsv(
            licenseesPath,
            "LicenseNumber",
            "Name",
            "SourceLicenseType",
            "LicenseStatus",
            "OriginalIssueDate",
            "ExpirationDate",
            "RelatedLicenseNumber",
            "RelatedLicenseType",
            "RelatedLicenseName");

        foreach (var row in licenseeRows)
        {
            if (!string.Equals(row["LicenseStatus"], "Active", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("The Minnesota individual sample contains a non-active record.");
            }

            var sourceType = row["SourceLicenseType"];
            var licenseLevel = sourceType switch
            {
                "Resident Real Est Salesperson" => "Salesperson",
                "Resident Real Estate Broker" => "Broker",
                _ => throw new InvalidDataException($"Unsupported Minnesota individual license type '{sourceType}'.")
            };

            var licensee = new RegulatoryLicensee
            {
                Snapshot = snapshot,
                State = State,
                LicenseNumber = row["LicenseNumber"],
                Name = row["Name"],
                LicenseType = sourceType,
                LicenseLevel = licenseLevel,
                LicenseStatus = row["LicenseStatus"],
                LicenseIssuedDate = ParseDate(row["OriginalIssueDate"]),
                OriginalLicenseDate = ParseDate(row["OriginalIssueDate"]),
                LicenseRenewalDate = ParseDate(row["ExpirationDate"])
            };
            if (!licensees.TryAdd(licensee.LicenseNumber, licensee))
            {
                throw new InvalidDataException($"The Minnesota individual sample repeats license number '{licensee.LicenseNumber}'.");
            }

            var relatedLicenseNumber = row["RelatedLicenseNumber"];
            if (string.IsNullOrWhiteSpace(relatedLicenseNumber))
            {
                continue;
            }

            if (!firmsByLicenseNumber.TryGetValue(relatedLicenseNumber, out var relatedFirm)
                || !string.Equals(row["RelatedLicenseType"], "Real Estate Company", StringComparison.Ordinal)
                || !string.Equals(row["RelatedLicenseName"], relatedFirm.LegalName, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"Minnesota licensee '{licensee.LicenseNumber}' has an unverified related firm '{relatedLicenseNumber}'.");
            }

            licensee.RelatedLicenseNumber = relatedFirm.LicenseNumber;
            licensee.RelatedLicenseType = row["RelatedLicenseType"];
            licensee.RelatedLicenseName = relatedFirm.LegalName;
            affiliations.Add(licensee.LicenseNumber, new RegulatoryFirmLicenseeAffiliation
            {
                Snapshot = snapshot,
                Firm = relatedFirm,
                Licensee = licensee,
                IsBrokerInCharge = false
            });
        }

        if (licensees.Count == 0 || firmsByLicenseNumber.Count == 0)
        {
            throw new InvalidDataException("The Minnesota PULSE sample contains no usable licensees or firms.");
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        dbContext.RegulatoryDataSnapshots.Add(snapshot);
        dbContext.RegulatoryFirms.AddRange(firmsByLicenseNumber.Values);
        dbContext.RegulatoryLicensees.AddRange(licensees.Values);
        dbContext.RegulatoryFirmLicenseeAffiliations.AddRange(affiliations.Values);
        await dbContext.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    private static List<Dictionary<string, string>> ReadCsv(string path, params string[] requiredColumns)
    {
        using var parser = new TextFieldParser(path)
        {
            TextFieldType = FieldType.Delimited,
            HasFieldsEnclosedInQuotes = true,
            TrimWhiteSpace = false
        };
        parser.SetDelimiters(",");

        var headers = parser.ReadFields()
            ?? throw new InvalidDataException($"The Minnesota PULSE CSV has no header row: {path}");
        var indexes = headers
            .Select((header, index) => (header, index))
            .ToDictionary(pair => pair.header, pair => pair.index, StringComparer.OrdinalIgnoreCase);
        foreach (var column in requiredColumns)
        {
            if (!indexes.ContainsKey(column))
            {
                throw new InvalidDataException($"The Minnesota PULSE CSV is missing required column '{column}': {path}");
            }
        }

        var records = new List<Dictionary<string, string>>();
        while (!parser.EndOfData)
        {
            var fields = parser.ReadFields()
                ?? throw new InvalidDataException($"The Minnesota PULSE CSV has an empty row: {path}");
            if (fields.Length != headers.Length)
            {
                throw new InvalidDataException($"The Minnesota PULSE CSV has an invalid row near line {parser.LineNumber}: {path}");
            }

            records.Add(indexes.ToDictionary(
                pair => pair.Key,
                pair => fields[pair.Value].Trim(),
                StringComparer.OrdinalIgnoreCase));
        }

        return records;
    }

    private static DateOnly? ParseDate(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : DateOnly.ParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string? EmptyToNull(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
