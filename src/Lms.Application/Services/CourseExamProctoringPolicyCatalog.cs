using Lms.Domain.Entities;

namespace Lms.Application.Services;

public enum CourseExamPolicyStatus
{
    Verified,
    Conditional,
    NeedsReview
}

public enum CourseExamPolicyEvidenceKind
{
    CommissionRule,
    CommissionNotice,
    CommissionHandbook,
    CommissionProviderChecklist
}

public sealed record CourseExamProctoringPolicyResolution(
    string Jurisdiction,
    string CourseCategory,
    string DeliveryMethod,
    CourseExamPolicyStatus Status,
    string Requirement,
    int? MinimumPassingPercent,
    bool? ClosedBookRequired,
    bool? IdentityVerificationRequired,
    bool? RemoteProctoringAllowed,
    bool? RemoteProctoringRequiresCommissionApproval,
    DateOnly? EffectiveThrough,
    CourseExamPolicyEvidenceKind? EvidenceKind,
    string? Citation,
    string? SourceDocument,
    string? ReviewNote)
{
    public string StatusLabel => Status switch
    {
        CourseExamPolicyStatus.Verified => "Source rule recorded",
        CourseExamPolicyStatus.Conditional => "Conditional rule recorded",
        _ => "Needs commission review"
    };
}

public static class CourseExamProctoringPolicyCatalog
{
    private sealed record Rule(
        string State,
        string CourseCategory,
        string? DeliveryMethod,
        CourseExamPolicyStatus Status,
        string Requirement,
        int? MinimumPassingPercent,
        bool? ClosedBookRequired,
        bool? IdentityVerificationRequired,
        bool? RemoteProctoringAllowed,
        bool? RemoteProctoringRequiresCommissionApproval,
        DateOnly? EffectiveThrough,
        CourseExamPolicyEvidenceKind EvidenceKind,
        string Citation,
        string SourceDocument,
        string? ReviewNote = null);

