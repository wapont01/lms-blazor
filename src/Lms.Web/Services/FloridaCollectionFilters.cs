namespace Lms.Web.Services;

public sealed record FloridaLicenseCategory(string Code, string Name);
public sealed record FloridaSpecialQualification(string Code, string Name, string Rank, string Profession);

public static class FloridaCollectionFilters
{
    public static IReadOnlyList<FloridaSpecialQualification> SpecialQualifications { get; } =
    [
        new("R142", "Real Estate Broker", "Broker", "Brokers"),
        new("R143", "Real Estate Broker Sales Associate", "Broker Sales", "Broker associates"),
        new("R141", "Real Estate Sales Associate", "Sales Associate", "Sales associates")
    ];

    public static bool MatchesCity(CollectedLicenseSample sample, string city) =>
        string.IsNullOrWhiteSpace(city) || sample.Addresses?.Any(address =>
            address?.Address?.Contains(city.Trim(), StringComparison.OrdinalIgnoreCase) == true) == true;

    public static IReadOnlyList<FloridaLicenseCategory> LicenseTypes { get; } =
    [
        new("2512", "Corporate Broker"),
        new("2511", "Course Sponsor"),
        new("2510", "Owner/Developer (Pseudo)"),
        new("2507", "Real Estate Additional Location"),
        new("2504", "Real Estate Branch Office"),
        new("2501", "Real Estate Broker or Sales"),
        new("2509", "Real Estate CE Course"),
        new("2502", "Real Estate Corporation"),
        new("2505", "Real Estate Instructor"),
        new("2503", "Real Estate Partnership"),
        new("2506", "Real Estate School"),
        new("2513", "Temporary Qualifying Broker")
    ];

    public static string? CategoryFor(CollectedLicenseSample sample) => sample.Category ?? sample.Rank switch
    {
        "Sales Associate" or "Broker Sales" or "Broker" => "Real Estate Broker or Sales",
        "RE Instructor" => "Real Estate Instructor",
        "TempQual Broker" => "Temporary Qualifying Broker",
        _ => null
    };
}
