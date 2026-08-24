using Lms.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Lms.Application.Data;

public static class CourseSeed
{
    private const string LegacyStateRegulatedComplianceType = "StateRegulated";
    private const string LegacyGeneralComplianceType = "General";
    private const string GettingStartedModuleTitle = "Getting Started";
    private const string WelcomeLessonTitle = "Welcome";
    private const string WelcomeLessonText = """
        Welcome!

        This course is organized into modules that contain lessons.

        Use the course menu to your right to jump directly into modules and lessons,

        or use the arrow buttons for step-by-step progression.
        """;
    private const string PrelicensingOrientationText = """
        NC Broker Pre-licensing Distance Education Orientation

        Course syllabus and schedule
        This 75-hour course follows the North Carolina Real Estate Commission Broker Prelicensing Course Syllabus. Course work is organized into regulatory Units of no more than 60 minutes. Each Unit contains required lessons and ends with a mandatory assessment. You must pass the Unit assessment before beginning the next Unit. The course concludes with a proctored end-of-course examination.

        Required materials and resources
        You need a reliable internet connection, a device capable of using this LMS, the course lessons and referenced forms, the current Commission syllabus, and the Williams Land Realty LLC Policies and Procedures Disclosure. Keep your login credentials private and retain access to the email address associated with your account.

        Completion period
        You must complete the course, all Unit assessments, and the end-of-course examination within 180 days after enrollment. The LMS records lesson activity, Unit-assessment results, instructional time, and completion evidence.

        Course navigation
        Use Course Menu to open Compliance Resources at any time. That page provides the syllabus and schedule, required materials, Policies and Procedures Disclosure, instructor contacts, and technical-support contacts. After beginning the timed course, open Units and lessons from Course Menu or use Previous and Next. A locked Unit cannot be entered until the preceding Unit assessment is passed.

        Before continuing, review Compliance Resources and confirm that you can access the required materials and support contacts.
        """;
    private static readonly string[] DeprecatedNavigationTitles = ["Navigation", "How to navigate this course"];
    private static readonly Guid RealEstateLicensingBasicsCourseId = Guid.Parse("64cc1cbb-45fc-4a76-b3b4-de399cb3830f");
    private static readonly Guid PropertyMarketingCourseId = Guid.Parse("c2a11379-8fb4-479a-9209-45441a56d812");
    private static readonly Guid TransactionCoordinationCourseId = Guid.Parse("f5e5df5f-5b7a-4d8c-aafd-0cd1745311e4");
    private static readonly Guid ClientCommunicationCourseId = Guid.Parse("9f4f6d8c-2f7a-4b6c-9a53-5b7a2d4e1c90");

