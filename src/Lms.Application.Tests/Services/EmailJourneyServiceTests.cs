using System.Text.RegularExpressions;
using Lms.Application.Data;
using Lms.Application.Services;
using Lms.Domain.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Lms.Application.Tests.Services;

public sealed class EmailJourneyServiceTests
{
    [Fact]
    public async Task SignupSelection_ConfirmsOnlyAfterValidEmailVerification_WithoutAdminApproval()
    {
        await using var fixture = await Fixture.CreateAsync();
        var snapshot = new RegulatoryDataSnapshot { State = "Florida", Status = RegulatorySnapshotStatuses.Approved };
        var record = new RegulatoryLicensee
        {
            SnapshotId = snapshot.Id, State = "Florida", Name = "Synthetic licensee",
            LicenseNumber = "SYNTHETIC-1", LicenseType = "Broker"
        };
        fixture.Db.RegulatoryDataSnapshots.Add(snapshot);
        fixture.Db.RegulatoryLicensees.Add(record);
        var pending = new RosterAccountRequest { UserAccountId = fixture.User.Id, RegulatoryLicenseeId = record.Id };
        fixture.Db.RosterAccountRequests.Add(pending);
        await fixture.Db.SaveChangesAsync();
        var id = await fixture.Service.SendVerificationAsync(fixture.User);
        var secret = fixture.VerificationSecret;
        Assert.Null(await fixture.Service.ConsumeAsync(id!.Value, EmailJourneyService.VerifyPurpose, "wrong"));
        Assert.Equal("Pending", (await fixture.Db.RosterAccountRequests.AsNoTracking().SingleAsync()).Status);
        Assert.NotNull(await fixture.Service.ConsumeAsync(id.Value, EmailJourneyService.VerifyPurpose, secret));
        fixture.Db.ChangeTracker.Clear();
        var saved = await fixture.Db.RosterAccountRequests.SingleAsync();
        Assert.Equal("Confirmed", saved.Status);
        Assert.Null(saved.ReviewedByUserId);
        Assert.Null(saved.ReviewedAtUtc);
        Assert.Empty(await fixture.Db.UserRegulatoryLicenseLinks.ToListAsync());
        Assert.Contains(await fixture.Db.AuditLogs.ToListAsync(), item => item.Action == "roster-request.confirmed");
    }

    [Fact]
    public async Task LoginCodeExpiresAtTenMinutes_AndDisablingJourneyDoesNotVerifyIt()
    {
        await using var fixture = await Fixture.CreateAsync();
        var verifyId = await fixture.Service.SendVerificationAsync(fixture.User);
        var user = await fixture.Service.ConsumeAsync(verifyId!.Value, EmailJourneyService.VerifyPurpose, fixture.VerificationSecret);
        var loginId = await fixture.Service.SendLoginCodeAsync(user!);
        var code = fixture.LoginCode;
        fixture.Options.Enabled = false;
        Assert.Null(await fixture.Service.ConsumeAsync(loginId!.Value, EmailJourneyService.LoginPurpose, code));
        fixture.Options.Enabled = true;
        fixture.Clock.Now = fixture.Clock.Now.AddMinutes(10);
        Assert.Null(await fixture.Service.ConsumeAsync(loginId.Value, EmailJourneyService.LoginPurpose, code));
    }

