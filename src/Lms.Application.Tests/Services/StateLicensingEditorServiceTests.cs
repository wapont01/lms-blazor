using System.Security.Claims;
using System.Text.Json;
using Lms.Application.Data;
using Lms.Application.Services;
using Lms.Domain.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Lms.Application.Tests.Services;

public class StateLicensingEditorServiceTests
{
    private static ClaimsPrincipal Actor(string role) => new(new ClaimsIdentity(
        [new Claim(ClaimTypes.Role, role), new Claim(ClaimTypes.Email, "editor@example.test"),
            new Claim(ClaimTypes.NameIdentifier, "b366fb5e-63c7-494f-9916-91ee5ce766f2")], "Test"));

    [Fact]
    public async Task DraftPublishEditAndUnpublish_KeepSourcesPrivateAndPublishedVersionStable()
    {
        await using var fixture = await Fixture.CreateAsync();
        var admin = Actor("Admin");
        var draft = new StateLicensingEntry
        {
            State = "Alaska", Question = "Original question?", Answer = "Original answer.",
            SourceNotes = "PRIVATE SOURCE MARKER"
        };
        await fixture.Service.SaveDraftAsync(admin, draft);
        Assert.Empty(await fixture.Service.GetPublishedAsync("Alaska"));
        await fixture.Service.PublishAsync(admin, draft.Id);
        var published = Assert.Single(await fixture.Service.GetPublishedAsync("Alaska"));
        Assert.Equal("Original answer.", published.Answer);
        Assert.DoesNotContain("PRIVATE SOURCE MARKER", JsonSerializer.Serialize(published));
        Assert.DoesNotContain("Source", JsonSerializer.Serialize(published));

        draft.Answer = "Edited draft answer.";
        await fixture.Service.SaveDraftAsync(admin, draft);
        Assert.Equal("Original answer.", Assert.Single(await fixture.Service.GetPublishedAsync("Alaska")).Answer);
        Assert.Equal("PRIVATE SOURCE MARKER",
            Assert.Single(await fixture.Service.GetForAdminAsync(admin, "Alaska")).SourceNotes);
        await fixture.Service.PublishAsync(admin, draft.Id);
        Assert.Equal("Edited draft answer.", Assert.Single(await fixture.Service.GetPublishedAsync("Alaska")).Answer);
        await fixture.Service.UnpublishAsync(admin, draft.Id);
        Assert.Empty(await fixture.Service.GetPublishedAsync("Alaska"));
        Assert.Single(await fixture.Service.GetForAdminAsync(admin, "Alaska"));
        Assert.Equal(5, await fixture.Db.AuditLogs.CountAsync());
    }

