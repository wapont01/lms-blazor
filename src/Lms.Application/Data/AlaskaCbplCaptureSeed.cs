using System.Globalization;
using System.Text;
using Lms.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualBasic.FileIO;

namespace Lms.Application.Data;

public static class AlaskaCbplCaptureSeed
{
    private const string State = "Alaska";
    private const string SourceName = "Alaska CBPL Professional License Database Download - Real Estate";
    private const string SourceUrl = "https://www.commerce.alaska.gov/cbp/main/";
    private const string FileName = "ProfessionalLicenseDownload.csv";
    private const string ParserVersion = "alaska-cbpl-professional-license-csv-v1-2026-10-03";
    private static readonly DateTime RetrievedAtUtc = new(2026, 10, 3, 0, 0, 0, DateTimeKind.Utc);

    public static async Task SeedAsync(ApplicationDbContext dbContext, string? workspaceRoot = null)
    {
        ArgumentNullException.ThrowIfNull(dbContext);

        var root = CaptureSeedRoot.Resolve(workspaceRoot);
        if (root is null)
        {
            return;
        }

        var sourcePath = Path.Combine(root, "docs", "ALASKA", "XLXS", FileName);
        if (!File.Exists(sourcePath))
        {
            return;
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
        var firms = new Dictionary<string, RegulatoryFirm>(StringComparer.OrdinalIgnoreCase);
        var licensees = new Dictionary<string, RegulatoryLicensee>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in ReadRows(sourcePath))
        {
            if (row.Program == "Real Estate" && row.Status is "Active" or "Probation")
            {
                var licensee = new RegulatoryLicensee
                {
                    SnapshotId = snapshot.Id,
                    State = State,
                    LicenseNumber = row.LicenseNumber,
                    Name = row.Owners,
                    LicenseType = row.LicenseType,
                    LicenseLevel = row.LicenseType,
                    LicenseStatus = row.Status,
                    LicenseIssuedDate = row.IssuedDate,
                    OriginalLicenseDate = row.IssuedDate,
                    LicenseRenewalDate = row.ExpirationDate
                };
                if (!licensees.TryAdd(licensee.LicenseNumber, licensee))
                {
                    throw new InvalidDataException(
                        $"The Alaska CBPL CSV contains duplicate current real-estate license number '{licensee.LicenseNumber}'.");
                }
            }
            else if (row.Program == "Real Estate Office" && row.Status == "Office Open")
            {
                var firm = new RegulatoryFirm
                {
                    SnapshotId = snapshot.Id,
                    State = State,
                    LicenseNumber = row.LicenseNumber,
                    LegalName = row.Owners.Length > 0 ? row.Owners : row.Dba,
                    TradeName = row.Owners.Length > 0 ? row.Dba : null,
                    LicenseStatus = row.Status,
                    LicenseIssuedDate = row.IssuedDate,
                    LicenseRenewalDate = row.ExpirationDate,
                    Address = row.Address
                };
                if (!firms.TryAdd(firm.LicenseNumber, firm))
                {
                    throw new InvalidDataException(
                        $"The Alaska CBPL CSV contains duplicate open real-estate office license number '{firm.LicenseNumber}'.");
                }
            }
        }

        if (licensees.Count == 0 || firms.Count == 0)
        {
            throw new InvalidDataException("The Alaska CBPL CSV did not contain current real-estate licensees and open offices.");
        }

        dbContext.RegulatoryDataSnapshots.Add(snapshot);
        dbContext.RegulatoryFirms.AddRange(firms.Values);
        dbContext.RegulatoryLicensees.AddRange(licensees.Values);
        await dbContext.SaveChangesAsync();
    }

