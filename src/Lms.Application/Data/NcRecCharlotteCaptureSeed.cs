using System.Globalization;
using System.Text.Json;
using Lms.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Lms.Application.Data;

/// <summary>
/// Imports the live NCREC crawl of the first 65 Charlotte firms (firm search limited to 65 results),
/// including each firm's broker-in-charge detail records and the licensees supervised by every BIC.
/// </summary>
public static class NcRecCharlotteCaptureSeed
{
    private const string SourceUrl = "https://license.ncrec.gov/ncrec/oecgi3.exe/O4W_FIRM_SEARCH_NEW";
    private const string CaptureFileName = "Charlotte - 65 Firms - NCREC Roster Capture 2026-09-17.json";
    private static readonly DateTime RetrievedAtUtc = new(2026, 9, 17, 22, 8, 0, DateTimeKind.Utc);

    public static async Task SeedAsync(ApplicationDbContext dbContext, string? workspaceRoot = null)
    {
        if (await dbContext.RegulatoryDataSnapshots.AnyAsync(snapshot =>
                snapshot.State == "North Carolina" && snapshot.SourceUrl == SourceUrl && snapshot.RetrievedAtUtc == RetrievedAtUtc))
        {
            return;
        }

        var root = CaptureSeedRoot.Resolve(workspaceRoot);
        if (root is null)
        {
            return;
        }

        var capturePath = Path.Combine(root, "docs", "NORTH CAROLINA", "LICENSING", "TEXTs", "BROKER ROSTER", CaptureFileName);
        if (!File.Exists(capturePath))
        {
            return;
        }

        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(capturePath));
        var rootElement = document.RootElement;

        var snapshot = new RegulatoryDataSnapshot
        {
            State = "North Carolina",
            SourceName = "North Carolina Real Estate Commission public licensing database",
            SourceUrl = SourceUrl,
            RetrievedAtUtc = RetrievedAtUtc,
            ParserVersion = rootElement.TryGetProperty("parserVersion", out var parser) ? parser.GetString() : "ncrec-charlotte65-v1",
            Status = RegulatorySnapshotStatuses.Approved,
            ReviewedAtUtc = RetrievedAtUtc
        };

        var firms = new Dictionary<string, RegulatoryFirm>(StringComparer.OrdinalIgnoreCase);
        var firmsByName = new Dictionary<string, RegulatoryFirm>(StringComparer.OrdinalIgnoreCase);
        foreach (var firmElement in rootElement.GetProperty("firms").EnumerateArray())
        {
            var licenseNumber = GetString(firmElement, "licenseNumber");
            var name = GetString(firmElement, "name");
            if (string.IsNullOrWhiteSpace(licenseNumber) || string.IsNullOrWhiteSpace(name) || firms.ContainsKey(licenseNumber))
            {
                continue;
            }

            var firm = new RegulatoryFirm
            {
                Snapshot = snapshot,
                State = "North Carolina",
                LicenseNumber = licenseNumber,
                LegalName = name,
                LicenseStatus = GetString(firmElement, "status"),
                Address = Truncate(GetString(firmElement, "address"), 500),
                QualifyingBrokerName = Truncate(GetString(firmElement, "qualifyingBroker"), 160)
            };
            firms[licenseNumber] = firm;
            firmsByName[name] = firm;
        }

        var licensees = new Dictionary<string, RegulatoryLicensee>(StringComparer.OrdinalIgnoreCase);
        foreach (var licenseeElement in rootElement.GetProperty("licensees").EnumerateArray())
        {
            var licenseNumber = GetString(licenseeElement, "licenseNumber");
            var name = GetString(licenseeElement, "name");
            if (string.IsNullOrWhiteSpace(licenseNumber) || string.IsNullOrWhiteSpace(name) || licensees.ContainsKey(licenseNumber))
            {
                continue;
            }

            licensees[licenseNumber] = new RegulatoryLicensee
            {
                Snapshot = snapshot,
                State = "North Carolina",
                LicenseNumber = licenseNumber,
                Name = name,
                LicenseType = GetString(licenseeElement, "type"),
                LicenseLevel = GetString(licenseeElement, "level"),
                LicenseStatus = GetString(licenseeElement, "status"),
                LicenseIssuedDate = ParseDate(GetString(licenseeElement, "issueDate")),
                IsBicEligible = licenseeElement.TryGetProperty("bicEligible", out var eligible) && eligible.ValueKind == JsonValueKind.True
            };
        }

