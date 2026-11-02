namespace Lms.Application.Services;

internal static partial class StateLicensingContent
{
    private static DateOnly ResearchDate => new(2026, 10, 23);

    internal static IReadOnlyDictionary<string, IReadOnlyList<StateLicensingFaq>> FirstHalf() =>
        new Dictionary<string, IReadOnlyList<StateLicensingFaq>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Alabama"] =
            [
                Q("What is the entry-level Alabama real estate license?",
                    "Alabama's entry-level credential is the salesperson license. A salesperson conducts licensed activity through a qualifying broker; the broker credential is a separate, higher-responsibility license.",
                    C("AREC: Become a Licensed Real Estate Professional", "https://arec.alabama.gov/pages/consumer/tools/becomerep.aspx")),
                Q("What education must an Alabama salesperson complete before the exam?",
                    "AREC requires a 60-clock-hour salesperson prelicense course from an approved provider. The course-credit record must reach AREC before the testing system lets the candidate schedule.",
                    C("AREC examination scheduling information", null, "src/docs/ALABAMA/LICENSING/TXTs/ALABAMA REAL ESTATE Examination scheduling information PORTAL.txt")),
                Q("How does Alabama's salesperson exam scheduling work?",
                    "Wait for the school to report prelicense credit to AREC; the saved candidate instructions say scheduling access may take up to 24 hours after that submission. Use the legal name matching government identification.",
                    C("AREC examination scheduling information", null, "src/docs/ALABAMA/LICENSING/TXTs/ALABAMA REAL ESTATE Examination scheduling information PORTAL.txt")),
                Q("What background and application checks should an Alabama applicant plan for?",
                    "AREC's application guidance calls for an application and fingerprint-based state and federal criminal-history check. Disclose criminal and license-history matters as directed rather than assuming a conviction is an automatic disqualification.",
                    C("AREC: Become a Licensed Real Estate Professional", "https://arec.alabama.gov/pages/consumer/tools/becomerep.aspx")),
                Q("What happens after an Alabama salesperson receives an initial license?",
                    "Alabama has a distinct post-license education requirement for a new salesperson, separate from routine continuing education. Confirm the current deadline and active-status consequences with AREC before planning a first renewal.",
                    C("AREC: Become a Licensed Real Estate Professional", "https://arec.alabama.gov/pages/consumer/tools/becomerep.aspx"))
            ],
            ["Alaska"] =
            [
                Q("What is the entry-level Alaska real estate credential?",
                    "Alaska's entry credential is a salesperson license. Applicants must be at least 19, complete approved prelicense education, pass the salesperson exam, and be sponsored by a licensed broker.",
                    C("Sales Person Licensing Requirements, Alaska Real Estate Commission", "https://www.commerce.alaska.gov/web/cbpl/ProfessionalLicensing/RealEstateCommission/ApplicantsLicensees/SalesPersonLicensingRequirements.aspx")),
                Q("Where should an Alaska salesperson candidate verify prelicense education?",
                    "Alaska requires 40 hours of approved prelicensing education for a salesperson. Verify the currently approved provider and course completion documentation with the Commission before registering.",
                    C("Sales Person Licensing Requirements, Alaska Real Estate Commission", "https://www.commerce.alaska.gov/web/cbpl/ProfessionalLicensing/RealEstateCommission/ApplicantsLicensees/SalesPersonLicensingRequirements.aspx")),
                Q("How should an Alaska applicant approach the licensing examination?",
                    "After completing the approved course, pass the Alaska salesperson examination. The Commission's applicant requirements identify the exam as a prerequisite and its candidate material controls current scheduling and result reporting.",
                    C("Sales Person Licensing Requirements, Alaska Real Estate Commission", "https://www.commerce.alaska.gov/web/cbpl/ProfessionalLicensing/RealEstateCommission/ApplicantsLicensees/SalesPersonLicensingRequirements.aspx")),
                Q("Does an Alaska application require an online filing?",
                    "Submit the salesperson application and required fee within six months after passing the exam. Check the Real Estate Commission's current portal and forms to confirm which application format is accepted.",
                    C("Sales Person Licensing Requirements, Alaska Real Estate Commission", "https://www.commerce.alaska.gov/web/cbpl/ProfessionalLicensing/RealEstateCommission/ApplicantsLicensees/SalesPersonLicensingRequirements.aspx")),
                Q("What should an Alaska licensee check before renewal?",
                    "The initial salesperson checklist establishes education, exam, sponsor, and application timing, but does not set out renewal CE. Check the Commission's separate renewal information for current cycle and continuing-education details.",
                    C("Alaska Real Estate Commission: Applicants and Licensees", "https://www.commerce.alaska.gov/web/cbpl/ProfessionalLicensing/RealEstateCommission/ApplicantsLicensees/FrequentlyAskedQuestions.aspx"))
            ],
            ["Arizona"] =
            [
                Q("What is the entry-level Arizona license, and what is the minimum age?",
                    "Arizona's entry-level real estate credential is salesperson; state law requires an applicant to be at least 18 before receiving a license. Broker applicants have additional experience and education criteria.",
                    C("A.R.S. § 32-2124, Qualifications of licensees", null, "src/docs/ARIZONA/TXTS/32-2124. Qualifications of licensees PORTAL.txt")),
                Q("What salesperson prelicense education does Arizona require?",
                    "The Department's salesperson checklist requires the approved salesperson prelicense course and the separate contract-writing course. Check course-validity windows and approved providers on the current ADRE checklist before enrolling.",
                    C("ADRE: Salesperson License", "https://azre.gov/about/services/salesperson-license")),
                Q("What must Arizona applicants pass?",
                    "Applicants must demonstrate competency through the required salesperson licensing examination. ADRE's current checklist identifies the required examination portions and directs candidates to its testing provider for registration.",
                    C("ADRE: Salesperson License", "https://azre.gov/about/services/salesperson-license")),
                Q("What background screening applies to an Arizona applicant?",
                    "Arizona law allows the Commissioner to require information reasonably needed to assess an applicant, including prior criminal records and a valid fingerprint-clearance card. Review the current ADRE clearance-card guidance before applying.",
                    C("A.R.S. §§ 32-2123 and 32-2124", null, "src/docs/ARIZONA/TXTS/Application for license as broker or salesperson PORTAL.txt")),
                Q("How does Arizona handle a salesperson license before broker affiliation?",
                    "The salesperson application and license status determine whether the person may practice; affiliation with a licensed employing broker is needed for active brokerage work. Verify the current ADRE application and status instructions before submitting.",
                    C("ADRE: Salesperson License", "https://azre.gov/about/services/salesperson-license"))
            ],
            ["Arkansas"] =
            [
                Q("What is the Arkansas entry-level real estate license?",
                    "Arkansas distinguishes salesperson from broker licensing. A salesperson is the entry-level license and must work through a broker to perform licensed activity; broker education and eligibility are separate.",
                    C("AREC: Education and licensing guidelines", "https://arec.arkansas.gov/education/", null, "Official AREC education page fetched on 2026-10-23; captured content may change, so confirm the current rule before applying.")),
                Q("What prelicense education qualifies an Arkansas salesperson?",
                    "AREC requires a 60-hour salesperson prelicense certificate from an eligible school. The Commission says that the salesperson certificate does not expire, while broker education is governed by a separate time window.",
                    C("AREC: Education", "https://arec.arkansas.gov/education/", null, "Official AREC education page fetched on 2026-10-23; captured content may change, so confirm the current rule before applying.")),
                Q("When should Arkansas applicants take the salesperson exam?",
                    "The salesperson prelicense certificate is an examination prerequisite. The saved education guidance does not give exam registration details, so use AREC's current candidate instructions for scheduling and score handling.",
                    C("AREC: Education", "https://arec.arkansas.gov/education/", null, "Official AREC education page fetched on 2026-10-23; captured content may change, so confirm the current rule before applying.")),
                Q("What is Arkansas's first-license post-license requirement?",
                    "A new salesperson must complete the Commission's specific 18-classroom-hour post-license curriculum by the end of the month six months after initial licensure. The course follows license issuance and is distinct from CE.",
                    C("AREC: Post-License Guidelines", "https://arec.arkansas.gov/education/", null, "Official AREC education page fetched on 2026-10-23; captured content may change, so confirm the current rule before applying.")),
                Q("When does an Arkansas salesperson first need annual continuing education?",
                    "AREC requires seven CE hours each year to renew an active license, but states that a newly licensed person first satisfies CE at the second active renewal after licensure. Post-license education remains a separate obligation.",
                    C("AREC: Continuing Education", "https://arec.arkansas.gov/education/", null, "Official AREC education page fetched on 2026-10-23; captured content may change, so confirm the current rule before applying."))
            ],
            ["California"] =
            [
                Q("What is California's salesperson credential and minimum age?",
                    "California's entry-level credential is the real estate salesperson license; applicants must be at least 18. A salesperson may obtain the license before broker employment but may not perform licensed acts without a responsible broker.",
                    C("DRE: Requirements to Apply for a Real Estate Salesperson License", "https://dre.ca.gov/Examinees/RequirementsSales.html", null, "Official DRE applicant page fetched on 2026-10-23; content may change, so verify the live checklist before applying.")),
                Q("What college-level courses qualify a California salesperson for the exam?",
                    "Complete Real Estate Principles, Real Estate Practice, and one approved third course from the statutory list. DRE's page sets minimum course length and explains acceptable schools and college accreditation.",
                    C("DRE: Requirements to Apply for a Real Estate Salesperson License", "https://dre.ca.gov/Examinees/RequirementsSales.html", null, "Official DRE applicant page fetched on 2026-10-23; content may change, so verify the live checklist before applying.")),
                Q("What special course content does California require for Real Estate Practice?",
                    "For exam qualification, a Real Estate Practice course submitted under the current rule must include implicit-bias and fair-housing components, including an interactive role-play element. Check DRE's current applicant page for the effective requirement.",
                    C("DRE: Requirements to Apply for a Real Estate Salesperson License", "https://dre.ca.gov/Examinees/RequirementsSales.html", null, "Official DRE applicant page fetched on 2026-10-23; content may change, so verify the live checklist before applying.")),
                Q("How does California examination and application sequencing work?",
                    "First qualify for and pass the written salesperson examination; DRE then provides a license application for submission and approval. This is not a single exam-and-license transaction, so allow for the separate application review.",
                    C("DRE: Requirements to Apply for a Real Estate Salesperson License", "https://dre.ca.gov/Examinees/RequirementsSales.html", null, "Official DRE applicant page fetched on 2026-10-23; content may change, so verify the live checklist before applying.")),
                Q("Can a California salesperson practice without a broker?",
                    "A person may hold the salesperson license without immediate broker employment, but cannot perform acts requiring a real estate license without a responsible licensed broker. California's license status and employment rules are separate issues.",
                    C("DRE: Requirements to Apply for a Real Estate Salesperson License", "https://dre.ca.gov/Examinees/RequirementsSales.html", null, "Official DRE applicant page fetched on 2026-10-23; content may change, so verify the live checklist before applying."))
            ],
            ["Colorado"] =
            [
                Q("Does Colorado license salespersons or brokers at entry level?",
                    "Colorado's entry-level real estate license is a broker license, not a salesperson credential. The Division separates associate, independent, and employing broker levels; do not treat these as interchangeable.",
                    C("Colorado Division: Broker License Application Requirements", null, "src/docs/COLORADO/TXTS/Broker License Application Requirements PORTAL.txt")),
                Q("What prelicense education is required for a first Colorado broker license?",
                    "The Division's saved application requirements list 168 hours across six subject areas, or a qualifying real-estate degree substitution. The course breakdown includes law, contracts, trust accounts, closings, and practical application.",
                    C("Colorado Division: Broker License Application Requirements", null, "src/docs/COLORADO/TXTS/Broker License Application Requirements PORTAL.txt")),
                Q("What examinations and background screening apply in Colorado?",
                    "A first-time Colorado broker applicant must pass both the national and state exam portions and complete a fingerprint-based criminal-history check. The saved Division page states each exam portion's passing result is valid for one year.",
                    C("Colorado Division: Broker License Application Requirements", null, "src/docs/COLORADO/TXTS/Broker License Application Requirements PORTAL.txt")),
                Q("What prior experience qualifies for Colorado independent or employing broker status?",
                    "The Division requires two years of active broker experience for independent or employing status; salesperson work does not count as equivalent broker experience. Applicants short of that experience may qualify at associate level.",
                    C("Colorado Division: Broker License Application Requirements", null, "src/docs/COLORADO/TXTS/Broker License Application Requirements PORTAL.txt")),
                Q("What additional step applies to Colorado employing-broker applicants?",
                    "An employing-level application requires a 24-hour brokerage-administration course, an experience affidavit, and a supporting experience log. This is an added level qualification, not part of the first-time associate-broker checklist.",
                    C("Colorado Division: Broker License Application Requirements", null, "src/docs/COLORADO/TXTS/Broker License Application Requirements PORTAL.txt"))
            ],
            ["Connecticut"] =
            [
                Q("What is the entry-level Connecticut real estate license?",
                    "Connecticut's entry credential is the salesperson license. The applicant must be at least 18 and complete the state's approved 60-hour Real Estate Principles and Practices course before the exam process.",
                    C("Real Estate Salesperson Examination Application, Connecticut DCP", "https://portal.ct.gov/dcp/license-services-division/all-license-applications/real-estate-salesperson---initialexam")),
                Q("What education must a Connecticut salesperson complete?",
                    "Complete 60 hours of approved Real Estate Principles and Practices education. Confirm provider approval and any exemption directly with DCP; the saved Connecticut local file is a licensee sample, not an education rule.",
                    C("Real Estate Salesperson Examination Application, Connecticut DCP", "https://portal.ct.gov/dcp/license-services-division/all-license-applications/real-estate-salesperson---initialexam")),
                Q("How does a Connecticut salesperson qualify for the state exam?",
                    "After the approved course, apply and pay the required fee, then pass Connecticut's salesperson examination. Follow the official application for current scheduling and score-report procedures.",
                    C("Real Estate Salesperson Examination Application, Connecticut DCP", "https://portal.ct.gov/dcp/license-services-division/all-license-applications/real-estate-salesperson---initialexam")),
                Q("What does Connecticut require for an active salesperson affiliation?",
                    "A Connecticut-licensed broker must affiliate with the salesperson before license issuance. Passing the examination alone does not complete the salesperson licensing process or authorize independent brokerage.",
                    C("Real Estate Salesperson Examination Application, Connecticut DCP", "https://portal.ct.gov/dcp/license-services-division/all-license-applications/real-estate-salesperson---initialexam")),
                Q("What should Connecticut licensees verify for renewal?",
                    "The initial examination application does not establish the later renewal cycle or CE hours. Use DCP's current salesperson renewal instructions; the saved Connecticut licensee sample is not a renewal source.",
                    C("Connecticut DCP: Real Estate Licensing", "https://portal.ct.gov/dcp/license-services-division/all-license-applications/real-estate-salesperson---initialexam"))
            ],
            ["Delaware"] =
            [
                Q("What is Delaware's entry-level real estate license?",
                    "The Delaware Real Estate Commission's salesperson license is the entry-level credential; broker licensure is a different application category. Applicants should use the salesperson instructions rather than broker-affiliation forms.",
                    C("Delaware DPR: Salesperson Licensure", "https://dpr.delaware.gov/boards/realestate/ressalesperson/", null, "Official Delaware salesperson application page fetched on 2026-10-23; content was truncated after its initial instructions, so verify remaining details online.")),
                Q("What education does Delaware require for a salesperson?",
                    "The official salesperson instructions require completion of a 99-hour Delaware prelicensing course and submission of its completion certificate. Follow the current course and certificate specifications on the licensure page.",
                    C("Delaware DPR: Salesperson Licensure", "https://dpr.delaware.gov/boards/realestate/ressalesperson/", null, "Official Delaware salesperson application page fetched on 2026-10-23; content was truncated after its initial instructions, so verify remaining details online.")),
                Q("What examination must a Delaware salesperson pass?",
                    "Delaware candidates must pass the general and Delaware-law portions of the salesperson examination. The official instructions identify Pearson VUE score reporting as part of the initial application package.",
                    C("Delaware DPR: Salesperson Licensure", "https://dpr.delaware.gov/boards/realestate/ressalesperson/", null, "Official Delaware salesperson application page fetched on 2026-10-23; content was truncated after its initial instructions, so verify remaining details online.")),
                Q("How does Delaware handle the initial salesperson application?",
                    "Apply through DELPROS and provide the course certificate and examination score report, along with other requested materials. The page states an application started online must be completed and submitted within six months.",
                    C("Delaware DPR: Salesperson Licensure", "https://dpr.delaware.gov/boards/realestate/ressalesperson/", null, "Official Delaware salesperson application page fetched on 2026-10-23; content was truncated after its initial instructions, so verify remaining details online.")),
                Q("Does a Delaware salesperson license automatically permit independent practice?",
                    "No: salesperson work is through a broker, and the Delaware Commission has broker-affiliation documentation. Do not confuse a form for adding an affiliated company with the initial salesperson qualification checklist.",
                    C("Delaware Real Estate Commission", "https://dpr.delaware.gov/boards/realestate/"))
            ],
            ["Florida"] =
            [
                Q("What is the Florida entry-level real estate license?",
                    "Florida's entry-level credential is sales associate. Applicants must be 18 or older and have a high-school diploma or equivalent; sales associate and broker applications are separate licensing categories.",
                    C("Florida DBPR: Sales Associate Initial Application (RE 1)", "https://www.myfloridalicense.com/CheckListDetail.asp?xactCode=1010&clientCode=2501&XACT_DEFN_ID=744")),
                Q("What education is required before Florida's sales-associate exam?",
                    "Complete the DBPR-approved 63-hour sales-associate prelicense course before examination. Retain the course completion documentation and check DBPR's current instructions for any accepted equivalent or exemption.",
                    C("Florida DBPR: Real Estate Associate Requirements", "https://www2.myfloridalicense.com/re/documents/RESLRequirements.pdf")),
                Q("What screening should a Florida applicant plan for?",
                    "Florida requires electronic fingerprints for the initial sales-associate application. Submit them through the approved process and make sure the identifying information matches your DBPR application.",
                    C("Florida DBPR: Sales Associate Initial Application (RE 1)", "https://www.myfloridalicense.com/CheckListDetail.asp?xactCode=1010&clientCode=2501&XACT_DEFN_ID=744")),
                Q("What examination follows Florida prelicense education?",
                    "After the education prerequisite, pass the Florida sales-associate state examination and complete the initial application. Verify the current provider and instructions through DBPR because passing alone does not issue a license.",
                    C("Florida DBPR: Real Estate Commission Licensure Information", "https://www2.myfloridalicense.com/real-estate-commission/licensure-information/")),
                Q("What post-license obligation applies to Florida sales associates?",
                    "A newly licensed sales associate must complete 45 hours of post-license education before the first renewal. This first-renewal obligation differs from continuing education for later renewals; confirm the actual due date on your license record.",
                    C("Florida DBPR: Real Estate Associate Requirements", "https://www2.myfloridalicense.com/re/documents/RESLRequirements.pdf"))
            ],
            ["Georgia"] =
            [
                Q("What is the Georgia entry-level real estate credential?",
                    "Georgia's entry-level credential is salesperson. Applicants must be at least 18 and hold a high-school diploma or GED; broker licensing is a separate license type.",
                    C("Georgia Real Estate Commission: Obtaining a License", "https://grec.state.ga.us/obtaining-a-license/real-estate/#salesperson")),
                Q("What education is required before a Georgia salesperson examination?",
                    "The standard route is a 75-hour GREC-approved salesperson prelicense course; candidates may also qualify through specified alternative education. Check the Commission's current education guide before selecting a route.",
                    C("GREC: Obtaining a License", "https://grec.state.ga.us/obtaining-a-license/real-estate/#salesperson")),
                Q("What does Georgia require after a salesperson's initial license?",
                    "A new Georgia salesperson must complete a 25-hour GREC-approved post-license course within the first year. This is separate from the 36-hour continuing-education cycle used for later renewal.",
                    C("GREC Rule 520-1-.05: Maintaining a License", "https://grec.state.ga.us/commission-rules/520-1-05-maintaining-a-license/")),
                Q("How does Georgia treat the salesperson examination?",
                    "A salesperson candidate must pass the state licensing examination after completing an accepted education route. Use GREC's candidate instructions for registration and application timing; the exam result alone does not complete license activation.",
                    C("GREC: Obtaining a License", "https://grec.state.ga.us/obtaining-a-license/real-estate/#salesperson")),
                Q("What education applies to Georgia license renewal?",
                    "Georgia requires 36 hours of continuing education per four-year renewal cycle, including three hours of license-law education. Confirm the current cycle and record status in GREC's licensee rules.",
                    C("GREC Rule 520-1-.05: Maintaining a License", "https://grec.state.ga.us/commission-rules/520-1-05-maintaining-a-license/"))
            ],
            ["Hawaii"] =
            [
                Q("What is Hawaii's entry-level real estate license?",
                    "Hawaii's entry-level credential is salesperson, distinct from the broker license. The saved Commission curriculum is explicitly for salesperson prelicensing, not a broker course or a complete application checklist.",
                    C("Hawaii Real Estate Commission: Salespersons' Prelicensing Curriculum", null, "src/docs/HAWAII/LICENSING/TXTs/HAWAII-SALESPERSONS-PRELICENSING-CURRICULUM.txt")),
                Q("What subjects are included in Hawaii's salesperson prelicense curriculum?",
                    "The Commission's curriculum includes Hawaii licensing law, agency, property rights, transfer, contracts, closing, disclosures, valuation, financing, management, ownership, fair housing, taxes, and mathematics.",
                    C("Hawaii Real Estate Commission: Salespersons' Prelicensing Curriculum", null, "src/docs/HAWAII/LICENSING/TXTs/HAWAII-SALESPERSONS-PRELICENSING-CURRICULUM.txt")),
                Q("What exam prerequisites should a Hawaii salesperson candidate check?",
                    "The Real Estate Branch publishes examination prerequisites separately from the course curriculum. Verify the current education completion proof and exam eligibility on that page before scheduling.",
                    C("Hawaii DCCA: Examination Prerequisites", "https://www.cca.hawaii.gov/reb/real_ed-exam_lic-exam_info-exam_prereq/")),
                Q("How does Hawaii issue a salesperson license after the exam?",
                    "Hawaii's licensing information describes an application step after examination; passing the test alone should not be treated as an issued license. Follow the current application and supporting-document checklist from the Real Estate Branch.",
                    C("Hawaii DCCA: General Licensing Information", "https://cca.hawaii.gov/reb/real_ed/general-licensing-information/")),
                Q("When may a Hawaii salesperson conduct brokerage activity?",
                    "The salesperson credential and the ability to practice under an active broker relationship are separate questions. Confirm current affiliation and active-status requirements with the Real Estate Branch before engaging in licensed activity.",
                    C("Hawaii DCCA: General Licensing Information", "https://cca.hawaii.gov/reb/real_ed/general-licensing-information/"))
            ],
            ["Idaho"] =
            [
                Q("What is Idaho's entry-level real estate license?",
                    "Idaho's entry-level license is salesperson, distinct from associate broker. The Commission requires 90 hours of salesperson prelicense education in two 45-hour course modules.",
                    C("Idaho DOPL: Real Estate", "https://dopl.idaho.gov/real-estate/")),
                Q("What does the saved Idaho education page establish for broker applicants?",
                    "The saved education page covers the broker path: at least 90 hours including brokerage management, real estate law, and two approved electives. This is broker coursework and must not be substituted for the salesperson course sequence.",
                    C("Idaho Broker Prelicense Education", null, "src/docs/IDAHO/TXTS/Broker Prelicense Education PORTAL.txt")),
                Q("What Idaho salesperson education hours are established by this saved source?",
                    "Idaho's licensing statute and DOPL education guidance set 90 hours for salesperson applicants, split into two 45-hour courses. Check DOPL's approved-school list and current course completion rules.",
                    C("Idaho DOPL: Real Estate", "https://dopl.idaho.gov/real-estate/")),
                Q("How should Idaho applicants verify examination and background steps?",
                    "Applicants must pass the Idaho salesperson examination and complete the required fingerprint-based background screening. DOPL's application instructions control how to schedule and submit the examination and background results.",
                    C("Idaho Code § 54-2012", "https://legislature.idaho.gov/statutesrules/idstat/Title54/T54CH20/SECT54-2012/")),
                Q("What should an Idaho licensee check for first renewal?",
                    "Idaho salesperson licensees should confirm the current biennial renewal date and continuing-education requirements with DOPL. The saved broker prelicense page is not evidence for salesperson renewal credit or first-renewal exceptions.",
                    C("Idaho Real Estate Commission", "https://dopl.idaho.gov/real-estate/"))
            ],
            ["Illinois"] =
            [
                Q("What is the entry-level Illinois real estate credential?",
                    "Illinois uses a broker license for entry-level brokerage practice; managing broker is a separate higher-level credential. An Illinois initial applicant should use the broker checklist, not a salesperson checklist from another state.",
                    C("IDFPR: Real Estate Broker Licensing", "https://idfpr.illinois.gov/DRE/")),
                Q("What education is required for an Illinois broker applicant?",
                    "The standard Illinois broker path requires 75 hours of approved prelicense education: 60 hours of broker topics and a 15-hour interactive course. Verify provider approval and course certificates with IDFPR.",
                    C("IDFPR: Real Estate Broker Licensing", "https://idfpr.illinois.gov/DRE/")),
                Q("What examination must an Illinois applicant pass?",
                    "After the education prerequisite, an Illinois applicant passes the broker licensing examination, which includes national and state content. Use the current PSI candidate bulletin for scheduling and reporting results.",
                    C("IDFPR: Real Estate Broker Licensing", "https://idfpr.illinois.gov/DRE/")),
                Q("What is Illinois's post-license requirement for a newly licensed broker?",
                    "A newly licensed Illinois broker must complete the required post-license education before the first renewal deadline. It is separate from later CE; confirm the applicable 45-hour post-license course and deadline for your license cycle.",
                    C("IDFPR: Real Estate Broker Licensing", "https://idfpr.illinois.gov/DRE/")),
                Q("How should Illinois candidates handle license application and background disclosure?",
                    "Submit the broker application through IDFPR, complete the required background screening, and arrange a sponsoring broker for active status. The regulator's checklist controls supporting documents and any screening explanations.",
                    C("IDFPR: Real Estate Broker Licensing", "https://idfpr.illinois.gov/DRE/"))
            ],
            ["Indiana"] =
            [
                Q("What is Indiana's entry-level real estate license?",
                    "Indiana's entry-level credential is broker. The applicant must complete 90 hours of approved prelicense education, pass the state examination, and meet application and screening requirements; managing broker status is distinct.",
                    C("Indiana Real Estate Commission, Article 6", null, "src/docs/INDIANA/TXTS/ARTICLE 6 REAL ESTATE SCHOOLS; PRELICENSING EDUCATION; EXAMINATION PORTAL.txt")),
                Q("What prelicensing education should an Indiana broker applicant complete?",
                    "Complete 90 hours of broker prelicense education through an approved provider. Article 6 governs school and course approval; PLA's current application instructions specify the completion evidence to submit.",
                    C("Indiana PLA: Real Estate Licensing Information", "https://www.in.gov/pla/professions/real-estate-home/real-estate-licensing-information/")),
                Q("What exam rule does Indiana Article 6 cover?",
                    "Indiana applicants must pass the state broker licensing examination after the approved education prerequisite. The saved Article 6 includes examination provisions; current PLA instructions control scheduling, score reporting, and retakes.",
                    C("Indiana Real Estate Commission, Article 6", null, "src/docs/INDIANA/TXTS/ARTICLE 6 REAL ESTATE SCHOOLS; PRELICENSING EDUCATION; EXAMINATION PORTAL.txt")),
                Q("How does affiliation affect an Indiana broker license?",
                    "A broker who will practice actively must affiliate with a licensed broker company or principal broker. Without the affiliation section completed, the PLA instructions say the license is issued unassigned or inactive.",
                    C("Indiana PLA: Real Estate Licensing Information", "https://www.in.gov/pla/professions/real-estate-home/real-estate-licensing-information/")),
                Q("What does Indiana require after a broker's initial license?",
                    "A newly licensed Indiana broker must complete 30 hours of post-license education within two years. Active renewal is a separate annual education obligation; check the PLA renewal schedule and license expiration before planning.",
                    C("Indiana PLA: Real Estate Licensing Information", "https://www.in.gov/pla/professions/real-estate-home/real-estate-licensing-information/"))
            ],
            ["Iowa"] =
            [
                Q("What is Iowa's entry-level real estate credential and minimum age?",
                    "Iowa's entry-level credential is salesperson; applicants must be 18 when the application is submitted. The Iowa Commission also has separate broker-by-examination requirements.",
                    C("Iowa Salesperson and Broker License by Exam", null, "src/docs/IOWA/TXTS/Real Estate Salesperson and Brokers License by Exam PORTAL.txt")),
                Q("What Iowa education is required before applying as a salesperson?",
                    "Iowa requires a 60-hour approved principles-and-practices prelicense course plus three 12-hour live courses: buying practices, listing practices, and professional/ethical practices. The courses must meet the one-year application window.",
                    C("Iowa Salesperson and Broker License by Exam", null, "src/docs/IOWA/TXTS/Real Estate Salesperson and Brokers License by Exam PORTAL.txt")),
                Q("What does Iowa require before the salesperson examination?",
                    "The 60-hour course must be completed before the applicant is eligible to take the exam. Both national and state portions must be passed, and scores expire on the last working day of the sixth month after passing.",
                    C("Iowa Salesperson and Broker License by Exam", null, "src/docs/IOWA/TXTS/Real Estate Salesperson and Brokers License by Exam PORTAL.txt")),
                Q("What Iowa application checks and insurance apply?",
                    "The saved Commission guide requires a national criminal-history check and errors-and-omissions insurance; applications are submitted electronically through My Iowa PLB. Background checks are stated to be valid for 210 days.",
                    C("Iowa Salesperson and Broker License by Exam", null, "src/docs/IOWA/TXTS/Real Estate Salesperson and Brokers License by Exam PORTAL.txt")),
                Q("How does Iowa distinguish initial salesperson qualification from renewal?",
                    "Education and exam validity apply to the initial application, while ongoing active renewal is a separate process with continuing-education requirements. The Commission says renewal notices do not replace the licensee's responsibility to file on time.",
                    C("Iowa Salesperson and Broker License by Exam", null, "src/docs/IOWA/TXTS/Real Estate Salesperson and Brokers License by Exam PORTAL.txt"))
            ],
            ["Kansas"] =
            [
                Q("What is the entry-level Kansas real estate license?",
                    "Kansas's entry-level real estate license is salesperson; a broker license has separate experience and education rules. The Commission's prelicense page distinguishes the salesperson courses from broker coursework.",
                    C("Kansas Real Estate Commission: Pre-License Education", null, "src/docs/KANSAS/TXTS/Pre-License Education PORTAL.txt")),
                Q("What courses does a Kansas salesperson applicant need?",
                    "A Kansas salesperson applicant not licensed in another state must complete the 30-hour Principles of Real Estate course, then every applicant must complete the 30-hour Kansas Practice course. These are separate courses with separate timing rules.",
                    C("Kansas Real Estate Commission: Pre-License Education", null, "src/docs/KANSAS/TXTS/Pre-License Education PORTAL.txt")),
                Q("How long do Kansas salesperson education certificates remain usable?",
                    "The Commission says Principles expires 12 months from the course date; it must receive the Kansas Practice certificate and license application within six months of that course date.",
                    C("Kansas Real Estate Commission: Pre-License Education", null, "src/docs/KANSAS/TXTS/Pre-License Education PORTAL.txt")),
                Q("What exam requirement follows Kansas salesperson education?",
                    "The saved Commission page ties the Principles course to eligibility for the salesperson exam. Use the current KREC exam instructions for registration and score requirements, and do not overlook the separate Kansas Practice course.",
                    C("Kansas Real Estate Commission: Pre-License Education", null, "src/docs/KANSAS/TXTS/Pre-License Education PORTAL.txt")),
                Q("What does Kansas require for a broker applicant instead?",
                    "A broker candidate's course set is distinct: the saved page lists Broker Fundamentals and Broker Management, each 30 hours, with different expiration windows; an out-of-state broker candidate has a Kansas law course.",
                    C("Kansas Real Estate Commission: Pre-License Education", null, "src/docs/KANSAS/TXTS/Pre-License Education PORTAL.txt"))
            ],
            ["Kentucky"] =
            [
                Q("What is Kentucky's entry-level real estate license?",
                    "Kentucky's entry-level credential is sales associate. Applicants complete an approved prelicense route, pass the sales-associate examination, undergo a national criminal-history check, and affiliate with a principal broker.",
                    C("KREC: New Application Overview", "https://krec.ky.gov/licensing/Pages/New-Application-Overview.aspx")),
                Q("What prelicense education should a Kentucky sales-associate candidate verify?",
                    "The usual Kentucky route is 96 hours of KREC-approved prelicense education; the Commission also recognizes a qualifying college-credit alternative. Check its current education page for approved options and course completion proof.",
                    C("KREC: Education", "https://krec.ky.gov/licensing/education/Pages/default.aspx")),
                Q("What examination is part of Kentucky initial licensure?",
                    "Pass the Kentucky sales-associate licensing examination after satisfying the education route. The Commission's candidate materials govern scheduling, score reporting, and the application window after passing.",
                    C("KREC: New Application Overview", "https://krec.ky.gov/licensing/Pages/New-Application-Overview.aspx")),
                Q("What application or background details should Kentucky applicants prepare?",
                    "A Kentucky applicant must submit the application within 60 days of passing and provide the required national criminal background check. Follow KREC's checklist for required disclosures and broker affiliation.",
                    C("KREC: New Application Overview", "https://krec.ky.gov/licensing/Pages/New-Application-Overview.aspx")),
                Q("What first-renewal education rule is verified for Kentucky here?",
                    "Kentucky requires 48 hours of post-license education within two years after activation. This is a one-time post-license obligation, distinct from the Commission's later renewal CE; verify the due date on the license record.",
                    C("KREC: Education", "https://krec.ky.gov/licensing/education/Pages/default.aspx"))
            ],
            ["Louisiana"] =
            [
                Q("What is Louisiana's entry-level real estate credential?",
                    "Louisiana's entry-level individual credential is salesperson. Applicants must be at least 18 and hold a high-school diploma or GED; associate broker and qualifying broker are separate categories.",
                    C("LREC: Become Licensed — Salesperson", "https://lrec.gov/become-licensed/salesperson/", null, "Official LREC salesperson requirements page fetched on 2026-10-23; confirm the live checklist before applying.")),
                Q("What does the saved Louisiana school document establish?",
                    "The saved RF-1 is a provider course-schedule form that must be submitted at least 10 days before a course starts. It does not establish an applicant's education hours, exam eligibility, or license checklist.",
                    C("LREC RF-1 School Pre-License Course Schedule", null, "src/docs/LOUISIANA/TXTS/66bfbcecb677cdf9f94e3ce9_School Pre-License Course Schedule - RF-1.txt")),
                Q("Where should a Louisiana salesperson verify required prelicense coursework?",
                    "The standard Louisiana route requires 90 hours of LREC-approved prelicense education. Check the Commission's prelicensure checklist for course content and the approved-provider list before enrolling.",
                    C("LREC: Become Licensed — Salesperson", "https://lrec.gov/become-licensed/salesperson/", null, "Official LREC salesperson requirements page fetched on 2026-10-23; confirm the live checklist before applying.")),
                Q("How should a Louisiana candidate verify the licensing examination?",
                    "After meeting the education prerequisite, pass the required Louisiana salesperson examination. LREC's application checklist controls current test registration, application records, and broker-sponsorship documentation.",
                    C("LREC: Become Licensed — Salesperson", "https://lrec.gov/become-licensed/salesperson/", null, "Official LREC salesperson requirements page fetched on 2026-10-23; confirm the live checklist before applying.")),
                Q("What is the source-backed first-license education distinction in Louisiana?",
                    "A new salesperson must complete 45 hours of post-license education within 180 days, in addition to annual renewal CE. These are distinct obligations; verify the applicable course and personal due date with LREC.",
                    C("LREC: Post Licensing Education Requirements", "https://lrec.gov/current-licensees/post-licensing-education-requirements"))
            ],
            ["Maine"] =
            [
                Q("What is Maine's entry-level real estate credential?",
                    "Maine's entry-level credential is sales agent. Applicants must be 18 or older and have a high-school diploma or equivalent; broker categories have separate education and experience rules.",
                    C("Maine Real Estate Commission: Individual Licensing", "https://www.maine.gov/pfr/professionallicensing/professions/real-estate-commission/licensing/individual-licenses")),
                Q("What does Maine's saved qualifying-education page verify?",
                    "The Commission's saved qualifying-education source identifies approved providers and sales-agent course materials. The Commission's current individual licensing page specifies a 55-hour approved prelicense course for applicants.",
                    C("Maine Qualifying Education", null, "src/docs/MAINE/TXTS/Qualifying Education PORTAL.txt")),
                Q("How should a Maine salesperson candidate verify examination eligibility?",
                    "After completing the 55-hour course, pass the state real estate examination. Use the Commission's current application instructions for exam registration and the documents required to issue the sales-agent license.",
                    C("Maine Real Estate Commission: Individual Licensing", "https://www.maine.gov/pfr/professionallicensing/professions/real-estate-commission/licensing/individual-licenses")),
                Q("What broker progression is established by Maine's course materials?",
                    "Maine's education materials separately list sales-agent, associate-broker, and designated-broker course categories. A sales-agent applicant should complete the sales-agent course rather than a broker-only qualifying course.",
                    C("Maine Qualifying Education", null, "src/docs/MAINE/TXTS/Qualifying Education PORTAL.txt")),
                Q("What should Maine licensees check for renewal and continuing education?",
                    "The first Maine sales-agent license is valid for two years and cannot be renewed; progression to associate broker involves additional education. Verify the transition and renewal timeline with the Commission before the initial term ends.",
                    C("Maine Real Estate Commission: Individual Licensing", "https://www.maine.gov/pfr/professionallicensing/professions/real-estate-commission/licensing/individual-licenses"))
            ],
            ["Maryland"] =
            [
                Q("What is Maryland's entry-level real estate license?",
                    "Maryland's entry-level credential is salesperson. Applicants must be at least 18 and the saved original application shows a broker certification for affiliation; without one, the license may be issued inactive.",
                    C("Maryland Original Salesperson License Application", null, "src/docs/MARYLAND/TXTS/mrec-origsalespersonlicense.txt")),
                Q("What relationship must Maryland's original salesperson application document?",
                    "The application includes the broker's identifying information and certification that the applicant will be licensed through that broker. It also provides an inactive option when no broker affiliation exists.",
                    C("Maryland Original Salesperson License Application", null, "src/docs/MARYLAND/TXTS/mrec-origsalespersonlicense.txt")),
                Q("What background disclosures appear on Maryland's application?",
                    "Maryland asks about criminal convictions and prior real estate license denial, suspension, or revocation. Applicants answering yes must provide requested court records or an explanation, and authorize review of application information.",
                    C("Maryland Original Salesperson License Application", null, "src/docs/MARYLAND/TXTS/mrec-origsalespersonlicense.txt")),
                Q("What education and exam credentials should a Maryland applicant confirm?",
                    "Maryland requires a 60-hour Commission-approved prelicensing course and passing both national and state examination portions. The current MREC application governs the post-exam filing and any supporting records.",
                    C("Maryland Real Estate Commission: Licensing and FAQs", "https://labor.maryland.gov/license/mrec/mrecfaq.shtml")),
                Q("What Maryland renewal requirements apply after initial licensure?",
                    "Maryland salesperson renewals require continuing education on the Commission's renewal cycle; the standard requirement is 15 hours every two years. Check MREC's current notice for your license's exact renewal date and required CE mix.",
                    C("Maryland Real Estate Commission: Licensing and FAQs", "https://labor.maryland.gov/license/mrec/mrecfaq.shtml"))
            ],
            ["Massachusetts"] =
            [
                Q("What is the entry-level Massachusetts real estate credential?",
                    "Massachusetts licenses salespersons separately from brokers; an applicant must be at least 18. The saved 254 CMR 2.00 licensure regulation addresses examination and application procedures and good moral character.",
                    C("254 CMR 2.00: Licensure", null, "src/docs/MASSACHUSETTS/TXTS/254 CMR 2.00 Licensure PORTAL.txt")),
                Q("What education must a Massachusetts salesperson complete?",
                    "The salesperson route requires 40 hours of prelicense education through a Board-approved school before the exam. The saved licensure regulation is from 2013, so verify the course approval and current checklist with the Board.",
                    C("Massachusetts: Prepare for and Schedule the Real Estate License Exam", "https://www.mass.gov/how-to/prepare-for-and-schedule-your-real-estate-license-exam")),
                Q("What does Massachusetts review beyond the exam?",
                    "Massachusetts's licensure regulation expressly addresses good moral character, application procedures, and examination eligibility. Disclose criminal or disciplinary matters and provide any supporting documents requested by the Board.",
                    C("254 CMR 2.00: Licensure", null, "src/docs/MASSACHUSETTS/TXTS/254 CMR 2.00 Licensure PORTAL.txt")),
                Q("How does Massachusetts treat a salesperson's broker relationship?",
                    "A salesperson is not a broker; the official licensing materials call for broker affiliation to practice. Complete the affiliation step through the Board's current process rather than assuming exam passage grants independent practice authority.",
                    C("Massachusetts: Prepare for and Schedule the Real Estate License Exam", "https://www.mass.gov/how-to/prepare-for-and-schedule-your-real-estate-license-exam")),
                Q("What source should a Massachusetts licensee use for renewal CE?",
                    "Massachusetts requires 12 hours of continuing education every two years for renewal. The saved 254 CMR 2.00 source is initial licensure; use the Board's separate CE rule and current renewal notice.",
                    C("Massachusetts Board: Broker and Salesperson Licensing", "https://www.mass.gov/orgs/board-of-registration-of-real-estate-brokers-and-salespersons"))
            ],
            ["Michigan"] =
            [
                Q("What is Michigan's entry-level real estate credential?",
                    "Michigan's entry-level credential is salesperson; applicants must be at least 18. The broker license is a separate credential with additional qualification requirements.",
                    C("Michigan LARA: Real Estate", "https://www.michigan.gov/realestate")),
                Q("What education must a Michigan salesperson complete?",
                    "Michigan requires 40 hours of approved prelicensure education, including four hours of civil-rights law. Complete it through an approved provider before taking the salesperson examination.",
                    C("Michigan LARA: Real Estate", "https://www.michigan.gov/realestate")),
                Q("How should a Michigan salesperson applicant meet the exam requirement?",
                    "After completing approved education, pass the Michigan salesperson examination administered through the state's designated provider. Check the current candidate bulletin for registration, score, and retake procedures.",
                    C("Michigan LARA: Real Estate", "https://www.michigan.gov/realestate")),
                Q("Does a Michigan salesperson need broker affiliation to practice?",
                    "A Michigan salesperson must be sponsored by a licensed Michigan broker to practice. Complete the broker sponsorship and license-activation steps through MiPLUS; exam passage alone does not establish an active practice relationship.",
                    C("Michigan LARA: Real Estate", "https://www.michigan.gov/realestate")),
                Q("What should Michigan applicants verify about renewal education?",
                    "Michigan salesperson licenses renew on a three-year cycle and require continuing education. Confirm the current number and first-renewal applicability in LARA's current renewal instructions before selecting classes.",
                    C("Michigan LARA: Real Estate", "https://www.michigan.gov/realestate"))
            ],
            ["Minnesota"] =
            [
                Q("What is Minnesota's entry-level real estate credential?",
                    "Minnesota licenses salespersons and brokers separately. A salesperson applicant must complete 90 hours of prelicense education and pass the state examination before qualifying for license issuance.",
                    C("Minnesota Commerce: Apply for a Real Estate License", null, "src/docs/MINNESOTA/TXTS/Apply for a Real Estate License PORTAL.txt")),
                Q("What does Minnesota require with an online salesperson application?",
                    "The saved application page says applicants must upload a signed and dated attestation that they reviewed all application answers and that the answers are accurate. The form requirement is separate from education and examination eligibility.",
                    C("Minnesota Commerce: Apply for a Real Estate License", null, "src/docs/MINNESOTA/TXTS/Apply for a Real Estate License PORTAL.txt")),
                Q("What Minnesota education and exam steps precede salesperson licensure?",
                    "Minnesota's prelicense requirement totals 90 hours, completed as the approved course sequence; applicants must also pass the state salesperson exam. Check the current education page for course sequencing and score-report handling.",
                    C("Minnesota Commerce: Real Estate Education", "https://mn.gov/commerce/licensing/list/education-licensees/index.jsp")),
                Q("How should a Minnesota salesperson establish broker affiliation?",
                    "A salesperson must be employed by a licensed broker to practice. The application distinguishes salesperson, broker, and company filings; ensure the supervising broker relationship is recorded before conducting licensed activity.",
                    C("Minnesota Commerce: Real Estate Licensing", "https://mn.gov/commerce/licensing/list/real-estate/real-estate.jsp")),
                Q("What Minnesota post-license or renewal rules should a new salesperson confirm?",
                    "Minnesota requires additional first-year education for new salespersons, then continuing education for renewal. Confirm the precise first-year course and current renewal credit in Commerce's licensee education instructions.",
                    C("Minnesota Commerce: Real Estate Education", "https://mn.gov/commerce/licensing/list/education-licensees/index.jsp"))
            ],
            ["Mississippi"] =
            [
                Q("What is the entry-level Mississippi real estate license?",
                    "Mississippi's entry-level license is salesperson, distinct from broker. Applicants must complete approved prelicense education, pass both exam portions, and submit a fingerprint-based background check.",
                    C("Mississippi Real Estate Commission Rules and Regulations, revised 2025-02-21", null, "src/docs/MISSISSIPPI/LICENSING/TXTs/MREC_RULES_AND_REGULATIONS_REV_02-21-2025.txt")),
                Q("What must a Mississippi salesperson applicant disclose?",
                    "Submit fingerprints for a state and federal criminal-history check and answer Commission background questions fully. The saved rulebook has a separate Chapter 1 on licensing; disclose matters called for on the current application.",
                    C("Mississippi Real Estate Commission Rules and Regulations, revised 2025-02-21", null, "src/docs/MISSISSIPPI/LICENSING/TXTs/MREC_RULES_AND_REGULATIONS_REV_02-21-2025.txt")),
                Q("Where should Mississippi candidates verify prelicense education?",
                    "The standard salesperson route requires 60 hours of approved prelicense education. Verify Commission-approved providers and course completion documentation in the current MREC applicant instructions.",
                    C("Mississippi Real Estate Commission: Salesperson Instructions", "https://www.mrec.ms.gov/wp-content/uploads/2022/02/Salesperson-Instructions.pdf")),
                Q("What should a Mississippi candidate check before scheduling the exam?",
                    "Pass both the national and state parts of the salesperson examination after meeting education requirements. MREC's instructions identify separate passing scores and control current registration and result reporting.",
                    C("Mississippi Real Estate Commission: Salesperson Instructions", "https://www.mrec.ms.gov/wp-content/uploads/2022/02/Salesperson-Instructions.pdf")),
                Q("What continuing-education framework applies in Mississippi?",
                    "A new salesperson must complete the separate 30-hour post-license course within one year. Later renewal requires continuing education on the Commission's renewal cycle; check for current rule amendments before relying on a deadline.",
                    C("Mississippi Real Estate Commission Rules and Regulations, revised 2025-02-21", null, "src/docs/MISSISSIPPI/LICENSING/TXTs/MREC_RULES_AND_REGULATIONS_REV_02-21-2025.txt"))
            ],
            ["Missouri"] =
            [
                Q("What is Missouri's entry-level real estate credential?",
                    "Missouri's entry-level credential is salesperson, separate from broker licensure. Applicants must be at least 18, provide fingerprints, and qualify through an education-and-exam route or a specified alternative.",
                    C("Missouri Examination and Licensure Eligibility Requirements", null, "src/docs/MISSOURI/TXTS/Examination_and_Licensure_Eligibility_Requirements.txt")),
                Q("What Missouri pre-exam education is required for a salesperson?",
                    "The standard route requires a certificate of satisfactory completion from a 48-hour pre-exam salesperson course at an accredited Missouri school. A licensed attorney may use a separate education-waiver route.",
                    C("Missouri Examination and Licensure Eligibility Requirements", null, "src/docs/MISSOURI/TXTS/Examination_and_Licensure_Eligibility_Requirements.txt")),
                Q("What must Missouri salesperson applicants pass and complete after the exam?",
                    "The standard route requires both Missouri salesperson exam portions and the 24-hour Missouri Real Estate Practice course before license application. The MREP course must follow successful completion of the 48-hour pre-exam course.",
                    C("Missouri Examination and Licensure Eligibility Requirements", null, "src/docs/MISSOURI/TXTS/Examination_and_Licensure_Eligibility_Requirements.txt")),
                Q("What is Missouri's deadline for submitting the salesperson application?",
                    "Under the standard route, the complete license application must be submitted within six months after completing the 48-hour pre-exam course. Alternative attorney and out-of-state routes have different trigger events, so check the applicable route.",
                    C("Missouri Examination and Licensure Eligibility Requirements", null, "src/docs/MISSOURI/TXTS/Examination_and_Licensure_Eligibility_Requirements.txt")),
                Q("What fingerprint screening applies to Missouri applicants?",
                    "All original Missouri license applicants must be fingerprinted for state and federal criminal-history checks before applying. The saved instructions explain how to register through the approved statewide vendor and route results to MREC.",
                    C("Missouri Examination and Licensure Eligibility Requirements", null, "src/docs/MISSOURI/TXTS/Examination_and_Licensure_Eligibility_Requirements.txt"))
            ]
        };

    private static StateLicensingFaq Q(string question, string answer, StateLicensingCitation citation)
    {
        IReadOnlyList<string> sourceFileNames = citation.RepositoryPath is null
            ? Array.Empty<string>()
            : [citation.RepositoryPath.Split('/')[^1]];

        return new StateLicensingFaq(
            question,
            answer,
            sourceFileNames,
            true,
            Citations: [citation]);
    }

    private static StateLicensingCitation C(
        string title,
        string? url,
        string? repositoryPath = null,
        string? currencyNote = null) =>
        new(
            title,
            url,
            repositoryPath,
            ResearchDate,
            currencyNote ??
            (repositoryPath is null
                ? "Official regulator source located and reviewed for this guide on 2026-10-23; the page was not independently live-verified, so recheck current instructions."
                : "Saved source capture; its capture date or currency may be limited and it was not live-verified on 2026-10-23. Confirm current requirements with the regulator."));
}