    public static async Task SeedAsync(ApplicationDbContext dbContext)
    {
        await NormalizeLegacyComplianceTypesAsync(dbContext);

        if (!await dbContext.Courses.AnyAsync())
        {
            dbContext.Courses.AddRange(
                new Course
                {
                    Id = Guid.Parse("64cc1cbb-45fc-4a76-b3b4-de399cb3830f"),
                    Title = "NC Real Estate 75-Hour Pre-Licensing Broker",
                    Slug = "nc-real-estate-75-hour-pre-licensing-broker",
                    Description = "A state-regulated, distance-learning pre-licensing course covering core NC licensing requirements, agency, contracts, and exam readiness.",
                    Level = "Beginner",
                    DurationHours = 75,
                    CreditHours = 75,
                    Jurisdiction = "NC",
                    ComplianceType = CourseComplianceTypes.Prelicensing,
                    DeliveryMethod = CourseDeliveryMethods.DistanceEducation,
                    CommissionCourseNumber = "NC-PRE-75",
                    CompletionWindowDays = 180,
                    RequiresProctoredExam = true,
                    RequiredInstructionalMinutes = 4500,
                    MinimumPassingPercent = 75,
                    MinimumAttendancePercent = 80,
                    Price = 99m,
                    IsPublished = true,
                    CreatedAt = DateTime.UtcNow
                },
                new Course
                {
                    Id = Guid.Parse("5bc4b74b-71ac-40fe-8bc9-1daf5a922782"),
                    Title = "Advanced Brokerage Compliance",
                    Slug = "advanced-brokerage-compliance",
                    Description = "An intermediate course covering supervision, disclosures, and brokerage best practices.",
                    Level = "Intermediate",
                    DurationHours = 18,
                    CreditHours = 4,
                    Jurisdiction = "General",
                    ComplianceType = CourseComplianceTypes.ContinuingEducation,
                    ContinuingEducationType = ContinuingEducationTypes.Elective,
                    Price = 119m,
                    IsPublished = true,
                    CreatedAt = DateTime.UtcNow
                },
                new Course
                {
                    Id = Guid.Parse("b9c7d0ef-3b89-4c1d-9e5e-6de7ce5585a1"),
                    Title = "Draft Course Outline",
                    Slug = "draft-course-outline",
                    Description = "A draft course kept private until it is ready for publication.",
                    Level = "Advanced",
                    DurationHours = 8,
                    CreditHours = 2,
                    Jurisdiction = "General",
                    ComplianceType = CourseComplianceTypes.ContinuingEducation,
                    ContinuingEducationType = ContinuingEducationTypes.Elective,
                    Price = 0m,
                    IsPublished = false,
                    CreatedAt = DateTime.UtcNow
                });

            await dbContext.SaveChangesAsync();
        }

        var requiredModuleIds = new[]
        {
            Guid.Parse("d4b7f3d1-4d60-4c7f-9190-4d41d1b8f0d1"),
            Guid.Parse("c3c0f6aa-5c55-40a1-bf33-0edb77f7a901")
        };

        var hasLegacyComplianceCourse = await dbContext.Courses.AnyAsync(course => course.Id == Guid.Parse("5bc4b74b-71ac-40fe-8bc9-1daf5a922782"));

        var missingModules = new List<Guid>();
        foreach (var moduleId in requiredModuleIds)
        {
            if (!await dbContext.Modules.AnyAsync(module => module.Id == moduleId))
            {
                missingModules.Add(moduleId);
            }
        }

        if (missingModules.Count > 0)
        {
            foreach (var moduleId in missingModules)
            {
                if (moduleId == Guid.Parse("d4b7f3d1-4d60-4c7f-9190-4d41d1b8f0d1"))
                {
                    dbContext.Modules.Add(new Module
                    {
                        Id = moduleId,
                        CourseId = Guid.Parse("64cc1cbb-45fc-4a76-b3b4-de399cb3830f"),
                        Title = "Getting Started",
                        OrderIndex = 1
                    });
                }
                else if (hasLegacyComplianceCourse && moduleId == Guid.Parse("c3c0f6aa-5c55-40a1-bf33-0edb77f7a901"))
                {
                    dbContext.Modules.Add(new Module
                    {
                        Id = moduleId,
                        CourseId = Guid.Parse("5bc4b74b-71ac-40fe-8bc9-1daf5a922782"),
                        Title = "Compliance Foundations",
                        OrderIndex = 1
                    });
                }
            }

            await dbContext.SaveChangesAsync();
        }

        var requiredLessonIds = new[]
        {
            Guid.Parse("a8b6e232-11ad-4c08-8d2d-8a6f9f00a101"),
            Guid.Parse("b9e4f47d-3a20-4a32-9be7-0cd54f0d9101"),
            Guid.Parse("d9a1f5b2-7fd4-4ce4-b973-9e5e1f7b2201"),
            Guid.Parse("f1c48cb6-3f25-4c5d-96d3-f0b936de4202"),
            Guid.Parse("98e37e1e-9ca5-4e4a-944b-4f3a454e5503")
        };

        var missingLessons = new List<Guid>();
        foreach (var lessonId in requiredLessonIds)
        {
            if (!await dbContext.Lessons.AnyAsync(lesson => lesson.Id == lessonId))
            {
                missingLessons.Add(lessonId);
            }
        }

        if (missingLessons.Count > 0)
        {
            var lessonEntries = new List<Lesson>();

            if (missingLessons.Contains(Guid.Parse("a8b6e232-11ad-4c08-8d2d-8a6f9f00a101")))
            {
                lessonEntries.Add(new Lesson
                {
                    Id = Guid.Parse("a8b6e232-11ad-4c08-8d2d-8a6f9f00a101"),
                    ModuleId = Guid.Parse("d4b7f3d1-4d60-4c7f-9190-4d41d1b8f0d1"),
                    Title = "Welcome",
                    ContentType = "Text",
                    TextContent = WelcomeLessonText,
                    DurationMinutes = 12,
                    OrderIndex = 1,
                    IsRequired = true
                });
            }

            if (hasLegacyComplianceCourse && missingLessons.Contains(Guid.Parse("b9e4f47d-3a20-4a32-9be7-0cd54f0d9101")))
            {
                lessonEntries.Add(new Lesson
                {
                    Id = Guid.Parse("b9e4f47d-3a20-4a32-9be7-0cd54f0d9101"),
                    ModuleId = Guid.Parse("c3c0f6aa-5c55-40a1-bf33-0edb77f7a901"),
                    Title = "Broker supervision rules",
                    ContentType = "Text",
                    TextContent = """
                        Objectives
                        - Explain the broker's duty to supervise transaction activity, advertising, and trust handling.
                        - Identify which records should be reviewed before a file is considered compliant.

                        --- page ---

                        Scenario
                        A new agent publishes a listing ad with incomplete brokerage attribution and later updates the text after receiving feedback. The file contains multiple versions, but none are tied to an approval timestamp. During a routine review, the broker must decide whether the corrective update is enough or whether additional remediation is required.

                        Supervisory controls should include pre-publication checks, documented approvals, and periodic file audits based on risk. Review should not be limited to final documents. Drafts, communication logs, and trust-related entries can reveal patterns that indicate training gaps or policy drift.

                        --- page ---

                        Key Takeaways
                        - Supervision is an ongoing control process, not a one-time sign-off.
                        - The most defensible files show who reviewed what, when, and why.
                        - Escalate repeated process failures into coaching plans with deadlines.
                        """,
                    DurationMinutes = 24,
                    OrderIndex = 1,
                    IsRequired = true
                });
            }

            if (hasLegacyComplianceCourse && missingLessons.Contains(Guid.Parse("d9a1f5b2-7fd4-4ce4-b973-9e5e1f7b2201")))
            {
                lessonEntries.Add(new Lesson
                {
                    Id = Guid.Parse("d9a1f5b2-7fd4-4ce4-b973-9e5e1f7b2201"),
                    ModuleId = Guid.Parse("c3c0f6aa-5c55-40a1-bf33-0edb77f7a901"),
                    Title = "Disclosure timing and material facts",
                    ContentType = "Text",
                    TextContent = """
                        Objectives
                        - Distinguish between complete disclosure and timely disclosure.
                        - Apply a repeatable approach for documenting consumer acknowledgement.

                        --- page ---

                        Scenario
                        A property file includes known water intrusion that was verbally discussed during a showing, but written disclosure was shared only after the offer was submitted. Even if the buyer proceeds, delayed documentation increases the risk of complaint because decision quality may have been affected.

                        Material facts should be disclosed as soon as they are verified and before key decision points such as offer drafting, contingency waiver, or contract execution. Teams should use a disclosure log that records fact source, date confirmed, date delivered, and acknowledgment status.

                        --- page ---

                        Key Takeaways
                        - Timing errors can create liability even when disclosures are eventually complete.
                        - Written records should match what was communicated verbally.
                        - Use milestone-based checks to prevent late delivery.
                        """,
                    DurationMinutes = 22,
                    OrderIndex = 3,
                    IsRequired = true
                });
            }

            if (hasLegacyComplianceCourse && missingLessons.Contains(Guid.Parse("f1c48cb6-3f25-4c5d-96d3-f0b936de4202")))
            {
                lessonEntries.Add(new Lesson
                {
                    Id = Guid.Parse("f1c48cb6-3f25-4c5d-96d3-f0b936de4202"),
                    ModuleId = Guid.Parse("c3c0f6aa-5c55-40a1-bf33-0edb77f7a901"),
                    Title = "Advertising and record retention controls",
                    ContentType = "Text",
                    TextContent = """
                        Objectives
                        - Build a compliant workflow for ad approval and version tracking.
                        - Define a retention set that supports audits and complaint response.

                        --- page ---

                        Scenario
                        An online ad claims "top-ranked school district" based on an outdated source. The final ad file exists, but there is no supporting documentation for the claim and no evidence of supervisory review. A regulator asks for substantiation six months later.

                        Advertising controls should connect every published claim to a dated source and named approver. Retention should include draft versions, final creatives, source references, and publication metadata. Without version history, teams cannot prove what was known at the time of publication.

                        --- page ---

                        Key Takeaways
                        - If a claim cannot be evidenced, it should not be published.
                        - Version control is a compliance control, not just an operations convenience.
                        - Fast retrieval reduces disruption during examinations.
                        """,
                    DurationMinutes = 21,
                    OrderIndex = 4,
                    IsRequired = true
                });
            }

            if (hasLegacyComplianceCourse && missingLessons.Contains(Guid.Parse("98e37e1e-9ca5-4e4a-944b-4f3a454e5503")))
            {
                lessonEntries.Add(new Lesson
                {
                    Id = Guid.Parse("98e37e1e-9ca5-4e4a-944b-4f3a454e5503"),
                    ModuleId = Guid.Parse("c3c0f6aa-5c55-40a1-bf33-0edb77f7a901"),
                    Title = "Incident response and corrective action",
                    ContentType = "Text",
                    TextContent = """
                        Objectives
                        - Execute immediate containment steps when a compliance incident is detected.
                        - Design corrective actions that are measurable and verifiable.

                        --- page ---

                        Scenario
                        A transaction coordinator discovers that trust account reconciliation was missed for two cycles. There is no evidence of intentional misuse, but the control failure is material and must be escalated quickly. The team needs to stabilize the process while preserving records for review.

                        Effective incident response follows a sequence: contain impact, secure documentation, notify stakeholders, investigate root cause, and implement corrective action. Corrective plans should include ownership, deadlines, success criteria, and validation checks performed by someone independent of the original error.

                        --- page ---

                        Key Takeaways
                        - Early escalation protects both clients and the brokerage.
                        - Corrective actions should fix systems, not just symptoms.
                        - Post-incident reviews should produce explicit policy or training updates.
                        """,
                    DurationMinutes = 23,
                    OrderIndex = 5,
                    IsRequired = true
                });
            }

            if (lessonEntries.Count > 0)
            {
                dbContext.Lessons.AddRange(lessonEntries);
                await dbContext.SaveChangesAsync();
            }
        }

        await EnsureAdditionalMockCoursesAsync(dbContext);
        await EnsureWelcomeLessonTitleAsync(dbContext);
        await RemoveLicensingChecklistLessonAsync(dbContext);
        await RemoveCoreLicensingConceptsModuleAsync(dbContext);
        await EnsureRealEstateLicensingMockDataAsync(dbContext);
        await EnsureIntroStructureForAllCoursesAsync(dbContext);
        await EnsureModuleAndLessonOrderingNormalizedAsync(dbContext);
        await EnsurePrelicensingOrientationAndUnitAssessmentsAsync(dbContext);
    }

    public static async Task NormalizeLegacyComplianceTypesAsync(ApplicationDbContext dbContext)
    {
        var legacyCourses = await dbContext.Courses
            .Where(course => course.ComplianceType == LegacyStateRegulatedComplianceType || course.ComplianceType == LegacyGeneralComplianceType)
            .ToListAsync();

        foreach (var course in legacyCourses)
        {
            if (course.ComplianceType == LegacyGeneralComplianceType)
            {
                course.ComplianceType = CourseComplianceTypes.Unspecified;
                course.UpdatedAt = DateTime.UtcNow;
                continue;
            }

            var title = course.Title.ToLowerInvariant();
            var regulatoryCode = course.CommissionCourseNumber?.ToLowerInvariant() ?? string.Empty;
            course.ComplianceType = !string.IsNullOrWhiteSpace(course.ContinuingEducationType)
                ? CourseComplianceTypes.ContinuingEducation
                : title.Contains("pre-licens") || title.Contains("prelicens") || regulatoryCode.Contains("pre") || course.RequiredInstructionalMinutes == 4500
                    ? CourseComplianceTypes.Prelicensing
                    : title.Contains("post-licens") || title.Contains("postlicens") || regulatoryCode.Contains("post")
                        ? CourseComplianceTypes.Postlicensing
                        : CourseComplianceTypes.Unspecified;
            course.UpdatedAt = DateTime.UtcNow;
        }

        if (legacyCourses.Count > 0)
        {
            await dbContext.SaveChangesAsync();
        }
    }

    // One-time repair for gaps/duplicates left by deletions that predate CourseService's own delete-time renumbering.
    private static async Task EnsureModuleAndLessonOrderingNormalizedAsync(ApplicationDbContext dbContext)
    {
        var courseIds = await dbContext.Modules
            .Select(module => module.CourseId)
            .Distinct()
            .ToListAsync();

        foreach (var courseId in courseIds)
        {
            var modules = await dbContext.Modules
                .Where(module => module.CourseId == courseId)
                .OrderBy(module => module.OrderIndex)
                .ThenBy(module => module.Id)
                .ToListAsync();

            for (var index = 0; index < modules.Count; index++)
            {
                modules[index].OrderIndex = index + 1;
            }
        }

        var moduleIds = await dbContext.Lessons
            .Select(lesson => lesson.ModuleId)
            .Distinct()
            .ToListAsync();

        foreach (var moduleId in moduleIds)
        {
            var lessons = await dbContext.Lessons
                .Where(lesson => lesson.ModuleId == moduleId)
                .OrderBy(lesson => lesson.OrderIndex)
                .ThenBy(lesson => lesson.Id)
                .ToListAsync();

            for (var index = 0; index < lessons.Count; index++)
            {
                lessons[index].OrderIndex = index + 1;
            }
        }

        await dbContext.SaveChangesAsync();
    }

