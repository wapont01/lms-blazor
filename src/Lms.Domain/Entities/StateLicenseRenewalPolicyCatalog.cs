namespace Lms.Domain.Entities;

public sealed class LicenseRenewalCeRequirements
{
    public decimal TotalHours { get; init; }
    public decimal CoreHours { get; init; }
    public decimal ElectiveHours { get; init; }
    public decimal BicHours { get; init; }
    public decimal LicenseLawHours { get; init; }
    public decimal BrokerCeHours { get; init; }
}

public sealed class StateLicenseRenewalPolicy
{
    public string State { get; init; } = string.Empty;
    public string LicenseCategory { get; init; } = string.Empty;
    public int RenewalCycleYears { get; init; }
    public int RenewalWindowStartMonth { get; init; }
    public int RenewalWindowEndMonth { get; init; }
    public bool HasPostlicensingDeadline { get; init; }
    public LicenseRenewalCeRequirements ContinuingEducation { get; init; } = new();
}

public static class StateLicenseRenewalPolicyCatalog
{
    private static readonly IReadOnlyDictionary<string, StateLicenseRenewalPolicy> Policies =
        new Dictionary<string, StateLicenseRenewalPolicy>(StringComparer.OrdinalIgnoreCase)
        {
            ["Arkansas|Salesperson"] = CreateFixed("Arkansas", "Salesperson", 1, 7, coreHours: 1, electiveHours: 6),
            ["Arkansas|Broker"] = CreateFixed("Arkansas", "Broker", 1, 7, coreHours: 1, electiveHours: 6),
            ["Hawaii|Salesperson"] = CreateFixed("Hawaii", "Salesperson", 2, 20, coreHours: 6, electiveHours: 14),
            ["Hawaii|Broker"] = CreateFixed("Hawaii", "Broker", 2, 20, coreHours: 6, electiveHours: 14),
            ["Illinois|Broker"] = CreateFixed("Illinois", "Broker", 2, 12, coreHours: 6, electiveHours: 6),
            ["Illinois|Managing Broker"] = CreateFixed("Illinois", "Managing Broker", 2, 24, coreHours: 6, electiveHours: 6, specialtyHours: 12),
            ["New York|Salesperson"] = CreateFixed("New York", "Salesperson", 2, 22.5m, coreHours: 22.5m, electiveHours: 0),
            ["New York|Broker"] = CreateFixed("New York", "Broker", 2, 22.5m, coreHours: 22.5m, electiveHours: 0),
            ["Texas|Sales Agent"] = CreateFixed("Texas", "Sales Agent", 2, 18, coreHours: 8, electiveHours: 10),
            ["Texas|Broker"] = CreateFixed("Texas", "Broker", 2, 18, coreHours: 17, electiveHours: 1),
            ["Virginia|Salesperson"] = CreateFixed("Virginia", "Salesperson", 2, 16, coreHours: 11, electiveHours: 5),
            ["Virginia|Broker"] = CreateFixed("Virginia", "Broker", 2, 24, coreHours: 19, electiveHours: 5),
            ["Georgia|Salesperson"] = CreateGeorgia("Salesperson"),
            ["Georgia|Broker"] = CreateGeorgia("Broker", brokerCeHours: 18),
            ["Georgia|Community Association Manager"] = CreateGeorgia("Community Association Manager"),
            ["South Carolina|Associate"] = Create("Associate", coreHours: 4, electiveHours: 6),
            ["South Carolina|Broker"] = Create("Broker", coreHours: 4, electiveHours: 6),
            ["South Carolina|Broker-in-Charge"] = Create("Broker-in-Charge", coreHours: 4, electiveHours: 2, bicHours: 4)
        };

    public static IReadOnlyCollection<StateLicenseRenewalPolicy> All => Policies.Values.ToArray();

    public static StateLicenseRenewalPolicy Get(string state, string licenseCategory)
    {
        if (TryGet(state, licenseCategory, out var policy))
        {
            return policy!;
        }

        throw new ArgumentException($"No renewal policy exists for state '{state}' and license category '{licenseCategory}'.");
    }

    public static bool TryGet(string state, string licenseCategory, out StateLicenseRenewalPolicy? policy)
    {
        return Policies.TryGetValue($"{state}|{licenseCategory}", out policy);
    }

