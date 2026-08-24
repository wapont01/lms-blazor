using System.Text;
using Lms.Application.Data;
using Lms.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Lms.Application.Services;

public sealed record PolicyDisclosurePurchaseItem(Guid CourseId, decimal UnitPrice, int Quantity = 1);

public sealed record PolicyDisclosurePreview(
    Guid CourseId,
    string CourseTitle,
    string ComplianceType,
    string? CommissionCourseNumber,
    string DeliveryMethod,
    int InstructionalMinutes,
    decimal TuitionAndFees,
    decimal ProctoringFee,
    int? CompletionWindowDays,
    decimal MinimumPassingPercent,
    string RetakePolicy,
    string SchoolName,
    string SupportEmail,
    string SupportTelephone,
    string LicenseExaminationPerformanceRecord,
    string AnnualSummaryReportData,
    string DisclosureText);

public interface IPolicyDisclosureService
{
    Task<PolicyDisclosurePreview?> GetCourseDisclosureAsync(Guid courseId);
    Task<IReadOnlyList<PolicyDisclosurePreview>> GetCheckoutDisclosuresAsync(Guid learnerId, IReadOnlyCollection<PolicyDisclosurePurchaseItem> items);
    Task<IReadOnlyList<Guid>> AcknowledgeAsync(Guid learnerId, IReadOnlyCollection<PolicyDisclosurePurchaseItem> items);
    Task FinalizePurchaseAsync(Guid learnerId, Guid paymentTransactionId, IReadOnlyCollection<Guid> acknowledgmentIds, IReadOnlyCollection<PolicyDisclosurePurchaseItem> items, decimal taxAmount, decimal discountAmount);
}

public sealed class PolicyDisclosureService : IPolicyDisclosureService
{
    public const string DisclosureVersion = "2026.08.23.1";

    private static readonly DateTime DisclosurePublishedAtUtc = new(2026, 8, 23, 0, 0, 0, DateTimeKind.Utc);
    private readonly ApplicationDbContext _dbContext;
    private readonly ISchoolProfileService _schoolProfileService;

    public PolicyDisclosureService(ApplicationDbContext dbContext, ISchoolProfileService schoolProfileService)
    {
        _dbContext = dbContext;
        _schoolProfileService = schoolProfileService;
    }

    public async Task<PolicyDisclosurePreview?> GetCourseDisclosureAsync(Guid courseId)
    {
        var course = await _dbContext.Courses.AsNoTracking().SingleOrDefaultAsync(existing => existing.Id == courseId);
        if (course is null || !CourseComplianceTypes.IsRegulated(course.ComplianceType))
        {
            return null;
        }

        var schoolProfile = await _schoolProfileService.GetAsync();
        return BuildPreview(course, course.Price, schoolProfile);
    }

    public async Task<IReadOnlyList<PolicyDisclosurePreview>> GetCheckoutDisclosuresAsync(Guid learnerId, IReadOnlyCollection<PolicyDisclosurePurchaseItem> items)
    {
        if (items.Count == 0)
        {
            return [];
        }

        var learnerExists = await _dbContext.UserAccounts.AsNoTracking().AnyAsync(user => user.Id == learnerId);
        if (!learnerExists)
        {
            throw new InvalidOperationException("Your session is out of date. Please sign in again.");
        }

        var courseIds = items.Select(item => item.CourseId).Distinct().ToList();
        var courses = await _dbContext.Courses
            .AsNoTracking()
            .Where(course => courseIds.Contains(course.Id))
            .ToDictionaryAsync(course => course.Id);
        var schoolProfile = await _schoolProfileService.GetAsync();

        var previews = new List<PolicyDisclosurePreview>();
        foreach (var item in items)
        {
            if (!courses.TryGetValue(item.CourseId, out var course))
            {
                throw new InvalidOperationException("A course in your cart is no longer available.");
            }

            if (!CourseComplianceTypes.IsRegulated(course.ComplianceType))
            {
                continue;
            }

            var quantity = Math.Max(item.Quantity, 1);
            var disclosedTuition = decimal.Round(item.UnitPrice * quantity, 2, MidpointRounding.AwayFromZero);
            previews.Add(BuildPreview(course, disclosedTuition, schoolProfile));
        }

        return previews;
    }

