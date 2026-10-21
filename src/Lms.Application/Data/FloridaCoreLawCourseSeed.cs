using System.Text.RegularExpressions;
using Lms.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Lms.Application.Data;

public static class FloridaCoreLawCourseSeed
{
    private const string CourseTitle = "Florida Core Law";
    private const string CoursePrintPath = "FLORIDA/COURSES/CE/Florida Core Law/txts/Course Print - Florida Core Law.txt";
    private const string LegacyDemoEnrollmentCleanupAction = "course.legacy-demo-enrollments.removed";

    public static async Task EnsureAsync(ApplicationDbContext dbContext, Guid courseId, string? workspaceRoot = null)
    {
        var root = CaptureSeedRoot.Resolve(workspaceRoot);
        var sourcePath = root is null ? null : Path.Combine(root, "docs", CoursePrintPath.Replace('/', Path.DirectorySeparatorChar));
        if (sourcePath is null || !File.Exists(sourcePath))
        {
            return;
        }

        var units = ParseCoursePrint(await File.ReadAllTextAsync(sourcePath));
        if (units.Count != 5)
        {
            throw new InvalidDataException("Florida Core Law source did not contain the expected five units.");
        }

        var existingCourse = await dbContext.Courses
            .Include(course => course.Modules)
                .ThenInclude(unit => unit.Lessons)
            .FirstOrDefaultAsync(course => course.Id == courseId);
        var cleanupWasRecorded = await dbContext.AuditLogs
            .AnyAsync(log => log.TargetType == "Course"
                && log.TargetId == courseId
                && log.Action == LegacyDemoEnrollmentCleanupAction);
        if (!cleanupWasRecorded)
        {
            await RemoveLegacyCourseProgressAsync(dbContext, existingCourse, courseId);
            dbContext.AuditLogs.Add(new AuditLog
            {
                ActorEmail = "system@lms.local",
                Action = LegacyDemoEnrollmentCleanupAction,
                TargetType = "Course",
                TargetId = courseId,
                Details = "Removed demo enrollments, mock completion, and course activity before keeping Florida Core Law as the real source course."
            });
            await dbContext.SaveChangesAsync();
        }

        if (existingCourse?.Title == CourseTitle && existingCourse.Modules.Count == 5)
        {
            if (existingCourse.Price != 30m || existingCourse.Industry != CourseIndustries.RealEstate)
            {
                existingCourse.Price = 30m;
                existingCourse.Industry = CourseIndustries.RealEstate;
                await dbContext.SaveChangesAsync();
            }

            return;
        }

        var course = existingCourse ?? new Course { Id = courseId };
        if (existingCourse is null)
        {
            dbContext.Courses.Add(course);
        }

        course.Title = CourseTitle;
        course.Slug = "florida-core-law";
        course.Description = "Florida Core Law continuing education from the supplied The CE Shop course print, dated January 7, 2025. The source specifies 3 CE hours; it does not allocate minutes by unit or lesson.";
        course.Level = "Continuing Education";
        course.DurationHours = 3;
        course.CreditHours = 3;
        course.ComplianceType = CourseComplianceTypes.ContinuingEducation;
        course.ContinuingEducationType = ContinuingEducationTypes.CoreLaw;
        course.Industry = CourseIndustries.RealEstate;
        course.DeliveryMethod = CourseDeliveryMethods.DistanceEducation;
        course.CommissionCourseNumber = null;
        course.State = "Florida";
        course.RequiredInstructionalMinutes = 180;
        course.MinimumPassingPercent = 80;
        course.MinimumAttendancePercent = 80;
        course.RequiresProctoredExam = false;
        course.UpdatedAt = DateTime.UtcNow;
        course.Price = 30m;
        course.IsPublished = false;
        course.IsArchived = false;
        course.ReviewStatus = CourseReviewStatuses.Draft;
        course.ReviewNote = "Imported from user-supplied The CE Shop materials. Verify current course/provider approval, reproduction rights, and exam answer keys before publication.";

        var activeCertificates = await dbContext.CompletionCertificates
            .Where(certificate => certificate.CourseId == courseId && !certificate.IsRevoked)
            .ToListAsync();
        foreach (var certificate in activeCertificates)
        {
            certificate.IsRevoked = true;
            certificate.RevokedAt = DateTime.UtcNow;
            certificate.RevocationReason = "The mock Florida course was replaced by the Florida Core Law source course. Completion must be reassessed.";
        }

        var oldAssessments = await dbContext.CourseAssessments
            .Where(assessment => assessment.CourseId == courseId)
            .ToListAsync();
        dbContext.CourseAssessments.RemoveRange(oldAssessments);
        var oldCheckpoints = await dbContext.CourseCheckpointDefinitions
            .Where(checkpoint => checkpoint.CourseId == courseId)
            .ToListAsync();
        dbContext.CourseCheckpointDefinitions.RemoveRange(oldCheckpoints);
        dbContext.Modules.RemoveRange(course.Modules);
        await dbContext.SaveChangesAsync();

        var createdUnits = new Dictionary<string, Module>(StringComparer.OrdinalIgnoreCase);
        var createdLessons = new List<Lesson>();
        foreach (var unitDefinition in units)
        {
            var unit = new Module
            {
                CourseId = courseId,
                Title = unitDefinition.Title,
                OrderIndex = unitDefinition.OrderIndex
            };
            createdUnits.Add(unit.Title, unit);
            dbContext.Modules.Add(unit);

            foreach (var lessonDefinition in unitDefinition.Lessons)
            {
                var lesson = new Lesson
                {
                    ModuleId = unit.Id,
                    Title = lessonDefinition.Title,
                    ContentType = "Text",
                    TextContent = lessonDefinition.TextContent,
                    DurationMinutes = 0,
                    OrderIndex = lessonDefinition.OrderIndex,
                    IsRequired = true
                };
                unit.Lessons.Add(lesson);
                createdLessons.Add(lesson);
            }
        }

        await dbContext.SaveChangesAsync();
        await SaveAssessmentsAsync(dbContext, courseId, createdUnits);
    }

