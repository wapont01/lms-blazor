using System.Globalization;
using System.Text.RegularExpressions;
using Lms.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Lms.Application.Data;

public static class FloridaDbprCaptureSeed
{
    private const string SourceUrl = "https://dbpr-publicrecords.myfloridalicense.com/qpr/single/?appid=90232c64-fcda-4ec3-b807-723c8e339cb8";
    private static readonly DateTime RetrievedAtUtc = new(2026, 8, 28, 21, 3, 0, DateTimeKind.Utc);

    public static async Task SeedAsync(ApplicationDbContext dbContext, string? workspaceRoot = null)
    {
        if (await dbContext.RegulatoryDataSnapshots.AnyAsync(snapshot =>
                snapshot.State == "Florida" && snapshot.SourceUrl == SourceUrl && snapshot.RetrievedAtUtc == RetrievedAtUtc))
        {
            return;
        }

        var root = CaptureSeedRoot.Resolve(workspaceRoot);
        if (root is null)
        {
            return;
        }

        var corporationsPath = Path.Combine(root, "docs", "FLORIDA", "LICENSING", "TEXTs", "Miami-Dade - First Page Real Estate Corporations - DBPR Capture.md");
        var relationshipsPath = Path.Combine(root, "docs", "FLORIDA", "LICENSING", "TEXTs", "Miami-Dade - First Page Corporation Related License Rosters - DBPR Capture.md");
        if (!File.Exists(corporationsPath) || !File.Exists(relationshipsPath))
        {
            return;
        }

        var snapshot = new RegulatoryDataSnapshot
        {
            State = "Florida",
            SourceName = "Florida DBPR public license search",
            SourceUrl = SourceUrl,
            RetrievedAtUtc = RetrievedAtUtc,
            ParserVersion = "dbpr-markdown-capture-v1",
            Status = RegulatorySnapshotStatuses.Approved,
            ReviewedAtUtc = RetrievedAtUtc
        };

        var firms = ParseFirms(File.ReadAllLines(corporationsPath), snapshot);
        var licensees = new Dictionary<string, RegulatoryLicensee>(StringComparer.OrdinalIgnoreCase);
        var affiliations = new List<RegulatoryFirmLicenseeAffiliation>();
        ParseRelationships(File.ReadAllLines(relationshipsPath), snapshot, firms, licensees, affiliations);

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

    private static Dictionary<string, RegulatoryFirm> ParseFirms(string[] lines, RegulatoryDataSnapshot snapshot)
    {
        var firms = new Dictionary<string, RegulatoryFirm>(StringComparer.OrdinalIgnoreCase);
        var inTable = false;
        foreach (var line in lines)
        {
            if (line.StartsWith("## Corporation Records", StringComparison.Ordinal))
            {
                inTable = true;
                continue;
            }

            if (inTable && line.StartsWith("## ", StringComparison.Ordinal))
            {
                break;
            }

            var cells = SplitRow(line);
            if (!inTable || cells.Count != 9 || !cells[1].StartsWith("CQ", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var firm = new RegulatoryFirm
            {
                Snapshot = snapshot,
                State = "Florida",
                LicenseNumber = cells[1],
                LegalName = cells[0],
                LicenseStatus = cells[2],
                LicenseIssuedDate = ParseDate(cells[3]),
                Address = cells[4]
            };
            firms[firm.LegalName] = firm;
        }

        return firms;
    }

    private static void ParseRelationships(
        string[] lines,
        RegulatoryDataSnapshot snapshot,
        Dictionary<string, RegulatoryFirm> firms,
        Dictionary<string, RegulatoryLicensee> licensees,
        List<RegulatoryFirmLicenseeAffiliation> affiliations)
    {
        string? currentFirmName = null;
        foreach (var line in lines)
        {
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                currentFirmName = line.StartsWith("## ", StringComparison.Ordinal) && line.EndsWith(" Roster", StringComparison.Ordinal)
                    ? line[3..^7].Trim()
                    : null;
                // strip a leading roster index only when the literal heading is not a known firm name
                if (currentFirmName is not null && !firms.ContainsKey(currentFirmName)
                    && currentFirmName.Length > 2 && char.IsDigit(currentFirmName[0]))
                {
                    currentFirmName = currentFirmName[(currentFirmName.IndexOf(' ') + 1)..];
                }
                continue;
            }

            var cells = SplitRow(line);
            string? firmName = currentFirmName;
            string licenseNumber;
            string status;
            string licenseeName;
            string relationship;
            string effective;
            string rank;
            string expires;

            if (cells.Count == 8 && Regex.IsMatch(cells[1], "^(BK|SL)\\d+$", RegexOptions.IgnoreCase))
            {
                firmName = cells[0].Trim();
                licenseNumber = cells[1];
                status = cells[2];
                licenseeName = cells[3];
                relationship = cells[4];
                effective = cells[5];
                rank = cells[6];
                expires = cells[7];
            }
            else if (cells.Count == 7 && Regex.IsMatch(cells[0], "^(BK|SL)\\d+$", RegexOptions.IgnoreCase))
            {
                licenseNumber = cells[0];
                status = cells[1];
                licenseeName = cells[2];
                relationship = cells[3];
                effective = cells[4];
                rank = cells[5];
                expires = cells[6];
            }
            else
            {
                continue;
            }

            if (firmName is null || !firms.TryGetValue(firmName, out var firm))
            {
                continue;
            }

            if (!licensees.TryGetValue(licenseNumber, out var licensee))
            {
                var isBroker = rank.Contains("Broker", StringComparison.OrdinalIgnoreCase);
                licensee = new RegulatoryLicensee
                {
                    Snapshot = snapshot,
                    State = "Florida",
                    LicenseNumber = licenseNumber,
                    Name = licenseeName,
                    LicenseType = isBroker ? "BK" : "SL",
                    LicenseLevel = isBroker ? "Broker" : "Sales Associate",
                    LicenseStatus = status,
                    LicenseIssuedDate = ParseDate(effective),
                    LicenseRenewalDate = ParseDate(expires)
                };
                licensees.Add(licenseNumber, licensee);
            }

            affiliations.Add(new RegulatoryFirmLicenseeAffiliation
            {
                Snapshot = snapshot,
                Firm = firm,
                Licensee = licensee,
                RelationshipEffectiveDate = ParseDate(effective),
                IsBrokerInCharge = relationship.Contains("Qualifying Broker", StringComparison.OrdinalIgnoreCase),
                BrokerInChargeName = relationship.Contains("Qualifying Broker", StringComparison.OrdinalIgnoreCase) ? licenseeName : null
            });
        }
    }

    private static List<string> SplitRow(string line)
    {
        if (!line.StartsWith('|') || !line.EndsWith('|'))
        {
            return [];
        }

        return line.Trim('|')
            .Split('|')
            .Select(cell => cell.Trim())
            .ToList();
    }

    private static DateOnly? ParseDate(string value)
    {
        return DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed
            : null;
    }
}
