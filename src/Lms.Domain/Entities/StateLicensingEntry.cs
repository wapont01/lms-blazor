namespace Lms.Domain.Entities;

public class StateLicensingEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string State { get; set; } = string.Empty;
    public string? SeedKey { get; set; }
    public string Question { get; set; } = string.Empty;
    public string Answer { get; set; } = string.Empty;
    public string SourceNotes { get; set; } = string.Empty;
    public string? PublishedQuestion { get; set; }
    public string? PublishedAnswer { get; set; }
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? PublishedAtUtc { get; set; }
    public DateTime? DeletedAtUtc { get; set; }
}
