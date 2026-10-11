using System.Globalization;
using System.Text;
using Lms.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualBasic.FileIO;

namespace Lms.Application.Data;

public static class TennesseeCaptureSeed
{
    private const string State = "Tennessee";
    private const string SourceName = "Tennessee Department of Commerce & Insurance Verify Public Search";
    private const string SourceUrl = "https://search.cloud.commerce.tn.gov/";
    private const string IndividualLicenseesFileName = "Tennessee_Nashville_Active_Individual_Licensees_20261010.csv";
    private const string RealEstateBrokersFileName = "Tennessee_Nashville_Active_Real_Estate_Brokers_20261010.csv";
    private const string FirmsFileName = "Tennessee_Nashville_Active_Firm_Campaign_20261010.csv";
    private const string ParserVersion = "tn-verify-nashville-active-sample-v1-2026-10-10";
    private static readonly DateTime RetrievedAtUtc = new(2026, 10, 10, 0, 0, 0, DateTimeKind.Utc);

    public static async Task SeedAsync(ApplicationDbContext dbContext, string? workspaceRoot = null)
    {
        ArgumentNullException.ThrowIfNull(dbContext);

        var root = CaptureSeedRoot.Resolve(workspaceRoot);
        if (root is null)
        {
            return;
        }

        var sourceDirectory = Path.Combine(root, "docs", "TENNESSEE", "XLXS");
        if (!Directory.Exists(sourceDirectory))
        {
            return;
        }

        var individualLicenseesPath = Path.Combine(sourceDirectory, IndividualLicenseesFileName);
        var realEstateBrokersPath = Path.Combine(sourceDirectory, RealEstateBrokersFileName);
        var firmsPath = Path.Combine(sourceDirectory, FirmsFileName);
        var missingFile = new[] { individualLicenseesPath, realEstateBrokersPath, firmsPath }
            .FirstOrDefault(path => !File.Exists(path));
        if (missingFile is not null)
        {
            throw new FileNotFoundException("All dated Tennessee individual and firm sample files are required.", missingFile);
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

        var firms = ReadFirms(firmsPath, snapshot.Id);
        var licensees = new Dictionary<string, RegulatoryLicensee>(StringComparer.OrdinalIgnoreCase);
        ReadLicensees(individualLicenseesPath, "Affiliate Broker", snapshot.Id, licensees);
        ReadLicensees(realEstateBrokersPath, "Real Estate Broker", snapshot.Id, licensees);

        if (firms.Count != 20 || licensees.Count != 40)
        {
            throw new InvalidDataException(
                $"The Tennessee capture expected 20 active firms and 40 active individual licensees; found {firms.Count} and {licensees.Count}.");
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        dbContext.RegulatoryDataSnapshots.Add(snapshot);
        dbContext.RegulatoryFirms.AddRange(firms.Values);
        dbContext.RegulatoryLicensees.AddRange(licensees.Values);
        await dbContext.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    private static Dictionary<string, RegulatoryFirm> ReadFirms(string sourcePath, Guid snapshotId)
    {
        var rows = ReadCsv(
            sourcePath,
            "State",
            "LicenseNumber",
            "EntityName",
            "SourceLicenseType",
            "SourceStatus",
            "QueryCity",
            "BusinessAddress",
            "LicenseExpiryOrRenewal");
        var firms = new Dictionary<string, RegulatoryFirm>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in rows)
        {
            if (!string.Equals(row["State"], State, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(row["SourceLicenseType"], "Real Estate Firm", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(row["SourceStatus"], "Active", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(row["QueryCity"], "Nashville", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("The Tennessee firm sample contains a record outside the active Nashville firm query.");
            }

            var firm = new RegulatoryFirm
            {
                SnapshotId = snapshotId,
                State = State,
                LicenseNumber = row["LicenseNumber"],
                LegalName = row["EntityName"],
                LicenseStatus = row["SourceStatus"],
                LicenseRenewalDate = ParseDate(row["LicenseExpiryOrRenewal"], sourcePath),
                Address = EmptyToNull(row["BusinessAddress"])
            };
            if (!firms.TryAdd(firm.LicenseNumber, firm))
            {
                throw new InvalidDataException($"The Tennessee firm sample repeats license number '{firm.LicenseNumber}'.");
            }
        }

        return firms;
    }

    private static void ReadLicensees(
        string sourcePath,
        string expectedLicenseType,
        Guid snapshotId,
        IDictionary<string, RegulatoryLicensee> licensees)
    {
        var rows = ReadCsv(
            sourcePath,
            "Name",
            "LicenseType",
            "LicenseNumber",
            "LicenseStatus",
            "ExpirationDate",
            "City",
            "State");

        if (rows.Count != 20)
        {
            throw new InvalidDataException(
                $"The Tennessee {expectedLicenseType} sample expected 20 records but contained {rows.Count}.");
        }

        foreach (var row in rows)
        {
            if (!string.Equals(row["LicenseType"], expectedLicenseType, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(row["LicenseStatus"], "Active", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(row["City"], "NASHVILLE", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(row["State"], "TN", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"The Tennessee {expectedLicenseType} sample contains a record outside its active Nashville query.");
            }

            var licensee = new RegulatoryLicensee
            {
                SnapshotId = snapshotId,
                State = State,
                LicenseNumber = row["LicenseNumber"],
                Name = row["Name"],
                LicenseType = row["LicenseType"],
                LicenseLevel = row["LicenseType"],
                LicenseStatus = row["LicenseStatus"],
                LicenseRenewalDate = ParseDate(row["ExpirationDate"], sourcePath)
            };
            if (!licensees.TryAdd(licensee.LicenseNumber, licensee))
            {
                throw new InvalidDataException(
                    $"The Tennessee individual sample repeats license number '{licensee.LicenseNumber}'.");
            }
        }
    }

    private static List<Dictionary<string, string>> ReadCsv(string path, params string[] requiredColumns)
    {
        using var parser = new TextFieldParser(path, Encoding.UTF8, detectEncoding: true)
        {
            TextFieldType = FieldType.Delimited,
            HasFieldsEnclosedInQuotes = true,
            TrimWhiteSpace = false
        };
        parser.SetDelimiters(",");

        var headers = parser.ReadFields()
            ?? throw new InvalidDataException($"The Tennessee CSV has no header row: {path}");
        var indexes = headers
            .Select((header, index) => (Name: header.Trim(), Index: index))
            .ToDictionary(column => column.Name, column => column.Index, StringComparer.OrdinalIgnoreCase);
        foreach (var column in requiredColumns)
        {
            if (!indexes.ContainsKey(column))
            {
                throw new InvalidDataException($"The Tennessee CSV is missing required column '{column}': {path}");
            }
        }

        var records = new List<Dictionary<string, string>>();
        while (!parser.EndOfData)
        {
            var fields = parser.ReadFields()
                ?? throw new InvalidDataException($"The Tennessee CSV contains an empty row near line {parser.LineNumber}: {path}");
            if (fields.Length != headers.Length)
            {
                throw new InvalidDataException($"The Tennessee CSV has an invalid row near line {parser.LineNumber}: {path}");
            }

            var row = indexes.ToDictionary(
                column => column.Key,
                column => fields[column.Value].Trim(),
                StringComparer.OrdinalIgnoreCase);
            if (requiredColumns.Any(column => string.IsNullOrWhiteSpace(row[column])))
            {
                throw new InvalidDataException($"The Tennessee CSV has an empty required value near line {parser.LineNumber}: {path}");
            }

            records.Add(row);
        }

        return records;
    }

    private static DateOnly ParseDate(string value, string sourcePath)
    {
        string[] formats = ["M/d/yyyy", "MM/dd/yyyy"];
        if (DateOnly.TryParseExact(value, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return date;
        }

        throw new InvalidDataException($"The Tennessee CSV has an invalid date '{value}': {sourcePath}");
    }

    private static string? EmptyToNull(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
