using Lms.Application.Data;
using Lms.Application.Services;
using Lms.Domain.Entities;
using Lms.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Lms.Web.UITests.Tests;

public sealed class FloridaBulkAlertServiceTests
{
    [Theory]
    [InlineData("Florida", "America/New_York", "2026-11-02 05:00")]
    [InlineData("Texas", "America/Chicago", "2026-11-02 04:00")]
    [InlineData("California", "America/Los_Angeles", "2026-11-02 02:00")]
    [InlineData("Colorado", "America/Denver", "2026-11-02 03:00")]
    [InlineData("Nevada", "America/Los_Angeles", "2026-11-02 02:00")]
    public async Task UpcomingReminderUsesExact24HourBoundaryAndStateTimeZoneWithoutDuplicates(
        string state, string zone, string localTime)
    {
        await using var fixture = await FloridaBulkTestFixture.CreateAsync();
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var admin = new UserAccount { Role = "Admin", Email = "admin@example.invalid", IsActive = true };
        db.UserAccounts.AddRange(admin,
            new UserAccount { Role = "Learner", Email = "learner@example.invalid", IsActive = true },
            new UserAccount { Role = "Admin", Email = "inactive@example.invalid", IsActive = false });
        await db.SaveChangesAsync();
        var due = new DateTime(2026, 11, 2, 10, 0, 0, DateTimeKind.Utc);
        var clock = new Clock { Now = new DateTimeOffset(due.AddHours(-24).AddTicks(-1)) };
        var email = new FakeEmail();
        // A saved schedule, not the legacy configuration flag, determines enabled state.
        var options = new FloridaBulkCollectionOptions { WeeklyEnabled = false };
        FloridaBulkAlertService Service() => new(db, email, new AuditLogService(db), options,
            NullLogger<FloridaBulkAlertService>.Instance, clock, state);
        var schedule = new BulkCollectionSchedule(state, true, 1, "Months", "02:00", zone, 2, due);
        var status = new FloridaBulkCollectionStatus("Idle", null, null, null, due, null, []);
        await Service().CheckAsync(status, schedule: schedule);
        Assert.Empty(await db.SystemNotifications.ToListAsync());
        clock.Now = clock.Now.AddTicks(1);
        await Service().CheckAsync(status, schedule: schedule);
        await Service().CheckAsync(status, schedule: schedule);
        var notification = await db.SystemNotifications.SingleAsync();
        Assert.Equal(admin.Id, notification.RecipientUserId);
        Assert.Equal(state + " roster update scheduled", notification.Title);
        Assert.Contains(localTime + " (" + zone + ")", notification.Message);
        Assert.Contains("2026-11-02 10:00 UTC", notification.Message);
        Assert.Single(email.Messages);
        clock.Now = new DateTimeOffset(due);
        await Service().CheckAsync(status, schedule: schedule);
        Assert.Single(await db.SystemNotifications.ToListAsync());
        Assert.Single(email.Messages);
    }

    [Fact]
    public async Task ReschedulingAndDisablingSuppressObsoleteReminderEmailsButNewDatesNotify()
    {
        await using var fixture = await FloridaBulkTestFixture.CreateAsync();
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.UserAccounts.Add(new() { Role = "Admin", Email = "admin@example.invalid", IsActive = true });
        await db.SaveChangesAsync();
        var clock = new Clock();
        var email = new FakeEmail { Succeeds = false };
        var options = new FloridaBulkCollectionOptions();
        var service = new FloridaBulkAlertService(db, email, new AuditLogService(db), options,
            NullLogger<FloridaBulkAlertService>.Instance, clock, "Nevada");
        var due = clock.Now.UtcDateTime.AddHours(12);
        var schedule = new BulkCollectionSchedule("Nevada", true, 1, "Days", "02:00", "America/Los_Angeles", 1, due);
        var status = new FloridaBulkCollectionStatus("Idle", null, null, null, due, null, []);
        await service.CheckAsync(status, schedule: schedule);
        Assert.Single(email.Messages);
        clock.Now = clock.Now.AddHours(1);
        email.Succeeds = true;
        await service.CheckAsync(status, schedule: schedule with { Enabled = false });
        Assert.Single(email.Messages);
        var later = due.AddDays(3);
        await service.CheckAsync(status with { NextRunAtUtc = later }, schedule: schedule with { NextRunAtUtc = later });
        Assert.Single(email.Messages);
        clock.Now = new DateTimeOffset(later.AddHours(-24));
        await service.CheckAsync(status with { NextRunAtUtc = later }, schedule: schedule with { NextRunAtUtc = later });
        Assert.Equal(2, email.Messages.Count);
        Assert.Equal(2, await db.SystemNotifications.CountAsync());
        Assert.Single(await db.AuditLogs.Where(log => log.Action == "regulatory-alert.email-sent").ToListAsync());
    }

