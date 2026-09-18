using System.Globalization;
using System.Text.Json;
using Lms.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Lms.Application.Data;

/// <summary>
/// Imports every incremental "what's new" NCREC capture produced by
/// tools/nc_ncrec_capture.py --new (newly issued NC licenses found by probing
/// past the highest previously captured license number).
/// </summary>
public static class NcRecNewLicenseCaptureSeed
{
    private const string SourceUrl = "https://license.ncrec.gov/ncrec/oecgi3.exe/O4W_WRAPPER?ID=O4W_LIC_SHOW_DETAIL_NEW";

    public static async Task SeedAsync(ApplicationDbContext dbContext, string? workspaceRoot = null)
    {
        var root = CaptureSeedRoot.Resolve(workspaceRoot);
        if (root is null)
        {
            return;
        }

        var rosterDirectory = Path.Combine(root, "docs", "NORTH CAROLINA", "LICENSING", "TEXTs", "BROKER ROSTER");
        if (!Directory.Exists(rosterDirectory))
        {
            return;
        }

        foreach (var capturePath in Directory.GetFiles(rosterDirectory, "NCREC New Licenses From *.json").OrderBy(path => path, StringComparer.Ordinal))
        {
            await ImportCaptureAsync(dbContext, capturePath);
        }
    }

    private static async Task ImportCaptureAsync(ApplicationDbContext dbContext, string capturePath)
    {
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(capturePath));
        var rootElement = document.RootElement;
        if (!rootElement.TryGetProperty("retrievedAtUtc", out var retrievedProperty)
            || !DateTime.TryParse(retrievedProperty.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var retrievedAtUtc))
        {
            return;
        }

        retrievedAtUtc = DateTime.SpecifyKind(retrievedAtUtc, DateTimeKind.Utc);
        if (await dbContext.RegulatoryDataSnapshots.AnyAsync(snapshot =>
                snapshot.State == "North Carolina" && snapshot.SourceUrl == SourceUrl && snapshot.RetrievedAtUtc == retrievedAtUtc))
        {
            return;
        }

        var snapshot = new RegulatoryDataSnapshot
        {
            State = "North Carolina",
            SourceName = "North Carolina Real Estate Commission public licensing database",
            SourceUrl = SourceUrl,
            RetrievedAtUtc = retrievedAtUtc,
            ParserVersion = rootElement.TryGetProperty("parserVersion", out var parser) ? parser.GetString() : "ncrec-whats-new-v1",
            Status = RegulatorySnapshotStatuses.Approved,
            ReviewedAtUtc = retrievedAtUtc
        };

        var licensees = new Dictionary<string, RegulatoryLicensee>(StringComparer.OrdinalIgnoreCase);
        var firms = new Dictionary<string, RegulatoryFirm>(StringComparer.OrdinalIgnoreCase);
        var affiliations = new List<RegulatoryFirmLicenseeAffiliation>();
        foreach (var licenseeElement in rootElement.GetProperty("licensees").EnumerateArray())
        {
            var licenseNumber = GetString(licenseeElement, "licenseNumber");
            var name = GetString(licenseeElement, "name");
            if (string.IsNullOrWhiteSpace(licenseNumber) || string.IsNullOrWhiteSpace(name) || licensees.ContainsKey(licenseNumber))
            {
                continue;
            }

            var licensee = new RegulatoryLicensee
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
            licensees[licenseNumber] = licensee;

            var firmNumber = GetString(licenseeElement, "primaryFirm");
            var firmName = GetString(licenseeElement, "primaryFirmName");
            if (firmNumber is null || firmName is null)
            {
                continue;
            }

            if (!firms.TryGetValue(firmNumber, out var firm))
            {
                firm = new RegulatoryFirm
                {
                    Snapshot = snapshot,
                    State = "North Carolina",
                    LicenseNumber = firmNumber,
                    LegalName = firmName
                };
                firms[firmNumber] = firm;
            }

            affiliations.Add(new RegulatoryFirmLicenseeAffiliation
            {
                Snapshot = snapshot,
                Firm = firm,
                Licensee = licensee,
                IsBrokerInCharge = licenseeElement.TryGetProperty("isBrokerInCharge", out var bic) && bic.ValueKind == JsonValueKind.True
            });
        }

        if (licensees.Count == 0)
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
}
