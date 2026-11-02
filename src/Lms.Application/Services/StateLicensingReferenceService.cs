using Lms.Domain.Entities;

namespace Lms.Application.Services;

public sealed record StateLicensingSource(
    string Title,
    string FileName,
    string RepositoryPath,
    DateOnly? CapturedDate,
    string Description,
    string ReferenceType = "Local TXT reference");

public sealed record StateLicensingFaq(
    string Question,
    string Answer,
    IReadOnlyList<string> SourceFileNames,
    bool IsSourceBacked,
    string? CatalogCitation = null,
    IReadOnlyList<StateLicensingCitation>? Citations = null);

public sealed record StateLicensingCitation(
    string Title,
    string? Url,
    string? RepositoryPath,
    DateOnly ReviewedOn,
    string CurrencyNote);

public sealed record StateLicensingAgency(string Name, string Url);

public sealed record StateLicensingState(
    string Name,
    string Slug,
    StateLicensingAgency Agency,
    IReadOnlyList<StateLicensingSource> Sources,
    IReadOnlyList<StateLicensingFaq> Questions)
{
    public bool HasLocalReferences => Sources.Any(source => source.ReferenceType == "Local TXT reference");
    public int SourcedAnswerCount => Questions.Count(question => question.IsSourceBacked && question.Citations?.Count > 0);
}

public static class StateLicensingReferenceService
{
    private static readonly IReadOnlyDictionary<string, StateLicensingAgency> Agencies =
        new Dictionary<string, StateLicensingAgency>(StringComparer.OrdinalIgnoreCase)
        {
            ["Alabama"] = new("Alabama Real Estate Commission", "https://arec.alabama.gov/"),
            ["Alaska"] = new("Alaska Real Estate Commission (Professional Licensing)", "https://www.commerce.alaska.gov/web/cbpl/ProfessionalLicensing/RealEstateCommission.aspx"),
            ["Arizona"] = new("Arizona Department of Real Estate", "https://azre.gov/"),
            ["Arkansas"] = new("Arkansas Real Estate Commission", "https://arec.arkansas.gov/"),
            ["California"] = new("California Department of Real Estate", "https://www.dre.ca.gov/"),
            ["Colorado"] = new("Colorado Division of Real Estate", "https://dre.colorado.gov/"),
            ["Connecticut"] = new("Connecticut Department of Consumer Protection — Real Estate", "https://portal.ct.gov/dcp/license-services-division/license-division/real-estate"),
            ["Delaware"] = new("Delaware Real Estate Commission", "https://dpr.delaware.gov/boards/realestate/"),
            ["Florida"] = new("Florida Real Estate Commission", "https://www2.myfloridalicense.com/real-estate-commission/"),
            ["Georgia"] = new("Georgia Real Estate Commission", "https://grec.state.ga.us/"),
            ["Hawaii"] = new("Hawaii Real Estate Commission", "https://cca.hawaii.gov/reb/"),
            ["Idaho"] = new("Idaho Real Estate Commission", "https://dopl.idaho.gov/real-estate/"),
            ["Illinois"] = new("Illinois Department of Financial and Professional Regulation — Real Estate", "https://idfpr.illinois.gov/profs/realestate.html"),
            ["Indiana"] = new("Indiana Real Estate Commission", "https://www.in.gov/pla/professions/real-estate-home/"),
            ["Iowa"] = new("Iowa Real Estate Commission", "https://dial.iowa.gov/licenses/real-estate"),
            ["Kansas"] = new("Kansas Real Estate Commission", "https://krec.ks.gov/"),
            ["Kentucky"] = new("Kentucky Real Estate Commission", "https://krec.ky.gov/"),
            ["Louisiana"] = new("Louisiana Real Estate Commission", "https://lrec.gov/"),
            ["Maine"] = new("Maine Real Estate Commission", "https://www.maine.gov/pfr/professionallicensing/professions/real-estate"),
            ["Maryland"] = new("Maryland Real Estate Commission", "https://labor.maryland.gov/license/mrec/"),
            ["Massachusetts"] = new("Massachusetts Board of Registration of Real Estate Brokers and Salespersons", "https://www.mass.gov/orgs/board-of-registration-of-real-estate-brokers-and-salespersons"),
            ["Michigan"] = new("Michigan Board of Real Estate Brokers and Salespersons", "https://www.michigan.gov/lara/bureau-list/bpl/occupational-boards/real-estate"),
            ["Minnesota"] = new("Minnesota Department of Commerce — Real Estate", "https://mn.gov/commerce/licensees/real-estate/"),
            ["Mississippi"] = new("Mississippi Real Estate Commission", "https://www.mrec.ms.gov/"),
            ["Missouri"] = new("Missouri Real Estate Commission", "https://pr.mo.gov/realestate.asp"),
            ["Montana"] = new("Montana Board of Realty Regulation", "https://boards.bsd.dli.mt.gov/real-estate/"),
            ["Nebraska"] = new("Nebraska Real Estate Commission", "https://nrec.nebraska.gov/"),
            ["Nevada"] = new("Nevada Real Estate Division", "https://red.nv.gov/"),
            ["New Hampshire"] = new("New Hampshire Real Estate Commission", "https://www.oplc.nh.gov/real-estate-commission"),
            ["New Jersey"] = new("New Jersey Real Estate Commission", "https://www.nj.gov/dobi/division_rec/"),
            ["New Mexico"] = new("New Mexico Real Estate Commission", "https://www.rld.nm.gov/real-estate-commission/"),
            ["New York"] = new("New York Department of State — Real Estate", "https://dos.ny.gov/real-estate"),
            ["North Carolina"] = new("North Carolina Real Estate Commission", "https://www.ncrec.gov/"),
            ["North Dakota"] = new("North Dakota Real Estate Commission", "https://realestatend.org/"),
            ["Ohio"] = new("Ohio Division of Real Estate and Professional Licensing", "https://com.ohio.gov/divisions-and-programs/real-estate-and-professional-licensing"),
            ["Oklahoma"] = new("Oklahoma Real Estate Commission", "https://oklahoma.gov/orec.html"),
            ["Oregon"] = new("Oregon Real Estate Agency", "https://www.oregon.gov/rea/"),
            ["Pennsylvania"] = new("Pennsylvania State Real Estate Commission", "https://www.pa.gov/agencies/dos/department-and-offices/bpoa/boards-commissions/real-estate-commission.html"),
            ["Rhode Island"] = new("Rhode Island Department of Business Regulation — Real Estate", "https://dbr.ri.gov/real-estate"),
            ["South Carolina"] = new("South Carolina Real Estate Commission", "https://llr.sc.gov/re/"),
            ["South Dakota"] = new("South Dakota Real Estate Commission", "https://dlr.sd.gov/realestate/"),
            ["Tennessee"] = new("Tennessee Real Estate Commission", "https://www.tn.gov/commerce/regboards/trec.html"),
            ["Texas"] = new("Texas Real Estate Commission", "https://www.trec.texas.gov/"),
            ["Utah"] = new("Utah Division of Real Estate", "https://realestate.utah.gov/"),
            ["Vermont"] = new("Vermont Office of Professional Regulation — Real Estate", "https://sos.vermont.gov/real-estate/"),
            ["Virginia"] = new("Virginia Department of Professional and Occupational Regulation — Real Estate Board", "https://www.dpor.virginia.gov/Boards/Real-Estate"),
            ["Washington"] = new("Washington Department of Licensing — Real Estate", "https://dol.wa.gov/professional-licenses/real-estate-broker"),
            ["West Virginia"] = new("West Virginia Real Estate Commission", "https://rec.wv.gov/"),
            ["Wisconsin"] = new("Wisconsin Department of Safety and Professional Services — Real Estate", "https://dsps.wi.gov/Pages/Professions/RealEstateSalesperson/Default.aspx"),
            ["Wyoming"] = new("Wyoming Real Estate Commission", "https://wyomingrealestatecommission.com/")
        };

