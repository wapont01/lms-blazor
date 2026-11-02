using System.Security.Claims;
using Lms.Application.Data;
using Lms.Application.Services;
using Lms.Domain.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Lms.Application.Tests.Services;

public sealed class InstructorRecordServiceTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private ApplicationDbContext _db = null!;
    private readonly Guid _admin = Guid.NewGuid(), _first = Guid.NewGuid(), _second = Guid.NewGuid(), _learner = Guid.NewGuid();
    private readonly Guid _faculty = Guid.NewGuid(), _unlinked = Guid.NewGuid();

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        _db = new(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(_connection).Options);
        await _db.Database.EnsureCreatedAsync();
        _db.UserAccounts.AddRange(
            User(_admin, "Admin", "Admin"), User(_first, "Same Name", "Instructor"),
            User(_second, "Same Name", "Instructor"), User(_learner, "Learner", "Learner"));
        var profile = new SchoolProfile { State = "Florida" };
        _db.SchoolProfiles.Add(profile);
        _db.SchoolStaffMembers.AddRange(
            new() { Id = _faculty, SchoolProfileId = profile.Id, Name = "Faculty one", Role = "Instructor",
                UserAccountId = _first, LicenseNumber = "ZH-ONE", LastObservationAtUtc = new DateTime(2026, 5, 1),
                HireReportedToCommissionAtUtc = new DateTime(2025, 1, 1) },
            new() { SchoolProfileId = profile.Id, Name = "Faculty two", Role = "Instructor",
                UserAccountId = _second, LicenseNumber = "ZH-TWO" },
            new() { Id = _unlinked, SchoolProfileId = profile.Id, Name = "Same Name", Role = "Instructor",
                Email = $"{_first}@example.invalid", LicenseNumber = "UNASSIGNED" },
            new() { SchoolProfileId = profile.Id, Name = "Officer", Role = "Officer" });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
    }

    [Fact]
    public async Task AdminSeesAllInstructorAccountsAndFacultyIncludingUnassigned()
    {
        var data = await Service(_admin, "Admin").GetAsync();
        Assert.True(data.IsAdmin);
        Assert.Equal(2, data.Accounts.Count);
        Assert.Equal(3, data.Faculty.Count);
        Assert.Contains(data.Faculty, m => m.Id == _unlinked);
    }

    [Fact]
    public async Task InstructorSeesOnlyOwnProfileLicenseAndExplicitlyLinkedDates()
    {
        var data = await Service(_first, "Instructor").GetAsync();
        Assert.False(data.IsAdmin);
        Assert.Equal(_first, Assert.Single(data.Accounts).Id);
        var faculty = Assert.Single(data.Faculty);
        Assert.Equal(_faculty, faculty.Id);
        Assert.Equal("ZH-ONE", faculty.LicenseNumber);
        Assert.Equal(new DateTime(2026, 5, 1), faculty.LastObservationAtUtc);
        Assert.Equal(new DateTime(2025, 1, 1), faculty.HireReportedToCommissionAtUtc);
        Assert.DoesNotContain(data.Faculty, m => m.Id == _unlinked);
    }

    [Fact]
    public async Task InstructorCannotAssignEvenOwnRecordOrAnotherRecord()
    {
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            Service(_first, "Instructor").LinkFacultyAsync(_unlinked, _first));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            Service(_first, "Instructor").LinkFacultyAsync(_faculty, _second));
        Assert.Equal(_first, (await _db.SchoolStaffMembers.FindAsync(_faculty))!.UserAccountId);
        Assert.Empty(await _db.AuditLogs.ToListAsync());
    }

    [Fact]
    public async Task AdminCanAssignReassignAndRemoveOwnershipWithAudit()
    {
        var admin = Service(_admin, "Admin");
        await admin.LinkFacultyAsync(_unlinked, _first);
        Assert.Equal(2, (await Service(_first, "Instructor").GetAsync()).Faculty.Count);
        await admin.LinkFacultyAsync(_unlinked, _second);
        Assert.Single((await Service(_first, "Instructor").GetAsync()).Faculty);
        Assert.Equal(2, (await Service(_second, "Instructor").GetAsync()).Faculty.Count);
        await admin.LinkFacultyAsync(_unlinked, null);
        Assert.Single((await Service(_second, "Instructor").GetAsync()).Faculty);
        Assert.Equal(3, await _db.AuditLogs.CountAsync(a => a.Action == "instructor-record.owner-changed"));
    }

    [Fact]
    public async Task RejectsLearnerOrMissingOwnershipTarget()
    {
        var service = Service(_admin, "Admin");
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.LinkFacultyAsync(_unlinked, _learner));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.LinkFacultyAsync(Guid.NewGuid(), _first));
        Assert.Null((await _db.SchoolStaffMembers.FindAsync(_unlinked))!.UserAccountId);
    }

    [Theory]
    [InlineData("Learner")]
    [InlineData("Broker")]
    public async Task UnrelatedRolesCannotRead(string claimedRole)
    {
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Service(_learner, claimedRole).GetAsync());
    }

    [Fact]
    public async Task AnonymousInactiveAndStaleAdminClaimsCannotRead()
    {
        var anonymous = new InstructorRecordService(_db, new Identity(new ClaimsPrincipal()), new AuditLogService(_db));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => anonymous.GetAsync());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Service(_first, "Admin").GetAsync());
        var user = await _db.UserAccounts.FindAsync(_first);
        user!.IsActive = false;
        await _db.SaveChangesAsync();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Service(_first, "Instructor").GetAsync());
    }

    [Fact]
    public async Task BrokerWithInstructorLicense_IsVisibleAndCanOwnFacultyWithoutLosingBrokerRole()
    {
        var broker = (await _db.UserAccounts.FindAsync(_learner))!;
        broker.Role = "Broker";
        broker.EmailVerifiedAtUtc = DateTime.UtcNow;
        var snapshot = new RegulatoryDataSnapshot
        {
            State = "Florida", Status = RegulatorySnapshotStatuses.Approved, RetrievedAtUtc = DateTime.UtcNow
        };
        var license = new RegulatoryLicensee
        {
            SnapshotId = snapshot.Id, State = "Florida", LicenseNumber = "ADDED-INSTRUCTOR",
            Name = broker.DisplayName, LicenseType = "Real Estate Instructor", LicenseStatus = "Current,Active"
        };
        _db.RegulatoryDataSnapshots.Add(snapshot);
        _db.RegulatoryLicensees.Add(license);
        _db.RosterAccountRequests.Add(new()
        {
            UserAccountId = broker.Id, RegulatoryLicenseeId = license.Id, Status = "Confirmed"
        });
        await _db.SaveChangesAsync();
        var admin = Service(_admin, "Admin");
        Assert.Contains((await admin.GetAsync()).Accounts, account => account.Id == broker.Id);
        await admin.LinkFacultyAsync(_unlinked, broker.Id);

        var service = Service(broker.Id, "Broker", "Instructor");
        var mine = await service.GetAsync();
        var account = Assert.Single(mine.Accounts);
        Assert.Equal(broker.Id, account.Id);
        Assert.Equal("ADDED-INSTRUCTOR", Assert.Single(account.LicenseRecords).LicenseNumber);
        Assert.Equal(_unlinked, Assert.Single(mine.Faculty).Id);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.LinkFacultyAsync(_faculty, broker.Id));
        var request = await _db.RosterAccountRequests.SingleAsync();
        request.Status = "Rejected";
        await _db.SaveChangesAsync();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.GetAsync());
    }

    private InstructorRecordService Service(Guid id, string role, params string[] additionalRoles) => new(_db,
        new Identity(new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, id.ToString()), new Claim(ClaimTypes.Role, role),
            .. additionalRoles.Select(value => new Claim(ClaimTypes.Role, value))], "test"))),
        new AuditLogService(_db));
    private static UserAccount User(Guid id, string name, string role) =>
        new() { Id = id, DisplayName = name, Email = $"{id}@example.invalid", Role = role, LicenseNumber = $"LICENSE-{id}" };
    private sealed class Identity(ClaimsPrincipal principal) : IInstructorRecordIdentity
    {
        public Task<ClaimsPrincipal> GetUserAsync() => Task.FromResult(principal);
    }
    public async Task DisposeAsync() { await _db.DisposeAsync(); await _connection.DisposeAsync(); }
}
