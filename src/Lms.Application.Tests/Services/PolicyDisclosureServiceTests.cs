using Lms.Application.Data;
using Lms.Application.Services;
using Lms.Domain.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Lms.Application.Tests.Services;

public class PolicyDisclosureServiceTests
{
    [Fact]
    public async Task GetCheckoutDisclosuresAsync_ReturnsOnlyRegulatedCoursesWithCommissionPublicationStatus()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var previews = await fixture.Service.GetCheckoutDisclosuresAsync(fixture.Learner.Id,
        [
            new PolicyDisclosurePurchaseItem(fixture.RegulatedCourse.Id, 479m),
            new PolicyDisclosurePurchaseItem(fixture.GeneralCourse.Id, 49m)
        ]);

        var preview = Assert.Single(previews);
        Assert.Equal(fixture.RegulatedCourse.Id, preview.CourseId);
        Assert.Equal(479m, preview.TuitionAndFees);
        Assert.Contains("No License Examination Performance Record has yet been published", preview.LicenseExaminationPerformanceRecord);
        Assert.Equal("wapont01@hotmail.com", preview.SupportEmail);
        Assert.Equal("7865530222", preview.SupportTelephone);
        Assert.Contains(preview.SupportEmail, preview.DisclosureText);
        Assert.Contains(preview.SupportTelephone, preview.DisclosureText);
        Assert.Contains("William A. Aponte, Instructor", preview.DisclosureText);
        Assert.Contains("Clara M. Aponte, Instructor", preview.DisclosureText);
        Assert.Contains("EDUCATION PROVIDER CERTIFICATION", preview.DisclosureText);
        Assert.Contains("CERTIFICATION OF TRUTH AND ACCURACY", preview.DisclosureText);
        Assert.Contains("CERTIFICATION OF RECEIPT", preview.DisclosureText);
    }

    [Fact]
    public async Task GetCheckoutDisclosuresAsync_PreservesExamRetakesAndExcludesThemFromContinuingEducation()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var postlicensing = new Course
        {
            Title = "Postlicensing Course",
            Slug = "ppd-postlicensing-course",
            ComplianceType = CourseComplianceTypes.Postlicensing,
            DeliveryMethod = CourseDeliveryMethods.InPerson,
            RequiredInstructionalMinutes = 1800,
            CompletionWindowDays = 180,
            MinimumPassingPercent = 75
        };
        var elective = new Course
        {
            Title = "CE Elective Course",
            Slug = "ppd-ce-elective-course",
            ComplianceType = CourseComplianceTypes.ContinuingEducation,
            ContinuingEducationType = ContinuingEducationTypes.Elective,
            DeliveryMethod = CourseDeliveryMethods.DistanceEducation,
            RequiredInstructionalMinutes = 240,
            CompletionWindowDays = 30
        };
        fixture.DbContext.AddRange(postlicensing, elective);
        await fixture.DbContext.SaveChangesAsync();

        var previews = await fixture.Service.GetCheckoutDisclosuresAsync(fixture.Learner.Id,
        [
            new PolicyDisclosurePurchaseItem(fixture.RegulatedCourse.Id, 479m),
            new PolicyDisclosurePurchaseItem(postlicensing.Id, 199m),
            new PolicyDisclosurePurchaseItem(elective.Id, 99m)
        ]);

        var prelicensingPreview = Assert.Single(previews, preview => preview.CourseId == fixture.RegulatedCourse.Id);
        var postlicensingPreview = Assert.Single(previews, preview => preview.CourseId == postlicensing.Id);
        var electivePreview = Assert.Single(previews, preview => preview.CourseId == elective.Id);
        Assert.Contains("One retake is permitted", prelicensingPreview.RetakePolicy);
        Assert.Contains("without a provider-imposed numerical limit", postlicensingPreview.RetakePolicy);
        Assert.Contains("Not applicable", electivePreview.RetakePolicy);
        Assert.DoesNotContain("Failed examination policy", electivePreview.DisclosureText);
    }

    [Fact]
    public async Task AcknowledgeAsync_UsesAuthenticatedLearnerIdentityBeforePayment()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var items = new[] { new PolicyDisclosurePurchaseItem(fixture.RegulatedCourse.Id, 479m) };

        var ids = await fixture.Service.AcknowledgeAsync(fixture.Learner.Id, items);
        var acknowledgment = await fixture.DbContext.PolicyDisclosureAcknowledgments.SingleAsync(existing => ids.Contains(existing.Id));

        Assert.Null(acknowledgment.PaymentTransactionId);
        Assert.Null(acknowledgment.EnrollmentId);
        Assert.Equal(fixture.Learner.LegalName, acknowledgment.StudentLegalName);
        Assert.Equal($"{fixture.Learner.LegalName} | Authenticated checkbox acknowledgment", acknowledgment.ElectronicSignature);
        Assert.Equal(479m, acknowledgment.TuitionAndFees);
        Assert.Equal(PolicyDisclosureService.DisclosureVersion, acknowledgment.DisclosureVersion);
    }

    [Fact]
    public async Task FinalizePurchaseAsync_LinksAcknowledgmentAndPersistsPerCoursePurchaseLine()
    {
        await using var fixture = await TestFixture.CreateAsync();
        var items = new[] { new PolicyDisclosurePurchaseItem(fixture.RegulatedCourse.Id, 479m) };
        var acknowledgmentIds = await fixture.Service.AcknowledgeAsync(fixture.Learner.Id, items);
        var transaction = new PaymentTransaction
        {
            LearnerId = fixture.Learner.Id,
            Amount = 517.32m,
            Status = "Completed",
            CompletedAt = DateTime.UtcNow
        };
        var enrollment = new Enrollment
        {
            UserAccountId = fixture.Learner.Id,
            CourseId = fixture.RegulatedCourse.Id,
            PaymentTransactionId = transaction.Id
        };
        fixture.DbContext.AddRange(transaction, enrollment);
        await fixture.DbContext.SaveChangesAsync();

        await fixture.Service.FinalizePurchaseAsync(
            fixture.Learner.Id,
            transaction.Id,
            acknowledgmentIds,
            items,
            38.32m,
            0m);

        var acknowledgment = await fixture.DbContext.PolicyDisclosureAcknowledgments.SingleAsync();
        var purchaseLine = await fixture.DbContext.PurchaseLines.SingleAsync();
        Assert.Equal(transaction.Id, acknowledgment.PaymentTransactionId);
        Assert.Equal(enrollment.Id, acknowledgment.EnrollmentId);
        Assert.Equal(479m, purchaseLine.LineSubtotal);
        Assert.Equal(38.32m, purchaseLine.TaxAmount);
        Assert.Equal(517.32m, purchaseLine.LineTotal);
    }

    private sealed class TestFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private TestFixture(SqliteConnection connection, ApplicationDbContext dbContext, PolicyDisclosureService service, UserAccount learner, Course regulatedCourse, Course generalCourse)
        {
            _connection = connection;
            DbContext = dbContext;
            Service = service;
            Learner = learner;
            RegulatedCourse = regulatedCourse;
            GeneralCourse = generalCourse;
        }

        public ApplicationDbContext DbContext { get; }
        public PolicyDisclosureService Service { get; }
        public UserAccount Learner { get; }
        public Course RegulatedCourse { get; }
        public Course GeneralCourse { get; }

        public static async Task<TestFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
            var dbContext = new ApplicationDbContext(options);
            await dbContext.Database.EnsureCreatedAsync();

            var learner = new UserAccount
            {
                Email = "learner@example.com",
                DisplayName = "Learner Example",
                LegalName = "Learner Legal Example",
                Role = "Learner",
                PasswordHash = "test"
            };
            var regulatedCourse = new Course
            {
                Title = "75-Hour Broker Prelicensing",
                Slug = "ppd-regulated-course",
                ComplianceType = CourseComplianceTypes.Prelicensing,
                DeliveryMethod = CourseDeliveryMethods.DistanceEducation,
                CommissionCourseNumber = "NC-PRE-75",
                RequiredInstructionalMinutes = 4500,
                CompletionWindowDays = 180,
                MinimumPassingPercent = 75,
                Price = 479m
            };
            var generalCourse = new Course
            {
                Title = "General Course",
                Slug = "ppd-general-course",
                ComplianceType = CourseComplianceTypes.Unspecified,
                Price = 49m
            };
            dbContext.AddRange(learner, regulatedCourse, generalCourse);
            await dbContext.SaveChangesAsync();
            var auditLogService = new AuditLogService(dbContext);
            return new TestFixture(connection, dbContext, new PolicyDisclosureService(dbContext, new SchoolProfileService(dbContext, auditLogService)), learner, regulatedCourse, generalCourse);
        }

        public async ValueTask DisposeAsync()
        {
            await DbContext.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }
}