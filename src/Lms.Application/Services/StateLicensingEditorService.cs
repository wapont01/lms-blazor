using System.Security.Claims;
using System.Text.Json;
using Lms.Application.Data;
using Lms.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Lms.Application.Services;

public sealed record PublishedStateQuestion(Guid Id, string Question, string Answer);

public sealed record StateLicensingSnapshot(
    string State, string Question, string Answer, string SourceNotes,
    string? PublishedQuestion, string? PublishedAnswer, DateTime? PublishedAtUtc, DateTime UpdatedAtUtc,
    DateTime? DeletedAtUtc = null)
{
    public static StateLicensingSnapshot From(StateLicensingEntry entry) =>
        new(entry.State, entry.Question, entry.Answer, entry.SourceNotes,
            entry.PublishedQuestion, entry.PublishedAnswer, entry.PublishedAtUtc, entry.UpdatedAtUtc, entry.DeletedAtUtc);
}

public sealed record StateLicensingChange(int Version, StateLicensingSnapshot? Previous);
public sealed record StateLicensingHistoryItem(
    Guid Id, DateTime CreatedAtUtc, string ActorEmail, string Action, StateLicensingChange? Change);

public class StateLicensingEditorService(ApplicationDbContext db, IAuditLogService audit)
{
    public async Task<List<StateLicensingHistoryItem>> GetHistoryAsync(ClaimsPrincipal actor, Guid id)
    {
        RequireAdmin(actor);
        var logs = await db.AuditLogs.AsNoTracking()
            .Where(log => log.TargetType == "StateLicensingEntry" && log.TargetId == id
                && log.Action.StartsWith("licensing.qa."))
            .OrderByDescending(log => log.CreatedAt).ThenByDescending(log => log.Id).ToListAsync();
        return logs.Select(log => new StateLicensingHistoryItem(
            log.Id, log.CreatedAt, log.ActorEmail, log.Action, ReadChange(log.Details))).ToList();
    }

    public async Task<List<PublishedStateQuestion>> GetPublishedAsync(string state)
    {
        var questions = await db.StateLicensingEntries.AsNoTracking()
            .Where(entry => entry.State == state && entry.PublishedAtUtc != null && entry.DeletedAtUtc == null)
            .Select(entry => new PublishedStateQuestion(entry.Id, entry.PublishedQuestion!, entry.PublishedAnswer!))
            .ToListAsync();
        return questions.OrderBy(item => QuestionPriority(item.Question))
            .ThenBy(item => item.Question, StringComparer.OrdinalIgnoreCase).ThenBy(item => item.Id).ToList();
    }

    public async Task<List<StateLicensingEntry>> GetForAdminAsync(ClaimsPrincipal actor, string state, bool deleted = false)
    {
        RequireAdmin(actor);
        var entries = await db.StateLicensingEntries.AsNoTracking()
            .Where(entry => entry.State == state && (entry.DeletedAtUtc != null) == deleted)
            .ToListAsync();
        return entries.OrderBy(item => QuestionPriority(item.Question))
            .ThenBy(item => item.Question, StringComparer.OrdinalIgnoreCase).ThenBy(item => item.Id).ToList();
    }

    private static int QuestionPriority(string question)
    {
        bool Has(params string[] terms) => terms.Any(term => question.Contains(term, StringComparison.OrdinalIgnoreCase));
        if (Has("broker applicants", "broker candidates", "broker education", "broker prelicens",
            "broker pre-licens", "become a broker", "upgrade to broker", "broker license education")) return 5;
        if (Has("post-licens", "postlicens", "post licens", "first-year", "first year", "first renewal",
            "after receiving", "after an initial", "receives an initial")) return 3;
        if (Has("renew", "continuing education", "continuing-education")) return 4;
        if (Has("education", "course", "hours", "prelicense", "prelicens", "pre-licens", "pre licens")) return 0;
        if (Has("exam", "pass")) return 1;
        return 2;
    }