    [Fact]
    public async Task ScheduledSuccessSurvivesLaterRunsAndEmailRetriesWithoutDuplicateNotifications()
    {
        await using var fixture = await FloridaBulkTestFixture.CreateAsync();
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.UserAccounts.Add(new() { Role = "Admin", Email = "admin@example.invalid", IsActive = true });
        await db.SaveChangesAsync();
        var clock = new Clock();
        var email = new FakeEmail { Succeeds = false };
        FloridaBulkAlertService Service(string state) => new(db, email, new AuditLogService(db), new(),
            NullLogger<FloridaBulkAlertService>.Instance, clock, state);
        var snapshotId = Guid.NewGuid();
        var status = new FloridaBulkCollectionStatus("Completed", null, clock.Now.UtcDateTime,
            clock.Now.UtcDateTime, null, snapshotId, []) { ScheduledRun = true };
        await Service("Nevada").CheckAsync(status with { ScheduledRun = false });
        await Service("Nevada").CheckAsync(status with { State = "Cancelled" });
        Assert.Empty(await db.SystemNotifications.ToListAsync());
        await Service("Nevada").CheckAsync(status);
        await Service("Nevada").CheckAsync(status);
        Assert.Single(email.Messages);
        Assert.Contains(snapshotId.ToString(), (await db.SystemNotifications.SingleAsync()).Message);
        var laterRun = status with
        {
            State = "Running", ScheduledRun = false, SnapshotId = Guid.NewGuid(),
            LastScheduledSuccessAtUtc = status.CompletedAtUtc, LastScheduledSuccessSnapshotId = snapshotId
        };
        clock.Now = clock.Now.AddHours(1);
        email.Succeeds = true;
        await Service("Nevada").CheckAsync(laterRun);
        Assert.Equal(2, email.Messages.Count);
        Assert.Single(await db.SystemNotifications.ToListAsync());
        await Service("Texas").CheckAsync(status);
        Assert.Equal(2, await db.SystemNotifications.CountAsync());
        Assert.Contains("Texas", email.Messages[^1].Subject);
        await Service("Nevada").CheckAsync(status with { CompletedAtUtc = clock.Now.UtcDateTime });
        Assert.Equal(3, await db.SystemNotifications.CountAsync());
    }

    [Fact]
    public async Task StaleThresholdIsExactAndNotificationsSurviveRestartWithoutDuplicates()
    {
        await using var fixture = await FloridaBulkTestFixture.CreateAsync();
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var admin = new UserAccount { Role = "Admin", Email = "admin@example.invalid", IsActive = true };
        db.UserAccounts.AddRange(admin,
            new UserAccount { Role = "Learner", Email = "learner@example.invalid", IsActive = true },
            new UserAccount { Role = "Admin", Email = "inactive@example.invalid", IsActive = false });
        await db.SaveChangesAsync();
        var clock = new Clock();
        var email = new FakeEmail();
        var options = new FloridaBulkCollectionOptions();
        FloridaBulkAlertService Service() => new(db, email, new AuditLogService(db), options,
            NullLogger<FloridaBulkAlertService>.Instance, clock);
        var last = clock.Now.UtcDateTime.AddDays(-8);
        var status = new FloridaBulkCollectionStatus("Completed", null, last, last, null, null, []);
        clock.Now = clock.Now.AddTicks(-1);
        await Service().CheckAsync(status);
        Assert.Empty(await db.SystemNotifications.ToListAsync());
        clock.Now = clock.Now.AddTicks(1);
        await Service().CheckAsync(status);
        await Service().CheckAsync(status);
        var notification = await db.SystemNotifications.SingleAsync();
        Assert.Equal(admin.Id, notification.RecipientUserId);
        Assert.Equal(notification.Id, Assert.Single(await new NotificationService(db).GetForUserAsync(admin.Id)).Id);
        Assert.Single(email.Messages);
        Assert.Contains("8 days", notification.Message);
        await Service().CheckAsync(status with { CompletedAtUtc = clock.Now.UtcDateTime });
        Assert.Single(await db.SystemNotifications.ToListAsync());
        clock.Now = clock.Now.AddDays(8);
        await Service().CheckAsync(status with { CompletedAtUtc = last.AddDays(8) });
        Assert.Equal(2, await db.SystemNotifications.CountAsync());
    }