    private static PolicyDisclosurePreview BuildPreview(Course course, decimal disclosedTuition, SchoolProfile schoolProfile)
    {
        var retakePolicy = course.ComplianceType switch
        {
            CourseComplianceTypes.Prelicensing => "One retake is permitted after an unsuccessful first end-of-course examination attempt within the course completion period.",
            CourseComplianceTypes.Postlicensing => "Unsuccessful end-of-course examinations may be retaken without a provider-imposed numerical limit within the course completion period.",
            _ => "Not applicable. Continuing Education Elective completion does not use a provider end-of-course examination retake policy."
        };
        var disclosureText = BuildDisclosureText(course, disclosedTuition, retakePolicy, schoolProfile);

        return new PolicyDisclosurePreview(
            course.Id,
            course.Title,
            course.ComplianceType,
            course.CommissionCourseNumber,
            course.DeliveryMethod,
            course.RequiredInstructionalMinutes,
            disclosedTuition,
            0m,
            course.CompletionWindowDays,
            course.MinimumPassingPercent,
            retakePolicy,
            schoolProfile.AdvertisedName,
            schoolProfile.SupportEmail,
            schoolProfile.SupportTelephone,
            schoolProfile.LicenseExaminationPerformanceRecord,
            schoolProfile.AnnualSummaryReportData,
            disclosureText);
    }

    public async Task<IReadOnlyList<Guid>> AcknowledgeAsync(Guid learnerId, IReadOnlyCollection<PolicyDisclosurePurchaseItem> items)
    {
        var learner = await _dbContext.UserAccounts.AsNoTracking().SingleOrDefaultAsync(user => user.Id == learnerId)
            ?? throw new InvalidOperationException("Your session is out of date. Please sign in again.");
        var legalName = string.IsNullOrWhiteSpace(learner.LegalName) ? learner.DisplayName : learner.LegalName;
        var signature = $"{legalName} | Authenticated checkbox acknowledgment";

        var previews = await GetCheckoutDisclosuresAsync(learnerId, items);
        var acknowledgedAtUtc = DateTime.UtcNow;
        var acknowledgments = previews.Select(preview => new PolicyDisclosureAcknowledgment
        {
            LearnerId = learnerId,
            CourseId = preview.CourseId,
            DisclosureVersion = DisclosureVersion,
            DisclosurePublishedAtUtc = DisclosurePublishedAtUtc,
            AcknowledgedAtUtc = acknowledgedAtUtc,
            StudentLegalName = legalName,
            StudentEmail = learner.Email,
            ElectronicSignature = signature,
            CourseTitle = preview.CourseTitle,
            CommissionCourseNumber = preview.CommissionCourseNumber,
            DeliveryMethod = preview.DeliveryMethod,
            InstructionalMinutes = preview.InstructionalMinutes,
            TuitionAndFees = preview.TuitionAndFees,
            ProctoringFee = preview.ProctoringFee,
            SupportEmail = preview.SupportEmail,
            SupportTelephone = preview.SupportTelephone,
            LicenseExaminationPerformanceRecord = preview.LicenseExaminationPerformanceRecord,
            AnnualSummaryReportData = preview.AnnualSummaryReportData,
            DisclosureTextSnapshot = preview.DisclosureText
        }).ToList();

        _dbContext.PolicyDisclosureAcknowledgments.AddRange(acknowledgments);
        await _dbContext.SaveChangesAsync();
        return acknowledgments.Select(acknowledgment => acknowledgment.Id).ToList();
    }

    public async Task FinalizePurchaseAsync(Guid learnerId, Guid paymentTransactionId, IReadOnlyCollection<Guid> acknowledgmentIds, IReadOnlyCollection<PolicyDisclosurePurchaseItem> items, decimal taxAmount, decimal discountAmount)
    {
        var transaction = await _dbContext.PaymentTransactions
            .SingleOrDefaultAsync(existing => existing.Id == paymentTransactionId && existing.LearnerId == learnerId)
            ?? throw new InvalidOperationException("The completed payment transaction could not be found.");
        var courseIds = items.Select(item => item.CourseId).Distinct().ToList();

        var hasPurchaseLines = await _dbContext.PurchaseLines.AnyAsync(line => line.PaymentTransactionId == transaction.Id);
        if (!hasPurchaseLines)
        {
            var courseTitles = await _dbContext.Courses
                .AsNoTracking()
                .Where(course => courseIds.Contains(course.Id))
                .ToDictionaryAsync(course => course.Id, course => course.Title);
            var subtotal = items.Sum(item => item.UnitPrice * Math.Max(item.Quantity, 1));

            foreach (var item in items)
            {
                var quantity = Math.Max(item.Quantity, 1);
                var lineSubtotal = decimal.Round(item.UnitPrice * quantity, 2, MidpointRounding.AwayFromZero);
                var ratio = subtotal == 0 ? 1m / items.Count : lineSubtotal / subtotal;
                var lineTax = decimal.Round(taxAmount * ratio, 2, MidpointRounding.AwayFromZero);
                var lineDiscount = decimal.Round(discountAmount * ratio, 2, MidpointRounding.AwayFromZero);
                _dbContext.PurchaseLines.Add(new PurchaseLine
                {
                    PaymentTransactionId = transaction.Id,
                    CourseId = item.CourseId,
                    CourseTitle = courseTitles[item.CourseId],
                    UnitPrice = item.UnitPrice,
                    Quantity = quantity,
                    LineSubtotal = lineSubtotal,
                    TaxAmount = lineTax,
                    DiscountAmount = lineDiscount,
                    LineTotal = lineSubtotal + lineTax - lineDiscount
                });
            }
        }

        if (acknowledgmentIds.Count > 0)
        {
            var acknowledgments = await _dbContext.PolicyDisclosureAcknowledgments
                .Where(acknowledgment => acknowledgmentIds.Contains(acknowledgment.Id) && acknowledgment.LearnerId == learnerId)
                .ToListAsync();
            if (acknowledgments.Count != acknowledgmentIds.Count)
            {
                throw new InvalidOperationException("A required disclosure acknowledgment could not be verified.");
            }

            var enrollments = await _dbContext.Enrollments
                .Where(enrollment => enrollment.UserAccountId == learnerId && courseIds.Contains(enrollment.CourseId))
                .ToDictionaryAsync(enrollment => enrollment.CourseId);
            foreach (var acknowledgment in acknowledgments)
            {
                acknowledgment.PaymentTransactionId = transaction.Id;
                acknowledgment.EnrollmentId = enrollments.GetValueOrDefault(acknowledgment.CourseId)?.Id;
            }
        }

        await _dbContext.SaveChangesAsync();
    }

