using Lms.Application.Data;
using Lms.Application.Services;
using Lms.Domain.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System.Text;
using Xunit;

namespace Lms.Application.Tests.Services;

public class EnrollmentServiceTests
{
    [Fact]
    public async Task BrokerScopedQueries_ReturnAllActiveLearnersForBrokers()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var brokerOne = await fixture.DbContext.UserAccounts.SingleAsync(user => user.Email == "broker@lms.com");
        var brokerTwo = await fixture.DbContext.UserAccounts.SingleAsync(user => user.Email == "broker2@lms.com");
        var learnerOne = await fixture.DbContext.UserAccounts.SingleAsync(user => user.Email == "learner@lms.com");
        var learnerTwo = await fixture.DbContext.UserAccounts.SingleAsync(user => user.Email == "learner2@lms.com");

        var courseA = await fixture.CreateCourseGraphWithEnrollmentAsync(learnerOne.Id, "Broker One Course");
        var courseB = await fixture.CreateCourseGraphWithEnrollmentAsync(learnerTwo.Id, "Broker Two Course");

        await fixture.PassAssessmentAsync(courseA.CourseId, learnerOne.Id);
        await fixture.CompleteAllRequiredLessonsAsync(learnerOne.Id, courseA.RequiredLessonIds);

        await fixture.PassAssessmentAsync(courseB.CourseId, learnerTwo.Id);
        await fixture.CompleteAllRequiredLessonsAsync(learnerTwo.Id, courseB.RequiredLessonIds);

        var brokerOneRows = await fixture.EnrollmentService.GetBrokerLearnerRowsAsync(brokerOne.Id);
        var brokerTwoRows = await fixture.EnrollmentService.GetBrokerLearnerRowsAsync(brokerTwo.Id);

        Assert.Contains(brokerOneRows, row => row.LearnerEmail == "learner@lms.com");
        Assert.Contains(brokerOneRows, row => row.LearnerEmail == "learner2@lms.com");
        Assert.Contains(brokerTwoRows, row => row.LearnerEmail == "learner@lms.com");
        Assert.Contains(brokerTwoRows, row => row.LearnerEmail == "learner2@lms.com");

        var brokerOneCertificates = await fixture.EnrollmentService.GetCertificateComplianceRowsAsync(brokerOne.Id);
        var brokerTwoCertificates = await fixture.EnrollmentService.GetCertificateComplianceRowsAsync(brokerTwo.Id);