    private static readonly Rule[] Rules =
    [
        Create("North Carolina", CourseComplianceTypes.Prelicensing, null,
            CourseExamPolicyStatus.Verified,
            "The end-of-course final must be proctored, closed-book, and passed at 75% or higher.",
            75, true, null, null, null, null,
            CourseExamPolicyEvidenceKind.CommissionRule,
            "21 NCAC 58H .0207(a), (c)",
            "NORTH CAROLINA/LICENSING/TEXTs/SYLLABUS/21 NCAC 58H .0207 COURSE COMPLETION CERTIFICATES AND REPORTS.txt"),
        Create("North Carolina", CourseComplianceTypes.Postlicensing, null,
            CourseExamPolicyStatus.Verified,
            "The end-of-course final must be proctored, closed-book, and passed at 75% or higher.",
            75, true, null, null, null, null,
            CourseExamPolicyEvidenceKind.CommissionRule,
            "21 NCAC 58H .0207(b), (c)",
            "NORTH CAROLINA/LICENSING/TEXTs/SYLLABUS/21 NCAC 58H .0207 COURSE COMPLETION CERTIFICATES AND REPORTS.txt"),
        Create("North Carolina", CourseComplianceTypes.Prelicensing, CourseDeliveryMethods.DistanceEducation,
            CourseExamPolicyStatus.Verified,
            "Proctoring or equivalent security controls must verify identity and prevent access to course materials.",
            75, true, true, true, null, null,
            CourseExamPolicyEvidenceKind.CommissionRule,
            "21 NCAC 58H .0207(c)",
            "NORTH CAROLINA/LICENSING/TEXTs/SYLLABUS/21 NCAC 58H .0207 COURSE COMPLETION CERTIFICATES AND REPORTS.txt"),
        Create("North Carolina", CourseComplianceTypes.Postlicensing, CourseDeliveryMethods.DistanceEducation,
            CourseExamPolicyStatus.Verified,
            "Proctoring or equivalent security controls must verify identity and prevent access to course materials.",
            75, true, true, true, null, null,
            CourseExamPolicyEvidenceKind.CommissionRule,
            "21 NCAC 58H .0207(c)",
            "NORTH CAROLINA/LICENSING/TEXTs/SYLLABUS/21 NCAC 58H .0207 COURSE COMPLETION CERTIFICATES AND REPORTS.txt"),
        Create("Alabama", CourseComplianceTypes.Prelicensing, CourseDeliveryMethods.DistanceEducation,
            CourseExamPolicyStatus.Verified,
            "A proctored final is required with a 70% minimum passing score; remote/virtual proctoring requires Commission approval.",
            70, null, null, true, true, null,
            CourseExamPolicyEvidenceKind.CommissionRule,
            "Rule 790-X-1-.21",
            "ALABAMA/LICENSING/TXTs/rule amendment-790-X-1-.21-distance-education-courses.txt"),
        Create("Alabama", CourseComplianceTypes.Postlicensing, CourseDeliveryMethods.DistanceEducation,
            CourseExamPolicyStatus.Verified,
            "A proctored final is required with a 70% minimum passing score; remote/virtual proctoring requires Commission approval.",
            70, null, null, true, true, null,
            CourseExamPolicyEvidenceKind.CommissionRule,
            "Rule 790-X-1-.21",
            "ALABAMA/LICENSING/TXTs/rule amendment-790-X-1-.21-distance-education-courses.txt"),
        Create("Georgia", CourseComplianceTypes.Prelicensing, null,
            CourseExamPolicyStatus.Verified,
            "A proctor is required for the final examination.",
            null, null, null, null, null, null,
            CourseExamPolicyEvidenceKind.CommissionRule,
            "GREC Rule 520-2-.04(11)(g)",
            "GEORGIA/LICENSING/TXTs/Chapter 520-2 STANDARDS FOR REAL ESTATE COURSES.txt"),
        Create("Georgia", CourseComplianceTypes.Postlicensing, null,
            CourseExamPolicyStatus.Verified,
            "A proctor is required for the final examination.",
            null, null, null, null, null, null,
            CourseExamPolicyEvidenceKind.CommissionRule,
            "GREC Rule 520-2-.04(11)(g)",
            "GEORGIA/LICENSING/TXTs/Chapter 520-2 STANDARDS FOR REAL ESTATE COURSES.txt"),
        Create("Georgia", CourseComplianceTypes.ContinuingEducation, null,
            CourseExamPolicyStatus.Conditional,
            "A proctor is required when a CE final examination is required to receive course credit.",
            null, null, null, null, null, null,
            CourseExamPolicyEvidenceKind.CommissionRule,
            "GREC Rule 520-2-.04(11)(g)",
            "GEORGIA/LICENSING/TXTs/Chapter 520-2 STANDARDS FOR REAL ESTATE COURSES.txt"),
        Create("South Carolina", CourseComplianceTypes.Prelicensing, null,
            CourseExamPolicyStatus.Verified,
            "A proctored final is required with a 70% minimum passing score.",
            70, null, null, true, null, new DateOnly(2027, 6, 30),
            CourseExamPolicyEvidenceKind.CommissionNotice,
            "S.C. Code Regs. 105-6(C)(5); distance prelicensing virtual-proctor waiver through 2027-06-30",
            "SOUTH CAROLINA/LICENSING/TXTs/Chapter 105.txt; SOUTH CAROLINA/LICENSING/TXTs/E-BLAST SCREC EDUCATION DELIVERY.txt"),
        Create("Texas", CourseComplianceTypes.Prelicensing, CourseDeliveryMethods.DistanceEducation,
            CourseExamPolicyStatus.Verified,
            "The provider checklist requires a proctored, closed-book/closed-note final and a procedure to verify the registered student.",
            null, true, true, null, null, null,
            CourseExamPolicyEvidenceKind.CommissionProviderChecklist,
            "TREC Qualifying Education Provider checklist (2023)",
            "TEXAS/LICENSING/TXTs/QE Provider - QE ERW DE Course Application Checklist 04.10.23.txt",
            "Provider checklist source; confirm the current checklist and course classification before enforcement."),
        Create("California", CourseComplianceTypes.ContinuingEducation, CourseDeliveryMethods.DistanceEducation,
            CourseExamPolicyStatus.Verified,
            "The distance-course handbook requires exam-integrity controls and proof of student identity; a designated, impartial proctor may administer the final and verify photo ID.",
            null, null, true, null, null, null,
            CourseExamPolicyEvidenceKind.CommissionHandbook,
            "California DRE Course Provider Handbook, correspondence/internet CE requirements",
            "CALIFORNIA/LICENSING/TXTs/Course Provider Handbook.txt",
            "Handbook describes the provider process; verify the applicable regulation and course approval conditions."),
        Create("California", CourseComplianceTypes.Prelicensing, CourseDeliveryMethods.DistanceEducation,
            CourseExamPolicyStatus.Verified,
            "The correspondence-course handbook requires a supervised final, designated impartial proctor instructions, and photo-ID verification before the exam.",
            null, null, true, null, null, null,
            CourseExamPolicyEvidenceKind.CommissionHandbook,
            "California DRE Course Provider Handbook, correspondence/internet pre-license requirements",
            "CALIFORNIA/LICENSING/TXTs/Course Provider Handbook.txt",
            "Handbook describes the provider process; verify the applicable regulation and course approval conditions."),
        Create("New York", CourseComplianceTypes.Prelicensing, CourseDeliveryMethods.DistanceEducation,
            CourseExamPolicyStatus.Verified,
            "Distance-learning qualifying-course finals must be proctored at a Department-approved location in New York State.",
            null, null, null, false, null, null,
            CourseExamPolicyEvidenceKind.CommissionRule,
            "19 NYCRR Part 176.23(g)",
            "NEW YORK/LICENSING/TXTs/re-law.txt"),
        Create("Illinois", CourseComplianceTypes.Prelicensing, CourseDeliveryMethods.DistanceEducation,
            CourseExamPolicyStatus.NeedsReview,
            "Verify whether this course is online distance education or home study/correspondence; Illinois applies different exam rules to those delivery types.",
            null, null, null, null, null, null,
            CourseExamPolicyEvidenceKind.CommissionRule,
            "68 Ill. Adm. Code 1450.1105(h), 1450.1155, 1450.1180",
            "ILLINOIS/LICENSING/TXTs/TITLE 68 PROFESSIONS AND OCCUPATIONS PORTAL.txt",
            "The LMS delivery classification does not yet distinguish online distance education from home study/correspondence."),
        Create("Illinois", CourseComplianceTypes.ContinuingEducation, CourseDeliveryMethods.DistanceEducation,
            CourseExamPolicyStatus.NeedsReview,
            "Verify whether this course is online distance education or home study/correspondence; Illinois applies different CE exam rules to those delivery types.",
            null, null, null, null, null, null,
            CourseExamPolicyEvidenceKind.CommissionRule,
            "68 Ill. Adm. Code 1450.1135(f), 1450.1180",
            "ILLINOIS/LICENSING/TXTs/TITLE 68 PROFESSIONS AND OCCUPATIONS PORTAL.txt",
            "The LMS delivery classification does not yet distinguish online distance education from home study/correspondence.")
    ];

