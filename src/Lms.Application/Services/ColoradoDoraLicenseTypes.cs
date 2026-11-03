namespace Lms.Application.Services;

public sealed record ColoradoDoraLicenseTypeOption(string Name, string Prefix, bool IsRealEstate, bool IsIndividual);

public interface IColoradoDoraLicenseTypeSelection
{
    IReadOnlySet<string> GetSelected();
}

public static class ColoradoDoraLicenseTypes
{
    public static IReadOnlyList<ColoradoDoraLicenseTypeOption> All { get; } =
    [
        new("Associate Level Real Estate Broker", "FA", true, true),
        new("Employing Level Real Estate Broker - Associate", "EA", true, true),
        new("Independent Level Real Estate Broker - Associate", "IA", true, true),
        new("Employing Level Real Estate Broker - Responsible Broker", "ER", true, true),
        new("Independent Level Real Estate Broker - Responsible Broker", "IR", true, true),
        new("Employing Level Real Estate Broker - Individual Proprietor", "EI", true, true),
        new("Independent Level Real Estate Broker - Individual Proprietor", "II", true, true),
        new("Real Estate Company - Employing", "EC", true, false),
        new("Real Estate Company - Independent", "IC", true, false),
        new("Mortgage Loan Originator", "MLO", false, true),
        new("Home Owner's Association", "HOA", false, false),
        new("HOA Designated Agent", "DA", true, true),
        new("Natural Person", "NP", false, true),
        new("Certified Residential Appraiser", "CR", false, true),
        new("Certified General Appraiser", "CG", false, true),
        new("Controlling Appraiser", "CA", false, true),
        new("Ad Valorem Appraiser", "AV", false, true),
        new("Appraisal Management Company", "AMC", false, false),
        new("Federally Regulated Appraisal Management Company", "AMC", false, false),
        new("Licensed Appraiser", "AL", false, true),
        new("Temporary Appraisal Permit", "AT", false, true),
        new("Subdivision Projects", "PJ", false, false),
        new("Subdivision Developer", "SD", false, false)
    ];

    public static IReadOnlyList<string> DefaultEnabled { get; } =
        Array.AsReadOnly(All.Where(option => option.IsRealEstate).Select(option => option.Name).Distinct(StringComparer.Ordinal).ToArray());

    public static string[] EnabledRealEstateTypes(IEnumerable<string> selected)
    {
        ArgumentNullException.ThrowIfNull(selected);
        var selectedTypes = selected.ToHashSet(StringComparer.Ordinal);
        return All.Where(option => option.IsRealEstate && selectedTypes.Contains(option.Name))
            .Select(option => option.Name).Distinct(StringComparer.Ordinal).ToArray();
    }
}
