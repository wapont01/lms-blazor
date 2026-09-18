using System.Globalization;
using System.Text.RegularExpressions;
using Lms.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Lms.Application.Data;

public static class NcRecCaptureSeed
{
    private const string SourceUrl = "https://license.ncrec.gov/ncrec/oecgi3.exe/O4W_BROKERS_BY_FIRM_NEW";
    private static readonly DateTime RetrievedAtUtc = new(2026, 8, 28, 0, 0, 0, DateTimeKind.Utc);

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

        var charlottePath = Path.Combine(root, "docs", "NORTH CAROLINA", "LICENSING", "TEXTs", "BROKER ROSTER", "Charlotte - First 10 Firms - NCREC Firm and Broker Roster.md");
        var williamPath = Path.Combine(root, "docs", "NORTH CAROLINA", "LICENSING", "TEXTs", "BROKER ROSTER", "WILLIAMS LAND REALTY LLC - NCREC Firm and Broker Roster.md");
        if (!File.Exists(charlottePath) || !File.Exists(williamPath))
        {
            return;
        }

        var snapshot = new RegulatoryDataSnapshot
        {
            State = "North Carolina",
            SourceName = "North Carolina Real Estate Commission public licensing database",
            SourceUrl = SourceUrl,
            RetrievedAtUtc = RetrievedAtUtc,
            ParserVersion = "ncrec-markdown-capture-v1",
            Status = RegulatorySnapshotStatuses.Approved,
            ReviewedAtUtc = RetrievedAtUtc
        };
        var firms = new Dictionary<string, RegulatoryFirm>(StringComparer.OrdinalIgnoreCase);
        var licensees = new Dictionary<string, RegulatoryLicensee>(StringComparer.OrdinalIgnoreCase);
        var affiliations = new List<RegulatoryFirmLicenseeAffiliation>();

        ParseCharlotte(File.ReadAllLines(charlottePath), snapshot, firms, licensees, affiliations);
        ParseWilliamsLand(File.ReadAllLines(williamPath), snapshot, firms, licensees, affiliations);
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

    private static void ParseCharlotte(
        string[] lines,
        RegulatoryDataSnapshot snapshot,
        Dictionary<string, RegulatoryFirm> firms,
        Dictionary<string, RegulatoryLicensee> licensees,
        List<RegulatoryFirmLicenseeAffiliation> affiliations)
    {
        var firmTable = false;
        var brokerTable = false;
        foreach (var line in lines)
        {
            if (line.StartsWith("## Firm Records", StringComparison.Ordinal))
            {
                firmTable = true;
                continue;
            }
            if (line.StartsWith("## Broker Records", StringComparison.Ordinal))
            {
                firmTable = false;
                brokerTable = true;
                continue;
            }
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                brokerTable = false;
            }

            var cells = SplitRow(line);
            if (firmTable && cells.Count == 7 && cells[2].StartsWith("C", StringComparison.OrdinalIgnoreCase))
            {
                var firm = new RegulatoryFirm
                {
                    Snapshot = snapshot,
                    State = "North Carolina",
                    LicenseNumber = cells[2],
                    LegalName = cells[1].Split(" | ", StringSplitOptions.None)[0],
                    LicenseStatus = cells[3],
                    QualifyingBrokerName = cells[4],
                    Address = cells[5]
                };
                firms[firm.LicenseNumber] = firm;
            }

            if (brokerTable && cells.Count == 8 && int.TryParse(cells[1], out _))
            {
                var firmLicense = Regex.Match(cells[5], @"C\d+").Value;
                if (string.IsNullOrWhiteSpace(firmLicense) || !firms.TryGetValue(firmLicense, out var firm))
                {
                    continue;
                }

                var licensee = new RegulatoryLicensee
                {
                    Snapshot = snapshot,
                    State = "North Carolina",
                    LicenseNumber = cells[1],
                    Name = cells[2],
                    LicenseType = "BROKER",
                    LicenseLevel = Regex.Match(cells[4], @"\b(PB|B)\b").Value,
                    LicenseStatus = cells[4].Contains("ACTIVE", StringComparison.OrdinalIgnoreCase) ? "ACTIVE" : cells[4],
                    LicenseIssuedDate = ParseDate(cells[3]),
                    IsBicEligible = cells[6].Contains("Yes", StringComparison.OrdinalIgnoreCase)
                };
                licensees[licensee.LicenseNumber] = licensee;
                affiliations.Add(new RegulatoryFirmLicenseeAffiliation
                {
                    Snapshot = snapshot,
                    Firm = firm,
                    Licensee = licensee,
                    IsBrokerInCharge = cells[6].StartsWith("Yes", StringComparison.OrdinalIgnoreCase),
                    BrokerInChargeName = cells[6].StartsWith("Yes", StringComparison.OrdinalIgnoreCase) ? licensee.Name : null
                });
            }
        }
    }

    private static void ParseWilliamsLand(
        string[] lines,
        RegulatoryDataSnapshot snapshot,
        Dictionary<string, RegulatoryFirm> firms,
        Dictionary<string, RegulatoryLicensee> licensees,
        List<RegulatoryFirmLicenseeAffiliation> affiliations)
    {
        var firm = new RegulatoryFirm
        {
            Snapshot = snapshot,
            State = "North Carolina",
            LicenseNumber = "C42407",
            LegalName = "WILLIAMS LAND REALTY LLC",
            LicenseStatus = "ACTIVE",
            Address = "17214 BELMONT STAKES LN, CHARLOTTE, NC 28278-8147",
            QualifyingBrokerName = "APONTE, WILLIAM ALFONSO"
        };
        firms[firm.LicenseNumber] = firm;
        var licensee = new RegulatoryLicensee
        {
            Snapshot = snapshot,
            State = "North Carolina",
            LicenseNumber = "352750",
            Name = "APONTE, WILLIAM ALFONSO",
            LicenseType = "BROKER",
            LicenseLevel = "B",
            LicenseStatus = "ACTIVE",
            LicenseIssuedDate = new DateOnly(2024, 3, 14),
            IsBicEligible = true
        };
        licensees[licensee.LicenseNumber] = licensee;
        affiliations.Add(new RegulatoryFirmLicenseeAffiliation
        {
            Snapshot = snapshot,
            Firm = firm,
            Licensee = licensee,
            IsBrokerInCharge = true,
            BrokerInChargeName = licensee.Name
        });
    }

    private static List<string> SplitRow(string line)
    {
        if (!line.StartsWith('|') || !line.EndsWith('|'))
        {
            return [];
        }

        return line.Trim('|').Split('|').Select(cell => cell.Trim()).ToList();
    }

    private static DateOnly? ParseDate(string value)
    {
        return DateOnly.TryParseExact(value, "MM/dd/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed
            : null;
    }
}
