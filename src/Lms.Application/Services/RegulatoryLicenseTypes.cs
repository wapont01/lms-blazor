namespace Lms.Application.Services;

/// <summary>
/// Everything a regulator publishes is imported, but only real-estate license types are offered in
/// LMS dropdowns and searches today. Other professions are shown as "coming later" for future products.
/// </summary>
public static class RegulatoryLicenseTypes
{
    public const string ComingLaterSuffix = " (coming later)";

    private static readonly IReadOnlyDictionary<string, HashSet<string>> NonRealEstate =
        new Dictionary<string, HashSet<string>>(StringComparer.Ordinal)
        {
            ["Arizona"] = new(StringComparer.OrdinalIgnoreCase) { "Candidate", "Unlicensed" },
            ["Colorado"] = new(ColoradoDoraLicenseTypes.All.Where(option => !option.IsRealEstate)
                .Select(option => option.Name), StringComparer.OrdinalIgnoreCase),
            ["Florida"] = new(StringComparer.OrdinalIgnoreCase)
            {
                "Certified Residential Appraiser", "General Appraiser", "Licensed Appraiser", "Registered Trainee Appraiser"
            },
            ["Nevada"] = new(NevadaNredBulkImportService.AllLicenseTypes
                .Except(NevadaNredBulkImportService.RealEstateLicenseTypes, StringComparer.Ordinal), StringComparer.OrdinalIgnoreCase)
        };

    public static bool IsRealEstate(string state, string? licenseType) =>
        !string.IsNullOrWhiteSpace(licenseType)
        && !(NonRealEstate.TryGetValue(state, out var excluded) && excluded.Contains(licenseType.Trim()));

    public static IReadOnlyList<string> NonRealEstateTypes(string state) =>
        NonRealEstate.TryGetValue(state, out var excluded) ? excluded.Order(StringComparer.Ordinal).ToArray() : [];
}
