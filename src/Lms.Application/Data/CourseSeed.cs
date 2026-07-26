using Lms.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Lms.Application.Data;

public static class CourseSeed
{
    private const string GettingStartedModuleTitle = "Getting Started";
    private const string WelcomeLessonTitle = "Welcome";
    private const string WelcomeLessonText = """
        Welcome!

        This course is organized into modules that contain lessons.

        Use the course menu to your right to jump directly into modules and lessons,

        or use the arrow buttons for step-by-step progression.
        """;
    private static readonly string[] DeprecatedNavigationTitles = ["Navigation", "How to navigate this course"];
    private static readonly Guid RealEstateLicensingBasicsCourseId = Guid.Parse("64cc1cbb-45fc-4a76-b3b4-de399cb3830f");
    private static readonly Guid PropertyMarketingCourseId = Guid.Parse("c2a11379-8fb4-479a-9209-45441a56d812");
    private static readonly Guid TransactionCoordinationCourseId = Guid.Parse("f5e5df5f-5b7a-4d8c-aafd-0cd1745311e4");
    private static readonly Guid ClientCommunicationCourseId = Guid.Parse("9f4f6d8c-2f7a-4b6c-9a53-5b7a2d4e1c90");

    public static async Task SeedAsync(ApplicationDbContext dbContext)
    {
        if (!await dbContext.Courses.AnyAsync())
        {
            dbContext.Courses.AddRange(
                new Course
                {
                    Id = Guid.Parse("64cc1cbb-45fc-4a76-b3b4-de399cb3830f"),
                    Title = "Real Estate Licensing Basics",
                    Slug = "real-estate-licensing-basics",
                    Description = "A beginner-friendly introduction to licensing requirements, terminology, and exam prep.",
                    Level = "Beginner",
                    DurationHours = 12,
                    CreditHours = 3,
                    Jurisdiction = "General",
                    Price = 49m,
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
                    Price = 79m,
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
                    Price = 0m,
                    IsPublished = false,
                    CreatedAt = DateTime.UtcNow
                });

            await dbContext.SaveChangesAsync();
        }

        if (!await dbContext.Modules.AnyAsync())
        {
            dbContext.Modules.AddRange(
            new Module
            {
                Id = Guid.Parse("d4b7f3d1-4d60-4c7f-9190-4d41d1b8f0d1"),
                CourseId = Guid.Parse("64cc1cbb-45fc-4a76-b3b4-de399cb3830f"),
                Title = "Getting Started",
                OrderIndex = 1
            },
            new Module
            {
                Id = Guid.Parse("c3c0f6aa-5c55-40a1-bf33-0edb77f7a901"),
                CourseId = Guid.Parse("5bc4b74b-71ac-40fe-8bc9-1daf5a922782"),
                Title = "Compliance Foundations",
                OrderIndex = 1
            });

            await dbContext.SaveChangesAsync();
        }

        if (!await dbContext.Lessons.AnyAsync())
        {
            dbContext.Lessons.AddRange(
                new Lesson
                {
                    Id = Guid.Parse("a8b6e232-11ad-4c08-8d2d-8a6f9f00a101"),
                    ModuleId = Guid.Parse("d4b7f3d1-4d60-4c7f-9190-4d41d1b8f0d1"),
                    Title = "Welcome",
                    ContentType = "Text",
                    TextContent = WelcomeLessonText,
                    DurationMinutes = 12,
                    OrderIndex = 1,
                    IsRequired = true
                },
                new Lesson
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
                },
                new Lesson
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
                },
                new Lesson
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
                },
                new Lesson
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

            await dbContext.SaveChangesAsync();
        }

        await EnsureAdditionalMockCoursesAsync(dbContext);
        await EnsureWelcomeLessonTitleAsync(dbContext);
        await RemoveLicensingChecklistLessonAsync(dbContext);
        await RemoveCoreLicensingConceptsModuleAsync(dbContext);
        await EnsureRealEstateLicensingMockDataAsync(dbContext);
        await EnsureIntroStructureForAllCoursesAsync(dbContext);
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
                Price = 69m,
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
                Price = 89m,
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
                Price = 74m,
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
        var hasCourse = await dbContext.Courses.AnyAsync(course => course.Id == RealEstateLicensingBasicsCourseId);
        if (!hasCourse)
        {
            return;
        }

        var hasChanges = false;

        var licensingRequirementsModuleId = Guid.Parse("1d7dbd76-2e6f-4f07-8f7d-7260ea61b8a1");
        var agencyAndContractsModuleId = Guid.Parse("623f95fd-2398-4c9e-bb7f-c07ece9e0190");
        var examReadinessModuleId = Guid.Parse("3a4d15bb-284f-4a10-b3e7-53bf5ba0261a");

        if (!await dbContext.Modules.AnyAsync(module => module.Id == licensingRequirementsModuleId))
        {
            dbContext.Modules.Add(new Module
            {
                Id = licensingRequirementsModuleId,
                CourseId = RealEstateLicensingBasicsCourseId,
                Title = "Licensing Requirements and Eligibility",
                OrderIndex = 2
            });
            hasChanges = true;
        }

        if (!await dbContext.Modules.AnyAsync(module => module.Id == agencyAndContractsModuleId))
        {
            dbContext.Modules.Add(new Module
            {
                Id = agencyAndContractsModuleId,
                CourseId = RealEstateLicensingBasicsCourseId,
                Title = "Agency, Disclosure, and Contracts",
                OrderIndex = 3
            });
            hasChanges = true;
        }

        if (!await dbContext.Modules.AnyAsync(module => module.Id == examReadinessModuleId))
        {
            dbContext.Modules.Add(new Module
            {
                Id = examReadinessModuleId,
                CourseId = RealEstateLicensingBasicsCourseId,
                Title = "Exam Readiness and First Transactions",
                OrderIndex = 4
            });
            hasChanges = true;
        }

        var lessonsToSeed = new List<Lesson>
        {
            new()
            {
                Id = Guid.Parse("f9bb3a77-8701-42d8-b8ec-72ecf7f3d6bf"),
                ModuleId = licensingRequirementsModuleId,
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
                ModuleId = licensingRequirementsModuleId,
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
                Id = Guid.Parse("26d5d194-b5d8-4ac4-aabc-2a2710f3cb7c"),
                ModuleId = agencyAndContractsModuleId,
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
                ModuleId = agencyAndContractsModuleId,
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
                Id = Guid.Parse("7e11b8af-edf6-423a-bcdb-feef164d7d39"),
                ModuleId = examReadinessModuleId,
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
                ModuleId = examReadinessModuleId,
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

}