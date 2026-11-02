using Lms.Application.Data;
using Lms.Application.Services;
using Lms.Domain.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Lms.Application.Tests.Services;

public sealed class FederatedAccountServiceTests
{
    private const string Issuer = "https://login.microsoftonline.com/9188040d-6c67-4c5b-b112-36a304b66dad/v2.0";

    [Fact]
    public async Task GoogleAndMicrosoftRemainDistinct_AndGoogleCannotClaimExistingEmail()
    {
        await using var f = await Fixture.CreateAsync();
        var microsoft = await f.Service.ResolveAsync(Issuer, "same-subject", "microsoft@example.invalid", "Learner");
        await Assert.ThrowsAsync<FederatedSignInException>(() =>
            f.Service.ResolveAsync("https://accounts.google.com", "same-subject", microsoft.Email, "Learner"));
        var google = await f.Service.ResolveAsync("https://accounts.google.com", "same-subject", "google@example.invalid", null);
        Assert.NotEqual(microsoft.Id, google.Id);
        Assert.Equal("Learner", google.Role);
        Assert.Equal("Guest", google.DisplayName);
        Assert.Equal(google.Id, (await f.Service.ResolveAsync("https://accounts.google.com", "same-subject", null, null)).Id);
        Assert.Equal(2, await f.Db.ExternalIdentities.CountAsync());
        Assert.Null(google.EmailVerifiedAtUtc);
        Assert.Null(google.LicenseNumber);
    }

    [Fact]
    public async Task FirstSignInCreatesLearner_ReturningIdentityUsesStableKeyNotChangedEmail()
    {
        await using var f = await Fixture.CreateAsync();
        var first = await f.Service.ResolveAsync(Issuer, "subject-1", "learner@example.invalid", "Microsoft Learner");
        var returning = await f.Service.ResolveAsync(Issuer, "subject-1", "other@example.invalid", "Changed name");
        Assert.Equal(first.Id, returning.Id);
        Assert.Equal("learner@example.invalid", returning.Email);
        Assert.Equal("Learner", first.Role);
        Assert.Null(first.EmailVerifiedAtUtc);
        Assert.Null(first.LicenseNumber);
        Assert.Single(await f.Db.ExternalIdentities.ToListAsync());
        Assert.Single(await f.Db.UserAccounts.ToListAsync());
        Assert.Empty(await f.Db.UserRegulatoryLicenseLinks.ToListAsync());
        Assert.Empty(await f.Db.Enrollments.ToListAsync());
    }

    [Fact]
    public async Task EmailCollisionCannotLinkOrTakeOverExistingAdmin()
    {
        await using var f = await Fixture.CreateAsync();
        f.Db.UserAccounts.Add(new UserAccount { Email = "admin@example.invalid", Role = "Admin", DisplayName = "Admin" });
        await f.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<FederatedSignInException>(() =>
            f.Service.ResolveAsync(Issuer, "attacker", "ADMIN@example.invalid", "New name"));
        Assert.Empty(await f.Db.ExternalIdentities.ToListAsync());
        Assert.Equal("Admin", (await f.Db.UserAccounts.SingleAsync()).DisplayName);
    }

    [Theory]
    [InlineData("inactive")]
    [InlineData("locked")]
    [InlineData("two-step")]
    [InlineData("reset-required")]
    public async Task ReturningIdentityCannotBypassLocalAccountRestrictions(string restriction)
    {
        await using var f = await Fixture.CreateAsync();
        var user = await f.Service.ResolveAsync(Issuer, "s1", "learner@example.invalid", "Learner");
        switch (restriction)
        {
            case "inactive": user.IsActive = false; break;
            case "locked": user.LockoutEndUtc = DateTime.UtcNow.AddMinutes(10); break;
            case "two-step": user.EmailTwoStepEnabled = true; break;
            case "reset-required": user.ForcePasswordChange = true; break;
        }
        await f.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<FederatedSignInException>(() => f.Service.ResolveAsync(Issuer, "s1", null, null));
    }

    [Fact]
    public async Task SameSubjectFromDifferentIssuerCannotReuseAccount()
    {
        await using var f = await Fixture.CreateAsync();
        await f.Service.ResolveAsync(Issuer, "s1", "learner@example.invalid", "Learner");
        await Assert.ThrowsAsync<FederatedSignInException>(() =>
            f.Service.ResolveAsync("https://another.example.invalid", "s1", "learner@example.invalid", "Learner"));
        Assert.Single(await f.Db.ExternalIdentities.ToListAsync());
    }

    [Theory]
    [InlineData("", "s1", "learner@example.invalid")]
    [InlineData("http://example.invalid", "s1", "learner@example.invalid")]
    [InlineData(Issuer, "", "learner@example.invalid")]
    [InlineData(Issuer, "s1", "not-an-email")]
    [InlineData(Issuer, "s1", null)]
    public async Task InvalidIdentityDoesNotCreateAccount(string issuer, string subject, string? email)
    {
        await using var f = await Fixture.CreateAsync();
        await Assert.ThrowsAsync<FederatedSignInException>(() => f.Service.ResolveAsync(issuer, subject, email, "Learner"));
        Assert.Empty(await f.Db.UserAccounts.ToListAsync());
        Assert.Empty(await f.Db.ExternalIdentities.ToListAsync());
    }

    private sealed class Fixture(SqliteConnection connection, ApplicationDbContext db) : IAsyncDisposable
    {
        public ApplicationDbContext Db { get; } = db;
        public FederatedAccountService Service { get; } = new(db,
            new UserAccountService(db, new AuditLogService(db)), new AuditLogService(db));

        public static async Task<Fixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
            await db.Database.EnsureCreatedAsync();
            return new(connection, db);
        }
        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