        var affiliations = new List<RegulatoryFirmLicenseeAffiliation>();
        var affiliationKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var affiliationElement in rootElement.GetProperty("affiliations").EnumerateArray())
        {
            var firmNumber = GetString(affiliationElement, "firm");
            var licenseNumber = GetString(affiliationElement, "licensee");
            if (firmNumber is null || licenseNumber is null
                || !firms.TryGetValue(firmNumber, out var firm)
                || !licensees.TryGetValue(licenseNumber, out var licensee)
                || !affiliationKeys.Add($"{firmNumber}|{licenseNumber}"))
            {
                continue;
            }

            affiliations.Add(new RegulatoryFirmLicenseeAffiliation
            {
                Snapshot = snapshot,
                Firm = firm,
                Licensee = licensee,
                IsBrokerInCharge = true,
                BrokerInChargeName = Truncate(licensee.Name, 160)
            });
        }

        foreach (var rosterElement in rootElement.GetProperty("supervisedRosters").EnumerateArray())
        {
            var bicName = GetString(rosterElement, "bicName");
            foreach (var rowElement in rosterElement.GetProperty("rows").EnumerateArray())
            {
                var licenseNumber = GetString(rowElement, "licenseNumber");
                var name = GetString(rowElement, "name");
                if (string.IsNullOrWhiteSpace(licenseNumber) || string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                if (!licensees.TryGetValue(licenseNumber, out var licensee))
                {
                    licensee = new RegulatoryLicensee
                    {
                        Snapshot = snapshot,
                        State = "North Carolina",
                        LicenseNumber = licenseNumber,
                        Name = name,
                        LicenseType = "BROKER",
                        LicenseLevel = GetString(rowElement, "level"),
                        LicenseStatus = "ACTIVE"
                    };
                    licensees[licenseNumber] = licensee;
                }

                licensee.LicenseRenewalDate ??= ParseDate(GetString(rowElement, "renewDate"));
                licensee.UpdateHours ??= ParseHours(GetString(rowElement, "updateHours"));
                licensee.ElectiveHours ??= ParseHours(GetString(rowElement, "electiveHours"));
                licensee.PostlicensingHours ??= ParseHours(GetString(rowElement, "plHours"));

                var firmName = GetString(rowElement, "firmName");
                if (firmName is not null
                    && firmsByName.TryGetValue(firmName, out var firm)
                    && affiliationKeys.Add($"{firm.LicenseNumber}|{licenseNumber}"))
                {
                    affiliations.Add(new RegulatoryFirmLicenseeAffiliation
                    {
                        Snapshot = snapshot,
                        Firm = firm,
                        Licensee = licensee,
                        IsBrokerInCharge = false,
                        BrokerInChargeName = Truncate(bicName, 160)
                    });
                }
            }
        }

        if (firms.Count == 0 || licensees.Count == 0)
        {
            return;
        }

        dbContext.RegulatoryDataSnapshots.Add(snapshot);
        dbContext.RegulatoryFirms.AddRange(firms.Values);
        dbContext.RegulatoryLicensees.AddRange(licensees.Values);
        dbContext.RegulatoryFirmLicenseeAffiliations.AddRange(affiliations);
        await dbContext.SaveChangesAsync();
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var value = property.GetString();
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static DateOnly? ParseDate(string? value)
        => DateOnly.TryParseExact(value, "MM/dd/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;

    private static int? ParseHours(string? value)
        => decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var hours) ? (int)hours : null;

    private static string? Truncate(string? value, int maxLength)
        => value is { Length: > 0 } ? (value.Length <= maxLength ? value : value[..maxLength]) : null;
}
