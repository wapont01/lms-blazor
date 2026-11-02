using System.Net;
using System.Text.RegularExpressions;
using Lms.Application.Data;
using Lms.Application.Services;
using Lms.Domain.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Lms.Application.Tests.Services;

public sealed class PasswordRecoveryServiceTests
{
    [Fact]
    public async Task ResetIsHashedSingleUse_AndOnlyNewPasswordAuthenticates()
    {
        await using var f = await Fixture.CreateAsync();
        await f.Service.RequestAsync(" RECOVERY@example.invalid ");
        var (id, token) = f.Link;
        var challenge = await f.Db.EmailChallenges.AsNoTracking().SingleAsync();
        Assert.NotEqual(token, challenge.SecretHash);
        Assert.Equal(64, token.Length);
        Assert.Equal(f.Clock.Now.UtcDateTime.AddMinutes(30), challenge.ExpiresAtUtc);
        Assert.True(await f.Service.ResetAsync(id, token, " abcdef "));
        Assert.False(await f.Service.ResetAsync(id, token, "replayed"));
        f.Db.ChangeTracker.Clear();
        Assert.Equal(LoginStatus.InvalidCredentials, (await f.Accounts.AuthenticateAsync(f.User.Email, "oldpass1")).Status);
        Assert.Equal(LoginStatus.Succeeded, (await f.Accounts.AuthenticateAsync(f.User.Email, " abcdef ")).Status);
        Assert.Equal(LoginStatus.InvalidCredentials, (await f.Accounts.AuthenticateAsync(f.User.Email, "abcdef")).Status);
        var user = await f.Db.UserAccounts.SingleAsync();
        Assert.Equal(f.User.Id, user.Id);
        Assert.Equal(f.User.Role, user.Role);
        Assert.False(user.ForcePasswordChange);
        Assert.Contains(await f.Db.AuditLogs.ToListAsync(), item => item.Action == "auth.password-reset.completed");
    }

    [Fact]
    public async Task ExpiredTokensAreRejectedAtThirtyMinutes()
    {
        await using var f = await Fixture.CreateAsync();
        await f.Service.RequestAsync(f.User.Email);
        var (id, token) = f.Link;
        f.Clock.Now = f.Clock.Now.AddMinutes(30);
        Assert.False(await f.Service.ResetAsync(id, token, "newpass1"));
    }

    [Fact]
    public async Task ResendInvalidatesPreviousLink_AndCooldownPreventsMailFlooding()
    {
        await using var f = await Fixture.CreateAsync();
        await f.Service.RequestAsync(f.User.Email);
        var old = f.Link;
        await f.Service.RequestAsync(f.User.Email);
        Assert.Single(f.Email.Messages);
        f.Clock.Now = f.Clock.Now.AddMinutes(1);
        await f.Service.RequestAsync(f.User.Email);
        Assert.Equal(2, f.Email.Messages.Count);
        var current = f.Link;
        Assert.False(await f.Service.ResetAsync(old.Id, old.Token, "newpass1"));
        Assert.True(await f.Service.ResetAsync(current.Id, current.Token, "newpass1"));
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("disabled")]
    [InlineData("federated")]
    public async Task IneligibleAccountsReceiveNoEmail(string scenario)
    {
        await using var f = await Fixture.CreateAsync();
        if (scenario == "disabled") f.User.IsActive = false;
        if (scenario == "federated") f.Db.ExternalIdentities.Add(new ExternalIdentity
        {
            UserAccountId = f.User.Id, Issuer = "https://example.invalid", Subject = "synthetic"
        });
        await f.Db.SaveChangesAsync();
        await f.Service.RequestAsync(scenario == "unknown" ? "missing@example.invalid" : f.User.Email);
        Assert.Empty(f.Email.Messages);
        Assert.Empty(await f.Db.EmailChallenges.ToListAsync());
    }

    [Theory]
    [InlineData("password")]
    [InlineData("email")]
    [InlineData("disabled")]
    [InlineData("purpose")]
    public async Task ChangedAccountOrPurposeInvalidatesLink(string change)
    {
        await using var f = await Fixture.CreateAsync();
        await f.Service.RequestAsync(f.User.Email);
        var link = f.Link;
        if (change == "password") await f.Accounts.ChangePasswordAsync(f.User.Id, "oldpass1", "changed1");
        if (change == "email") f.User.Email = "changed@example.invalid";
        if (change == "disabled") f.User.IsActive = false;
        if (change == "purpose") (await f.Db.EmailChallenges.SingleAsync()).Purpose = EmailJourneyService.VerifyPurpose;
        await f.Db.SaveChangesAsync();
        Assert.False(await f.Service.ResetAsync(link.Id, link.Token, "newpass1"));
    }

