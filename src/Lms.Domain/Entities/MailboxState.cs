using System.ComponentModel.DataAnnotations;

namespace Lms.Domain.Entities;

public sealed class MailboxState
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserAccountId { get; set; }
    [MaxLength(100)] public string AccountKey { get; set; } = "";
    [MaxLength(100)] public string? MessageId { get; set; }
    public bool Archived { get; set; }
    public bool IsDraft { get; set; }
    [MaxLength(320)] public string Recipient { get; set; } = "";
    [MaxLength(320)] public string Cc { get; set; } = "";
    [MaxLength(200)] public string Subject { get; set; } = "";
    [MaxLength(50000)] public string Body { get; set; } = "";
    [MaxLength(100)] public string? ReplyToId { get; set; }
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