    private static readonly IReadOnlyList<StateLicensingState> States = Array.AsReadOnly(
        DevelopmentCourseCatalog.States
            .Select(BuildState)
            .ToArray());

    public static IReadOnlyList<StateLicensingState> GetStates() => States;

    public static StateLicensingState? FindState(string? state)
    {
        if (string.IsNullOrWhiteSpace(state))
        {
            return null;
        }

        var query = state.Trim();
        return States.FirstOrDefault(item =>
            string.Equals(item.Name, query, StringComparison.OrdinalIgnoreCase)
            || string.Equals(item.Slug, query, StringComparison.OrdinalIgnoreCase));
    }

    private static StateLicensingState BuildState(string name)
    {
        var questions = StateLicensingContent.GetQuestions(name);
        var sources = questions
            .SelectMany(question => question.Citations ?? [])
            .Where(citation => !string.IsNullOrWhiteSpace(citation.RepositoryPath))
            .DistinctBy(citation => citation.RepositoryPath, StringComparer.OrdinalIgnoreCase)
            .Select(citation => new StateLicensingSource(
                citation.Title,
                citation.RepositoryPath!.Split(['/', '\\'])[^1],
                citation.RepositoryPath,
                null,
                citation.CurrencyNote))
            .ToArray();
        return new StateLicensingState(
            name,
            ToSlug(name),
            Agencies[name],
            Array.AsReadOnly(sources),
            questions);
    }

    private static string ToSlug(string name) => name.Replace(' ', '-').ToLowerInvariant();
}