    public async Task SaveDraftAsync(ClaimsPrincipal actor, StateLicensingEntry draft)
    {
        RequireAdmin(actor);
        var state = StateLicensingReferenceService.FindState(draft.State)
            ?? throw new InvalidOperationException("Select a valid state.");
        if (string.IsNullOrWhiteSpace(draft.Question) || draft.Question.Trim().Length > 500
            || string.IsNullOrWhiteSpace(draft.Answer))
        {
            throw new InvalidOperationException("Enter a question (up to 500 characters) and an answer.");
        }

        await using var transaction = await db.Database.BeginTransactionAsync();
        var entry = await FindCurrentAsync(draft.Id);
        var previous = entry is null ? null : StateLicensingSnapshot.From(entry);
        if (entry is null)
        {
            entry = new StateLicensingEntry { Id = draft.Id, State = state.Name };
            db.StateLicensingEntries.Add(entry);
        }
        if (entry.State != state.Name)
        {
            throw new InvalidOperationException("An existing Q&A cannot be moved to another state.");
        }
        RequireNotDeleted(entry);
        entry.Question = draft.Question.Trim();
        entry.Answer = draft.Answer.Trim();
        entry.SourceNotes = draft.SourceNotes.Trim();
        entry.UpdatedAtUtc = DateTime.UtcNow;
        await RecordAsync(actor, entry.Id, "licensing.qa.draft", previous);
        await transaction.CommitAsync();
    }

    public async Task PublishAsync(ClaimsPrincipal actor, Guid id)
    {
        RequireAdmin(actor);
        await using var transaction = await db.Database.BeginTransactionAsync();
        var entry = await FindCurrentAsync(id)
            ?? throw new InvalidOperationException("Q&A not found. Save a draft first.");
        RequireNotDeleted(entry);
        if (string.IsNullOrWhiteSpace(entry.SourceNotes))
        {
            throw new InvalidOperationException("Add source information before publishing.");
        }
        var previous = StateLicensingSnapshot.From(entry);
        entry.PublishedQuestion = entry.Question;
        entry.PublishedAnswer = entry.Answer;
        entry.PublishedAtUtc = DateTime.UtcNow;
        await RecordAsync(actor, id, "licensing.qa.publish", previous);
        await transaction.CommitAsync();
    }

    public async Task UnpublishAsync(ClaimsPrincipal actor, Guid id)
    {
        RequireAdmin(actor);
        await using var transaction = await db.Database.BeginTransactionAsync();
        var entry = await FindCurrentAsync(id)
            ?? throw new InvalidOperationException("Q&A not found.");
        RequireNotDeleted(entry);
        var previous = StateLicensingSnapshot.From(entry);
        entry.PublishedAtUtc = null;
        await RecordAsync(actor, id, "licensing.qa.unpublish", previous);
        await transaction.CommitAsync();
    }

    public async Task DeleteAsync(ClaimsPrincipal actor, Guid id)
    {
        RequireAdmin(actor);
        await using var transaction = await db.Database.BeginTransactionAsync();
        var entry = await FindCurrentAsync(id)
            ?? throw new InvalidOperationException("Q&A not found.");
        RequireNotDeleted(entry);
        var previous = StateLicensingSnapshot.From(entry);
        entry.DeletedAtUtc = DateTime.UtcNow;
        await RecordAsync(actor, id, "licensing.qa.delete", previous);
        await transaction.CommitAsync();
    }

    private static void RequireNotDeleted(StateLicensingEntry entry)
    {
        if (entry.DeletedAtUtc is not null)
            throw new InvalidOperationException("This Q&A is deleted and can only be viewed in the Deleted list.");
    }

    private async Task<StateLicensingEntry?> FindCurrentAsync(Guid id)
    {
        var entry = await db.StateLicensingEntries.FindAsync(id);
        if (entry is not null)
        {
            await db.Entry(entry).ReloadAsync();
            if (db.Entry(entry).State == EntityState.Detached) return null;
        }
        return entry;
    }

    private static StateLicensingChange? ReadChange(string? details)
    {
        if (string.IsNullOrWhiteSpace(details)) return null;
        try
        {
            var change = JsonSerializer.Deserialize<StateLicensingChange>(details);
            if (change?.Version != 1)
                throw new InvalidOperationException("The Q&A audit record has an unsupported format.");
            return change;
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("The Q&A audit record could not be read.", ex);
        }
    }

    private Task RecordAsync(ClaimsPrincipal actor, Guid id, string action, StateLicensingSnapshot? previous)
    {
        Guid? actorId = Guid.TryParse(actor.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var parsed) ? parsed : null;
        return audit.WriteAsync(actorId, actor.FindFirst(ClaimTypes.Email)?.Value, action, "StateLicensingEntry", id,
            JsonSerializer.Serialize(new StateLicensingChange(1, previous)));
    }

    private static void RequireAdmin(ClaimsPrincipal actor)
    {
        if (actor.Identity?.IsAuthenticated != true || !actor.IsInRole("Admin"))
        {
            throw new UnauthorizedAccessException("Only admins can manage licensing Q&A and source information.");
        }
    }
}