    [Fact]
    public async Task WrongTokenAttemptLimitAndUnknownIdsAreRejected()
    {
        await using var f = await Fixture.CreateAsync();
        await f.Service.RequestAsync(f.User.Email);
        var (id, token) = f.Link;
        Assert.False(await f.Service.ResetAsync(Guid.NewGuid(), token, "newpass1"));
        for (var i = 0; i < 5; i++)
            Assert.False(await f.Service.ResetAsync(id, new string('X', 64), "newpass1"));
        Assert.False(await f.Service.ResetAsync(id, token, "newpass1"));
    }

    [Fact]
    public async Task DeliveryFailureRevokesToken()
    {
        await using var f = await Fixture.CreateAsync();
        f.Email.Succeeds = false;
        await f.Service.RequestAsync(f.User.Email);
        Assert.NotNull((await f.Db.EmailChallenges.AsNoTracking().SingleAsync()).ConsumedAtUtc);
        Assert.False(await f.Service.ResetAsync(f.Link.Id, f.Link.Token, "newpass1"));
        Assert.Contains(await f.Db.AuditLogs.ToListAsync(), item => item.Action == "auth.password-reset.delivery-failed");
    }

    [Theory]
    [InlineData("abcdefg")]
    [InlineData("        ")]
    public async Task InvalidPasswordsDoNotConsumeLink(string password)
    {
        await using var f = await Fixture.CreateAsync();
        await f.Service.RequestAsync(f.User.Email);
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.ResetAsync(f.Link.Id, f.Link.Token, password));
        Assert.True(await f.Service.ResetAsync(f.Link.Id, f.Link.Token, "newpass1"));
    }

    [Fact]
    public async Task RecoveryClearsPasswordLockoutButPreservesTwoStepAndUnverifiedEmail()
    {
        await using var f = await Fixture.CreateAsync();
        f.User.LockoutEndUtc = DateTime.UtcNow.AddHours(1);
        f.User.FailedLoginCount = 4;
        f.User.ForcePasswordChange = true;
        f.User.EmailTwoStepEnabled = true;
        await f.Db.SaveChangesAsync();
        await f.Service.RequestAsync(f.User.Email);
        Assert.True(await f.Service.ResetAsync(f.Link.Id, f.Link.Token, "newpass1"));
        var user = await f.Db.UserAccounts.AsNoTracking().SingleAsync();
        Assert.Null(user.LockoutEndUtc);
        Assert.Equal(0, user.FailedLoginCount);
        Assert.True(user.EmailTwoStepEnabled);
        Assert.Null(user.EmailVerifiedAtUtc);
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
        public UserAccountService Accounts { get; }
        public PasswordRecoveryService Service { get; }
        public (Guid Id, string Token) Link
        {
            get
            {
                var body = WebUtility.HtmlDecode(Email.Messages.Last().HtmlBody);
                return (Guid.Parse(Regex.Match(body, "id=([^&]+)").Groups[1].Value),
                    Regex.Match(body, "token=([A-F0-9]+)").Groups[1].Value);
            }
        }

        private Fixture(SqliteConnection connection, ApplicationDbContext db, UserAccount user, UserAccountService accounts)
        {
            _connection = connection; Db = db; User = user; Accounts = accounts;
            Service = new(db, Email, new PasswordRecoveryOptions { Enabled = true, PublicBaseUrl = "https://lms.example.invalid" },
                new AuditLogService(db), Clock, NullLogger<PasswordRecoveryService>.Instance);
        }

        public static async Task<Fixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
            await db.Database.EnsureCreatedAsync();
            var accounts = new UserAccountService(db, new AuditLogService(db));
            var user = await accounts.RegisterLearnerAsync("recovery@example.invalid", "oldpass1");
            return new(connection, db, user, accounts);
        }

        public async ValueTask DisposeAsync() { await Db.DisposeAsync(); await _connection.DisposeAsync(); }
    }
}
