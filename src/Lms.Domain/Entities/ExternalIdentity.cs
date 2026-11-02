namespace Lms.Domain.Entities;

public sealed class ExternalIdentity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Issuer { get; set; } = "";
    public string Subject { get; set; } = "";
    public Guid UserAccountId { get; set; }
    public UserAccount? UserAccount { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
