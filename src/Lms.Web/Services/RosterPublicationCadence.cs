namespace Lms.Web.Services;

using Lms.Application.Services;

public sealed record RosterPublicationCadence(string Frequency, string Evidence, string SourceUrl,
    string ReviewedOn, string SchedulingNote, string? SavedEvidence = null)
{
    public static RosterPublicationCadence ForState(string state) => state switch
    {
        "Florida" => new("Weekly",
            "DBPR provides updated download files in CSV format on a weekly basis.",
            "https://www2.myfloridalicense.com/real-estate-commission/public-records/",
            "2026-11-02", "Recommended collection: 1 Week. Daily collection usually rechecks the same weekly files; collecting less often than weekly delays new data. Publication weekday and time are not specified on this page."),
        "Texas" => new("Daily", "All data updated daily.",
            "https://www.trec.texas.gov/public/high-value-data-sets",
            "2026-11-02", "Recommended collection: 1 Day. More frequent collection usually rechecks the same daily files; a weekly collection can leave the LMS several days behind the source. Publication time is not specified."),
        "California" => new("Daily",
            "Broker Associate List (Updated Daily); Licensee List ZIP (Updated Daily).",
            "https://dre.ca.gov/Licensees/ExamineeLicenseeListDataFiles.html",
            "2026-11-02", "Recommended collection: 1 Day. More frequent collection usually rechecks the same daily files; a weekly collection can leave the LMS several days behind the source. Publication time is not specified. The weekly examinee and new-licensee lists are separate sources, not this collector."),
        "Colorado" => new("Not verified",
            "The selected official dataset page did not expose a publication frequency in the retrieved content.",
            "https://data.colorado.gov/Regulations/Licensed-Real-Estate-Professionals-in-Colorado/4zse-6bnw/about_data",
            "2026-11-02", "Recommended collection: not established until regulator cadence is verified. The current LMS interval is an administrator choice, not evidence that DORA updates at that rate. Frequent collection may repeat unchanged data; infrequent collection may delay changes. A dataset modification timestamp alone does not establish a recurring publication rate."),
        "Nevada" => new("Monthly",
            "The Nevada Real Estate Division updates lists of active licensees, registrants, and certificate and permit holders that are under the authority of the Division on a monthly basis.",
            "https://red.nv.gov/Content/Administration/Active_Licensee_Lists/",
            "2026-11-02", "Recommended collection: 1 Month, not every 7 Days. Weekly collection usually downloads the same monthly roster; it only reduces the delay in detecting a new release when its date is unknown. Use 1 Month for a calendar-month interval rather than 30 Days. No publication day or time is specified, so the collection date is an administrator choice, not a confirmed NRED release date.",
            @"src\docs\NEVADA\TXTS\Active Licensee Lists PORTAL.txt (September 2026 capture, line 5)"),
        "New York" => new("Daily",
            "Dataset Summary - Posting Frequency: Daily. Data is only accurate as of the previous business day.",
            "https://data.ny.gov/Economic-Development/Active-Real-Estate-Salespersons-and-Brokers/yg7h-zjbf",
            "2026-11-02", "Recommended collection: 1 Day. More frequent collection usually rechecks the same daily dataset; a weekly collection can leave the LMS several days behind the source. Publication time is not specified. The dataset lists active licenses only."),
        "Arizona" => new("On demand",
            "The lists are generated on demand and can take a few seconds to generate.",
            ArizonaAdreBulkImportService.SourcePage,
            "2026-11-03", "Recommended collection: 1 Week. ADRE builds each list from its live license database when it is downloaded, so any interval returns current data; daily collection adds a 50 MB download and a 225,000-row snapshot each day, while longer intervals delay changes. Only the Real Estate category is imported; cemetery and campground licenses are excluded."),
        "Connecticut" => new("Daily",
            "Real Estate Salesperson Licenses and Real Estate Broker Licenses: \"Updated daily.\" All three datasets list Update Frequency: Daily.",
            ConnecticutDcpBulkImportService.SourcePage,
            "2026-11-03", "Recommended collection: 1 Day. More frequent collection usually rechecks the same daily datasets; a weekly collection can leave the LMS several days behind the source. Publication time is not specified. The salesperson and broker datasets include every status; the broker association dataset lists actively licensed salespersons only."),
        _ => new("Not verified", "No regulator publication cadence has been recorded for this state.",
            "", "", "Recommended collection: not established. Verify the official source before choosing an interval. More frequent collection may repeat unchanged data; less frequent collection may delay changes.")
    };
}