        Assert.Contains(brokerOneCertificates, row => row.LearnerEmail == "learner@lms.com");
        Assert.Contains(brokerOneCertificates, row => row.LearnerEmail == "learner2@lms.com");
        Assert.Contains(brokerTwoCertificates, row => row.LearnerEmail == "learner@lms.com");
        Assert.Contains(brokerTwoCertificates, row => row.LearnerEmail == "learner2@lms.com");
    }

    [Fact]
    public async Task EnrollLearnerByBrokerAsync_DoesNotRequireAssignment_AndCreatesNotification()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var broker = await fixture.DbContext.UserAccounts.SingleAsync(user => user.Email == "broker@lms.com");
        var learner = await fixture.DbContext.UserAccounts.SingleAsync(user => user.Email == "learner@lms.com");

        var course = new Course
        {
            Title = "Lifecycle Enrollment Course",
            Slug = Guid.NewGuid().ToString("N"),
            Description = "Lifecycle test",
            Level = "Beginner",
            DurationHours = 1,
            CreditHours = 1,
            Price = 0,
            IsPublished = true
        };
        fixture.DbContext.Courses.Add(course);
        await fixture.DbContext.SaveChangesAsync();
        fixture.DbContext.Enrollments.Add(new Enrollment
        {
            UserAccountId = broker.Id,
            CourseId = course.Id,
            EnrollmentSource = "LearnerPurchase",
            ConsentStatus = "NotRequired",
            EnrolledAt = DateTime.UtcNow,
            ProgressPercent = 0,
            Completed = false
        });
        await fixture.DbContext.SaveChangesAsync();

        var success = await fixture.EnrollmentService.EnrollLearnerByBrokerAsync(broker.Id, learner.Id, course.Id, broker.Id, broker.Email);
        Assert.True(success.Succeeded);

        var enrollment = await fixture.DbContext.Enrollments
            .AsNoTracking()
            .SingleOrDefaultAsync(existing => existing.UserAccountId == learner.Id && existing.CourseId == course.Id);
        Assert.NotNull(enrollment);

        var notification = await fixture.DbContext.SystemNotifications
            .AsNoTracking()
            .Where(existing => existing.RecipientUserId == learner.Id && existing.Category == "enrollment")
            .OrderByDescending(existing => existing.CreatedAt)
            .FirstOrDefaultAsync();

        Assert.NotNull(notification);
    }

    [Fact]
    public async Task EnrollLearnerByBrokerAsync_Fails_ForLearnerOwnedEnrollment()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var broker = await fixture.DbContext.UserAccounts.SingleAsync(user => user.Email == "broker@lms.com");
        var admin = await fixture.DbContext.UserAccounts.SingleAsync(user => user.Email == "admin@lms.com");
        var learner = await fixture.DbContext.UserAccounts.SingleAsync(user => user.Email == "learner@lms.com");

        await fixture.EnrollmentService.AssignLearnerToBrokerAsync(broker.Id, learner.Id, broker.Id, broker.Email);

        var course = new Course
        {
            Title = "Learner Owned Broker Enrollment Check",
            Slug = Guid.NewGuid().ToString("N"),
            Description = "Ownership guardrail test",
            Level = "Beginner",
            DurationHours = 1,
            CreditHours = 1,
            Price = 0,
            IsPublished = true
        };
        fixture.DbContext.Courses.Add(course);
        await fixture.DbContext.SaveChangesAsync();
        fixture.DbContext.Enrollments.Add(new Enrollment
        {
            UserAccountId = broker.Id,
            CourseId = course.Id,
            EnrollmentSource = "LearnerPurchase",
            ConsentStatus = "NotRequired",
            EnrolledAt = DateTime.UtcNow,
            ProgressPercent = 0,
            Completed = false
        });
        await fixture.DbContext.SaveChangesAsync();

        var adminEnrollment = await fixture.EnrollmentService.AdminEnrollLearnerAsync(
            learner.Id,
            course.Id,
            admin.Id,
            admin.Email,
            "create learner purchase for ownership test");
        Assert.True(adminEnrollment.Succeeded);

        var brokerResult = await fixture.EnrollmentService.EnrollLearnerByBrokerAsync(
            broker.Id,
            learner.Id,
            course.Id,
            broker.Id,
            broker.Email);

        Assert.False(brokerResult.Succeeded);
        Assert.Contains("already enrolled", brokerResult.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BrokerEnrollmentActions_Fail_WhenBrokerIsNotAssignedToLearner()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var broker = await fixture.DbContext.UserAccounts.SingleAsync(user => user.Email == "broker@lms.com");
        var learner = await fixture.DbContext.UserAccounts.SingleAsync(user => user.Email == "learner@lms.com");

        await fixture.EnrollmentService.AssignLearnerToBrokerAsync(broker.Id, learner.Id, broker.Id, broker.Email);

        var sourceCourse = new Course
        {
            Title = "Unassigned Source",
            Slug = Guid.NewGuid().ToString("N"),
            Description = "Source course",
            Level = "Beginner",
            DurationHours = 1,
            CreditHours = 1,
            Price = 0,
            IsPublished = true
        };

        var targetCourse = new Course
        {
            Title = "Unassigned Target",
            Slug = Guid.NewGuid().ToString("N"),
            Description = "Target course",
            Level = "Beginner",
            DurationHours = 1,
            CreditHours = 1,
            Price = 0,
            IsPublished = true
        };

        fixture.DbContext.Courses.AddRange(sourceCourse, targetCourse);
        await fixture.DbContext.SaveChangesAsync();

        var enrollResult = await fixture.EnrollmentService.EnrollLearnerByBrokerAsync(
            broker.Id,
            learner.Id,
            sourceCourse.Id,
            broker.Id,
            broker.Email);

        var unenrollResult = await fixture.EnrollmentService.UnenrollLearnerByBrokerAsync(
            broker.Id,
            learner.Id,
            sourceCourse.Id,
            broker.Id,
            broker.Email);

        var transferResult = await fixture.EnrollmentService.TransferEnrollmentByBrokerAsync(
            broker.Id,
            learner.Id,
            sourceCourse.Id,
            targetCourse.Id,
            broker.Id,
            broker.Email);

        Assert.False(enrollResult.Succeeded);
        Assert.False(unenrollResult.Succeeded);
        Assert.False(transferResult.Succeeded);

        Assert.Contains("not purchased", enrollResult.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("not purchased", unenrollResult.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("not purchased", transferResult.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TransferEnrollmentByBrokerAsync_MovesEnrollmentToTargetCourse()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var broker = await fixture.DbContext.UserAccounts.SingleAsync(user => user.Email == "broker@lms.com");
        var learner = await fixture.DbContext.UserAccounts.SingleAsync(user => user.Email == "learner@lms.com");

        await fixture.EnrollmentService.AssignLearnerToBrokerAsync(broker.Id, learner.Id, broker.Id, broker.Email);

        var fromCourse = new Course
        {
            Title = "Transfer Source",
            Slug = Guid.NewGuid().ToString("N"),
            Description = "Source course",
            Level = "Beginner",
            DurationHours = 1,
            CreditHours = 1,
            Price = 0,
            IsPublished = true
        };

        var toCourse = new Course
        {
            Title = "Transfer Target",
            Slug = Guid.NewGuid().ToString("N"),
            Description = "Target course",
            Level = "Beginner",
            DurationHours = 1,
            CreditHours = 1,
            Price = 0,
            IsPublished = true
        };

        fixture.DbContext.Courses.AddRange(fromCourse, toCourse);
        await fixture.DbContext.SaveChangesAsync();
        fixture.DbContext.Enrollments.Add(new Enrollment
        {
            UserAccountId = broker.Id,
            CourseId = fromCourse.Id,
            EnrollmentSource = "LearnerPurchase",
            ConsentStatus = "NotRequired",
            EnrolledAt = DateTime.UtcNow,
            ProgressPercent = 0,
            Completed = false
        });
        fixture.DbContext.Enrollments.Add(new Enrollment
        {
            UserAccountId = broker.Id,
            CourseId = toCourse.Id,
            EnrollmentSource = "LearnerPurchase",
            ConsentStatus = "NotRequired",
            EnrolledAt = DateTime.UtcNow,
            ProgressPercent = 0,
            Completed = false
        });
        await fixture.DbContext.SaveChangesAsync();

        var enrolled = await fixture.EnrollmentService.EnrollLearnerByBrokerAsync(broker.Id, learner.Id, fromCourse.Id, broker.Id, broker.Email);
        Assert.True(enrolled.Succeeded);

        var result = await fixture.EnrollmentService.TransferEnrollmentByBrokerAsync(
            broker.Id,
            learner.Id,
            fromCourse.Id,
            toCourse.Id,
            broker.Id,
            broker.Email);

        Assert.True(result.Succeeded);
        Assert.Equal("Enrollment transferred.", result.Message);

        var hasSourceEnrollment = await fixture.DbContext.Enrollments
            .AsNoTracking()
            .AnyAsync(existing => existing.UserAccountId == learner.Id && existing.CourseId == fromCourse.Id);
        var hasTargetEnrollment = await fixture.DbContext.Enrollments
            .AsNoTracking()
            .AnyAsync(existing => existing.UserAccountId == learner.Id && existing.CourseId == toCourse.Id);

        Assert.False(hasSourceEnrollment);
        Assert.True(hasTargetEnrollment);
    }

    [Fact]
    public async Task TransferEnrollmentByBrokerAsync_Fails_ForLearnerOwnedEnrollment()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var broker = await fixture.DbContext.UserAccounts.SingleAsync(user => user.Email == "broker@lms.com");
        var learner = await fixture.DbContext.UserAccounts.SingleAsync(user => user.Email == "learner@lms.com");

        await fixture.EnrollmentService.AssignLearnerToBrokerAsync(broker.Id, learner.Id, broker.Id, broker.Email);

        var sourceSeed = await fixture.CreateCourseGraphWithEnrollmentAsync(learner.Id, "Learner Owned Source");

        var targetCourse = new Course
        {
            Title = "Learner Owned Target",
            Slug = Guid.NewGuid().ToString("N"),
            Description = "Target course",
            Level = "Beginner",
            DurationHours = 1,
            CreditHours = 1,
            Price = 0,
            IsPublished = true
        };

        fixture.DbContext.Courses.Add(targetCourse);
        await fixture.DbContext.SaveChangesAsync();
        await fixture.SeedBrokerPurchaseAsync(broker.Id, sourceSeed.CourseId);
        await fixture.SeedBrokerPurchaseAsync(broker.Id, targetCourse.Id);

        var result = await fixture.EnrollmentService.TransferEnrollmentByBrokerAsync(
            broker.Id,
            learner.Id,
            sourceSeed.CourseId,
            targetCourse.Id,
            broker.Id,
            broker.Email);

        Assert.False(result.Succeeded);
        Assert.Contains("not broker enrolled", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UnenrollLearnerByBrokerAsync_Fails_ForLearnerOwnedEnrollment()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var broker = await fixture.DbContext.UserAccounts.SingleAsync(user => user.Email == "broker@lms.com");
        var learner = await fixture.DbContext.UserAccounts.SingleAsync(user => user.Email == "learner@lms.com");

        await fixture.EnrollmentService.AssignLearnerToBrokerAsync(broker.Id, learner.Id, broker.Id, broker.Email);

        var sourceSeed = await fixture.CreateCourseGraphWithEnrollmentAsync(learner.Id, "Learner Owned Enrollment");
        await fixture.SeedBrokerPurchaseAsync(broker.Id, sourceSeed.CourseId);

        var result = await fixture.EnrollmentService.UnenrollLearnerByBrokerAsync(
            broker.Id,
            learner.Id,
            sourceSeed.CourseId,
            broker.Id,
            broker.Email);

        Assert.False(result.Succeeded);
        Assert.Contains("not broker enrolled", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AdminEnrollLearnerAsync_CreatesLearnerOwnedEnrollment()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var admin = await fixture.DbContext.UserAccounts.SingleAsync(user => user.Email == "admin@lms.com");
        var learner = await fixture.DbContext.UserAccounts.SingleAsync(user => user.Email == "learner@lms.com");

        var course = new Course
        {
            Title = "Admin Repair Enrollment",
            Slug = Guid.NewGuid().ToString("N"),
            Description = "Repair course",
            Level = "Beginner",
            DurationHours = 1,
            CreditHours = 1,
            Price = 0,
            IsPublished = true
        };

        fixture.DbContext.Courses.Add(course);
        await fixture.DbContext.SaveChangesAsync();

        var result = await fixture.EnrollmentService.AdminEnrollLearnerAsync(learner.Id, course.Id, admin.Id, admin.Email, "restore missing purchase");

        Assert.True(result.Succeeded);
        Assert.Equal("Enrollment restored.", result.Message);

        var enrollment = await fixture.DbContext.Enrollments
            .AsNoTracking()
            .SingleAsync(existing => existing.UserAccountId == learner.Id && existing.CourseId == course.Id);

        Assert.Equal("LearnerPurchase", enrollment.EnrollmentSource);
        Assert.Null(enrollment.SponsoredByBrokerUserId);
        Assert.Equal("NotRequired", enrollment.ConsentStatus);
    }

    [Fact]
    public async Task AdminTransferEnrollmentAsync_MovesLearnerOwnedEnrollment()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var admin = await fixture.DbContext.UserAccounts.SingleAsync(user => user.Email == "admin@lms.com");
        var learner = await fixture.DbContext.UserAccounts.SingleAsync(user => user.Email == "learner@lms.com");

        var sourceCourse = new Course
        {
            Title = "Admin Transfer Source",
            Slug = Guid.NewGuid().ToString("N"),
            Description = "Source course",
            Level = "Beginner",
            DurationHours = 1,
            CreditHours = 1,
            Price = 0,
            IsPublished = true
        };

        var targetCourse = new Course
        {
            Title = "Admin Transfer Target",
            Slug = Guid.NewGuid().ToString("N"),
            Description = "Target course",
            Level = "Beginner",
            DurationHours = 1,
            CreditHours = 1,
            Price = 0,
            IsPublished = true
        };

        fixture.DbContext.Courses.AddRange(sourceCourse, targetCourse);
        await fixture.DbContext.SaveChangesAsync();

        var createResult = await fixture.EnrollmentService.AdminEnrollLearnerAsync(learner.Id, sourceCourse.Id, admin.Id, admin.Email, "create source enrollment");
        Assert.True(createResult.Succeeded);

        var transferResult = await fixture.EnrollmentService.AdminTransferEnrollmentAsync(learner.Id, sourceCourse.Id, targetCourse.Id, admin.Id, admin.Email, "correct wrong course");

        Assert.True(transferResult.Succeeded);
        Assert.Equal("Enrollment transferred.", transferResult.Message);

        var hasSourceEnrollment = await fixture.DbContext.Enrollments
            .AsNoTracking()
            .AnyAsync(existing => existing.UserAccountId == learner.Id && existing.CourseId == sourceCourse.Id);
        var hasTargetEnrollment = await fixture.DbContext.Enrollments
            .AsNoTracking()
            .AnyAsync(existing => existing.UserAccountId == learner.Id && existing.CourseId == targetCourse.Id);

        Assert.False(hasSourceEnrollment);
        Assert.True(hasTargetEnrollment);
    }

    [Fact]
    public async Task TransferEnrollmentByBrokerAsync_Fails_WhenSourceEnrollmentMissing_AndLeavesTargetUnchanged()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var broker = await fixture.DbContext.UserAccounts.SingleAsync(user => user.Email == "broker@lms.com");
        var learner = await fixture.DbContext.UserAccounts.SingleAsync(user => user.Email == "learner@lms.com");

        await fixture.EnrollmentService.AssignLearnerToBrokerAsync(broker.Id, learner.Id, broker.Id, broker.Email);

        var fromCourse = new Course
        {
            Title = "Missing Source",
            Slug = Guid.NewGuid().ToString("N"),
            Description = "Missing source course",
            Level = "Beginner",
            DurationHours = 1,
            CreditHours = 1,
            Price = 0,
            IsPublished = true
        };

        var toCourse = new Course
        {
            Title = "Target Without Side Effects",
            Slug = Guid.NewGuid().ToString("N"),
            Description = "Target course",
            Level = "Beginner",
            DurationHours = 1,
            CreditHours = 1,
            Price = 0,
            IsPublished = true
        };

        fixture.DbContext.Courses.AddRange(fromCourse, toCourse);
        await fixture.DbContext.SaveChangesAsync();
        await fixture.SeedBrokerPurchaseAsync(broker.Id, fromCourse.Id);
        await fixture.SeedBrokerPurchaseAsync(broker.Id, toCourse.Id);

        var result = await fixture.EnrollmentService.TransferEnrollmentByBrokerAsync(
            broker.Id,
            learner.Id,
            fromCourse.Id,
            toCourse.Id,
            broker.Id,
            broker.Email);

        Assert.False(result.Succeeded);
        Assert.Contains("Transfer source enrollment not found", result.Message, StringComparison.OrdinalIgnoreCase);

        var hasTargetEnrollment = await fixture.DbContext.Enrollments
            .AsNoTracking()
            .AnyAsync(existing => existing.UserAccountId == learner.Id && existing.CourseId == toCourse.Id);

        Assert.False(hasTargetEnrollment);
    }

    [Fact]
    public async Task BrokerEnrollmentActions_Fail_WhenCourseNotPurchasedByBroker()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var broker = await fixture.DbContext.UserAccounts.SingleAsync(user => user.Email == "broker@lms.com");
        var learner = await fixture.DbContext.UserAccounts.SingleAsync(user => user.Email == "learner@lms.com");

        await fixture.EnrollmentService.AssignLearnerToBrokerAsync(broker.Id, learner.Id, broker.Id, broker.Email);

        var course = new Course
        {
            Title = "Unpurchased Broker Course",
            Slug = Guid.NewGuid().ToString("N"),
            Description = "Broker purchase guardrail",
            Level = "Beginner",
            DurationHours = 1,
            CreditHours = 1,
            Price = 0,
            IsPublished = true
        };

        fixture.DbContext.Courses.Add(course);
        await fixture.DbContext.SaveChangesAsync();

        var enrollResult = await fixture.EnrollmentService.EnrollLearnerByBrokerAsync(
            broker.Id,
            learner.Id,
            course.Id,
            broker.Id,
            broker.Email);

        Assert.False(enrollResult.Succeeded);
        Assert.Contains("not purchased", enrollResult.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SetLessonCompletionAsync_IssuesAndRevokesCertificateAsProgressChanges()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var learner = await fixture.DbContext.UserAccounts.SingleAsync(user => user.Email == "learner@lms.com");
        var seed = await fixture.CreateCourseGraphWithEnrollmentAsync(learner.Id, "Compliance Basics");

        await fixture.EnrollmentService.SetLessonCompletionAsync(learner.Id, seed.RequiredLessonIds[0], true);
        await fixture.EnrollmentService.SetLessonCompletionAsync(learner.Id, seed.RequiredLessonIds[1], true);

        var issuedCertificate = await fixture.DbContext.CompletionCertificates
            .AsNoTracking()
            .SingleOrDefaultAsync(certificate => certificate.EnrollmentId == seed.EnrollmentId);

        Assert.NotNull(issuedCertificate);
        Assert.False(issuedCertificate!.IsRevoked);
        Assert.True(issuedCertificate.ExpiresAt > issuedCertificate.IssuedAt);
        Assert.Equal(64, issuedCertificate.VerificationCode.Length);

        await fixture.EnrollmentService.SetLessonCompletionAsync(learner.Id, seed.RequiredLessonIds[0], false);

        var revokedCertificate = await fixture.DbContext.CompletionCertificates
            .AsNoTracking()
            .SingleAsync(certificate => certificate.EnrollmentId == seed.EnrollmentId);

        Assert.True(revokedCertificate.IsRevoked);
        Assert.NotNull(revokedCertificate.RevokedAt);

        var enrollment = await fixture.DbContext.Enrollments
            .AsNoTracking()
            .SingleAsync(existing => existing.Id == seed.EnrollmentId);

        Assert.False(enrollment.Completed);
        Assert.True(enrollment.ProgressPercent < 100m);
    }

    [Fact]
    public async Task GetCertificateComplianceSummaryAsync_ReturnsAccurateStatusCounts()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var learner = await fixture.DbContext.UserAccounts.SingleAsync(user => user.Email == "learner@lms.com");

        var activeSeed = await fixture.CreateCourseGraphWithEnrollmentAsync(learner.Id, "Active Certificate Course");
        await fixture.PassAssessmentAsync(activeSeed.CourseId, learner.Id);
        await fixture.CompleteAllRequiredLessonsAsync(learner.Id, activeSeed.RequiredLessonIds);

        var expiredSeed = await fixture.CreateCourseGraphWithEnrollmentAsync(learner.Id, "Expired Certificate Course");
        await fixture.PassAssessmentAsync(expiredSeed.CourseId, learner.Id);
        await fixture.CompleteAllRequiredLessonsAsync(learner.Id, expiredSeed.RequiredLessonIds);

        var revokedSeed = await fixture.CreateCourseGraphWithEnrollmentAsync(learner.Id, "Revoked Certificate Course");
        await fixture.PassAssessmentAsync(revokedSeed.CourseId, learner.Id);
        await fixture.CompleteAllRequiredLessonsAsync(learner.Id, revokedSeed.RequiredLessonIds);

        var activeCertificate = await fixture.DbContext.CompletionCertificates.SingleAsync(certificate => certificate.EnrollmentId == activeSeed.EnrollmentId);
        activeCertificate.ExpiresAt = DateTime.UtcNow.AddDays(120);

        var expiredCertificate = await fixture.DbContext.CompletionCertificates.SingleAsync(certificate => certificate.EnrollmentId == expiredSeed.EnrollmentId);
        expiredCertificate.ExpiresAt = DateTime.UtcNow.AddDays(-2);

        var revokedCertificate = await fixture.DbContext.CompletionCertificates.SingleAsync(certificate => certificate.EnrollmentId == revokedSeed.EnrollmentId);
        revokedCertificate.IsRevoked = true;
        revokedCertificate.RevokedAt = DateTime.UtcNow;
        revokedCertificate.RevocationReason = "Manual compliance rollback";

        await fixture.DbContext.SaveChangesAsync();

        var summary = await fixture.EnrollmentService.GetCertificateComplianceSummaryAsync();
        var rows = await fixture.EnrollmentService.GetCertificateComplianceRowsAsync();

        Assert.Equal(3, summary.TotalCertificates);
        Assert.Equal(1, summary.ActiveCertificates);
        Assert.Equal(0, summary.ExpiringSoonCertificates);
        Assert.Equal(1, summary.ExpiredCertificates);
        Assert.Equal(1, summary.RevokedCertificates);

        Assert.Equal(3, rows.Count);
        Assert.Contains(rows, row => row.Status == "Active");
        Assert.Contains(rows, row => row.Status == "Expired");
        Assert.Contains(rows, row => row.Status == "Revoked");
    }

    [Fact]
    public async Task RenewCertificateAsync_ClearsRevocationAndRotatesVerificationCode()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var learner = await fixture.DbContext.UserAccounts.SingleAsync(user => user.Email == "learner@lms.com");
        var admin = await fixture.DbContext.UserAccounts.SingleAsync(user => user.Email == "admin@lms.com");
        var seed = await fixture.CreateCourseGraphWithEnrollmentAsync(learner.Id, "Renewal Course");

        await fixture.PassAssessmentAsync(seed.CourseId, learner.Id);
        await fixture.CompleteAllRequiredLessonsAsync(learner.Id, seed.RequiredLessonIds);

        var certificate = await fixture.DbContext.CompletionCertificates.SingleAsync(existing => existing.EnrollmentId == seed.EnrollmentId);
        var originalVerification = certificate.VerificationCode;

        certificate.IsRevoked = true;
        certificate.RevokedAt = DateTime.UtcNow;
        certificate.RevocationReason = "Manual hold";
        certificate.ExpiresAt = DateTime.UtcNow.AddDays(-1);
        await fixture.DbContext.SaveChangesAsync();

        var renewed = await fixture.EnrollmentService.RenewCertificateAsync(certificate.Id, admin.Id, admin.Email);
        Assert.True(renewed);

        var refreshed = await fixture.DbContext.CompletionCertificates.AsNoTracking().SingleAsync(existing => existing.Id == certificate.Id);
        Assert.False(refreshed.IsRevoked);
        Assert.Null(refreshed.RevokedAt);
        Assert.Null(refreshed.RevocationReason);
        Assert.True(refreshed.ExpiresAt > DateTime.UtcNow);
        Assert.NotEqual(originalVerification, refreshed.VerificationCode);
    }

    [Fact]
    public async Task GenerateCertificatePdfAsync_RespectsAccessControl()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var learner = await fixture.DbContext.UserAccounts.SingleAsync(user => user.Email == "learner@lms.com");
        var instructor = await fixture.DbContext.UserAccounts.SingleAsync(user => user.Email == "instructor@lms.com");
        var seed = await fixture.CreateCourseGraphWithEnrollmentAsync(learner.Id, "PDF Access Course");

        await fixture.PassAssessmentAsync(seed.CourseId, learner.Id);
        await fixture.CompleteAllRequiredLessonsAsync(learner.Id, seed.RequiredLessonIds);

        var certificate = await fixture.DbContext.CompletionCertificates.AsNoTracking().SingleAsync(existing => existing.EnrollmentId == seed.EnrollmentId);

        var learnerPdf = await fixture.EnrollmentService.GenerateCertificatePdfAsync(certificate.Id, learner.Id, isPrivileged: false);
        Assert.NotNull(learnerPdf);
        Assert.StartsWith("%PDF", Encoding.ASCII.GetString(learnerPdf!));

        var unauthorizedPdf = await fixture.EnrollmentService.GenerateCertificatePdfAsync(certificate.Id, instructor.Id, isPrivileged: false);
        Assert.Null(unauthorizedPdf);
    }

    [Fact]
    public async Task SetLessonCompletionAsync_DoesNotIssueCertificateBeforeCompletionOrAssessmentPass()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var learner = await fixture.DbContext.UserAccounts.SingleAsync(user => user.Email == "learner@lms.com");
        var seed = await fixture.CreateCourseGraphWithEnrollmentAsync(learner.Id, "Assessment Gate Course");

        await fixture.EnrollmentService.SetLessonCompletionAsync(learner.Id, seed.RequiredLessonIds[0], true);

        var beforePass = await fixture.DbContext.CompletionCertificates
            .AsNoTracking()
            .SingleOrDefaultAsync(certificate => certificate.EnrollmentId == seed.EnrollmentId);

        Assert.Null(beforePass);

        await fixture.PassAssessmentAsync(seed.CourseId, learner.Id);
        await fixture.EnrollmentService.SetLessonCompletionAsync(learner.Id, seed.RequiredLessonIds[1], true);

        var afterPass = await fixture.DbContext.CompletionCertificates
            .AsNoTracking()
            .SingleOrDefaultAsync(certificate => certificate.EnrollmentId == seed.EnrollmentId);

        Assert.NotNull(afterPass);
        Assert.False(afterPass!.IsRevoked);
    }

    private sealed class TestFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        public ApplicationDbContext DbContext { get; }
        public IEnrollmentService EnrollmentService { get; }
        public IAssessmentService AssessmentService { get; }

        private TestFixture(SqliteConnection connection, ApplicationDbContext dbContext, IEnrollmentService enrollmentService, IAssessmentService assessmentService)
        {
            _connection = connection;
            DbContext = dbContext;
            EnrollmentService = enrollmentService;
            AssessmentService = assessmentService;
        }

        public static async Task<TestFixture> CreateAsync()
        {
            var connection = new SqliteConnection("DataSource=:memory:");
            await connection.OpenAsync();

            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlite(connection)
                .Options;

            var dbContext = new ApplicationDbContext(options);
            await dbContext.Database.EnsureCreatedAsync();

            var auditLogService = new AuditLogService(dbContext);
            var userAccountService = new UserAccountService(dbContext, auditLogService);
            await userAccountService.EnsureSeedUsersAsync();

            var assessmentService = new AssessmentService(dbContext, auditLogService);
            var enrollmentService = new EnrollmentService(dbContext, auditLogService, assessmentService);
            return new TestFixture(connection, dbContext, enrollmentService, assessmentService);
        }

        public async Task<(Guid CourseId, Guid EnrollmentId, List<Guid> RequiredLessonIds)> CreateCourseGraphWithEnrollmentAsync(Guid learnerId, string title)
        {
            var course = new Course
            {
                Title = title,
                Slug = Guid.NewGuid().ToString("N"),
                Description = "Test course",
                Level = "Beginner",
                DurationHours = 2,
                CreditHours = 2,
                Price = 0,
                IsPublished = true
            };

            DbContext.Courses.Add(course);

            var module = new Module
            {
                Course = course,
                Title = "Module 1",
                OrderIndex = 1
            };

            DbContext.Modules.Add(module);

            var lessonA = new Lesson
            {
                Module = module,
                Title = "Lesson A",
                ContentType = "Text",
                TextContent = "A",
                DurationMinutes = 10,
                OrderIndex = 1,
                IsRequired = true
            };

            var lessonB = new Lesson
            {
                Module = module,
                Title = "Lesson B",
                ContentType = "Text",
                TextContent = "B",
                DurationMinutes = 10,
                OrderIndex = 2,
                IsRequired = true
            };

            DbContext.Lessons.AddRange(lessonA, lessonB);

            var enrollment = new Enrollment
            {
                UserAccountId = learnerId,
                Course = course,
                EnrollmentSource = "LearnerPurchase",
                ConsentStatus = "NotRequired",
                ProgressPercent = 0,
                Completed = false,
                EnrolledAt = DateTime.UtcNow
            };

            DbContext.Enrollments.Add(enrollment);
            await DbContext.SaveChangesAsync();

            await AssessmentService.EnsureDefaultAssessmentForCourseAsync(course.Id);

            return (course.Id, enrollment.Id, [lessonA.Id, lessonB.Id]);
        }

        public async Task SeedBrokerPurchaseAsync(Guid brokerUserId, Guid courseId)
        {
            var exists = await DbContext.Enrollments
                .AsNoTracking()
                .AnyAsync(existing =>
                    existing.UserAccountId == brokerUserId &&
                    existing.CourseId == courseId &&
                    existing.EnrollmentSource == "LearnerPurchase");

            if (exists)
            {
                return;
            }

            DbContext.Enrollments.Add(new Enrollment
            {
                UserAccountId = brokerUserId,
                CourseId = courseId,
                EnrollmentSource = "LearnerPurchase",
                ConsentStatus = "NotRequired",
                SponsoredByBrokerUserId = null,
                EnrolledAt = DateTime.UtcNow,
                ProgressPercent = 0,
                Completed = false
            });

            await DbContext.SaveChangesAsync();
        }

        public async Task CompleteAllRequiredLessonsAsync(Guid learnerId, List<Guid> lessonIds)
        {
            foreach (var lessonId in lessonIds)
            {
                await EnrollmentService.SetLessonCompletionAsync(learnerId, lessonId, true);
            }
        }

        public async Task PassAssessmentAsync(Guid courseId, Guid learnerId)
        {
            await AssessmentService.EnsureDefaultAssessmentForCourseAsync(courseId);

            var assessment = await DbContext.CourseAssessments
                .Include(existing => existing.Questions)
                .FirstAsync(existing => existing.CourseId == courseId && existing.IsRequired);

            var answers = assessment.Questions.ToDictionary(question => question.Id, question => question.CorrectOption);
            await AssessmentService.SubmitAttemptAsync(courseId, learnerId, answers);
        }

        public async ValueTask DisposeAsync()
        {
            await DbContext.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }
}
