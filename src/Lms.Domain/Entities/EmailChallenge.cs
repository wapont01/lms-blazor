namespace Lms.Domain.Entities;

public sealed class EmailChallenge
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserAccountId { get; set; }
    public UserAccount? UserAccount { get; set; }
    public string Purpose { get; set; } = string.Empty;
    public string SecretHash { get; set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? ConsumedAtUtc { get; set; }
    public int FailedAttempts { get; set; }
}
