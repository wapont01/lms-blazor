namespace Lms.Domain.Entities;

public sealed class RosterAccountRequest
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserAccountId { get; set; }
    public UserAccount? UserAccount { get; set; }
    public Guid RegulatoryLicenseeId { get; set; }
    public RegulatoryLicensee? RegulatoryLicensee { get; set; }
    public string Status { get; set; } = "Pending";
    public DateTime SubmittedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ReviewedAtUtc { get; set; }
    public Guid? ReviewedByUserId { get; set; }
    public string ReviewNote { get; set; } = string.Empty;
}