    /// <summary>
    /// Resolves a policy from raw user/roster values, normalizing state abbreviations and license-category aliases.
    /// Returns false when the state or category cannot be mapped unambiguously; callers must not guess a cycle.
    /// </summary>
    public static bool TryResolve(string? state, string? licenseCategory, out StateLicenseRenewalPolicy? policy)
    {
        policy = null;
        var normalizedState = NormalizeState(state);
        if (normalizedState is null || string.IsNullOrWhiteSpace(licenseCategory))
        {
            return false;
        }

        foreach (var candidate in ExpandCategoryCandidates(licenseCategory))
        {
            if (Policies.TryGetValue($"{normalizedState}|{candidate}", out policy))
            {
                return true;
            }
        }

        return false;
    }

    private static readonly IReadOnlyDictionary<string, string> StateAbbreviations =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["AR"] = "Arkansas",
            ["HI"] = "Hawaii",
            ["IL"] = "Illinois",
            ["NY"] = "New York",
            ["TX"] = "Texas",
            ["VA"] = "Virginia",
            ["GA"] = "Georgia",
            ["SC"] = "South Carolina"
        };

    private static string? NormalizeState(string? state)
    {
        if (string.IsNullOrWhiteSpace(state))
        {
            return null;
        }

        var trimmed = state.Trim();
        return StateAbbreviations.TryGetValue(trimmed, out var fullName) ? fullName : trimmed;
    }

    private static IEnumerable<string> ExpandCategoryCandidates(string licenseCategory)
    {
        var trimmed = licenseCategory.Trim();
        yield return trimmed;

        switch (trimmed.ToUpperInvariant())
        {
            case "SA":
            case "SALES AGENT":
                yield return "Sales Agent";
                yield return "Salesperson";
                break;
            case "SALESPERSON":
                yield return "Salesperson";
                yield return "Sales Agent";
                yield return "Associate";
                break;
            case "B":
            case "BK":
            case "BROKER":
                yield return "Broker";
                break;
            case "AB":
            case "ASSOCIATE BROKER":
                yield return "Associate Broker";
                yield return "Associate";
                yield return "Broker";
                break;
            case "ASSOCIATE":
                yield return "Associate";
                break;
            case "BIC":
            case "BROKER-IN-CHARGE":
                yield return "Broker-in-Charge";
                break;
            case "MB":
            case "MANAGING BROKER":
                yield return "Managing Broker";
                break;
            case "CAM":
                yield return "Community Association Manager";
                break;
        }
    }

    private static StateLicenseRenewalPolicy Create(string licenseCategory, int coreHours, int electiveHours, int bicHours = 0)
    {
        return new StateLicenseRenewalPolicy
        {
            State = "South Carolina",
            LicenseCategory = licenseCategory,
            RenewalCycleYears = 2,
            RenewalWindowStartMonth = 4,
            RenewalWindowEndMonth = 6,
            HasPostlicensingDeadline = false,
            ContinuingEducation = new LicenseRenewalCeRequirements
            {
                TotalHours = coreHours + electiveHours + bicHours,
                CoreHours = coreHours,
                ElectiveHours = electiveHours,
                BicHours = bicHours
            }
        };
    }

    private static StateLicenseRenewalPolicy CreateGeorgia(string licenseCategory, int brokerCeHours = 0)
    {
        return new StateLicenseRenewalPolicy
        {
            State = "Georgia",
            LicenseCategory = licenseCategory,
            RenewalCycleYears = 4,
            RenewalWindowStartMonth = 1,
            RenewalWindowEndMonth = 12,
            HasPostlicensingDeadline = string.Equals(licenseCategory, "Salesperson", StringComparison.OrdinalIgnoreCase),
            ContinuingEducation = new LicenseRenewalCeRequirements
            {
                TotalHours = 36,
                CoreHours = 0,
                ElectiveHours = 36 - 3 - brokerCeHours,
                LicenseLawHours = 3,
                BrokerCeHours = brokerCeHours
            }
        };
    }

    private static StateLicenseRenewalPolicy CreateFixed(
        string state,
        string licenseCategory,
        int renewalCycleYears,
        decimal totalHours,
        decimal coreHours,
        decimal electiveHours,
        decimal specialtyHours = 0)
    {
        return new StateLicenseRenewalPolicy
        {
            State = state,
            LicenseCategory = licenseCategory,
            RenewalCycleYears = renewalCycleYears,
            RenewalWindowStartMonth = 0,
            RenewalWindowEndMonth = 0,
            HasPostlicensingDeadline = false,
            ContinuingEducation = new LicenseRenewalCeRequirements
            {
                TotalHours = totalHours,
                CoreHours = coreHours,
                ElectiveHours = electiveHours,
                BicHours = specialtyHours
            }
        };
    }
}
