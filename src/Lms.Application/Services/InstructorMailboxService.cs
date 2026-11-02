using System.Net.Mail;
using Lms.Application.Data;
using Lms.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Lms.Application.Services;

public interface IMailboxIdentity
{
    Task<Guid> GetInstructorIdAsync();
}

public sealed record MailboxAccount(string Key, string Address, string Provider, bool TestMode);
public sealed record MailboxMessage(string Id, string Subject, string From, string[] To, string[] Cc,
    string Body, DateTimeOffset CreatedAt, bool Read, bool Sent, string? ThreadId, string[] Attachments);
public sealed record MailboxPage(IReadOnlyList<MailboxMessage> Messages, bool HasNext);
public sealed record MailboxFile(string Id, string Name, string ContentType, byte[] Content);
public sealed record MailboxCompose(string To, string Cc, string Subject, string Body,
    IReadOnlyList<EmailAttachment> Attachments, string? ReplyToId = null);

// Provider IDs remain opaque to the UI; a Microsoft Graph adapter can implement this contract.
public interface IMailboxProvider
{
    Task<MailboxAccount> GetAccountAsync(Guid owner);
    Task<MailboxPage> ListAsync(Guid owner, bool sent, int page);
    Task<MailboxMessage> ReadAsync(Guid owner, string id, bool sent);
    Task<IReadOnlyList<MailboxMessage>> ConversationAsync(Guid owner, MailboxMessage message);
    Task SendAsync(Guid owner, MailboxCompose message);
    Task<MailboxFile> DownloadAsync(Guid owner, string messageId, bool sent, string attachmentId);
}

public sealed class InstructorMailboxService(
    IMailboxIdentity identity, IMailboxProvider provider, ApplicationDbContext db)
{
    public async Task<MailboxAccount> GetAccountAsync() =>
        await provider.GetAccountAsync(await identity.GetInstructorIdAsync());

    public async Task<MailboxPage> ListAsync(bool sent, int page)
    {
        if (page < 0) throw new ArgumentOutOfRangeException(nameof(page));
        return await provider.ListAsync(await identity.GetInstructorIdAsync(), sent, page);
    }

    public async Task<IReadOnlyList<MailboxMessage>> ReadConversationAsync(string id, bool sent)
    {
        var owner = await identity.GetInstructorIdAsync();
        var message = await provider.ReadAsync(owner, id, sent);
        return await provider.ConversationAsync(owner, message);
    }

    public async Task<MailboxFile> DownloadAsync(string messageId, bool sent, string attachmentId) =>
        await provider.DownloadAsync(await identity.GetInstructorIdAsync(), messageId, sent, attachmentId);

    public async Task<MailboxPage> ListArchivedAsync(int page)
    {
        if (page < 0) throw new ArgumentOutOfRangeException(nameof(page));
        var owner = await identity.GetInstructorIdAsync();
        var states = (await GetStatesAsync()).Where(s => s.Archived)
            .OrderByDescending(s => s.UpdatedAtUtc).Skip(page * 20).Take(21).ToList();
        var messages = new List<MailboxMessage>();
        foreach (var state in states.Take(20))
            messages.Add(await provider.ReadAsync(owner, state.MessageId!, false));
        return new(messages, states.Count > 20);
    }

    public async Task SendAsync(MailboxCompose message, Guid? draftId)
    {
        Validate(message);
        var owner = await identity.GetInstructorIdAsync();
        var account = await provider.GetAccountAsync(owner);
        var draft = draftId.HasValue ? await FindDraftAsync(owner, account.Key, draftId.Value) : null;
        await provider.SendAsync(owner, message);
        if (draft is not null)
        {
            db.MailboxStates.Remove(draft);
            await db.SaveChangesAsync();
        }
    }

    public async Task<List<MailboxState>> GetStatesAsync()
    {
        var owner = await identity.GetInstructorIdAsync();
        var account = await provider.GetAccountAsync(owner);
        return await db.MailboxStates.AsNoTracking()
            .Where(s => s.UserAccountId == owner && s.AccountKey == account.Key).ToListAsync();
    }

    public async Task<Guid> SaveDraftAsync(MailboxCompose message, Guid? id)
    {
        if (message.Attachments.Count > 0)
            throw new InvalidOperationException("Remove attachments before saving a draft. Add them again before sending.");
        if (message.To.Length > 320 || message.Cc.Length > 320 || message.Subject.Length > 200 || message.Body.Length > 50000)
            throw new InvalidOperationException("Draft exceeds the recipient, subject, or message length limit.");
        var owner = await identity.GetInstructorIdAsync();
        var account = await provider.GetAccountAsync(owner);
        var draft = id.HasValue ? await FindDraftAsync(owner, account.Key, id.Value)
            : new MailboxState { UserAccountId = owner, AccountKey = account.Key, IsDraft = true };
        if (!id.HasValue) db.MailboxStates.Add(draft);
        if (message.ReplyToId is not null) await provider.ReadAsync(owner, message.ReplyToId, false);
        draft.Recipient = message.To;
        draft.Cc = message.Cc;
        draft.Subject = message.Subject;
        draft.Body = message.Body;
        draft.ReplyToId = message.ReplyToId;
        draft.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return draft.Id;
    }

    public async Task ArchiveAsync(string id, bool archived)
    {
        var owner = await identity.GetInstructorIdAsync();
        var account = await provider.GetAccountAsync(owner);
        await provider.ReadAsync(owner, id, false);
        var state = await db.MailboxStates.SingleOrDefaultAsync(s =>
            s.UserAccountId == owner && s.AccountKey == account.Key && s.MessageId == id);
        if (state is null)
        {
            state = new MailboxState { UserAccountId = owner, AccountKey = account.Key, MessageId = id };
            db.MailboxStates.Add(state);
        }
        state.Archived = archived;
        await db.SaveChangesAsync();
    }

    private async Task<MailboxState> FindDraftAsync(Guid owner, string key, Guid id) =>
        await db.MailboxStates.SingleOrDefaultAsync(s =>
            s.Id == id && s.UserAccountId == owner && s.AccountKey == key && s.IsDraft)
        ?? throw new InvalidOperationException("Draft is not available in your mailbox.");

    public static void Validate(MailboxCompose message)
    {
        if (!ValidAddress(message.To) || (message.Cc.Length > 0 && !ValidAddress(message.Cc)))
            throw new InvalidOperationException("Enter one valid recipient address and, optionally, one CC address.");
        if (string.IsNullOrWhiteSpace(message.Subject) || message.Subject.Length > 200
            || message.Subject.Any(char.IsControl) || string.IsNullOrWhiteSpace(message.Body) || message.Body.Length > 50000)
            throw new InvalidOperationException("Enter a subject (up to 200 characters) and a message (up to 50,000 characters).");
        if (message.Attachments.Count > 5 || message.Attachments.Sum(a => (long)a.Content.Length) > 5 * 1024 * 1024)
            throw new InvalidOperationException("Attach at most 5 files totaling no more than 5 MB.");
    }

    private static bool ValidAddress(string address) =>
        address.Length <= 320 && !address.Any(char.IsControl)
        && MailAddress.TryCreate(address, out var parsed) && parsed.Address == address;
}