    private static IEnumerable<AlaskaLicenseRecord> ReadRows(string sourcePath)
    {
        using var parser = new TextFieldParser(sourcePath, Encoding.UTF8, detectEncoding: true)
        {
            TextFieldType = FieldType.Delimited,
            HasFieldsEnclosedInQuotes = true,
            TrimWhiteSpace = false
        };
        parser.SetDelimiters(",");

        var headerFields = parser.ReadFields()
            ?? throw new InvalidDataException("The Alaska CBPL CSV is empty.");
        var columns = headerFields
            .Select((name, index) => (Name: name.Trim(), Index: index))
            .ToDictionary(column => column.Name, column => column.Index, StringComparer.OrdinalIgnoreCase);
        var requiredColumns = new[]
        {
            "Program", "ProfType", "LicenseNum", "DBA", "Owners", "Status", "DateIssued",
            "DateExpired", "ADDRESS1", "ADDRESS2", "CITY", "STATE", "ZIP"
        };
        var missingColumns = requiredColumns.Where(column => !columns.ContainsKey(column)).ToArray();
        if (missingColumns.Length > 0)
        {
            throw new InvalidDataException(
                $"The Alaska CBPL CSV is missing required columns: {string.Join(", ", missingColumns)}.");
        }

        while (!parser.EndOfData)
        {
            var fields = parser.ReadFields()
                ?? throw new InvalidDataException($"The Alaska CBPL CSV contains an empty record near line {parser.LineNumber}.");
            var program = GetField(fields, columns, "Program");
            if (program is not ("Real Estate" or "Real Estate Office"))
            {
                continue;
            }

            var status = GetField(fields, columns, "Status");
            if ((program == "Real Estate" && status is not ("Active" or "Probation"))
                || (program == "Real Estate Office" && status != "Office Open"))
            {
                continue;
            }

            var licenseNumber = GetField(fields, columns, "LicenseNum");
            var owners = GetField(fields, columns, "Owners");
            var dba = GetField(fields, columns, "DBA");
            if (licenseNumber.Length == 0
                || (program == "Real Estate" && owners.Length == 0)
                || (program == "Real Estate Office" && owners.Length == 0 && dba.Length == 0))
            {
                throw new InvalidDataException(
                    $"The Alaska CBPL CSV has a current real-estate record without a license number or usable name near line {parser.LineNumber}.");
            }

            yield return new AlaskaLicenseRecord(
                program,
                GetField(fields, columns, "ProfType"),
                licenseNumber,
                dba,
                owners,
                status,
                ParseDate(GetField(fields, columns, "DateIssued"), "DateIssued", parser.LineNumber),
                ParseDate(GetField(fields, columns, "DateExpired"), "DateExpired", parser.LineNumber),
                BuildAddress(fields, columns));
        }
    }

    private static string GetField(string[] fields, IReadOnlyDictionary<string, int> columns, string name)
    {
        var index = columns[name];
        if (index >= fields.Length)
        {
            throw new InvalidDataException($"The Alaska CBPL CSV row does not contain the '{name}' column.");
        }

        return fields[index].Trim();
    }

    private static DateOnly? ParseDate(string value, string column, long lineNumber)
    {
        if (value.Length == 0)
        {
            return null;
        }

        string[] formats = ["M/d/yyyy", "MM/dd/yyyy", "M/d/yy", "MM/dd/yy"];
        if (DateOnly.TryParseExact(
                value,
                formats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var date))
        {
            return date;
        }

        throw new InvalidDataException(
            $"The Alaska CBPL CSV has an invalid {column} value '{value}' near line {lineNumber}.");
    }

    private static string? BuildAddress(string[] fields, IReadOnlyDictionary<string, int> columns)
    {
        var parts = new[]
        {
            GetField(fields, columns, "ADDRESS1"),
            GetField(fields, columns, "ADDRESS2"),
            GetField(fields, columns, "CITY"),
            GetField(fields, columns, "STATE"),
            GetField(fields, columns, "ZIP")
        }.Where(part => part.Length > 0);

        var address = string.Join(", ", parts);
        return address.Length == 0 ? null : address;
    }

    private sealed record AlaskaLicenseRecord(
        string Program,
        string LicenseType,
        string LicenseNumber,
        string Dba,
        string Owners,
        string Status,
        DateOnly? IssuedDate,
        DateOnly? ExpirationDate,
        string? Address);
}