    private static async Task RemoveLegacyCourseProgressAsync(ApplicationDbContext dbContext, Course? course, Guid courseId)
    {
        var enrollments = await dbContext.Enrollments
            .Where(enrollment => enrollment.CourseId == courseId)
            .ToListAsync();
        dbContext.Enrollments.RemoveRange(enrollments);

        var checkpointProgress = await dbContext.ModuleCheckpointProgresses
            .Where(progress => progress.CourseId == courseId)
            .ToListAsync();
        dbContext.ModuleCheckpointProgresses.RemoveRange(checkpointProgress);

        var activitySessions = await dbContext.CourseActivitySessions
            .Where(session => session.CourseId == courseId)
            .ToListAsync();
        dbContext.CourseActivitySessions.RemoveRange(activitySessions);

        if (course is not null)
        {
            course.UpdatedAt = DateTime.UtcNow;
        }

        await dbContext.SaveChangesAsync();
    }

    public static List<UnitDefinition> ParseCoursePrint(string coursePrint)
    {
        var lines = coursePrint.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var contentStart = Array.FindIndex(lines, line => string.Equals(line.Trim(), "Unit 1: Course Introduction: Florida Core Law", StringComparison.Ordinal));
        if (contentStart < 0)
        {
            throw new InvalidDataException("Florida Core Law source unit content could not be located.");
        }

        var lessonTitlesByUnit = ParseLessonTitlesFromContents(lines, contentStart);
        var units = new List<UnitDefinition>();
        UnitDefinition? currentUnit = null;
        string? currentLessonTitle = null;
        var lessonLines = new List<string>();
        var inResources = false;

        void FinishLesson()
        {
            if (currentUnit is null || string.IsNullOrWhiteSpace(currentLessonTitle))
            {
                return;
            }

            var content = string.Join("\n", lessonLines).Trim();
            if (!string.IsNullOrWhiteSpace(content))
            {
                currentUnit.Lessons.Add(new LessonDefinition(currentLessonTitle, content, currentUnit.Lessons.Count + 1));
            }
            lessonLines.Clear();
        }

        for (var lineIndex = contentStart; lineIndex < lines.Length; lineIndex++)
        {
            var line = lines[lineIndex].TrimEnd();
            var trimmed = line.Trim();

            if (!inResources && Regex.Match(trimmed, @"^Unit (?<number>\d+): (?<title>.+)$") is { Success: true } unitMatch)
            {
                FinishLesson();
                var title = Regex.Replace(unitMatch.Groups["title"].Value.Trim(), @"\s+Unit Introduction$", string.Empty, RegexOptions.IgnoreCase);
                currentUnit = new UnitDefinition($"Unit {unitMatch.Groups["number"].Value}: {title}", int.Parse(unitMatch.Groups["number"].Value));
                units.Add(currentUnit);
                currentLessonTitle = "Unit Introduction";
                continue;
            }

            if (currentUnit is null)
            {
                continue;
            }

            if (!inResources && currentLessonTitle is null && trimmed == "Resources")
            {
                FinishLesson();
                currentLessonTitle = "Additional Resources";
                inResources = true;
                continue;
            }

            var lessonMatch = Regex.Match(trimmed, @"(?<prefix>.*?)(?:^|\s)Lesson (?<number>\d+): (?<title>.+)$");
            if (!inResources && lessonMatch.Success)
            {
                var prefix = lessonMatch.Groups["prefix"].Value.Trim();
                if (prefix.Length > 0)
                {
                    lessonLines.Add(prefix);
                }
                FinishLesson();
                var lessonNumber = int.Parse(lessonMatch.Groups["number"].Value);
                currentLessonTitle = lessonTitlesByUnit.TryGetValue((currentUnit!.OrderIndex, lessonNumber), out var sourceTitle)
                    ? sourceTitle
                    : lessonMatch.Groups["title"].Value.Trim();
                var remainingText = trimmed[(lessonMatch.Index + lessonMatch.Length)..].Trim();
                if (remainingText.StartsWith(currentLessonTitle, StringComparison.OrdinalIgnoreCase))
                {
                    remainingText = remainingText[currentLessonTitle.Length..].Trim();
                }
                if (remainingText.Length > 0)
                {
                    lessonLines.Add(remainingText);
                }
                continue;
            }

            var keyPointsMatch = Regex.Match(trimmed, @"(?<prefix>.*?)(?:^|\s)Key Points: (?<title>.+)$");
            if (!inResources && keyPointsMatch.Success)
            {
                var prefix = keyPointsMatch.Groups["prefix"].Value.Trim();
                if (prefix.Length > 0)
                {
                    lessonLines.Add(prefix);
                }
                FinishLesson();
                currentLessonTitle = $"Key Points: {keyPointsMatch.Groups["title"].Value.Trim()}";
                continue;
            }

            if (!inResources && (trimmed.Equals("Unit Exam", StringComparison.OrdinalIgnoreCase)
                || trimmed.Equals("Final Exam", StringComparison.OrdinalIgnoreCase)))
            {
                FinishLesson();
                currentLessonTitle = null;
                continue;
            }

            if (currentLessonTitle is not null)
            {
                lessonLines.Add(line);
            }
        }

        FinishLesson();
        return units;
    }

