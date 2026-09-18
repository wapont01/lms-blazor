using System.ComponentModel.DataAnnotations;

namespace Lms.Domain.Entities;

public static class RegulatorySnapshotStatuses
{
    public const string PendingReview = "PendingReview";
    public const string Approved = "Approved";
    public const string Rejected = "Rejected";
}

public class RegulatoryDataSnapshot
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required, StringLength(40)]
    public string State { get; set; } = string.Empty;

    [Required, StringLength(120)]
    public string SourceName { get; set; } = string.Empty;

    [Required, StringLength(1000)]
    public string SourceUrl { get; set; } = string.Empty;

    [StringLength(80)]
    public string? ParserVersion { get; set; }

    public decimal? AcquisitionCost { get; set; }
    public DateTime RetrievedAtUtc { get; set; }
    public DateTime ImportedAtUtc { get; set; } = DateTime.UtcNow;

    [Required, StringLength(30)]
    public string Status { get; set; } = RegulatorySnapshotStatuses.PendingReview;

    public DateTime? ReviewedAtUtc { get; set; }
    public Guid? ReviewedByUserId { get; set; }

    public ICollection<RegulatoryFirm> Firms { get; set; } = new List<RegulatoryFirm>();
    public ICollection<RegulatoryLicensee> Licensees { get; set; } = new List<RegulatoryLicensee>();
}

public class RegulatoryFirm
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SnapshotId { get; set; }

    [Required, StringLength(40)]
    public string State { get; set; } = string.Empty;

    [Required, StringLength(40)]
    public string LicenseNumber { get; set; } = string.Empty;

    [Required, StringLength(200)]
    public string LegalName { get; set; } = string.Empty;

    [StringLength(200)]
    public string? TradeName { get; set; }

    [StringLength(40)]
    public string? LicenseStatus { get; set; }

    public DateOnly? LicenseIssuedDate { get; set; }

    [StringLength(500)]
    public string? Address { get; set; }

    [StringLength(160)]
    public string? QualifyingBrokerName { get; set; }

    public RegulatoryDataSnapshot? Snapshot { get; set; }
    public ICollection<RegulatoryFirmLicenseeAffiliation> LicenseeAffiliations { get; set; } = new List<RegulatoryFirmLicenseeAffiliation>();
}

public class RegulatoryLicensee
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SnapshotId { get; set; }

    [Required, StringLength(40)]
    public string State { get; set; } = string.Empty;

    [Required, StringLength(40)]
    public string LicenseNumber { get; set; } = string.Empty;

    [Required, StringLength(160)]
    public string Name { get; set; } = string.Empty;

    [StringLength(40)]
    public string? LicenseType { get; set; }

    [StringLength(20)]
    public string? LicenseLevel { get; set; }

    [StringLength(40)]
    public string? LicenseStatus { get; set; }

    public DateOnly? LicenseIssuedDate { get; set; }
    public DateOnly? LicenseRenewalDate { get; set; }
    public int? UpdateHours { get; set; }
    public int? ElectiveHours { get; set; }
    public int? PostlicensingHours { get; set; }
    public bool IsBicEligible { get; set; }

    public RegulatoryDataSnapshot? Snapshot { get; set; }
    public ICollection<RegulatoryFirmLicenseeAffiliation> FirmAffiliations { get; set; } = new List<RegulatoryFirmLicenseeAffiliation>();
}

public class RegulatoryFirmLicenseeAffiliation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SnapshotId { get; set; }
    public Guid FirmId { get; set; }
    public Guid LicenseeId { get; set; }

    public DateOnly? RelationshipEffectiveDate { get; set; }
    public bool IsBrokerInCharge { get; set; }

    [StringLength(160)]
    public string? BrokerInChargeName { get; set; }

    public RegulatoryDataSnapshot? Snapshot { get; set; }
    public RegulatoryFirm? Firm { get; set; }
    public RegulatoryLicensee? Licensee { get; set; }
}

public class UserRegulatoryLicenseLink
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserAccountId { get; set; }
    public Guid RegulatorySnapshotId { get; set; }
    public Guid RegulatoryLicenseeId { get; set; }
    public DateTime VerifiedAtUtc { get; set; } = DateTime.UtcNow;
    public Guid? VerifiedByUserId { get; set; }

    public UserAccount? UserAccount { get; set; }
    public RegulatoryDataSnapshot? RegulatorySnapshot { get; set; }
    public RegulatoryLicensee? RegulatoryLicensee { get; set; }
}

public class CourseEligibilityPolicy
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required, StringLength(40)]
    public string State { get; set; } = string.Empty;

    [StringLength(20)]
    public string? LicenseLevel { get; set; }

    [Required, StringLength(40)]
    public string ComplianceType { get; set; } = string.Empty;

    [StringLength(40)]
    public string? ContinuingEducationType { get; set; }

    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveThrough { get; set; }

    [Required, StringLength(500)]
    public string EligibilityMessage { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;
}