    public static async Task SeedEnrollmentsAsync(ApplicationDbContext dbContext)
    {
        var learnersByEmail = await dbContext.UserAccounts
            .Where(user =>
                user.Email == "learner@lms.com" ||
                user.Email == "learner2@lms.com" ||
                user.Email == "learner3@lms.com")
            .ToDictionaryAsync(user => user.Email);

        var enrollmentSeeds = new List<EnrollmentSeed>();

        if (learnersByEmail.TryGetValue("learner@lms.com", out var primaryLearner))
        {
            enrollmentSeeds.AddRange(
            [
                new EnrollmentSeed(primaryLearner.Id, Guid.Parse("64cc1cbb-45fc-4a76-b3b4-de399cb3830f"), 35m, -3),
                new EnrollmentSeed(primaryLearner.Id, Guid.Parse("5bc4b74b-71ac-40fe-8bc9-1daf5a922782"), 0m, -2),
                new EnrollmentSeed(primaryLearner.Id, Guid.Parse("c2a11379-8fb4-479a-9209-45441a56d812"), 0m, -1)
            ]);
        }

        if (learnersByEmail.TryGetValue("learner2@lms.com", out var secondaryLearner))
        {
            enrollmentSeeds.AddRange(
            [
                new EnrollmentSeed(secondaryLearner.Id, Guid.Parse("5bc4b74b-71ac-40fe-8bc9-1daf5a922782"), 50m, -5),
                new EnrollmentSeed(secondaryLearner.Id, Guid.Parse("c2a11379-8fb4-479a-9209-45441a56d812"), 0m, -4)
            ]);
        }

        if (learnersByEmail.TryGetValue("learner3@lms.com", out var tertiaryLearner))
        {
            enrollmentSeeds.AddRange(
            [
                new EnrollmentSeed(tertiaryLearner.Id, Guid.Parse("c2a11379-8fb4-479a-9209-45441a56d812"), 20m, -6),
                new EnrollmentSeed(tertiaryLearner.Id, Guid.Parse("9f4f6d8c-2f7a-4b6c-9a53-5b7a2d4e1c90"), 0m, -2)
            ]);
        }

        if (enrollmentSeeds.Count == 0)
        {
            return;
        }

        var existingEnrollmentKeys = await dbContext.Enrollments
            .Select(enrollment => new { enrollment.UserAccountId, enrollment.CourseId })
            .ToListAsync();

        var hasChanges = false;
        foreach (var enrollmentSeed in enrollmentSeeds)
        {
            var exists = existingEnrollmentKeys.Any(existing =>
                existing.UserAccountId == enrollmentSeed.UserAccountId &&
                existing.CourseId == enrollmentSeed.CourseId);

            if (exists)
            {
                continue;
            }

            dbContext.Enrollments.Add(new Enrollment
            {
                UserAccountId = enrollmentSeed.UserAccountId,
                CourseId = enrollmentSeed.CourseId,
                EnrollmentSource = "LearnerPurchase",
                ConsentStatus = "NotRequired",
                EnrolledAt = DateTime.UtcNow.AddDays(enrollmentSeed.EnrolledDaysAgo),
                ProgressPercent = enrollmentSeed.ProgressPercent,
                Completed = false
            });

            hasChanges = true;
        }

        if (hasChanges)
        {
            await dbContext.SaveChangesAsync();
        }

        await SeedBrokerAssignmentsAsync(dbContext);
        await SeedBrokerCoursePurchasesAsync(dbContext);
        await EnsureLearnerThreeCanReceiveRealEstateLicensingBasicsAsync(dbContext);
    }

    private sealed record EnrollmentSeed(Guid UserAccountId, Guid CourseId, decimal ProgressPercent, int EnrolledDaysAgo);

