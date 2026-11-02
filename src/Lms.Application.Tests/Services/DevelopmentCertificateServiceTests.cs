using Lms.Application.Data;
using Lms.Application.Services;
using Lms.Domain.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Lms.Application.Tests.Services;

public sealed class DevelopmentCertificateServiceTests
{
    [Fact]
    public async Task Initialize_CreatesAvailableStateLoginsAndPdfs_Idempotently_WithoutOfficialCredit()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        foreach (var state in new[] { "Florida", "North Carolina", "Alaska", "California" })
        {
            var snapshot = new RegulatoryDataSnapshot { State = state };
            db.RegulatoryDataSnapshots.Add(snapshot);
            for (var i = 1; i <= 3; i++)
            {
                db.RegulatoryLicensees.Add(new RegulatoryLicensee
                {
                    SnapshotId = snapshot.Id, State = state, Name = $"Roster Person {i}",
                    LicenseNumber = $"{state}-{i}", LicenseType = "Broker",
                    LicenseStatus = i == 3 ? "Inactive" : state == "California" ? "Licensed" : "Active"
                });
            }
            db.RegulatoryLicensees.Add(new RegulatoryLicensee
            {
                SnapshotId = snapshot.Id, State = state, Name = "AAA Company", LicenseNumber = "COMPANY",
                LicenseType = "Corporation", LicenseStatus = "Active"
            });
            db.RegulatoryLicensees.Add(new RegulatoryLicensee
            {
                SnapshotId = snapshot.Id, State = state, Name = "AAA Restricted", LicenseNumber = "RESTRICTED",
                LicenseType = "Broker", LicenseStatus = "Active", IsRestricted = true
            });
        }
        var partialSnapshot = new RegulatoryDataSnapshot { State = "Virginia" };
        db.RegulatoryDataSnapshots.Add(partialSnapshot);
        db.RegulatoryLicensees.Add(new RegulatoryLicensee
        {
            SnapshotId = partialSnapshot.Id, State = "Virginia", Name = "Single Person", LicenseNumber = "VA1",
            LicenseType = "Real Estate Individual", LicenseStatus = "Current"
        });
        var inactiveSnapshot = new RegulatoryDataSnapshot { State = "Wyoming" };
        db.RegulatoryDataSnapshots.Add(inactiveSnapshot);
        db.RegulatoryLicensees.Add(new RegulatoryLicensee
        {
            SnapshotId = inactiveSnapshot.Id, State = "Wyoming", Name = "Inactive Person", LicenseNumber = "WY1",
            LicenseType = "Broker", LicenseStatus = "Inactive"
        });
        await db.SaveChangesAsync();
        var users = new UserAccountService(db, new AuditLogService(db));
        var service = new DevelopmentCertificateService(db, users);
        var directory = Path.Combine(Path.GetTempPath(), $"lms-dev-certificates-{Guid.NewGuid():N}");
        try
        {
            var password = $"Test-{Guid.NewGuid():N}!8";
            await service.InitializeAsync(password, directory);
            var first = await service.GetAsync(null, true);
            Assert.Equal(9, first.Count);
            var coverage = await service.GetCoverageAsync();
            Assert.Equal(50, coverage.Count);
            Assert.Equal(4, coverage.Count(state => state.Status == "Ready"));
            Assert.Contains(coverage, state => state.State == "Virginia" && state.AccountCount == 1 && state.Status.StartsWith("Partial:"));
            Assert.Contains(coverage, state => state.State == "Alabama" && state.Status == "Skipped: no roster records in database");
            Assert.Contains(coverage, state => state.State == "Wyoming" && state.Status == "Skipped: no usable active individual roster records");
            Assert.True(File.Exists(Path.Combine(directory, "coverage.json")));
            Assert.All(first, item =>
            {
                Assert.StartsWith("DEV-", item.Number);
                Assert.EndsWith("@certificate-dev.invalid", item.Email);
                Assert.Contains(item.State.Replace(' ', '-'), item.FileName);
                Assert.DoesNotContain(":", item.FileName);
                var pdf = File.ReadAllBytes(Path.Combine(directory, item.FileName));
                Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));
                Assert.True(pdf.Length > 1000);
            });
            await service.InitializeAsync(null, directory);
            Assert.Equal(first.Select(item => item.Number), (await service.GetAsync(null, true)).Select(item => item.Number));
            Assert.Equal(9, await db.UserAccounts.CountAsync());
            foreach (var certificate in first)
            {
                Assert.Equal(LoginStatus.Succeeded, (await users.AuthenticateAsync(certificate.Email, password)).Status);
                Assert.Single(await service.GetAsync(certificate.UserId, false));
            }
            Assert.Empty(await service.GetAsync(Guid.NewGuid(), false));
            Assert.Empty(await service.GetAsync(null, false));
            Assert.All(await db.UserAccounts.ToListAsync(), user =>
            {
                Assert.Equal("Learner", user.Role);
                Assert.NotNull(user.MarketingEmailOptOutAtUtc);
                Assert.NotNull(user.LicenseNumber);
            });
            Assert.Equal(22, await db.RegulatoryLicensees.CountAsync());
            Assert.Empty(await db.CompletionCertificates.ToListAsync());
            Assert.Empty(await db.Enrollments.ToListAsync());
            Assert.Empty(await db.ExamAttempts.ToListAsync());
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                foreach (var file in Directory.GetFiles(directory, "DEV-*.pdf"))
                {
                    File.Delete(file);
                }
                foreach (var file in Directory.GetFiles(directory, "DEV_*.pdf"))
                {
                    File.Delete(file);
                }
                File.Delete(Path.Combine(directory, "coverage.json"));
                Directory.Delete(directory);
            }
        }
    }
}