    private static Dictionary<(int Unit, int Lesson), string> ParseLessonTitlesFromContents(string[] lines, int contentStart)
    {
        var titles = new Dictionary<(int Unit, int Lesson), string>();
        var currentUnitNumber = 0;
        for (var lineIndex = 0; lineIndex < contentStart; lineIndex++)
        {
            var line = lines[lineIndex].Trim();
            var unitMatch = Regex.Match(line, @"^Unit (?<unit>\d+): .+\s+\d+$");
            if (unitMatch.Success)
            {
                currentUnitNumber = int.Parse(unitMatch.Groups["unit"].Value);
                continue;
            }

            var lessonMatch = Regex.Match(line, @"^Lesson (?<lesson>\d+): (?<title>.+?)\s+\d+$");
            if (currentUnitNumber > 0 && lessonMatch.Success)
            {
                titles[(currentUnitNumber, int.Parse(lessonMatch.Groups["lesson"].Value))] = lessonMatch.Groups["title"].Value.Trim();
            }
        }

        return titles;
    }

    private static async Task SaveAssessmentsAsync(ApplicationDbContext dbContext, Guid courseId, IReadOnlyDictionary<string, Module> units)
    {
        var definitions = GetAssessmentDefinitions();
        foreach (var definition in definitions)
        {
            var assessment = new CourseAssessment
            {
                CourseId = courseId,
                ModuleId = definition.UnitTitle is null ? null : units[definition.UnitTitle].Id,
                Title = definition.Title,
                PassPercent = 80m,
                IsRequired = true
            };
            dbContext.CourseAssessments.Add(assessment);
            foreach (var question in definition.Questions.Select((item, index) => new AssessmentQuestion
            {
                CourseAssessmentId = assessment.Id,
                Prompt = item.Prompt,
                OptionA = item.OptionA,
                OptionB = item.OptionB,
                OptionC = item.OptionC,
                OptionD = item.OptionD,
                CorrectOption = item.CorrectOption,
                OrderIndex = index + 1
            }))
            {
                dbContext.AssessmentQuestions.Add(question);
            }
        }

        await dbContext.SaveChangesAsync();
    }