    private static async Task SeedBrokerAssignmentsAsync(ApplicationDbContext dbContext)
    {
        var brokerIds = await dbContext.UserAccounts
            .AsNoTracking()
            .Where(user => user.Role == "Broker" && user.IsActive)
            .Select(user => user.Id)
            .ToListAsync();

        var learnerIds = await dbContext.UserAccounts
            .AsNoTracking()
            .Where(user => user.Role == "Learner" && user.IsActive)
            .Select(user => user.Id)
            .ToListAsync();

        if (brokerIds.Count == 0 || learnerIds.Count == 0)
        {
            return;
        }

        var existingAssignments = await dbContext.BrokerLearnerAssignments
            .Where(assignment => brokerIds.Contains(assignment.BrokerUserId))
            .ToListAsync();

        if (existingAssignments.Count > 0)
        {
            dbContext.BrokerLearnerAssignments.RemoveRange(existingAssignments);
            await dbContext.SaveChangesAsync();
        }

        var now = DateTime.UtcNow;
        foreach (var brokerId in brokerIds)
        {
            foreach (var learnerId in learnerIds)
            {
                dbContext.BrokerLearnerAssignments.Add(new BrokerLearnerAssignment
                {
                    BrokerUserId = brokerId,
                    LearnerUserId = learnerId,
                    AssignedAt = now,
                    AssignedByUserId = null
                });
            }
        }

        try
        {
            await dbContext.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when ((ex.InnerException?.Message ?? string.Empty).Contains("FOREIGN KEY constraint failed", StringComparison.OrdinalIgnoreCase))
        {
            // Some legacy SQLite databases may contain mixed GUID text formats,
            // which can trigger FK violations for synthesized broker assignments.
            // Broker assignment seed data is non-critical for app boot; skip to keep startup resilient.
        }
    }

    private static async Task SeedBrokerCoursePurchasesAsync(ApplicationDbContext dbContext)
    {
        var brokers = await dbContext.UserAccounts
            .Where(user =>
                user.Role == "Broker" &&
                user.IsActive &&
                (user.Email == "broker@lms.com" || user.Email == "broker2@lms.com"))
            .OrderBy(user => user.Email)
            .ToListAsync();

        if (brokers.Count == 0)
        {
            return;
        }

        var publishedCatalogCourseIds = await dbContext.Courses
            .AsNoTracking()
            .Where(course => course.IsPublished && !course.IsArchived)
            .OrderBy(course => course.Title)
            .Select(course => course.Id)
            .ToListAsync();

        if (publishedCatalogCourseIds.Count == 0)
        {
            return;
        }

        var brokerOneCourseIds = publishedCatalogCourseIds.Take(5).ToList();
        var brokerTwoCourseIds = publishedCatalogCourseIds.Skip(1).Take(5).ToList();

        var plannedPurchases = new List<(Guid BrokerUserId, Guid CourseId)>();
        foreach (var broker in brokers)
        {
            var selectedCourses = broker.Email == "broker@lms.com" ? brokerOneCourseIds : brokerTwoCourseIds;
            plannedPurchases.AddRange(selectedCourses.Select(courseId => (broker.Id, courseId)));
        }

        if (plannedPurchases.Count == 0)
        {
            return;
        }

        var existingPurchases = await dbContext.Enrollments
            .AsNoTracking()
            .Where(enrollment =>
                enrollment.EnrollmentSource == "LearnerPurchase" &&
                plannedPurchases.Select(item => item.BrokerUserId).Contains(enrollment.UserAccountId))
            .Select(enrollment => new { enrollment.UserAccountId, enrollment.CourseId })
            .ToListAsync();

        var hasChanges = false;
        foreach (var purchase in plannedPurchases)
        {
            var exists = existingPurchases.Any(existing =>
                existing.UserAccountId == purchase.BrokerUserId &&
                existing.CourseId == purchase.CourseId);

            if (exists)
            {
                continue;
            }

            dbContext.Enrollments.Add(new Enrollment
            {
                UserAccountId = purchase.BrokerUserId,
                CourseId = purchase.CourseId,
                EnrollmentSource = "LearnerPurchase",
                SponsoredByBrokerUserId = null,
                ConsentStatus = "NotRequired",
                EnrolledAt = DateTime.UtcNow,
                ProgressPercent = 0,
                Completed = false
            });

            hasChanges = true;
        }

        if (hasChanges)
        {
            await dbContext.SaveChangesAsync();
        }
    }

    private static async Task EnsureLearnerThreeCanReceiveRealEstateLicensingBasicsAsync(ApplicationDbContext dbContext)
    {
        var learner = await dbContext.UserAccounts
            .SingleOrDefaultAsync(user => user.Email == "learner3@lms.com" && user.Role == "Learner" && user.IsActive);

        if (learner is null)
        {
            return;
        }

        var realEstateBasicsExists = await dbContext.Enrollments
            .AnyAsync(enrollment =>
                enrollment.UserAccountId == learner.Id &&
                enrollment.CourseId == RealEstateLicensingBasicsCourseId);

        if (!realEstateBasicsExists)
        {
            return;
        }

        var propertyMarketingExists = await dbContext.Enrollments
            .AnyAsync(enrollment =>
                enrollment.UserAccountId == learner.Id &&
                enrollment.CourseId == PropertyMarketingCourseId);

        if (!propertyMarketingExists)
        {
            dbContext.Enrollments.Add(new Enrollment
            {
                UserAccountId = learner.Id,
                CourseId = PropertyMarketingCourseId,
                EnrollmentSource = "LearnerPurchase",
                ConsentStatus = "NotRequired",
                EnrolledAt = DateTime.UtcNow,
                ProgressPercent = 0,
                Completed = false
            });
        }

        var enrollmentToRemove = await dbContext.Enrollments
            .Include(enrollment => enrollment.CompletionCertificate)
            .FirstOrDefaultAsync(enrollment =>
                enrollment.UserAccountId == learner.Id &&
                enrollment.CourseId == RealEstateLicensingBasicsCourseId);

        if (enrollmentToRemove is null)
        {
            await dbContext.SaveChangesAsync();
            return;
        }

        var lessonIds = await dbContext.Lessons
            .AsNoTracking()
            .Where(lesson => lesson.Module!.CourseId == RealEstateLicensingBasicsCourseId)
            .Select(lesson => lesson.Id)
            .ToListAsync();

        var progresses = await dbContext.LessonProgresses
            .Where(progress => progress.UserAccountId == learner.Id && lessonIds.Contains(progress.LessonId))
            .ToListAsync();

        if (progresses.Count > 0)
        {
            dbContext.LessonProgresses.RemoveRange(progresses);
        }

        if (enrollmentToRemove.CompletionCertificate is not null)
        {
            dbContext.CompletionCertificates.Remove(enrollmentToRemove.CompletionCertificate);
        }

        dbContext.Enrollments.Remove(enrollmentToRemove);
        await dbContext.SaveChangesAsync();
    }

    private static async Task EnsureIntroStructureForAllCoursesAsync(ApplicationDbContext dbContext)
    {
        var courses = await dbContext.Courses
            .Include(course => course.Modules)
                .ThenInclude(module => module.Lessons)
            .ToListAsync();

        var hasChanges = false;

        foreach (var course in courses)
        {
            var orderedModules = course.Modules
                .OrderBy(module => module.OrderIndex)
                .ToList();

            var introModule = orderedModules
                .FirstOrDefault(module => string.Equals(module.Title, GettingStartedModuleTitle, StringComparison.OrdinalIgnoreCase));

            if (introModule is null)
            {
                foreach (var module in orderedModules)
                {
                    module.OrderIndex += 1;
                }

                introModule = new Module
                {
                    Id = Guid.NewGuid(),
                    CourseId = course.Id,
                    Title = GettingStartedModuleTitle,
                    OrderIndex = 1
                };

                dbContext.Modules.Add(introModule);
                orderedModules.Insert(0, introModule);
                hasChanges = true;
            }

            var introLessons = introModule.Lessons.OrderBy(lesson => lesson.OrderIndex).ToList();

            var welcomeLesson = introLessons.FirstOrDefault(lesson => string.Equals(lesson.Title, WelcomeLessonTitle, StringComparison.OrdinalIgnoreCase));
            if (welcomeLesson is null)
            {
                dbContext.Lessons.Add(new Lesson
                {
                    Id = Guid.NewGuid(),
                    ModuleId = introModule.Id,
                    Title = WelcomeLessonTitle,
                    ContentType = "Text",
                    TextContent = WelcomeLessonText,
                    DurationMinutes = 8,
                    OrderIndex = 1,
                    IsRequired = true
                });

                hasChanges = true;
            }
            else if (welcomeLesson.OrderIndex != 1)
            {
                welcomeLesson.OrderIndex = 1;
                hasChanges = true;
            }

            if (!string.Equals(welcomeLesson?.TextContent ?? string.Empty, WelcomeLessonText, StringComparison.Ordinal))
            {
                if (welcomeLesson is not null)
                {
                    welcomeLesson.TextContent = WelcomeLessonText;
                    hasChanges = true;
                }
            }

            var deprecatedNavigationLessons = orderedModules
                .SelectMany(module => module.Lessons)
                .Where(lesson =>
                    DeprecatedNavigationTitles.Any(title => string.Equals(lesson.Title, title, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            if (deprecatedNavigationLessons.Count > 0)
            {
                dbContext.Lessons.RemoveRange(deprecatedNavigationLessons);
                hasChanges = true;
            }

        }

        if (hasChanges)
        {
            await dbContext.SaveChangesAsync();
        }
    }

    private static async Task EnsurePrelicensingOrientationAndUnitAssessmentsAsync(ApplicationDbContext dbContext)
    {
        var course = await dbContext.Courses
            .Include(existing => existing.Modules)
                .ThenInclude(module => module.Lessons)
            .SingleOrDefaultAsync(existing => existing.Id == RealEstateLicensingBasicsCourseId);

        if (course is null)
        {
            return;
        }

        var orientationModule = course.Modules
            .FirstOrDefault(module => string.Equals(module.Title, GettingStartedModuleTitle, StringComparison.OrdinalIgnoreCase));
        var orientationLesson = orientationModule?.Lessons
            .FirstOrDefault(lesson => string.Equals(lesson.Title, WelcomeLessonTitle, StringComparison.OrdinalIgnoreCase));

        if (orientationLesson is not null && !string.Equals(orientationLesson.TextContent, PrelicensingOrientationText, StringComparison.Ordinal))
        {
            orientationLesson.TextContent = PrelicensingOrientationText;
        }

        var assessmentDefinitions = new Dictionary<Guid, (Guid Id, string Prompt, string CorrectAnswer, string[] Distractors)>
        {
            [Guid.Parse("1d7dbd76-2e6f-4f07-8f7d-7260ea61b8a1")] = (
                Guid.Parse("40cb0ce3-3e52-468f-9eb5-a752510acc01"),
                "Which record best demonstrates that a licensing candidate is ready to proceed?",
                "A dated checklist showing education, application, background-check, and examination milestones",
                ["An undated list of goals", "A verbal estimate of processing time", "A social-media study reminder"]),
            [Guid.Parse("623f95fd-2398-4c9e-bb7f-c07ece9e0190")] = (
                Guid.Parse("40cb0ce3-3e52-468f-9eb5-a752510acc02"),
                "What is the strongest practice when agency duties and contract deadlines overlap?",
                "Document representation status, required disclosures, delivery, and deadline acknowledgments",
                ["Rely on verbal confirmation only", "Delay disclosure until closing", "Assume all parties share the same agent"]),
            [Guid.Parse("3a4d15bb-284f-4a10-b3e7-53bf5ba0261a")] = (
                Guid.Parse("40cb0ce3-3e52-468f-9eb5-a752510acc03"),
                "Which approach best supports examination readiness and compliant first transactions?",
                "Track missed topics, remediate them, and use documented transaction checklists",
                ["Memorize answer letters", "Skip review of weak topics", "Use informal notes instead of transaction records"]),
            [Guid.Parse("8f026ab8-cc84-4c31-8d84-a8dfdcb46329")] = (
                Guid.Parse("40cb0ce3-3e52-468f-9eb5-a752510acc07"),
                "What should a learner do after identifying a missed examination topic?",
                "Record the missed concept, review the governing material, and verify improvement with another practice set",
                ["Memorize the missed answer", "Ignore the topic if the overall score passed", "Replace the study log with an undated note"]),
            [Guid.Parse("a0dd7d8d-dae7-4cf6-8066-57b72ad2f514")] = (
                Guid.Parse("40cb0ce3-3e52-468f-9eb5-a752510acc04"),
                "Which property-management practice is consistent with fair-housing compliance?",
                "Apply documented, neutral criteria consistently to every applicant",
                ["Use different criteria by neighborhood", "Make exceptions based on protected status", "Avoid documenting screening decisions"]),
            [Guid.Parse("db5cc8d7-7c8b-40b8-9a00-95bd4d4948d5")] = (
                Guid.Parse("40cb0ce3-3e52-468f-9eb5-a752510acc05"),
                "What is the most defensible control for agency and trust-account activity?",
                "Maintain timely disclosures, transaction records, reconciliations, and escalation evidence",
                ["Reconcile only after a complaint", "Combine client and operating funds", "Discard superseded transaction records"]),
            [Guid.Parse("7e7e9736-c1a8-4d19-955f-9d48d9de7bd5")] = (
                Guid.Parse("40cb0ce3-3e52-468f-9eb5-a752510acc06"),
                "Which response best manages an identified brokerage compliance risk?",
                "Contain the issue, preserve records, escalate it, and verify corrective action",
                ["Wait for the issue to recur", "Delete incomplete records", "Close the matter without assigning responsibility"])
        };

        var existingDefinitions = await dbContext.CourseCheckpointDefinitions
            .Include(definition => definition.Options)
            .Where(definition => definition.CourseId == course.Id)
            .ToListAsync();

        foreach (var module in course.Modules.Where(module => assessmentDefinitions.ContainsKey(module.Id)))
        {
            var existingGate = existingDefinitions.FirstOrDefault(definition => definition.ModuleId == module.Id && definition.LessonId is null);
            if (existingGate is not null)
            {
                existingGate.GatesProgression = true;
                continue;
            }

            var assessment = assessmentDefinitions[module.Id];
            var definition = new CourseCheckpointDefinition
            {
                Id = assessment.Id,
                CourseId = course.Id,
                ModuleId = module.Id,
                LessonId = null,
                Key = $"regulatory-unit:{module.Id:D}",
                Title = $"{module.Title} Unit Assessment",
                Prompt = assessment.Prompt,
                Description = "Select the best answer. A passing result is required before the next Unit unlocks.",
                GatesProgression = true,
                OrderIndex = 1
            };

            dbContext.CourseCheckpointDefinitions.Add(definition);
            dbContext.CourseCheckpointOptions.Add(new CourseCheckpointOption
            {
                CourseCheckpointDefinitionId = definition.Id,
                Key = "correct",
                Label = assessment.CorrectAnswer,
                IsCorrect = true,
                OrderIndex = 1
            });

            for (var index = 0; index < assessment.Distractors.Length; index++)
            {
                dbContext.CourseCheckpointOptions.Add(new CourseCheckpointOption
                {
                    CourseCheckpointDefinitionId = definition.Id,
                    Key = $"distractor-{index + 1}",
                    Label = assessment.Distractors[index],
                    IsCorrect = false,
                    OrderIndex = index + 2
                });
            }
        }

        await dbContext.SaveChangesAsync();
    }

    private static async Task EnsureAdditionalMockCoursesAsync(ApplicationDbContext dbContext)
    {
        var hasChanges = false;

        if (!await dbContext.Courses.AnyAsync(course => course.Id == PropertyMarketingCourseId))
        {
            dbContext.Courses.Add(new Course
            {
                Id = PropertyMarketingCourseId,
                Title = "Property Marketing and Lead Conversion",
                Slug = "property-marketing-and-lead-conversion",
                Description = "A practical course on campaign planning, listing content, lead qualification, and conversion workflows.",
                Level = "Intermediate",
                DurationHours = 16,
                CreditHours = 3,
                Jurisdiction = "General",
                ComplianceType = CourseComplianceTypes.ContinuingEducation,
                ContinuingEducationType = ContinuingEducationTypes.Elective,
                Price = 99m,
                IsPublished = true,
                CreatedAt = DateTime.UtcNow
            });

            hasChanges = true;
        }

        if (!await dbContext.Courses.AnyAsync(course => course.Id == TransactionCoordinationCourseId))
        {
            dbContext.Courses.Add(new Course
            {
                Id = TransactionCoordinationCourseId,
                Title = "Transaction Coordination Mastery",
                Slug = "transaction-coordination-mastery",
                Description = "A workflow-focused course covering schedules, documentation controls, communication checkpoints, and closing readiness.",
                Level = "Advanced",
                DurationHours = 20,
                CreditHours = 4,
                Jurisdiction = "General",
                ComplianceType = CourseComplianceTypes.ContinuingEducation,
                ContinuingEducationType = ContinuingEducationTypes.Elective,
                Price = 119m,
                IsPublished = true,
                CreatedAt = DateTime.UtcNow
            });

            hasChanges = true;
        }

        if (!await dbContext.Courses.AnyAsync(course => course.Id == ClientCommunicationCourseId))
        {
            dbContext.Courses.Add(new Course
            {
                Id = ClientCommunicationCourseId,
                Title = "Client Communication and Negotiation Strategies",
                Slug = "client-communication-and-negotiation-strategies",
                Description = "An applied course on expectation-setting, high-stakes communication, and negotiation structure for real estate transactions.",
                Level = "Intermediate",
                DurationHours = 14,
                CreditHours = 3,
                Jurisdiction = "General",
                ComplianceType = CourseComplianceTypes.ContinuingEducation,
                ContinuingEducationType = ContinuingEducationTypes.Elective,
                Price = 109m,
                IsPublished = true,
                CreatedAt = DateTime.UtcNow
            });

            hasChanges = true;
        }

        var pmModuleCampaignsId = Guid.Parse("454a1c4d-70a8-4d9e-8970-bbe4f6a8e77f");
        var pmModuleLeadsId = Guid.Parse("f5d174c1-e1e4-46e1-b422-f023915e14c9");
        var tcModuleScheduleId = Guid.Parse("a791083d-b4af-4024-a93d-7d99d2f0915f");
        var tcModuleClosingId = Guid.Parse("0fd3c5e1-3406-4ef2-bf95-21f5fb7557de");
        var ccModuleDiscoveryId = Guid.Parse("6c02b698-7884-4588-9aaf-2da5a9304b9c");
        var ccModuleNegotiationId = Guid.Parse("1a5d2f97-22fe-4faf-a9c1-1949f0f55764");

        if (!await dbContext.Modules.AnyAsync(module => module.Id == pmModuleCampaignsId))
        {
            dbContext.Modules.Add(new Module
            {
                Id = pmModuleCampaignsId,
                CourseId = PropertyMarketingCourseId,
                Title = "Campaign Planning Fundamentals",
                OrderIndex = 1
            });
            hasChanges = true;
        }

        if (!await dbContext.Modules.AnyAsync(module => module.Id == pmModuleLeadsId))
        {
            dbContext.Modules.Add(new Module
            {
                Id = pmModuleLeadsId,
                CourseId = PropertyMarketingCourseId,
                Title = "Lead Qualification and Follow-Up",
                OrderIndex = 2
            });
            hasChanges = true;
        }

        if (!await dbContext.Modules.AnyAsync(module => module.Id == tcModuleScheduleId))
        {
            dbContext.Modules.Add(new Module
            {
                Id = tcModuleScheduleId,
                CourseId = TransactionCoordinationCourseId,
                Title = "Schedule and Milestone Control",
                OrderIndex = 1
            });
            hasChanges = true;
        }

        if (!await dbContext.Modules.AnyAsync(module => module.Id == tcModuleClosingId))
        {
            dbContext.Modules.Add(new Module
            {
                Id = tcModuleClosingId,
                CourseId = TransactionCoordinationCourseId,
                Title = "Closing Readiness and File Audit",
                OrderIndex = 2
            });
            hasChanges = true;
        }

        if (!await dbContext.Modules.AnyAsync(module => module.Id == ccModuleDiscoveryId))
        {
            dbContext.Modules.Add(new Module
            {
                Id = ccModuleDiscoveryId,
                CourseId = ClientCommunicationCourseId,
                Title = "Discovery, Framing, and Expectation Setting",
                OrderIndex = 1
            });
            hasChanges = true;
        }

        if (!await dbContext.Modules.AnyAsync(module => module.Id == ccModuleNegotiationId))
        {
            dbContext.Modules.Add(new Module
            {
                Id = ccModuleNegotiationId,
                CourseId = ClientCommunicationCourseId,
                Title = "Negotiation Scenarios and Deal Alignment",
                OrderIndex = 2
            });
            hasChanges = true;
        }

        var lessonsToSeed = new List<Lesson>
        {
            new()
            {
                Id = Guid.Parse("e0f8182b-3fa3-40e2-b6ec-f829ed3f9e81"),
                ModuleId = pmModuleCampaignsId,
                Title = "Audience-first campaign setup",
                ContentType = "Text",
                TextContent = """
                    Start every campaign by defining a target segment with clear buying intent signals.
                    Build one primary message, one supporting proof point, and one CTA for that segment.

                    Campaign plans fail when teams mix too many messages at launch.
                    Keep your first wave narrow, measure response quality, and expand once conversion patterns are stable.
                    """,
                DurationMinutes = 18,
                OrderIndex = 1,
                IsRequired = true
            },
            new()
            {
                Id = Guid.Parse("ec0f8116-3d0e-4d9c-afaf-96f39bbf3f4a"),
                ModuleId = pmModuleCampaignsId,
                Title = "Listing copy that converts",
                ContentType = "Text",
                TextContent = """
                    Strong listing copy explains value before features.
                    Lead with the buyer outcome, then support it with concrete property details.

                    Use short sections for readability:
                    - lifestyle benefit
                    - property evidence
                    - next action

                    Avoid generic claims that cannot be substantiated.
                    """,
                DurationMinutes = 16,
                OrderIndex = 2,
                IsRequired = true
            },
            new()
            {
                Id = Guid.Parse("a1f4b2c6-c0ce-4cef-b23a-6ef8cf2f5e50"),
                ModuleId = pmModuleLeadsId,
                Title = "Lead scoring and response windows",
                ContentType = "Text",
                TextContent = """
                    Assign lead scores using explicit criteria: budget clarity, schedule urgency, financing status, and engagement depth.
                    Route high-score leads for immediate outreach with a timed response SLA.

                    Response speed matters, but consistency matters more.
                    Teams should track first-response time and contact-attempt cadence as operational KPIs.
                    """,
                DurationMinutes = 20,
                OrderIndex = 1,
                IsRequired = true
            },
            new()
            {
                Id = Guid.Parse("db83958f-84f7-4720-a0c3-82f29bdb3938"),
                ModuleId = pmModuleLeadsId,
                Title = "Follow-up sequences that keep momentum",
                ContentType = "Text",
                TextContent = """
                    Design follow-up as a sequence, not a one-off message.
                    Plan touchpoints across email, SMS, and phone with a clear purpose for each step.

                    Every follow-up should answer one buyer question and set one concrete next step.
                    If a lead stalls, move to a value-based re-engagement script instead of repeating the same ask.
                    """,
                DurationMinutes = 19,
                OrderIndex = 2,
                IsRequired = true
            },
            new()
            {
                Id = Guid.Parse("8e39807a-6e6a-41fb-8dea-8f1632e2dc56"),
                ModuleId = tcModuleScheduleId,
                Title = "Milestone mapping for active files",
                ContentType = "Text",
                TextContent = """
                    Build a milestone map from contract execution through closing.
                    Include dependencies, owner roles, and required documentation at each step.

                    A visible schedule prevents silent delays.
                    When dependencies shift, update the plan immediately and notify all stakeholders with revised dates.
                    """,
                DurationMinutes = 22,
                OrderIndex = 1,
                IsRequired = true
            },
            new()
            {
                Id = Guid.Parse("5ef8b5a9-cac6-4e01-a8d6-b4f5a3f31d59"),
                ModuleId = tcModuleScheduleId,
                Title = "Escalation triggers and exception handling",
                ContentType = "Text",
                TextContent = """
                    Define escalation triggers before issues occur.
                    Examples include missed lender milestones, unsigned addenda, and unresolved inspection items.

                    Exception handling must be documented with timestamped actions, responsible parties, and recovery deadlines.
                    This record becomes critical if schedules are challenged later.
                    """,
                DurationMinutes = 17,
                OrderIndex = 2,
                IsRequired = true
            },
            new()
            {
                Id = Guid.Parse("a8b53c03-48c7-4e39-b67b-a4453f3f2faa"),
                ModuleId = tcModuleClosingId,
                Title = "Pre-closing document verification",
                ContentType = "Text",
                TextContent = """
                    Run a pre-closing audit 72 hours before settlement.
                    Validate signatures, identity details, disclosures, and financial figures against source records.

                    Last-minute corrections are manageable when detected early.
                    Use a checklist with accountable owners to avoid verbal-only confirmations.
                    """,
                DurationMinutes = 21,
                OrderIndex = 1,
                IsRequired = true
            },
            new()
            {
                Id = Guid.Parse("ab1bf2ee-64d8-4cd8-8cd0-338d4f56ef9c"),
                ModuleId = tcModuleClosingId,
                Title = "Post-close handoff and archive standards",
                ContentType = "Text",
                TextContent = """
                    Closeout is not complete until handoff and archive standards are met.
                    Deliver final documents to stakeholders with a clear index and storage location.

                    Archive files using naming conventions, retention tags, and access controls.
                    A disciplined archive process reduces retrieval time during audits and client support requests.
                    """,
                DurationMinutes = 18,
                OrderIndex = 2,
                IsRequired = true
            },
            new()
            {
                Id = Guid.Parse("e2f1e9f5-39fb-48e7-8bf3-8cf7fc188402"),
                ModuleId = ccModuleDiscoveryId,
                Title = "Client discovery interviews that surface priorities",
                ContentType = "Text",
                TextContent = """
                    Strong client communication starts with structured discovery.
                    Ask about schedule, risk tolerance, financing certainty, and decision drivers before discussing tactics.

                    --- page ---

                    Use a repeatable interview flow so critical details are captured every time.
                    Summarize priorities back to the client and confirm alignment in writing to prevent expectation gaps later.
                    """,
                DurationMinutes = 18,
                OrderIndex = 1,
                IsRequired = true
            },
            new()
            {
                Id = Guid.Parse("4bdca25f-61f8-43b1-8ddd-0c8600f0ab96"),
                ModuleId = ccModuleDiscoveryId,
                Title = "Messaging frameworks for difficult conversations",
                ContentType = "Text",
                TextContent = """
                    Difficult conversations require clarity, empathy, and boundaries.
                    Use a three-part structure: context, options, and recommended next step.

                    --- page ---

                    Avoid ambiguous language when discussing risk, delays, or concessions.
                    Confirm understanding with a recap message that documents what was decided and who owns each follow-up action.
                    """,
                DurationMinutes = 17,
                OrderIndex = 2,
                IsRequired = true
            },
            new()
            {
                Id = Guid.Parse("7da8e5f0-67f1-46ea-90a1-a89de4dfaf8f"),
                ModuleId = ccModuleNegotiationId,
                Title = "Offer strategy and concession planning",
                ContentType = "Text",
                TextContent = """
                    Effective negotiation starts before an offer is drafted.
                    Define must-have terms, walk-away thresholds, and tradable concessions with your client.

                    --- page ---

                    Scenario planning improves speed under pressure.
                    Prepare responses for multiple counteroffer paths so client decisions stay aligned with their original priorities.
                    """,
                DurationMinutes = 20,
                OrderIndex = 1,
                IsRequired = true
            },
            new()
            {
                Id = Guid.Parse("a347ac9e-f98c-4bdf-b3fe-5745b5c06103"),
                ModuleId = ccModuleNegotiationId,
                Title = "Multi-party alignment and closing communication",
                ContentType = "Text",
                TextContent = """
                    Multi-party deals fail when communication is fragmented.
                    Build a communication map covering client, lender, title, and counterpart milestones.

                    --- page ---

                    Use concise status updates with decision deadlines and unresolved items.
                    Consistent communication cadence reduces surprises and keeps deals moving toward closing.
                    """,
                DurationMinutes = 19,
                OrderIndex = 2,
                IsRequired = true
            }
        };

        foreach (var lesson in lessonsToSeed)
        {
            if (!await dbContext.Lessons.AnyAsync(existing => existing.Id == lesson.Id))
            {
                dbContext.Lessons.Add(lesson);
                hasChanges = true;
            }
        }

        if (hasChanges)
        {
            await dbContext.SaveChangesAsync();
        }
    }

    private static async Task RemoveCoreLicensingConceptsModuleAsync(ApplicationDbContext dbContext)
    {
        var coreLicensingConceptsModules = await dbContext.Modules
            .Where(module => module.CourseId == RealEstateLicensingBasicsCourseId && module.Title == "Core Licensing Concepts")
            .ToListAsync();

        if (coreLicensingConceptsModules.Count == 0)
        {
            return;
        }

        dbContext.Modules.RemoveRange(coreLicensingConceptsModules);
        await dbContext.SaveChangesAsync();
    }

    private static async Task EnsureWelcomeLessonTitleAsync(ApplicationDbContext dbContext)
    {
        var welcomeLessonId = Guid.Parse("a8b6e232-11ad-4c08-8d2d-8a6f9f00a101");
        var lesson = await dbContext.Lessons.FirstOrDefaultAsync(existing => existing.Id == welcomeLessonId);

        if (lesson is null || lesson.Title == "Welcome")
        {
            return;
        }

        lesson.Title = "Welcome";
        lesson.TextContent = WelcomeLessonText;
        await dbContext.SaveChangesAsync();
    }

    private static async Task RemoveLicensingChecklistLessonAsync(ApplicationDbContext dbContext)
    {
        var licensingChecklistLessonId = Guid.Parse("a8b6e232-11ad-4c08-8d2d-8a6f9f00a102");
        var lesson = await dbContext.Lessons.FirstOrDefaultAsync(existing => existing.Id == licensingChecklistLessonId);

        if (lesson is null)
        {
            return;
        }

        dbContext.Lessons.Remove(lesson);
        await dbContext.SaveChangesAsync();
    }

    private static async Task EnsureRealEstateLicensingMockDataAsync(ApplicationDbContext dbContext)
    {
        var course = await dbContext.Courses
            .SingleOrDefaultAsync(course => course.Id == RealEstateLicensingBasicsCourseId);

        if (course is null)
        {
            return;
        }

        var hasChanges = false;

        if (course.ComplianceType != CourseComplianceTypes.Prelicensing)
        {
            course.ComplianceType = CourseComplianceTypes.Prelicensing;
            hasChanges = true;
        }

        if (course.Title != "NC Real Estate 75-Hour Pre-Licensing Broker")
        {
            course.Title = "NC Real Estate 75-Hour Pre-Licensing Broker";
            hasChanges = true;
        }

        if (course.Jurisdiction != "NC")
        {
            course.Jurisdiction = "NC";
            hasChanges = true;
        }

        if (course.RequiredInstructionalMinutes != 4500)
        {
            course.RequiredInstructionalMinutes = 4500;
            hasChanges = true;
        }

        if (course.MinimumPassingPercent != 75)
        {
            course.MinimumPassingPercent = 75;
            hasChanges = true;
        }

        if (course.MinimumAttendancePercent != 80)
        {
            course.MinimumAttendancePercent = 80;
            hasChanges = true;
        }

        if (course.DurationHours != 75)
        {
            course.DurationHours = 75;
            hasChanges = true;
        }

        if (course.CreditHours != 75)
        {
            course.CreditHours = 75;
            hasChanges = true;
        }

        const string requiredDescription = "A state-regulated, distance-learning pre-licensing course covering core NC licensing requirements, agency, contracts, and exam readiness.";
        if (course.Description != requiredDescription)
        {
            course.Description = requiredDescription;
            hasChanges = true;
        }

        if (course.DeliveryMethod != CourseDeliveryMethods.DistanceEducation)
        {
            course.DeliveryMethod = CourseDeliveryMethods.DistanceEducation;
            hasChanges = true;
        }

        if (course.CommissionCourseNumber != "NC-PRE-75")
        {
            course.CommissionCourseNumber = "NC-PRE-75";
            hasChanges = true;
        }

        if (course.CompletionWindowDays != 180)
        {
            course.CompletionWindowDays = 180;
            hasChanges = true;
        }

        if (!course.RequiresProctoredExam)
        {
            course.RequiresProctoredExam = true;
            hasChanges = true;
        }

        var unitDefinitions = new[]
        {
            (Guid.Parse("1d7dbd76-2e6f-4f07-8f7d-7260ea61b8a1"), "Licensing Requirements and Eligibility"),
            (Guid.Parse("623f95fd-2398-4c9e-bb7f-c07ece9e0190"), "Agency, Disclosure, and Contracts"),
            (Guid.Parse("3a4d15bb-284f-4a10-b3e7-53bf5ba0261a"), "Exam Readiness and First Transactions I"),
            (Guid.Parse("8f026ab8-cc84-4c31-8d84-a8dfdcb46329"), "Exam Readiness and First Transactions II"),
            (Guid.Parse("a0dd7d8d-dae7-4cf6-8066-57b72ad2f514"), "Property Management and Fair Housing"),
            (Guid.Parse("db5cc8d7-7c8b-40b8-9a00-95bd4d4948d5"), "Agency Relationships and Trust Accounts"),
            (Guid.Parse("7e7e9736-c1a8-4d19-955f-9d48d9de7bd5"), "Ethics, Risk, and Practice Management")
        };

        var desiredModuleIds = unitDefinitions.Select(module => module.Item1).ToHashSet();
        var existingModules = await dbContext.Modules
            .Where(module => module.CourseId == RealEstateLicensingBasicsCourseId)
            .ToListAsync();

        var legacyModules = existingModules
            .Where(module => !desiredModuleIds.Contains(module.Id))
            .ToList();

        if (legacyModules.Count > 0)
        {
            var legacyLessonIds = await dbContext.Lessons
                .Where(lesson => legacyModules.Select(module => module.Id).Contains(lesson.ModuleId))
                .Select(lesson => lesson.Id)
                .ToListAsync();

            if (legacyLessonIds.Count > 0)
            {
                var legacyLessons = await dbContext.Lessons
                    .Where(lesson => legacyLessonIds.Contains(lesson.Id))
                    .ToListAsync();
                dbContext.Lessons.RemoveRange(legacyLessons);
            }

            dbContext.Modules.RemoveRange(legacyModules);
            hasChanges = true;
            await dbContext.SaveChangesAsync();
        }

        var orderedModuleIds = unitDefinitions.Select(item => item.Item1).ToArray();

        foreach (var (moduleId, title) in unitDefinitions)
        {
            var module = await dbContext.Modules
                .SingleOrDefaultAsync(existing => existing.Id == moduleId && existing.CourseId == RealEstateLicensingBasicsCourseId);

            if (module is null)
            {
                dbContext.Modules.Add(new Module
                {
                    Id = moduleId,
                    CourseId = RealEstateLicensingBasicsCourseId,
                    Title = title,
                    OrderIndex = Array.IndexOf(orderedModuleIds, moduleId) + 1
                });
                hasChanges = true;
                continue;
            }

            if (module.Title != title || module.OrderIndex != Array.IndexOf(orderedModuleIds, moduleId) + 1)
            {
                module.Title = title;
                module.OrderIndex = Array.IndexOf(orderedModuleIds, moduleId) + 1;
                hasChanges = true;
            }
        }

        await dbContext.SaveChangesAsync();

        var desiredLessonsByModule = new Dictionary<Guid, List<Lesson>>
        {
            [unitDefinitions[0].Item1] =
            [
                new()
                {
                    Id = Guid.Parse("f9bb3a77-8701-42d8-b8ec-72ecf7f3d6bf"),
                    ModuleId = unitDefinitions[0].Item1,
                    Title = "License pathways and schedules",
                    ContentType = "Text",
                    TextContent = """
                        Compare common licensing pathways by education hours, sponsorship requirements, and exam sequence.
                        Build a realistic schedule that includes application review windows and retake buffer time.

                        --- page ---

                        New candidates often underestimate dependency steps like fingerprints, background checks, and sponsor approval.
                        A practical plan should include checkpoints for each dependency with date ranges, not single-day assumptions.

                        --- page ---

                        Key Takeaways
                        - Plan from your target activation date backward.
                        - Track each requirement as a milestone with proof of completion.
                        - Leave contingency time for state processing delays.
                        """,
                    DurationMinutes = 20,
                    OrderIndex = 1,
                    IsRequired = true
                },
                new()
                {
                    Id = Guid.Parse("6630c0eb-83d5-4e3f-9833-5cdbda2be7f9"),
                    ModuleId = unitDefinitions[0].Item1,
                    Title = "Education hour tracking and compliance records",
                    ContentType = "Text",
                    TextContent = """
                        Keep a structured record of completed course hours, provider certificates, and eligibility documents.
                        Missing or inconsistent records are a common cause of application delays.

                        --- page ---

                        Use a single source of truth for training records with date, provider, completion confirmation, and file location.
                        This record should be ready for audit or application review without manual reconstruction.
                        """,
                    DurationMinutes = 16,
                    OrderIndex = 2,
                    IsRequired = true
                },
                new()
                {
                    Id = Guid.Parse("d4b5d4d5-1b6d-4fbc-bbb7-5d74cb8a7a36"),
                    ModuleId = unitDefinitions[0].Item1,
                    Title = "Exam readiness checklist and remediation",
                    ContentType = "Text",
                    TextContent = """
                        Build a review plan for the licensing exam using daily checkpoints, missed-topic logs, and a final practice sequence.
                        The goal is not just to study, but to reduce errors before the exam day.
                        """,
                    DurationMinutes = 18,
                    OrderIndex = 3,
                    IsRequired = true
                }
            ],
            [unitDefinitions[1].Item1] =
            [
                new()
                {
                    Id = Guid.Parse("26d5d194-b5d8-4ac4-aabc-2a2710f3cb7c"),
                    ModuleId = unitDefinitions[1].Item1,
                    Title = "Agency relationships and fiduciary duties",
                    ContentType = "Text",
                    TextContent = """
                        Understand when agency is formed and what duties attach at each stage of representation.
                        Clarify obligations around loyalty, disclosure, confidentiality, and accounting.

                        --- page ---

                        Scenario
                        A buyer asks for market strategy advice before signing a representation agreement.
                        Your communication language can create implied duties if boundaries are unclear.

                        --- page ---

                        Key Takeaways
                        - Confirm representation status early.
                        - Document disclosures and acknowledgements.
                        - Avoid ambiguous advice before agency terms are clear.
                        """,
                    DurationMinutes = 21,
                    OrderIndex = 1,
                    IsRequired = true
                },
                new()
                {
                    Id = Guid.Parse("d896b637-9a5c-4f0f-8cd6-abbb60529799"),
                    ModuleId = unitDefinitions[1].Item1,
                    Title = "Contract basics and contingency checkpoints",
                    ContentType = "Text",
                    TextContent = """
                        Review the core sections of a purchase contract and the business risks tied to each schedule.
                        Focus on contingency periods, notice requirements, and amendment handling.

                        --- page ---

                        Build a checklist for contract execution through contingency removal.
                        Include timestamped confirmation of delivery, response windows, and signed updates.
                        """,
                    DurationMinutes = 18,
                    OrderIndex = 2,
                    IsRequired = true
                },
                new()
                {
                    Id = Guid.Parse("b7d2c9d4-4a14-41e8-a1ef-36f2e2c10b7d"),
                    ModuleId = unitDefinitions[1].Item1,
                    Title = "Disclosure and broker communication expectations",
                    ContentType = "Text",
                    TextContent = """
                        Clarify when communication becomes disclosure and why timing matters.
                        Use a documented process for review, response, and record retention during client conversations.
                        """,
                    DurationMinutes = 19,
                    OrderIndex = 3,
                    IsRequired = true
                }
            ],
            [unitDefinitions[2].Item1] =
            [
                new()
                {
                    Id = Guid.Parse("7e11b8af-edf6-423a-bcdb-feef164d7d39"),
                    ModuleId = unitDefinitions[2].Item1,
                    Title = "Exam blueprint strategy and study cycles",
                    ContentType = "Text",
                    TextContent = """
                        Organize exam prep by topic weight and error trends from practice sets.
                        High performers use short cycles: assess, remediate, retest, and review retention.

                        --- page ---

                        Treat practice exams as diagnostics, not just scores.
                        Categorize misses by concept, vocabulary, and test-taking execution to target remediation.
                        """,
                    DurationMinutes = 17,
                    OrderIndex = 1,
                    IsRequired = true
                },
                new()
                {
                    Id = Guid.Parse("5ef598f8-958c-4fbf-9238-b2ce20f3509c"),
                    ModuleId = unitDefinitions[2].Item1,
                    Title = "First 30-day transaction readiness",
                    ContentType = "Text",
                    TextContent = """
                        Prepare for your first active client files with a 30-day operating plan.
                        Include lead intake scripts, disclosure timing rules, and supervision touchpoints.

                        --- page ---

                        The transition from exam prep to production is smoother when workflows are pre-built.
                        Use templates for file setup, communication logs, and milestone tracking before your first contract.
                        """,
                    DurationMinutes = 19,
                    OrderIndex = 2,
                    IsRequired = true
                },
                new()
                {
                    Id = Guid.Parse("7d68bf42-4d96-44f0-b97d-9ab6d3af2d61"),
                    ModuleId = unitDefinitions[2].Item1,
                    Title = "Practice exam debrief and correction loop",
                    ContentType = "Text",
                    TextContent = """
                        Use each practice exam to create a targeted study log by topic, not just a score shorthand.
                        A structured correction loop prevents repeated errors from becoming predictable gaps.
                        """,
                    DurationMinutes = 17,
                    OrderIndex = 3,
                    IsRequired = true
                }
            ],
            [unitDefinitions[3].Item1] =
            [
                new()
                {
                    Id = Guid.Parse("f3523b84-8ff8-4b8a-8f5a-6b8e58a8bd1d"),
                    ModuleId = unitDefinitions[3].Item1,
                    Title = "Practice exam review and missed-topic remediation",
                    ContentType = "Text",
                    TextContent = """
                        Review missed concepts by domain and convert them into a focused remediation plan.
                        Practice sets should drive study sessions, not merely measure outcomes.
                        """,
                    DurationMinutes = 16,
                        OrderIndex = 1,
                    IsRequired = true
                }
            ],
                [unitDefinitions[4].Item1] =
            [
                new()
                {
                    Id = Guid.Parse("b0cc3f2f-3f6d-4db2-af04-7d0bbeb3a329"),
                        ModuleId = unitDefinitions[4].Item1,
                    Title = "Fair housing and property management basics",
                    ContentType = "Text",
                    TextContent = """
                        Recognize the legal and operational issues that arise when advertising, managing, and showing properties.
                        Keep intake, screening, and occupancy records consistent with policy and local requirements.
                        """,
                    DurationMinutes = 18,
                    OrderIndex = 1,
                    IsRequired = true
                },
                new()
                {
                    Id = Guid.Parse("7b1d6e6e-4b0c-4ec1-9149-8e3d8ce6322a"),
                    ModuleId = unitDefinitions[4].Item1,
                    Title = "Property operations and maintenance records",
                    ContentType = "Text",
                    TextContent = """
                        Create a repeatable property file checklist from listing through close.
                        Track inspection requests, repair notes, and communication logs with deadlines attached.
                        """,
                    DurationMinutes = 20,
                    OrderIndex = 2,
                    IsRequired = true
                },
                new()
                {
                    Id = Guid.Parse("fc2f5447-21da-4298-b72d-02a43f44f7fe"),
                    ModuleId = unitDefinitions[4].Item1,
                    Title = "Tenant and client communication safeguards",
                    ContentType = "Text",
                    TextContent = """
                        Document how you respond to service requests, complaints, and maintenance follow-up to reduce risk.
                        A consistent record improves compliance and trust during management transitions.
                        """,
                    DurationMinutes = 16,
                    OrderIndex = 3,
                    IsRequired = true
                }
            ],
            [unitDefinitions[5].Item1] =
            [
                new()
                {
                    Id = Guid.Parse("f6f9ac48-26a9-4fb8-9f62-1d6d0881d6d8"),
                    ModuleId = unitDefinitions[5].Item1,
                    Title = "Trust accounts and brokerage records",
                    ContentType = "Text",
                    TextContent = """
                        Separate client funds from business funds and reconcile each trust ledger to source documents.
                        The result should be auditable without requiring a file-by-file reconstruction.
                        """,
                    DurationMinutes = 17,
                    OrderIndex = 1,
                    IsRequired = true
                },
                new()
                {
                    Id = Guid.Parse("8ef8f2bf-4d2a-4ad9-a41d-7546747bf0a7"),
                    ModuleId = unitDefinitions[5].Item1,
                    Title = "Supervision, documentation, and review cycles",
                    ContentType = "Text",
                    TextContent = """
                        Documentation is most credible when review standards are defined and repeatable.
                        Build checks for file completeness, accuracy, and approval timing before the file is closed.
                        """,
                    DurationMinutes = 19,
                    OrderIndex = 2,
                    IsRequired = true
                },
                new()
                {
                    Id = Guid.Parse("a17d5556-4b8f-4871-b3c8-28b724d31d67"),
                    ModuleId = unitDefinitions[5].Item1,
                    Title = "Record retention and audit-readiness review",
                    ContentType = "Text",
                    TextContent = """
                        Establish a calendar for document retention review, file audits, and policy refreshes.
                        Verified records reduce enforcement risk and strengthen the brokerage's operating discipline.
                        """,
                    DurationMinutes = 18,
                    OrderIndex = 3,
                    IsRequired = true
                }
            ],
            [unitDefinitions[6].Item1] =
            [
                new()
                {
                    Id = Guid.Parse("72d916a7-b1d2-4d10-8f8b-c54d17201f42"),
                    ModuleId = unitDefinitions[6].Item1,
                    Title = "Ethics and risk recognition",
                    ContentType = "Text",
                    TextContent = """
                        Identify conflicts, disclosure triggers, and risk patterns before they become violations.
                        Practice evaluating decisions by both legal risk and consumer impact.
                        """,
                    DurationMinutes = 18,
                    OrderIndex = 1,
                    IsRequired = true
                },
                new()
                {
                    Id = Guid.Parse("8a001f9d-928a-49fc-a189-f03c0d63bc71"),
                    ModuleId = unitDefinitions[6].Item1,
                    Title = "Closing preparation and first-client workflow",
                    ContentType = "Text",
                    TextContent = """
                        Prepare a controlled workflow for your first active listings and buyer files.
                        Include communication templates, review checkpoints, and contingency reminders.
                        """,
                    DurationMinutes = 21,
                    OrderIndex = 2,
                    IsRequired = true
                },
                new()
                {
                    Id = Guid.Parse("fb87b511-1dc0-4b1b-bb3b-0c4d5b51fd22"),
                    ModuleId = unitDefinitions[6].Item1,
                    Title = "Risk review and practice management habits",
                    ContentType = "Text",
                    TextContent = """
                        Evaluate recurring risks in your operations and prioritize a limited set of practical control improvements.
                        Good habits are built through consistent checks, not annual policy review alone.
                        """,
                    DurationMinutes = 20,
                    OrderIndex = 3,
                    IsRequired = true
                }
            ]
        };

        foreach (var moduleId in desiredLessonsByModule.Keys)
        {
            var moduleLessons = await dbContext.Lessons
                .Where(lesson => lesson.ModuleId == moduleId)
                .ToListAsync();

            if (moduleLessons.Count > 0)
            {
                dbContext.Lessons.RemoveRange(moduleLessons);
            }

            foreach (var lesson in desiredLessonsByModule[moduleId])
            {
                dbContext.Lessons.Add(lesson);
            }

            hasChanges = true;
        }

        if (hasChanges)
        {
            await dbContext.SaveChangesAsync();
        }
    }

}