    [Theory]
    [InlineData("Learner")]
    [InlineData("Instructor")]
    [InlineData("Broker")]
    [InlineData("")]
    public async Task NonAdmins_CannotReadSourcesOrMutateEntries(string role)
    {
        await using var fixture = await Fixture.CreateAsync();
        var actor = Actor(role);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Service.GetForAdminAsync(actor, "Alaska"));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Service.SaveDraftAsync(actor, new()));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Service.PublishAsync(actor, Guid.NewGuid()));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Service.UnpublishAsync(actor, Guid.NewGuid()));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Service.GetHistoryAsync(actor, Guid.NewGuid()));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Service.DeleteAsync(actor, Guid.NewGuid()));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Service.GetForAdminAsync(actor, "Alaska", deleted: true));
    }

    [Fact]
    public async Task History_PreservesPreviousContentForEveryActionWithoutOverwritingIt()
    {
        await using var fixture = await Fixture.CreateAsync();
        var admin = Actor("Admin");
        var originalAnswer = new string('A', 3000) + "\nOriginal answer";
        var draft = new StateLicensingEntry
        {
            State = "Alaska", Question = "Original question?", Answer = originalAnswer,
            SourceNotes = new string('S', 3000) + "\nPrivate source"
        };
        await fixture.Service.SaveDraftAsync(admin, draft);
        var created = Assert.Single(await fixture.Service.GetHistoryAsync(admin, draft.Id));
        Assert.NotNull(created.Change);
        Assert.Null(created.Change.Previous);

        await fixture.Service.PublishAsync(admin, draft.Id);
        var beforeEdit = StateLicensingSnapshot.From(
            Assert.Single(await fixture.Service.GetForAdminAsync(admin, "Alaska")));
        draft.Question = "Edited question?";
        draft.Answer = "Edited answer.";
        draft.SourceNotes = "New source";
        await fixture.Service.SaveDraftAsync(admin, draft);
        var history = await fixture.Service.GetHistoryAsync(admin, draft.Id);
        var edited = Assert.Single(history.Where(item => item.Change?.Previous?.PublishedAtUtc != null));
        Assert.Equal(beforeEdit, edited.Change!.Previous);
        Assert.Equal("editor@example.test", edited.ActorEmail);
        Assert.InRange(edited.CreatedAtUtc, DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow);
        var log = await fixture.Db.AuditLogs.SingleAsync(item => item.Id == edited.Id);
        Assert.Equal(Guid.Parse("b366fb5e-63c7-494f-9916-91ee5ce766f2"), log.ActorUserId);
        Assert.Equal(draft.Id, log.TargetId);
        Assert.Equal(originalAnswer, Assert.Single(await fixture.Service.GetPublishedAsync("Alaska")).Answer);

        await fixture.Service.PublishAsync(admin, draft.Id);
        var beforeUnpublish = StateLicensingSnapshot.From(
            Assert.Single(await fixture.Service.GetForAdminAsync(admin, "Alaska")));
        await fixture.Service.UnpublishAsync(admin, draft.Id);
        history = await fixture.Service.GetHistoryAsync(admin, draft.Id);
        Assert.Equal(5, history.Count);
        Assert.Equal(beforeEdit, history.Single(item => item.Id == edited.Id).Change!.Previous);
        Assert.Equal(beforeUnpublish, history.Single(item => item.Action == "licensing.qa.unpublish").Change!.Previous);
        var publishedRevisions = history.Where(item => item.Action == "licensing.qa.publish").ToList();
        Assert.Contains(publishedRevisions, item => item.Change!.Previous!.PublishedAtUtc is null);
        Assert.Contains(publishedRevisions, item => item.Change!.Previous!.PublishedAnswer == originalAnswer
            && item.Change.Previous.Answer == "Edited answer.");
        Assert.Empty(await fixture.Service.GetPublishedAsync("Alaska"));
        Assert.DoesNotContain("Private source", JsonSerializer.Serialize(new PublishedStateQuestion(draft.Id, draft.Question, draft.Answer)));
    }

    [Fact]
    public async Task History_IsEntrySpecificAndDistinguishesLegacyEventsFromCreation()
    {
        await using var fixture = await Fixture.CreateAsync();
        var id = Guid.NewGuid();
        var audit = new AuditLogService(fixture.Db);
        await audit.WriteAsync(null, "old@example.test", "licensing.qa.draft", "StateLicensingEntry", id);
        await audit.WriteAsync(null, "other@example.test", "licensing.qa.draft", "StateLicensingEntry", Guid.NewGuid());
        var legacy = Assert.Single(await fixture.Service.GetHistoryAsync(Actor("Admin"), id));
        Assert.Null(legacy.Change);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            fixture.Service.GetHistoryAsync(new ClaimsPrincipal(), id));
    }

    [Fact]
    public async Task Save_UsesPersistedPreviousVersionRatherThanStaleTrackedValues()
    {
        await using var fixture = await Fixture.CreateAsync();
        var draft = new StateLicensingEntry { State = "Alaska", Question = "Question?", Answer = "First" };
        await fixture.Service.SaveDraftAsync(Actor("Admin"), draft);
        await fixture.Db.StateLicensingEntries.Where(entry => entry.Id == draft.Id)
            .ExecuteUpdateAsync(update => update.SetProperty(entry => entry.Answer, "Another admin's change"));
        draft.Answer = "Latest";
        await fixture.Service.SaveDraftAsync(Actor("Admin"), draft);
        var revision = Assert.Single((await fixture.Service.GetHistoryAsync(Actor("Admin"), draft.Id))
            .Where(item => item.Change?.Previous is not null));
        Assert.Equal("Another admin's change", revision.Change!.Previous!.Answer);
    }

    [Fact]
    public async Task FirstEditOfImportedAnswer_CapturesOriginalContentAndSources()
    {
        await using var fixture = await Fixture.CreateAsync();
        await StateLicensingEntrySeed.SeedAsync(fixture.Db);
        var draft = (await fixture.Service.GetForAdminAsync(Actor("Admin"), "Idaho")).First();
        var previous = StateLicensingSnapshot.From(draft);
        draft.Answer = "Updated draft";
        await fixture.Service.SaveDraftAsync(Actor("Admin"), draft);
        var revision = Assert.Single(await fixture.Service.GetHistoryAsync(Actor("Admin"), draft.Id));
        Assert.Equal(previous, revision.Change!.Previous);
        Assert.Equal(previous.PublishedAnswer,
            (await fixture.Service.GetPublishedAsync("Idaho")).Single(item => item.Id == draft.Id).Answer);
    }

    [Fact]
    public async Task MalformedHistory_IsReportedInsteadOfSilentlyOmitted()
    {
        await using var fixture = await Fixture.CreateAsync();
        var id = Guid.NewGuid();
        await new AuditLogService(fixture.Db).WriteAsync(null, "admin@example.test",
            "licensing.qa.draft", "StateLicensingEntry", id, "{invalid");
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.GetHistoryAsync(Actor("Admin"), id));
        Assert.IsType<JsonException>(error.InnerException);
    }

    [Fact]
    public async Task AuditFailure_RollsBackBothTheEntryAndAuditRecord()
    {
        await using var fixture = await Fixture.CreateAsync();
        var draft = new StateLicensingEntry { State = "Alaska", Question = "Question?", Answer = "Original" };
        await fixture.Service.SaveDraftAsync(Actor("Admin"), draft);
        var failing = new StateLicensingEditorService(fixture.Db, new FailingAudit(fixture.Db));
        draft.Answer = "Must not persist";
        await Assert.ThrowsAsync<InvalidOperationException>(() => failing.SaveDraftAsync(Actor("Admin"), draft));
        Assert.Equal("Original", (await fixture.Db.StateLicensingEntries.AsNoTracking().SingleAsync()).Answer);
        Assert.Single(await fixture.Db.AuditLogs.AsNoTracking().ToListAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Delete_RetainsPreviousVersionAndHistoryButHidesEntryAndPreventsMutation(bool publish)
    {
        await using var fixture = await Fixture.CreateAsync();
        var admin = Actor("Admin");
        var draft = new StateLicensingEntry
        {
            State = "Alaska", Question = "Delete me?", Answer = "Retained answer", SourceNotes = "Private sources"
        };
        await fixture.Service.SaveDraftAsync(admin, draft);
        if (publish) await fixture.Service.PublishAsync(admin, draft.Id);
        var previous = StateLicensingSnapshot.From(Assert.Single(await fixture.Service.GetForAdminAsync(admin, "Alaska")));
        await fixture.Service.DeleteAsync(admin, draft.Id);
        Assert.Empty(await fixture.Service.GetPublishedAsync("Alaska"));
        Assert.Empty(await fixture.Service.GetForAdminAsync(admin, "Alaska"));
        var deleted = Assert.Single(await fixture.Service.GetForAdminAsync(admin, "Alaska", deleted: true));
        Assert.NotNull(deleted.DeletedAtUtc);
        Assert.Equal("Retained answer", deleted.Answer);
        Assert.Equal("Private sources", deleted.SourceNotes);
        var history = await fixture.Service.GetHistoryAsync(admin, draft.Id);
        Assert.Equal(publish ? 3 : 2, history.Count);
        var deletion = Assert.Single(history.Where(item => item.Action == "licensing.qa.delete"));
        Assert.Equal(previous, deletion.Change!.Previous);
        Assert.Equal("editor@example.test", deletion.ActorEmail);
        Assert.InRange(deleted.DeletedAtUtc.Value, DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.SaveDraftAsync(admin, draft));
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.PublishAsync(admin, draft.Id));
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.UnpublishAsync(admin, draft.Id));
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.DeleteAsync(admin, draft.Id));
        Assert.Equal(history.Count, (await fixture.Service.GetHistoryAsync(admin, draft.Id)).Count);
    }

    [Fact]
    public async Task Delete_ImportedAnswerIsNotRecreatedBySeed()
    {
        await using var fixture = await Fixture.CreateAsync();
        var admin = Actor("Admin");
        await StateLicensingEntrySeed.SeedAsync(fixture.Db);
        var originalCount = await fixture.Db.StateLicensingEntries.CountAsync();
        var entry = (await fixture.Service.GetForAdminAsync(admin, "Idaho")).First();
        await fixture.Service.DeleteAsync(admin, entry.Id);
        await StateLicensingEntrySeed.SeedAsync(fixture.Db);
        Assert.Equal(originalCount, await fixture.Db.StateLicensingEntries.CountAsync());
        Assert.Equal(entry.Id, Assert.Single(await fixture.Service.GetForAdminAsync(admin, "Idaho", deleted: true)).Id);
        Assert.DoesNotContain(await fixture.Service.GetPublishedAsync("Idaho"), item => item.Id == entry.Id);
    }

    [Fact]
    public async Task Delete_AuditFailureRollsBackDeletion()
    {
        await using var fixture = await Fixture.CreateAsync();
        var admin = Actor("Admin");
        var draft = new StateLicensingEntry { State = "Alaska", Question = "Question?", Answer = "Original" };
        await fixture.Service.SaveDraftAsync(admin, draft);
        var failing = new StateLicensingEditorService(fixture.Db, new FailingAudit(fixture.Db));
        await Assert.ThrowsAsync<InvalidOperationException>(() => failing.DeleteAsync(admin, draft.Id));
        Assert.Null((await fixture.Db.StateLicensingEntries.AsNoTracking().SingleAsync()).DeletedAtUtc);
        Assert.Single(await fixture.Db.AuditLogs.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task QuestionLists_FollowEducationalTrackWithoutUsingUnpublishedEdits()
    {
        await using var fixture = await Fixture.CreateAsync();
        var admin = Actor("Admin");
        string[] expected =
        [
            "What prelicensing education courses are required?",
            "How do I schedule the exam?",
            "Does the application need fingerprints?",
            "What is the entry-level license?",
            "What post-license courses are required before first renewal?",
            "What renewal education hours are required?",
            "What education is required for broker applicants?"
        ];
        foreach (var question in expected.Reverse())
        {
            var entry = new StateLicensingEntry
            {
                State = "Alaska", Question = question, Answer = "Original answer", SourceNotes = "Private"
            };
            await fixture.Service.SaveDraftAsync(admin, entry);
            await fixture.Service.PublishAsync(admin, entry.Id);
        }
        Assert.Equal(expected, (await fixture.Service.GetPublishedAsync("Alaska")).Select(item => item.Question));
        Assert.Equal(expected, (await fixture.Service.GetForAdminAsync(admin, "Alaska")).Select(item => item.Question));
        var draft = (await fixture.Service.GetForAdminAsync(admin, "Alaska")).Last();
        draft.Question = "Additional prelicensing course requirements?";
        await fixture.Service.SaveDraftAsync(admin, draft);
        Assert.Equal(expected, (await fixture.Service.GetPublishedAsync("Alaska")).Select(item => item.Question));
        await fixture.Service.PublishAsync(admin, draft.Id);
        Assert.Equal(draft.Id, (await fixture.Service.GetPublishedAsync("Alaska")).First().Id);
    }

    [Fact]
    public async Task NorthCarolina_OrdersPrelicensingBeforeExamPostlicensingAndRenewal()
    {
        await using var fixture = await Fixture.CreateAsync();
        await StateLicensingEntrySeed.SeedAsync(fixture.Db);
        var questions = await fixture.Service.GetPublishedAsync("North Carolina");
        Assert.Contains("75-hour course", questions[0].Question);
        Assert.Contains("exam-eligibility", questions[1].Question);
        Assert.Contains("postlicensing", questions[^2].Question);
        Assert.Contains("renewal", questions[^1].Question);
        Assert.Equal(questions.Select(item => item.Question),
            (await fixture.Service.GetForAdminAsync(Actor("Admin"), "North Carolina")).Select(item => item.Question));
    }

    private sealed class FailingAudit(ApplicationDbContext db) : IAuditLogService
    {
        public async Task WriteAsync(Guid? actorUserId, string? actorEmail, string action, string targetType,
            Guid? targetId, string? details = null)
        {
            await new AuditLogService(db).WriteAsync(actorUserId, actorEmail, action, targetType, targetId, details);
            throw new InvalidOperationException("Simulated audit failure.");
        }
        public Task<List<AuditLog>> GetRecentAsync(int take = 30) => throw new NotSupportedException();
        public Task<List<AuditLog>> GetFilteredAsync(string? actorContains, string? actionContains,
            DateTime? fromUtc, DateTime? toUtc, int take = 200) => throw new NotSupportedException();
    }

    [Fact]
    public async Task PublishRequiresSourcesAndDraftRequiresValidStateQuestionAndAnswer()
    {
        await using var fixture = await Fixture.CreateAsync();
        var admin = Actor("Admin");
        var draft = new StateLicensingEntry { State = "Alaska", Question = "Question?", Answer = "Answer." };
        await fixture.Service.SaveDraftAsync(admin, draft);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.PublishAsync(admin, draft.Id));
        Assert.Empty(await fixture.Service.GetPublishedAsync("Alaska"));
        draft.State = "Invalid";
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.SaveDraftAsync(admin, draft));
        draft.State = "Alaska";
        draft.Question = " ";
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.SaveDraftAsync(admin, draft));
    }

    [Fact]
    public async Task Seed_ImportsAllExistingAnswersOnceAndPreservesAdminEdits()
    {
        await using var fixture = await Fixture.CreateAsync();
        await StateLicensingEntrySeed.SeedAsync(fixture.Db);
        var expected = StateLicensingReferenceService.GetStates().Sum(state => state.Questions.Count);
        Assert.Equal(expected, await fixture.Db.StateLicensingEntries.CountAsync());
        var entry = await fixture.Db.StateLicensingEntries.FirstAsync();
        entry.Question = "Admin edited question";
        await fixture.Db.SaveChangesAsync();
        await StateLicensingEntrySeed.SeedAsync(fixture.Db);
        Assert.Equal(expected, await fixture.Db.StateLicensingEntries.CountAsync());
        Assert.Equal("Admin edited question", (await fixture.Db.StateLicensingEntries.FindAsync(entry.Id))!.Question);
        Assert.Equal(50, await fixture.Db.StateLicensingEntries.Select(item => item.State).Distinct().CountAsync());
        Assert.All(await fixture.Db.StateLicensingEntries.ToListAsync(), item =>
        {
            Assert.NotNull(item.PublishedAtUtc);
            Assert.False(string.IsNullOrWhiteSpace(item.SourceNotes));
        });
    }

    private sealed class Fixture(SqliteConnection connection, ApplicationDbContext db) : IAsyncDisposable
    {
        public ApplicationDbContext Db { get; } = db;
        public StateLicensingEditorService Service { get; } = new(db, new AuditLogService(db));
        public static async Task<Fixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlite(connection).Options);
            await db.Database.EnsureCreatedAsync();
            return new Fixture(connection, db);
        }
        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