    private static string BuildDisclosureText(Course course, decimal tuitionAndFees, string retakePolicy, SchoolProfile schoolProfile)
    {
        var text = new StringBuilder();
        var isPrelicensing = string.Equals(course.ComplianceType, CourseComplianceTypes.Prelicensing, StringComparison.OrdinalIgnoreCase);
        var isPostlicensing = string.Equals(course.ComplianceType, CourseComplianceTypes.Postlicensing, StringComparison.OrdinalIgnoreCase);
        var isContinuingEducation = string.Equals(course.ComplianceType, CourseComplianceTypes.ContinuingEducation, StringComparison.OrdinalIgnoreCase);
        var completionWindow = course.CompletionWindowDays.HasValue
            ? $"{course.CompletionWindowDays} calendar days"
            : "the period stated in the course schedule";
        var providerNumber = string.IsNullOrWhiteSpace(schoolProfile.ProviderLicenseNumber)
            ? "Not yet assigned"
            : schoolProfile.ProviderLicenseNumber;
        var instructorNumber = string.IsNullOrWhiteSpace(schoolProfile.InstructorLicenseNumber)
            ? "Not yet assigned"
            : schoolProfile.InstructorLicenseNumber;
        var instructors = schoolProfile.StaffMembers
            .Where(member => string.Equals(member.Role, SchoolStaffRoles.Instructor, StringComparison.OrdinalIgnoreCase))
            .OrderBy(member => member.Name)
            .Select(member => $"{member.Name}, Instructor")
            .ToList();
        if (instructors.Count == 0)
        {
            instructors.Add($"{schoolProfile.PrimaryInstructorName}, Instructor");
        }

        text.AppendLine(schoolProfile.AdvertisedName);
        text.AppendLine($"{schoolProfile.StreetAddress}, {schoolProfile.City}, {schoolProfile.State} {schoolProfile.PostalCode}");
        text.AppendLine($"{schoolProfile.SupportTelephone} | {schoolProfile.SupportEmail} | {schoolProfile.WebsiteUrl ?? "Website not listed"}");
        text.AppendLine("POLICIES & PROCEDURES DISCLOSURE");
        text.AppendLine($"Date of publication: {DisclosurePublishedAtUtc:MMMM d, yyyy}");
        text.AppendLine($"Disclosure version: {DisclosureVersion}");
        text.AppendLine($"Legal name of Education Provider: {schoolProfile.LegalName}");
        text.AppendLine($"Education Provider number: {providerNumber}");
        text.AppendLine($"Education Director: {schoolProfile.EducationDirectorName}");
        text.AppendLine($"Faculty and full-time officials: {string.Join("; ", instructors)}; {schoolProfile.CorporateOfficerName}, Corporate Officer");
        text.AppendLine();
        text.AppendLine("EDUCATION PROVIDER CERTIFICATION");
        text.AppendLine("This Education Provider is certified by the North Carolina Real Estate Commission. The Commission's address is 1313 Navaho Drive, Raleigh, NC 27609. Complaints concerning the Education Provider or its affiliated instructors may be directed in writing to the Commission. The Complaint Form is available from the Commission's homepage at ncrec.gov.");
        text.AppendLine("Per Commission Rule 58H .0204, each prospective student must receive this Policies & Procedures Disclosure before paying any non-refundable tuition or fee. The Provider retains the student's signed or electronic certification of receipt.");
        text.AppendLine();
        text.AppendLine("NO STUDENT SHALL BE DENIED ADMISSION ON THE BASIS OF AGE, SEX, RACE, COLOR, NATIONAL ORIGIN, FAMILIAL STATUS, HANDICAPPING CONDITION, OR RELIGION.");
        text.AppendLine();
        text.AppendLine("COURSE OFFERING");
        text.AppendLine($"Course: {course.Title}");
        text.AppendLine($"Program: {CourseComplianceTypes.ToDisplayText(course.ComplianceType)}{(isContinuingEducation ? $" - {ContinuingEducationTypes.ToDisplayText(course.ContinuingEducationType ?? string.Empty)}" : string.Empty)}");
        text.AppendLine($"Commission course number: {course.CommissionCourseNumber ?? "Not yet assigned"}");
        text.AppendLine($"Delivery method: {CourseDeliveryMethods.ToDisplayText(course.DeliveryMethod)}");
        text.AppendLine($"Instructional hours: {course.RequiredInstructionalMinutes / 60m:0.##}");
        text.AppendLine($"All-inclusive tuition and fees for this purchase: ${tuitionAndFees:0.00}");
        text.AppendLine("Student proctoring fee: $0.00 (included, when proctoring applies)");
        text.AppendLine();
        text.AppendLine("COURSE MATERIALS");
        text.AppendLine("Required lessons, assignments, assessments, and provider-supplied references are delivered through the learning management system. Any additional mandatory material must be identified in the course catalog before enrollment.");
        text.AppendLine();
        text.AppendLine("ATTENDANCE AND COURSE COMPLETION");
        if (isContinuingEducation)
        {
            text.AppendLine("To receive Continuing Education credit, a broker must meet the attendance and participation requirements of Commission Rule 58A .1705, provide the required legal name and broker license number, present identification when required, and personally perform all work required to complete the course.");
            text.AppendLine($"A distance Continuing Education course must be completed within {completionWindow}.");
        }
        else
        {
            text.AppendLine($"The student must complete all required course work and assessments, satisfy the applicable attendance requirement, and pass the closed-book end-of-course examination with a minimum score of {course.MinimumPassingPercent:0.##}% within {completionWindow}.");
            text.AppendLine("An end-of-course examination will not be administered until the student satisfies the applicable attendance and course-work requirements. Identity verification and the configured proctoring controls are required. Unauthorized materials and electronic assistance are prohibited.");
            text.AppendLine($"Failed examination policy: {retakePolicy}");
            text.AppendLine($"This is {schoolProfile.AdvertisedName}'s provider policy and is not represented as a Commission-mandated retake count.");
        }
        text.AppendLine();
        text.AppendLine("REGISTRATION, TECHNOLOGY, AND SUPPORT");
        text.AppendLine("Enrollment is personal to the registered student. The student must supply accurate identifying and reporting information and must personally complete all required work. Online students need a supported desktop or laptop computer, current web browser, reliable internet access, email, and any webcam or microphone required for identity verification or proctoring.");
        text.AppendLine($"Technical and administrative support: {schoolProfile.SupportEmail}; {schoolProfile.SupportTelephone}; {schoolProfile.SupportHours ?? "support hours available upon request"}.");
        text.AppendLine();
        text.AppendLine("STUDENT CONDUCT, CANCELLATION, AND REFUNDS");
        text.AppendLine("Cheating results in dismissal, a failing course grade, loss of eligibility under makeup or retake policies, and reporting to the North Carolina Real Estate Commission as required by Commission Rule 58H .0203(h). Cancellation, withdrawal, transfer, and refund terms presented during checkout form part of this disclosure and apply to this purchase.");
        text.AppendLine();
        text.AppendLine("PERFORMANCE INFORMATION");
        text.AppendLine(schoolProfile.LicenseExaminationPerformanceRecord);
        text.AppendLine(schoolProfile.AnnualSummaryReportData);
        text.AppendLine("Commission-published pass-rate information: https://www.ncrec.gov/PrelicensingEducation/ExamPassRates");
        text.AppendLine();
        text.AppendLine("CERTIFICATION OF TRUTH AND ACCURACY");
        text.AppendLine($"{schoolProfile.EducationDirectorName}, Education Director, certifies that this disclosure is true and correct and that the Education Provider will abide by the policies herein.");
        text.AppendLine();
        text.AppendLine("CERTIFICATION OF RECEIPT");
        text.AppendLine($"I certify that I received a copy of {schoolProfile.AdvertisedName}'s Policies & Procedures Disclosure before payment of any non-refundable course registration fee or tuition. My authenticated checkbox acknowledgment confirms receipt and does not waive rights provided by law.");
        return text.ToString();
    }
}