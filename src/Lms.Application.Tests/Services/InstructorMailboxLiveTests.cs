using System.Text.Json;
using Lms.Application.Services;
using Xunit;
using Xunit.Abstractions;

namespace Lms.Application.Tests.Services;

public sealed class InstructorMailboxLiveTests(ITestOutputHelper output)
{
    [LiveMailboxFact]
    public async Task ExistingSyntheticInboxes_VerifyLiveDeliveryAndReportThreadedReplyAvailability()
    {
        var key = Required("MAILBOX_LIVE_API_KEY");
        var instructorInbox = Guid.Parse(Required("MAILBOX_LIVE_INSTRUCTOR_INBOX"));
        var commissionInbox = Guid.Parse(Required("MAILBOX_LIVE_COMMISSION_INBOX"));
        Assert.NotEqual(instructorInbox, commissionInbox);
        var instructor = Guid.NewGuid();
        var commission = Guid.NewGuid();
        var options = new InstructorMailboxOptions
        {
            Enabled = true,
            ApiKey = key,
            Accounts = [
                new() { UserId = instructor, InboxId = instructorInbox },
                new() { UserId = commission, InboxId = commissionInbox }
            ]
        };
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        var provider = new MailSlurpMailboxProvider(client, options);
        var instructorAccount = await provider.GetAccountAsync(instructor);
        var commissionAccount = await provider.GetAccountAsync(commission);
        options.AllowedRecipients = [instructorAccount.Address, commissionAccount.Address];
        options.Validate();
        var marker = $"LMS synthetic round-trip {Guid.NewGuid():N}";
        var requestBody = $"Synthetic instructor request. Correlation: {marker}. No real commission or learner data.";
        var replyBody = $"Synthetic commission reply. Correlation: {marker}. Request received and acknowledged.";
        await provider.SendAsync(instructor,
            new(commissionAccount.Address, "", marker, requestBody, []));
        output.WriteLine("Instructor send accepted by live MailSlurp.");

        var received = await WaitAsync(provider, commission,
            m => m.Subject == marker && string.Equals(m.From, instructorAccount.Address, StringComparison.OrdinalIgnoreCase));
        var request = await provider.ReadAsync(commission, received.Id, false);
        Assert.Equal(requestBody, request.Body);
        output.WriteLine("Simulated commission received the exact instructor message.");

        var threadedReplySupported = true;
        try
        {
            await provider.SendAsync(commission,
                new(instructorAccount.Address, "", "Re: " + marker, replyBody, [], request.Id));
            output.WriteLine("Simulated commission threaded reply accepted by live MailSlurp.");
        }
        catch (MailboxProviderRestrictionException)
        {
            threadedReplySupported = false;
            output.WriteLine("MailSlurp evaluation explicitly rejected threaded replies. Testing a separate return email, not a threaded-reply fallback.");
            await provider.SendAsync(commission,
                new(instructorAccount.Address, "", "Separate return email: " + marker, replyBody, []));
        }
        var returned = await WaitAsync(provider, instructor,
            m => m.Subject.Contains(marker, StringComparison.Ordinal)
                && string.Equals(m.From, commissionAccount.Address, StringComparison.OrdinalIgnoreCase));
        var reply = await provider.ReadAsync(instructor, returned.Id, false);
        Assert.Equal(replyBody, reply.Body);
        Assert.Contains(instructorAccount.Address, reply.To, StringComparer.OrdinalIgnoreCase);
        output.WriteLine("Instructor received the exact simulated commission response.");

        var sent = await provider.ListAsync(instructor, true, 0);
        var outbound = Assert.Single(sent.Messages, m => m.Subject == marker);
        var conversation = await provider.ConversationAsync(instructor, reply);
        Assert.Contains(conversation, m => m.Id == reply.Id && !m.Sent);
        if (threadedReplySupported)
            Assert.Contains(conversation, m => m.Id == outbound.Id && m.Sent);
        Assert.All(conversation, m => Assert.Contains(marker, m.Subject));
        output.WriteLine(threadedReplySupported
            ? "Instructor conversation contains both outbound request and inbound reply."
            : "Bidirectional email delivery verified. Threaded replies are NOT verified/supported on this evaluation account.");

        var evidence = new
        {
            verified_at = DateTimeOffset.UtcNow,
            provider = "MailSlurp live API",
            synthetic_only = true,
            correlation = marker,
            outbound_id = outbound.Id,
            commission_received_id = request.Id,
            instructor_received_id = reply.Id,
            instructor_thread_messages = conversation.Count,
            request_received = true,
            response_received = true,
            threaded_reply_supported = threadedReplySupported,
            thread_verified = threadedReplySupported,
            response_mode = threadedReplySupported ? "threaded reply" : "separate new email"
        };
        var evidencePath = Environment.GetEnvironmentVariable("MAILBOX_LIVE_EVIDENCE_PATH");
        if (!string.IsNullOrWhiteSpace(evidencePath))
            await File.WriteAllTextAsync(evidencePath, JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }));
        output.WriteLine(JsonSerializer.Serialize(evidence));
    }

    private static async Task<MailboxMessage> WaitAsync(IMailboxProvider provider, Guid owner,
        Func<MailboxMessage, bool> predicate)
    {
        for (var attempt = 0; attempt < 30; attempt++)
        {
            var page = await provider.ListAsync(owner, false, 0);
            var found = page.Messages.SingleOrDefault(predicate);
            if (found is not null) return found;
            await Task.Delay(TimeSpan.FromSeconds(2));
        }
        throw new TimeoutException("Live MailSlurp did not deliver the uniquely marked test message within the polling window. No automatic resend attempted.");
    }

    private static string Required(string name) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
            ? value : throw new InvalidOperationException($"Missing live-test setting: {name}");
}

public sealed class LiveMailboxFactAttribute : FactAttribute
{
    public LiveMailboxFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("RUN_LIVE_MAILBOX_TESTS") != "1")
            Skip = "Set RUN_LIVE_MAILBOX_TESTS=1 and supply two existing synthetic MailSlurp inboxes to test live delivery.";
    }
}