    private static IReadOnlyList<AssessmentDefinition> GetAssessmentDefinitions() =>
    [
        new("Unit 2 Exam", "Unit 2: Real Estate Law Review and Update",
        [
            Q("While working with a buyer, a licensee fills in contract blanks, drafts an early occupancy addendum, gives legal assurances, and charges a fee to prepare documents. Which task is legally permitted?", "Charge a fee for preparing legal documents", "Draft the early occupancy addendum", "Fill in the contract blanks", "Interpret the contract provisions", "C"),
            Q("Which individual is exempt from the requirement for real estate licensure?", "An employee receiving commission for leasing company property", "An entrepreneur matching overseas buyers with brokers for a fee", "A person selling their own property", "A property manager leasing apartments for an absentee owner", "C"),
            Q("What is the purpose of licensing?", "Allow real estate professionals to make money", "Protect the public through education, training, and standards", "Provide income for the state", "Standardize requirements across the United States", "B"),
            Q("A broker's license is revoked. What happens to the sales associate working under that broker?", "The associate's license becomes involuntarily inactive", "The associate's license is also revoked", "Nothing changes", "The associate is automatically transferred to another broker", "A"),
            Q("For an unlicensed assistant to be compensated on a completed-transaction basis, what must be true?", "The assistant must be licensed", "The assistant must be paid in cash", "Pay must not exceed $4,000 per month", "The income must be reported on taxes", "A"),
            Q("Which task may an unlicensed assistant perform for a broker?", "Assemble closing documents", "Call prospective clients to solicit business", "Host an open house", "Interpret contract information for a client", "A"),
            Q("How does licensing protect the public?", "It establishes a nationwide standard", "It requires every licensee to hold a broker license", "It requires education, training, and professional standards", "It requires a business degree", "C"),
            Q("Which action may a licensee take without potentially practicing law?", "Draft a new offer form", "Strike legally required contract paragraphs", "Advise that attorney review is unnecessary", "Use standard approved language in contingencies and addenda", "D"),
            Q("Which circumstance specifically relates to unauthorized practice of law?", "Predicting a specific tax change", "Predicting a rental property's return", "Recommending an investment", "Writing unfamiliar contract addenda or provisions", "D"),
            Q("A person performing real estate activity under a court order, such as a bankruptcy trustee, is what?", "Not required to have a real estate license", "Required to have a broker license", "Required to have an associate broker license", "Required to have a sales associate license", "A")
        ]),
        new("Unit 3 Exam", "Unit 3: FREC Rules Review and Update",
        [
            Q("When an escrow deposit is held by a title agent or attorney, what is true of written verification and seller-notice rules?", "They apply at the broker's discretion", "They apply at the seller's discretion", "They no longer apply", "They still apply", "D"),
            Q("A neutral party hears testimony and makes a binding decision in an escrow dispute. What is this?", "Arbitration", "Interpleading the matter with the court", "Mediation", "Requesting an EDO", "A"),
            Q("What needs to be recorded in the ledger when an escrow disbursement is made?", "The check number", "The name of the disbursing broker", "The name of the reconciling broker", "The escrow account name", "A"),
            Q("How many days after escrow deposits are made must the depositing broker request written verification from the escrow holder?", "Five", "Seven", "Three", "Two", "C"),
            Q("Why may a licensee not deposit a client's earnest-money check into a personal account before transferring it to escrow?", "It commingles escrow funds with personal funds", "It fails to record the deposit", "It fails to notify the client and broker", "Funds must only be transferred at week's end", "A"),
            Q("A Florida real estate team name may not appear how in relation to the brokerage name?", "Above", "Below", "Larger than", "Smaller than", "C"),
            Q("What should be updated each time a deposit or withdrawal is made from a client's escrow account?", "Bank reconciliation", "Journal", "Ledger", "Trial balance", "C"),
            Q("By when must sales associates and broker associates deliver escrow funds to their broker/employer?", "Within the business day of receipt", "By the end of the next business day after receipt", "Within three business days", "Within two business weeks", "B")
        ]),
        new("Unit 4 Exam", "Unit 4: Agency Law and Fiduciary Duties",
        [
            Q("Which fiduciary duty requires quality, knowledgeable, prudent service and living up to the client's trust?", "Confidentiality", "Loyalty", "Obedience", "Reasonable skill and care", "D"),
            Q("Which action violates rules against unauthorized practice of law?", "Predicting taxes in a geographic area", "Predicting rental investment return", "Recommending an investment", "Writing unfamiliar contract addenda or provisions", "D"),
            Q("When a licensee knows a buyer or seller already has representation, what disclosure is required?", "No written relationship disclosure is needed to that represented party", "A no-brokerage relationship must always be disclosed in writing", "Broker permission must be obtained before showing", "Written permission to transition to transaction broker is required", "A"),
            Q("How many duties are owed in a Florida no-brokerage relationship?", "Nine", "None", "Seven", "Three", "D"),
            Q("A licensee discloses in writing that there is no brokerage relationship. What relationship is established?", "Contract agency", "Non-representation", "Sales agency", "Transaction brokerage", "B"),
            Q("Which act specifies the forms used to notify consumers of single agency or non-representation?", "Administrative Procedures Act", "Brokerage Relationship Disclosure Act", "Organizational Structure Act", "Standardized Brokerage Forms Act", "B"),
            Q("Which fiduciary duty is missing from loyalty, care, obedience, confidentiality, and accounting?", "Disclosure", "Disobedience", "Lawfulness", "Skill", "A"),
            Q("A listing agent shows a property to a buyer represented by another licensee. What notice is required?", "The buyer's agent must become a transaction broker", "The listing agent must give notice of non-representation", "The listing agent breaches the buyer's agency relationship", "No notice of non-representation is required", "D"),
            Q("Which action is unauthorized practice of law?", "Explaining a contingency", "Interpreting legal consequences of a client's actions", "Using attorney-reviewed forms", "Using standard addendum language", "B"),
            Q("Which fiduciary duty requires keeping client information confidential?", "Accounting", "Confidentiality", "Disclosure", "Reasonable skill and care", "B")
        ]),
        new("Unit 5 Exam", "Unit 5: Laws Affecting Real Estate",
        [
            Q("A combat-disabled veteran dies. To whom may the homestead tax discount transfer under the 2020 law, subject to conditions?", "A named beneficiary", "The veteran's estate", "The veteran's surviving children", "The veteran's surviving spouse", "D"),
            Q("A 2021 Florida law change allows condominium associations to employ or contract with which providers?", "Providers listed in a state licensing-board database", "Providers owned or operated by a board member or financially related person", "Unlicensed and uninsured providers", "Providers headquartered outside Florida", "B"),
            Q("When must a senior citizen notify the state about income for an additional homestead exemption?", "Annually, whether or not income changed", "Every three years", "Whenever outside retirement income is received", "If income changes and disqualifies them from the exemption", "D"),
            Q("How can a scrivener's error in a Florida deed be officially corrected?", "File a curative notice with the county clerk of court", "File a motion to have the property re-deeded and pay to re-record", "It is superficial and requires no action", "It cannot be corrected", "A"),
            Q("What can an owner do to remove a discriminatory provision from community-association documents?", "At least three owners may sue in county court", "One owner may request removal and the board may vote by majority", "Nothing; the county recorder removes it", "Only the board may bring a court action", "B"),
            Q("May a condo owner install an electric-vehicle charger in an exclusively designated parking area?", "No", "Only if the owner has a disability", "Yes, with a state permit application", "Yes, in a parking area exclusively designated to the owner", "D"),
            Q("Can a homeowner claim accrued homestead benefits two years after buying a replacement home?", "No, but current-year benefits may be claimed", "No, only future benefits may be claimed", "Yes, accrued benefits may be claimed for up to three years", "Yes, regardless of when the exemption is filed", "C")
        ]),
        new("Florida Core Law Final Exam", null,
        [
            Q("When parties agree to binding arbitration in an escrow dispute, what is true?", "The arbitrator advocates for one party", "The arbitrator must reach consensus", "The arbitrator's decision is final", "The decision requires all parties' approval", "C"),
            Q("Which action is legally allowed when a licensee avoids unauthorized practice of law?", "Create new real estate forms", "Interpret legal consequences", "Use existing forms reviewed by an attorney", "Write addenda for unusual circumstances", "C"),
            Q("What is the maximum fine described for a contractor violating the Florida fire-sprinkler law?", "No fine for the association; $50,000 for the contractor", "$25,000 for the association", "$25,000 each for contractor and association", "$10,000 for the contractor", "D"),
            Q("What is the maximum Florida condominium transfer fee described in the 2021 update?", "$0", "$100", "$150", "$50", "B"),
            Q("How long must a Florida real estate firm keep escrow-account records?", "Five years", "Four years", "Seven years", "Six years", "A"),
            Q("After a combat-disabled veteran dies, who may receive the homestead property-tax discount transfer?", "The discount ends", "It applies to the estate", "It transfers to surviving children", "It transfers to a surviving spouse", "D"),
            Q("A buyer cancels within the statutory review period for an existing condominium. What may the broker do with the deposit?", "Refund it without approval of another party", "Refuse to refund it and forfeit commission", "Submit an interpleader", "Request an escrow-disbursement order", "A"),
            Q("How long must a condominium association retain received bids for work or services?", "One year after receiving the bid", "One year after work is performed", "Seven years after receiving the bid", "Seven years after work is performed", "A"),
            Q("What duty is owed to an unrepresented buyer customer?", "Disclosing conflicts of interest", "Disclosing material facts", "Loyalty", "Obedience", "B"),
            Q("When must a senior citizen report income changes affecting an additional homestead exemption?", "Every year before April 15", "If income is from a foreign source", "If income decreases and qualifies them for another exemption", "If income increases and disqualifies them", "D"),
            Q("In a team advertisement, what is wrong if the team name is larger than the brokerage name?", "Nothing; the ad complies", "The ad needs a photograph", "The team name cannot be larger than the brokerage name", "The team name must always appear below the brokerage name", "C"),
            Q("How often must a Florida brokerage update its team-member list?", "Annually", "Biannually", "Monthly", "Weekly", "A"),
            Q("If a broker dies and no other broker is in the office, what happens to the associate's license?", "The commission keeps the office open", "There are six months to make the license active", "The firm remains licensed without a broker", "The license becomes involuntarily inactive", "D"),
            Q("If a telemarketer willfully violates Florida's recorded-sales-call law, consumer damages may be what?", "Doubled", "Increased to include jail time", "Quadrupled", "Tripled", "D"),
            Q("After the first year of homestead exemption, what is the maximum annual assessment increase?", "1%", "3%", "The lesser of 3% or the CPI change", "The CPI change", "C")
        ])
    ];

    private static QuestionDefinition Q(string prompt, string optionA, string optionB, string optionC, string optionD, string correctOption) =>
        new(prompt, optionA, optionB, optionC, optionD, correctOption);

    public sealed class UnitDefinition(string title, int orderIndex)
    {
        public string Title { get; } = title;
        public int OrderIndex { get; } = orderIndex;
        public List<LessonDefinition> Lessons { get; } = new();
    }

    public sealed record LessonDefinition(string Title, string TextContent, int OrderIndex);

    private sealed record AssessmentDefinition(string Title, string? UnitTitle, IReadOnlyList<QuestionDefinition> Questions);

    private sealed record QuestionDefinition(string Prompt, string OptionA, string OptionB, string OptionC, string OptionD, string CorrectOption);
}