    public static CourseExamProctoringPolicyResolution Resolve(Course course)
    {
        var jurisdiction = NormalizeState(course.State);
        var courseCategory = course.ComplianceType?.Trim() ?? string.Empty;
        var deliveryMethod = course.DeliveryMethod?.Trim() ?? string.Empty;
        var rule = Rules
            .Where(candidate => string.Equals(candidate.State, jurisdiction, StringComparison.OrdinalIgnoreCase)
                && string.Equals(candidate.CourseCategory, courseCategory, StringComparison.OrdinalIgnoreCase)
                && (candidate.DeliveryMethod is null
                    || string.Equals(candidate.DeliveryMethod, deliveryMethod, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(candidate => candidate.DeliveryMethod is not null)
            .FirstOrDefault();

        if (rule is not null)
        {
            return new CourseExamProctoringPolicyResolution(
                jurisdiction,
                courseCategory,
                deliveryMethod,
                rule.Status,
                rule.Requirement,
                rule.MinimumPassingPercent,
                rule.ClosedBookRequired,
                rule.IdentityVerificationRequired,
                rule.RemoteProctoringAllowed,
                rule.RemoteProctoringRequiresCommissionApproval,
                rule.EffectiveThrough,
                rule.EvidenceKind,
                rule.Citation,
                rule.SourceDocument,
                rule.ReviewNote);
        }

        return new CourseExamProctoringPolicyResolution(
            jurisdiction,
            courseCategory,
            deliveryMethod,
            CourseExamPolicyStatus.NeedsReview,
            "No verified course-exam proctoring rule is cataloged for this state, course category, and delivery mode.",
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            "Verify the current Commission rule and course approval conditions before setting or enforcing proctoring requirements.");
    }

    private static Rule Create(
        string state,
        string courseCategory,
        string? deliveryMethod,
        CourseExamPolicyStatus status,
        string requirement,
        int? minimumPassingPercent,
        bool? closedBookRequired,
        bool? identityVerificationRequired,
        bool? remoteProctoringAllowed,
        bool? remoteProctoringRequiresCommissionApproval,
        DateOnly? effectiveThrough,
        CourseExamPolicyEvidenceKind evidenceKind,
        string citation,
        string sourceDocument,
        string? reviewNote = null)
    {
        return new Rule(state, courseCategory, deliveryMethod, status, requirement, minimumPassingPercent,
            closedBookRequired, identityVerificationRequired, remoteProctoringAllowed,
            remoteProctoringRequiresCommissionApproval, effectiveThrough, evidenceKind, citation,
            sourceDocument, reviewNote);
    }

    private static string NormalizeState(string? state)
    {
        var normalized = state?.Trim() ?? string.Empty;
        return StateNames.GetValueOrDefault(normalized, normalized);
    }

    private static readonly IReadOnlyDictionary<string, string> StateNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["AL"] = "Alabama",
        ["CA"] = "California",
        ["GA"] = "Georgia",
        ["IL"] = "Illinois",
        ["NC"] = "North Carolina",
        ["NY"] = "New York",
        ["SC"] = "South Carolina",
        ["TX"] = "Texas"
    };
}