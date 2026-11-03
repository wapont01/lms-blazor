using System.Text;
using Lms.Domain.Entities;

namespace Lms.Application.Services;

public sealed record RegulatoryLocationFirm(string LicenseNumber, string Name, string? City, bool IsActive = true);

public sealed record RegulatoryLocationMatch(string? FirmLicenseNumber, string? MatchMethod);

/// <summary>
/// Regulators that publish branch offices without naming the owning firm are linked by name: exact name, then
/// normalized name (legal suffixes and punctuation ignored), then a conservative closest name flagged for review. A
/// closest name must be nearly identical, or one name's distinctive words (at least two, ignoring generic real-estate,
/// place and franchise-brand words) must all appear in the other; otherwise the branch stays unlinked, because a wrong
/// link is worse than none. Several firms can share a name; the one in the branch's city wins, then an active firm, then
/// the lowest license number.
/// </summary>
public sealed class RegulatoryLocationMatcher
{
    public const double NearlyIdentical = 0.95;

    private static readonly HashSet<string> NameNoise = new(StringComparer.Ordinal)
    {
        "LLC", "L", "C", "INC", "INCORPORATED", "CORP", "CORPORATION", "CO", "COMPANY", "LTD", "LLP", "PC", "PLLC", "THE", "RBO"
    };

    // Words shared by unrelated firms: real-estate terms, places and franchise brands whose offices are separately owned.
    private static readonly HashSet<string> GenericWords = new(StringComparer.Ordinal)
    {
        "AND", "OF", "BY", "AT", "IN", "DBA", "D", "B", "A", "S", "REAL", "ESTATE", "REALTY", "REALTORS", "REALTOR", "PROPERTIES",
        "PROPERTY", "GROUP", "HOMES", "HOME", "ASSOCIATES", "SERVICES", "MANAGEMENT", "TEAM", "BROKERAGE", "BROKERS", "BROKER",
        "PARTNERS", "COMMERCIAL", "RESIDENTIAL", "INVESTMENTS", "INVESTMENT", "INTERNATIONAL", "SOLUTIONS", "AGENCY", "OFFICE",
        "BRANCH", "SALES", "LAND", "APPRAISAL", "APPRAISALS", "REFERRAL", "REFERRALS", "NETWORK", "RENTALS", "RENTAL",
        "DEVELOPMENT", "ADVISORS", "EXECUTIVES", "DIRECT", "CONCEPTS", "ENTERPRISES", "HOLDINGS", "VENTURES", "CAPITAL",
        "DELAWARE", "OREGON", "VIRGINIA", "WASHINGTON", "MARYLAND", "PENNSYLVANIA", "NORTHWEST", "NW", "PACIFIC", "ATLANTIC",
        "MID", "COAST", "COASTAL", "BAY", "BEACH", "BEACHES", "COUNTY", "COUNTIES", "VALLEY", "AMERICAN", "AMERICA", "NATIONAL",
        "UNITED", "FIRST", "PREMIER", "METRO", "CENTRAL", "NORTH", "SOUTH", "EAST", "WEST", "TRI", "ALL", "CITY", "TOWN",
        "CENTURY", "21", "KELLER", "WILLIAMS", "RE", "MAX", "REMAX", "COLDWELL", "BANKER", "BERKSHIRE", "HATHAWAY",
        "HOMESERVICES", "ERA", "EXIT", "SOTHEBYS", "SOTHEBY", "WINDERMERE", "COMPASS", "PRUDENTIAL", "WEICHERT", "LONG",
        "FOSTER", "EXP", "JOHN", "SCOTT", "HOWARD", "HANNA", "LANDMARK", "FATHOM"
    };

    private readonly ILookup<string, RegulatoryLocationFirm> byName;
    private readonly ILookup<string, RegulatoryLocationFirm> byKey;
    private readonly List<(RegulatoryLocationFirm Firm, string Key, HashSet<string> Words)> normalized;

    public RegulatoryLocationMatcher(IEnumerable<RegulatoryLocationFirm> firms)
    {
        var list = firms.ToList();
        byName = list.ToLookup(firm => firm.Name.Trim(), StringComparer.OrdinalIgnoreCase);
        normalized = list.Select(firm => NormalizeName(firm.Name)).Zip(list, (key, firm) => (Firm: firm, Key: key, Words: DistinctiveWords(key))).ToList();
        byKey = normalized.Where(item => item.Key.Length > 0).ToLookup(item => item.Key, item => item.Firm, StringComparer.Ordinal);
    }

    public RegulatoryLocationMatch Match(string branchName, string? branchCity)
    {
        RegulatoryLocationFirm Pick(IEnumerable<RegulatoryLocationFirm> candidates) =>
            candidates.OrderBy(firm => branchCity is not null
                    && string.Equals(firm.City, branchCity, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(firm => firm.IsActive ? 0 : 1)
                .ThenBy(firm => firm.LicenseNumber, StringComparer.Ordinal).First();

        var name = branchName.Trim();
        if (byName.Contains(name))
            return new(Pick(byName[name]).LicenseNumber, RegulatoryLocationMatchMethods.ExactName);
        var key = NormalizeName(name);
        if (key.Length > 0 && byKey.Contains(key))
            return new(Pick(byKey[key]).LicenseNumber, RegulatoryLocationMatchMethods.NormalizedName);
        if (normalized.Count == 0 || key.Length == 0)
            return new(null, RegulatoryLocationMatchMethods.NoMatch);
        var words = DistinctiveWords(key);
        var scored = normalized.Where(item => Similarity(key, item.Key) >= NearlyIdentical || SharesDistinctiveWords(words, item.Words))
            .Select(item => (item.Firm, Score: Similarity(key, item.Key))).ToList();
        if (scored.Count == 0)
            return new(null, RegulatoryLocationMatchMethods.NoMatch);
        var best = scored.Max(item => item.Score);
        return new(Pick(scored.Where(item => item.Score == best).Select(item => item.Firm)).LicenseNumber,
            RegulatoryLocationMatchMethods.ClosestName);
    }

    private static HashSet<string> DistinctiveWords(string key) =>
        key.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(word => !GenericWords.Contains(word)).ToHashSet(StringComparer.Ordinal);

    private static bool SharesDistinctiveWords(HashSet<string> left, HashSet<string> right)
    {
        var (smaller, larger) = left.Count <= right.Count ? (left, right) : (right, left);
        return smaller.Count >= 2 && smaller.IsSubsetOf(larger);
    }

    public static string NormalizeName(string name)
    {
        var builder = new StringBuilder(name.Length);
        foreach (var character in name.ToUpperInvariant().Replace("&", " AND ", StringComparison.Ordinal))
            builder.Append(char.IsLetterOrDigit(character) ? character : ' ');
        return string.Join(' ', builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(token => !NameNoise.Contains(token)));
    }

    /// <summary>Levenshtein similarity in [0, 1] between two normalized names.</summary>
    public static double Similarity(string left, string right)
    {
        if (left.Length == 0 || right.Length == 0) return 0;
        var previous = new int[right.Length + 1];
        var current = new int[right.Length + 1];
        for (var j = 0; j <= right.Length; j++) previous[j] = j;
        for (var i = 1; i <= left.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= right.Length; j++)
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1),
                    previous[j - 1] + (left[i - 1] == right[j - 1] ? 0 : 1));
            (previous, current) = (current, previous);
        }
        return 1 - (double)previous[right.Length] / Math.Max(left.Length, right.Length);
    }
}