    [Fact]
    public async Task FreeSandboxSendsLinkWithoutUploadingAttachment()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Options.IncludeCertificateAttachment = false;
        var id = await fixture.Service.SendVerificationAsync(fixture.User);
        var user = await fixture.Service.ConsumeAsync(id!.Value, EmailJourneyService.VerifyPurpose, fixture.VerificationSecret);
        Assert.True(await fixture.Service.SendCertificateAsync(user!));
        Assert.Null(fixture.Email.Messages.Last().Attachments);
        Assert.Contains("/auth/email-test/certificate", fixture.Email.Messages.Last().HtmlBody);
    }

    [Fact]
    public async Task VerificationAndCode_AreHashedPurposeBoundAndSingleUse()
    {
        await using var fixture = await Fixture.CreateAsync();
        var id = await fixture.Service.SendVerificationAsync(fixture.User);
        var secret = fixture.VerificationSecret;
        Assert.DoesNotContain(secret, (await fixture.Db.EmailChallenges.SingleAsync()).SecretHash);
        Assert.Null(await fixture.Service.ConsumeAsync(id!.Value, EmailJourneyService.LoginPurpose, secret));
        Assert.Null(await fixture.Service.ConsumeAsync(id.Value, EmailJourneyService.VerifyPurpose, "wrong"));
        var verified = await fixture.Service.ConsumeAsync(id.Value, EmailJourneyService.VerifyPurpose, secret);
        Assert.NotNull(verified?.EmailVerifiedAtUtc);
        Assert.True(verified!.EmailTwoStepEnabled);
        Assert.Null(await fixture.Service.ConsumeAsync(id.Value, EmailJourneyService.VerifyPurpose, secret));
        var codeId = await fixture.Service.SendLoginCodeAsync(verified);
        var code = fixture.LoginCode;
        Assert.Matches("^[0-9]{6}$", code);
        Assert.DoesNotContain(code, (await fixture.Db.EmailChallenges.AsNoTracking().SingleAsync(item => item.Id == codeId)).SecretHash);
        Assert.NotNull(await fixture.Service.ConsumeAsync(codeId!.Value, EmailJourneyService.LoginPurpose, code));
        Assert.Null(await fixture.Service.ConsumeAsync(codeId.Value, EmailJourneyService.LoginPurpose, code));
    }

    [Fact]
    public async Task ExpiryAttemptLimitAndResendInvalidateChallenges()
    {
        await using var fixture = await Fixture.CreateAsync();
        var oldId = await fixture.Service.SendVerificationAsync(fixture.User);
        var oldSecret = fixture.VerificationSecret;
        var newId = await fixture.Service.SendVerificationAsync(fixture.User);
        var secret = fixture.VerificationSecret;
        Assert.Null(await fixture.Service.ConsumeAsync(oldId!.Value, EmailJourneyService.VerifyPurpose, oldSecret));
        for (var i = 0; i < 5; i++)
            Assert.Null(await fixture.Service.ConsumeAsync(newId!.Value, EmailJourneyService.VerifyPurpose, "wrong"));
        Assert.Null(await fixture.Service.ConsumeAsync(newId!.Value, EmailJourneyService.VerifyPurpose, secret));
        var expiredId = await fixture.Service.SendVerificationAsync(fixture.User);
        secret = fixture.VerificationSecret;
        fixture.Clock.Now = fixture.Clock.Now.AddMinutes(31);
        Assert.Null(await fixture.Service.ConsumeAsync(expiredId!.Value, EmailJourneyService.VerifyPurpose, secret));
    }

    [Fact]
    public async Task FailedDeliveryInvalidatesChallenge_AndOtherAccountsAreExcluded()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Email.Succeeds = false;
        Assert.Null(await fixture.Service.SendVerificationAsync(fixture.User));
        var row = await fixture.Db.EmailChallenges.AsNoTracking().SingleAsync();
        Assert.NotNull(row.ConsumedAtUtc);
        Assert.Contains(await fixture.Db.AuditLogs.ToListAsync(), item => item.Action.EndsWith("delivery-failed"));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.SendVerificationAsync(new UserAccount { Email = "other@example.invalid" }));
        Assert.Null(await fixture.Service.GetCertificateAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task DisabledUserCannotConsume_AndSyntheticCertificateCreatesNoCredit()
    {
        await using var fixture = await Fixture.CreateAsync();
        var id = await fixture.Service.SendVerificationAsync(fixture.User);
        fixture.User.IsActive = false;
        await fixture.Db.SaveChangesAsync();
        Assert.Null(await fixture.Service.ConsumeAsync(id!.Value, EmailJourneyService.VerifyPurpose, fixture.VerificationSecret));
        fixture.User.IsActive = true;
        await fixture.Db.SaveChangesAsync();
        var verified = await fixture.Service.ConsumeAsync(id.Value, EmailJourneyService.VerifyPurpose, fixture.VerificationSecret);
        Assert.True(await fixture.Service.SendCertificateAsync(verified!));
        var certificateEmail = fixture.Email.Messages.Last();
        var attachment = Assert.Single(certificateEmail.Attachments!);
        Assert.Equal("application/pdf", attachment.ContentType);
        Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString(attachment.Content, 0, 4));
        Assert.Contains("/auth/email-test/certificate", certificateEmail.HtmlBody);
        Assert.Contains("myfloridalicense.com", certificateEmail.HtmlBody);
        Assert.Empty(await fixture.Db.CompletionCertificates.ToListAsync());
        Assert.Empty(await fixture.Db.Enrollments.ToListAsync());
    }

    [Fact]
    public async Task OnlyConfiguredRegistrationEnablesTwoStep()
    {
        await using var fixture = await Fixture.CreateAsync();
        var accounts = new UserAccountService(fixture.Db, new AuditLogService(fixture.Db), emailJourney: fixture.Options);
        var normal = await accounts.RegisterLearnerAsync("normal@example.invalid", "TestPassword123!");
        Assert.False(normal.EmailTwoStepEnabled);
        fixture.Db.UserAccounts.Remove(fixture.User);
        await fixture.Db.SaveChangesAsync();
        var test = await accounts.RegisterLearnerAsync(fixture.Options.TestRecipient, "TestPassword123!");
        Assert.True(test.EmailTwoStepEnabled);
        Assert.Null(test.EmailVerifiedAtUtc);
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class FakeEmail : IEmailService
    {
        public bool Succeeds { get; set; } = true;
        public List<EmailMessage> Messages { get; } = [];
        public Task<bool> SendAsync(EmailMessage message) { Messages.Add(message); return Task.FromResult(Succeeds); }
        public Task<bool> SendReceiptAsync(string recipientEmail, string invoiceNumber, decimal amount, List<string> courseNames) =>
            throw new NotSupportedException();
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        public ApplicationDbContext Db { get; }
        public UserAccount User { get; }
        public FakeEmail Email { get; } = new();
        public Clock Clock { get; } = new();
        public EmailJourneyOptions Options { get; } = new()
        {
            Enabled = true, TestRecipient = "test@example.invalid", PublicBaseUrl = "http://localhost:5000",
            IncludeCertificateAttachment = true
        };
        public EmailJourneyService Service { get; }
        public string VerificationSecret => Regex.Match(Email.Messages.Last().HtmlBody, "token=([A-F0-9]+)").Groups[1].Value;
        public string LoginCode => Regex.Match(Email.Messages.Last().HtmlBody, "<strong>([0-9]{6})</strong>").Groups[1].Value;
        private Fixture(SqliteConnection connection, ApplicationDbContext db, UserAccount user)
        {
            _connection = connection; Db = db; User = user;
            Service = new(db, Email, Options, new AuditLogService(db), Clock);
        }
        public static async Task<Fixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
            await db.Database.EnsureCreatedAsync();
            var user = new UserAccount { Email = "test@example.invalid", DisplayName = "Synthetic Learner", IsActive = true };
            db.UserAccounts.Add(user);
            await db.SaveChangesAsync();
            return new(connection, db, user);
        }
        public async ValueTask DisposeAsync() { await Db.DisposeAsync(); await _connection.DisposeAsync(); }
    }
}