    [Fact]
    public async Task OnlyScheduledFailuresAlertAndFailedEmailRetriesAfterOneHourEvenAfterRecovery()
    {
        await using var fixture = await FloridaBulkTestFixture.CreateAsync();
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.UserAccounts.Add(new() { Role = "Admin", Email = "admin@example.invalid", IsActive = true });
        await db.SaveChangesAsync();
        var clock = new Clock();
        var email = new FakeEmail { Succeeds = false };
        var service = new FloridaBulkAlertService(db, email, new AuditLogService(db),
            new(), NullLogger<FloridaBulkAlertService>.Instance, clock);
        var status = new FloridaBulkCollectionStatus("Failed", "<private source error>", clock.Now.UtcDateTime,
            clock.Now.UtcDateTime, null, null, []);
        await service.CheckAsync(status);
        await service.CheckAsync(status with { ScheduledRun = true, State = "Cancelled" });
        Assert.Empty(await db.SystemNotifications.ToListAsync());
        await service.CheckAsync(status with { ScheduledRun = true });
        await service.CheckAsync(status with { ScheduledRun = true });
        Assert.Single(email.Messages);
        Assert.DoesNotContain("private", email.Messages[0].HtmlBody);
        Assert.Single(await db.SystemNotifications.ToListAsync());
        await service.CheckAsync(status with { State = "Completed", LastScheduledFailureAtUtc = status.StartedAtUtc });
        Assert.Single(await db.SystemNotifications.ToListAsync());
        email.Succeeds = true;
        clock.Now = clock.Now.AddHours(1);
        await service.CheckAsync(status with { State = "Completed" });
        await service.CheckAsync(status with { State = "Completed" });
        Assert.Equal(2, email.Messages.Count);
        Assert.Single(await db.AuditLogs.Where(log => log.Action == "regulatory-alert.email-sent").ToListAsync());
    }

    [Fact]
    public async Task DisabledAlertsAndDisabledSchedulerDoNotNotify()
    {
        await using var fixture = await FloridaBulkTestFixture.CreateAsync();
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.UserAccounts.Add(new() { Role = "Admin", Email = "admin@example.invalid", IsActive = true });
        await db.SaveChangesAsync();
        var email = new FakeEmail();
        var options = new FloridaBulkCollectionOptions { AlertsEnabled = false };
        var service = new FloridaBulkAlertService(db, email, new AuditLogService(db), options,
            NullLogger<FloridaBulkAlertService>.Instance);
        var status = new FloridaBulkCollectionStatus("Failed", null, DateTime.UtcNow, DateTime.UtcNow.AddDays(-9), null, null, []) { ScheduledRun = true };
        await service.CheckAsync(status);
        options.AlertsEnabled = true;
        options.WeeklyEnabled = false;
        await service.CheckAsync(status);
        Assert.Empty(await db.SystemNotifications.ToListAsync());
        Assert.Empty(email.Messages);
        options.WeeklyEnabled = true;
        options.AlertEmailEnabled = false;
        await service.CheckAsync(status);
        Assert.Equal(2, await db.SystemNotifications.CountAsync());
        Assert.Empty(email.Messages);
    }

    [Theory]
    [InlineData("Texas")]
    [InlineData("California")]
    public async Task StateAndFloridaHaveIndependentIncidentsAndRetryQueues(string state)
    {
        await using var fixture = await FloridaBulkTestFixture.CreateAsync();
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.UserAccounts.Add(new() { Role = "Admin", Email = "admin@example.invalid", IsActive = true });
        await db.SaveChangesAsync();
        var clock = new Clock();
        var email = new FakeEmail { Succeeds = false };
        FloridaBulkAlertService Service(string state) => new(db, email, new AuditLogService(db),
            new(), NullLogger<FloridaBulkAlertService>.Instance, clock, state);
        var failure = new FloridaBulkCollectionStatus("Failed", null, clock.Now.UtcDateTime, clock.Now.UtcDateTime,
            null, null, []) { ScheduledRun = true };
        await Service("Florida").CheckAsync(failure);
        await Service(state).CheckAsync(failure);
        await Service(state).CheckAsync(failure);
        Assert.Equal(2, await db.SystemNotifications.CountAsync());
        Assert.Equal(2, email.Messages.Count);
        email.Succeeds = true;
        clock.Now = clock.Now.AddHours(1);
        var recovered = failure with { State = "Completed", CompletedAtUtc = clock.Now.UtcDateTime, ScheduledRun = false };
        await Service(state).CheckAsync(recovered);
        Assert.Equal(3, email.Messages.Count);
        Assert.Contains(state, email.Messages[^1].Subject);
        await Service("Florida").CheckAsync(recovered);
        Assert.Equal(4, email.Messages.Count);
        Assert.Contains("Florida", email.Messages[^1].Subject);
        await Service(state).CheckAsync(recovered with { CompletedAtUtc = clock.Now.UtcDateTime.AddDays(-8) });
        Assert.Equal(3, await db.SystemNotifications.CountAsync());
        Assert.True(await db.SystemNotifications.AnyAsync(n => n.Title == state + " roster needs a refresh"));
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
}
