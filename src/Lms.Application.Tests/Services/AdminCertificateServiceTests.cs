using Lms.Application.Data;
using Lms.Application.Services;
using Lms.Domain.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Lms.Application.Tests.Services;

public sealed class AdminCertificateServiceTests
{
    [Fact]
    public async Task Queries_RequireState_FilterBeforePaging_AndReturnStableBoundedPages()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var course = new Course { Title = "Ethics", Slug = "paging-ethics", State = "Florida" };
        db.Courses.Add(course);
        for (var i = 0; i < 45; i++)
        {
            var state = i < 43 ? "Florida" : "North Carolina";
            var user = new UserAccount
            {
                Email = $"person{i:D2}@certificate-dev.invalid", DisplayName = "Same Name",
                LegalName = $"Person {i:D2}", Role = "Learner", IsActive = true,
                State = state, LicenseNumber = $"LICENSE-{i:D2}"
            };
            var enrollment = new Enrollment { UserAccountId = user.Id, CourseId = course.Id };
            db.UserAccounts.Add(user);
            db.Enrollments.Add(enrollment);
            db.CompletionCertificates.Add(new CompletionCertificate
            {
                UserAccountId = user.Id, CourseId = course.Id, EnrollmentId = enrollment.Id,
                CertificateNumber = $"CERT-{i:D2}", VerificationCode = $"CODE-{i:D2}",
                CertificationState = i == 0 ? null : state, CertificationLicenseNumber = user.LicenseNumber,
                IssuedAt = new DateTime(2025, 1, 1), ExpiresAt = DateTime.UtcNow.AddYears(1),
                IsRevoked = i == 1
            });
        }
        await db.SaveChangesAsync();
        var official = new AdminCertificateService(db);
        var samples = new DevelopmentCertificateService(db, new UserAccountService(db, new AuditLogService(db)));
        foreach (var state in new[] { "", "Not a state" })
        {
            Assert.Empty((await official.GetOfficialAsync(state, "", "", "", 1)).Items);
            Assert.Empty((await samples.GetPageAsync(state, "", "", 1)).Items);
        }
        var first = await official.GetOfficialAsync("Florida", "", "", "", 1);
        var second = await official.GetOfficialAsync("Florida", "", "", "", 2);
        var last = await official.GetOfficialAsync("Florida", "", "", "", 99);
        Assert.Equal(43, first.Total);
        Assert.Equal(20, first.Items.Count);
        Assert.Equal(20, second.Items.Count);
        Assert.Equal(3, last.Items.Count);
        Assert.Equal(3, last.Page);
        Assert.Empty(first.Items.Select(item => item.CertificateId).Intersect(second.Items.Select(item => item.CertificateId)));
        Assert.Equal(first.Items, (await official.GetOfficialAsync("Florida", "", "", "", 1)).Items);
        Assert.Equal(2, (await official.GetOfficialAsync("North Carolina", "", "", "", 1)).Total);
        Assert.Single((await official.GetOfficialAsync("Florida", " license-01 ", "Ethics", "Revoked", 1)).Items);
        Assert.Single((await official.GetOfficialAsync("Florida", " cert-00 ", "Ethics", "Active", 1)).Items);
        Assert.Empty((await official.GetOfficialAsync("Florida", "", "Other course", "", 1)).Items);
        Assert.Equal(new[] { "Ethics" }, await official.GetOfficialCoursesAsync("Florida"));

        var sampleFirst = await samples.GetPageAsync("Florida", "", "", 1);
        var sampleSecond = await samples.GetPageAsync("Florida", "", "", 2);
        var sampleLast = await samples.GetPageAsync("Florida", "", "", 99);
        Assert.Equal(43, sampleFirst.Total);
        Assert.Equal(20, sampleFirst.Items.Count);
        Assert.Equal(20, sampleSecond.Items.Count);
        Assert.Equal(3, sampleLast.Items.Count);
        Assert.Empty(sampleFirst.Items.Select(item => item.UserId).Intersect(sampleSecond.Items.Select(item => item.UserId)));
        Assert.Equal(sampleFirst.Items, (await samples.GetPageAsync("Florida", "", "", 1)).Items);
        Assert.Equal(2, (await samples.GetPageAsync("North Carolina", "", "", 1)).Total);
        Assert.Single((await samples.GetPageAsync("Florida", " license-01 ", "Ethics at Work", 1)).Items);
        Assert.Single((await samples.GetPageAsync("Florida", " person 00 ", "", 1)).Items);
        Assert.Single((await samples.GetPageAsync("Florida", sampleFirst.Items[0].Number.ToUpperInvariant(), "", 1)).Items);
        Assert.Empty((await samples.GetPageAsync("Florida", "", "Other course", 1)).Items);
        Assert.Empty((await samples.GetPageAsync("Alabama", "", "", 1)).Items);
        Assert.Equal(43, (await samples.GetCoverageAsync()).Single(item => item.State == "Florida").AccountCount);
    }
}
