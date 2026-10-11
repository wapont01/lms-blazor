using Lms.Application.Data;
using Lms.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Lms.Application.Services;

public interface IRegulatoryDirectoryService
{
    Task<RegulatoryDataSnapshot> ImportAsync(RegulatorySnapshotImport input);
    Task<bool> ApproveAsync(Guid snapshotId, Guid actorUserId, string actorEmail);
    Task<RegulatoryDataSnapshot?> GetLatestApprovedSnapshotAsync(string state);
    Task<List<RegulatorySnapshotSummary>> GetRecentSnapshotsAsync(int take = 25);
    Task<List<RegulatoryLicenseeSummary>> GetApprovedLicenseesAsync(string? state = null, int take = 250);
    Task<List<CampaignFirmTarget>> GetCampaignFirmTargetsAsync(string? state = null, int take = 100);
    Task<bool> LinkLicenseeToUserAsync(Guid userId, Guid licenseeId, Guid actorUserId, string actorEmail);
    Task<bool> TryAutoLinkLicenseAsync(Guid userId, string? state, string? licenseNumber);
}

public sealed record RegulatorySnapshotImport(
    string State,
    string SourceName,
    string SourceUrl,
    DateTime RetrievedAtUtc,
    decimal? AcquisitionCost,
    string? ParserVersion,
    IReadOnlyCollection<RegulatoryFirmImport> Firms);

public sealed record RegulatoryFirmImport(
    string LicenseNumber,
    string LegalName,
    string? TradeName,
    string? LicenseStatus,
    string? Address,
    string? QualifyingBrokerName,
    IReadOnlyCollection<RegulatoryLicenseeImport> Licensees,
    DateOnly? LicenseRenewalDate = null);

public sealed record RegulatoryLicenseeImport(
    string LicenseNumber,
    string Name,
    string? LicenseType,
    string? LicenseLevel,
    string? LicenseStatus,
    DateOnly? LicenseRenewalDate,
    int? UpdateHours,
    int? ElectiveHours,
    int? PostlicensingHours,
    bool IsBicEligible,
    bool IsBrokerInCharge,
    string? BrokerInChargeName);

public sealed record RegulatorySnapshotSummary(
    Guid SnapshotId,
    string State,
    string SourceName,
    DateTime RetrievedAtUtc,
    decimal? AcquisitionCost,
    string Status,
    int FirmCount,
    int LicenseeCount,
    DateTime? ReviewedAtUtc);

public sealed record RegulatoryLicenseeSummary(
    Guid LicenseeId,
    string State,
    string LicenseNumber,
    string Name,
    string? LicenseType,
    string? LicenseLevel,
    string? LicenseStatus,
    DateOnly? LicenseIssuedDate,
    DateOnly? LicenseRenewalDate,
    string? FirmName,
    string? FirmLicenseNumber,
    bool IsBrokerInCharge,
    Guid? LinkedUserId);

public sealed record CampaignFirmTarget(
    string State,
    string FirmName,
    string FirmLicenseNumber,
    string? TradeName,
    string? BrokerName,
    string? Address,
    int LicenseeCount,
    DateOnly? NextRenewalDate,
    string? RenewalDateSource,
    int RenewalsWithinTwelveMonths);

public sealed class RegulatoryDirectoryService : IRegulatoryDirectoryService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IAuditLogService _auditLogService;

    public RegulatoryDirectoryService(ApplicationDbContext dbContext, IAuditLogService auditLogService)
    {
        _dbContext = dbContext;
        _auditLogService = auditLogService;
    }

    public async Task<RegulatoryDataSnapshot> ImportAsync(RegulatorySnapshotImport input)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(input.State);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.SourceName);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.SourceUrl);
        ArgumentNullException.ThrowIfNull(input.Firms);

        var state = input.State.Trim();
        var retrievedAtUtc = DateTime.SpecifyKind(input.RetrievedAtUtc, DateTimeKind.Utc);
        var existingSnapshot = await _dbContext.RegulatoryDataSnapshots
            .FirstOrDefaultAsync(snapshot => snapshot.State == state
                && snapshot.SourceUrl == input.SourceUrl.Trim()
                && snapshot.RetrievedAtUtc == retrievedAtUtc);
        if (existingSnapshot is not null)
        {
            return existingSnapshot;
        }

        var snapshot = new RegulatoryDataSnapshot
        {
            State = state,
            SourceName = input.SourceName.Trim(),
            SourceUrl = input.SourceUrl.Trim(),
            RetrievedAtUtc = retrievedAtUtc,
            AcquisitionCost = input.AcquisitionCost,
            ParserVersion = string.IsNullOrWhiteSpace(input.ParserVersion) ? null : input.ParserVersion.Trim(),
            Status = RegulatorySnapshotStatuses.PendingReview
        };

        var firmsByLicense = new Dictionary<string, RegulatoryFirm>(StringComparer.OrdinalIgnoreCase);
        var licenseesByLicense = new Dictionary<string, RegulatoryLicensee>(StringComparer.OrdinalIgnoreCase);
        foreach (var firmInput in input.Firms)
        {
            if (string.IsNullOrWhiteSpace(firmInput.LicenseNumber) || string.IsNullOrWhiteSpace(firmInput.LegalName))
            {
                throw new InvalidOperationException("Every imported firm must include a license number and legal name.");
            }

            var firmLicenseNumber = firmInput.LicenseNumber.Trim();
            var firm = new RegulatoryFirm
            {
                Snapshot = snapshot,
                State = state,
                LicenseNumber = firmLicenseNumber,
                LegalName = firmInput.LegalName.Trim(),
                TradeName = Normalize(firmInput.TradeName),
                LicenseStatus = Normalize(firmInput.LicenseStatus),
                Address = Normalize(firmInput.Address),
                QualifyingBrokerName = Normalize(firmInput.QualifyingBrokerName),
                LicenseRenewalDate = firmInput.LicenseRenewalDate
            };
            if (!firmsByLicense.TryAdd(firmLicenseNumber, firm))
                {
                throw new InvalidOperationException($"The import contains duplicate firm license '{firmLicenseNumber}'.");
            }

            snapshot.Firms.Add(firm);
            foreach (var licenseeInput in firmInput.Licensees)
            {
                if (string.IsNullOrWhiteSpace(licenseeInput.LicenseNumber) || string.IsNullOrWhiteSpace(licenseeInput.Name))
                {
                    throw new InvalidOperationException("Every imported licensee must include a license number and name.");
                }

                var licenseNumber = licenseeInput.LicenseNumber.Trim();
                if (!licenseesByLicense.TryGetValue(licenseNumber, out var licensee))
                {
                    licensee = new RegulatoryLicensee
                    {
                        Snapshot = snapshot,
                        State = state,
                        LicenseNumber = licenseNumber,
                        Name = licenseeInput.Name.Trim(),
                        LicenseType = Normalize(licenseeInput.LicenseType),
                        LicenseLevel = Normalize(licenseeInput.LicenseLevel),
                        LicenseStatus = Normalize(licenseeInput.LicenseStatus),
                        LicenseRenewalDate = licenseeInput.LicenseRenewalDate,
                        UpdateHours = licenseeInput.UpdateHours,
                        ElectiveHours = licenseeInput.ElectiveHours,
                        PostlicensingHours = licenseeInput.PostlicensingHours,
                        IsBicEligible = licenseeInput.IsBicEligible
                    };
                    licenseesByLicense.Add(licenseNumber, licensee);
                    snapshot.Licensees.Add(licensee);
                }

                firm.LicenseeAffiliations.Add(new RegulatoryFirmLicenseeAffiliation
                {
                    Snapshot = snapshot,
                    Licensee = licensee,
                    IsBrokerInCharge = licenseeInput.IsBrokerInCharge,
                    BrokerInChargeName = Normalize(licenseeInput.BrokerInChargeName)
                });
            }
        }

        _dbContext.RegulatoryDataSnapshots.Add(snapshot);
        await _dbContext.SaveChangesAsync();
        return snapshot;
    }

    public async Task<bool> ApproveAsync(Guid snapshotId, Guid actorUserId, string actorEmail)
    {
        var snapshot = await _dbContext.RegulatoryDataSnapshots
            .Include(existing => existing.Firms)
            .Include(existing => existing.Licensees)
            .FirstOrDefaultAsync(existing => existing.Id == snapshotId);

        if (snapshot is null
            || snapshot.Status != RegulatorySnapshotStatuses.PendingReview
            || (snapshot.Firms.Count == 0 && snapshot.Licensees.Count == 0))
        {
            return false;
        }

        snapshot.Status = RegulatorySnapshotStatuses.Approved;
        snapshot.ReviewedAtUtc = DateTime.UtcNow;
        snapshot.ReviewedByUserId = actorUserId;
        await _dbContext.SaveChangesAsync();
        await _auditLogService.WriteAsync(actorUserId, actorEmail, "regulatory-snapshot.approved", "RegulatoryDataSnapshot", snapshot.Id, $"State={snapshot.State}; RetrievedAtUtc={snapshot.RetrievedAtUtc:O}");
        return true;
    }

    public async Task<RegulatoryDataSnapshot?> GetLatestApprovedSnapshotAsync(string state)
    {
        if (string.IsNullOrWhiteSpace(state))
        {
            return null;
        }

        return await _dbContext.RegulatoryDataSnapshots
            .AsNoTracking()
            .Where(snapshot => snapshot.State == state.Trim() && snapshot.Status == RegulatorySnapshotStatuses.Approved)
            .OrderByDescending(snapshot => snapshot.RetrievedAtUtc)
            .ThenByDescending(snapshot => snapshot.ImportedAtUtc)
            .FirstOrDefaultAsync();
    }

    public async Task<List<RegulatorySnapshotSummary>> GetRecentSnapshotsAsync(int take = 25)
    {
        return await _dbContext.RegulatoryDataSnapshots
            .AsNoTracking()
            .OrderByDescending(snapshot => snapshot.RetrievedAtUtc)
            .ThenByDescending(snapshot => snapshot.ImportedAtUtc)
            .Take(Math.Clamp(take, 1, 100))
            .Select(snapshot => new RegulatorySnapshotSummary(
                snapshot.Id,
                snapshot.State,
                snapshot.SourceName,
                snapshot.RetrievedAtUtc,
                snapshot.AcquisitionCost,
                snapshot.Status,
                snapshot.Firms.Count,
                snapshot.Licensees.Count,
                snapshot.ReviewedAtUtc))
            .ToListAsync();
    }

    public async Task<List<RegulatoryLicenseeSummary>> GetApprovedLicenseesAsync(string? state = null, int take = 250)
    {
        var query = _dbContext.RegulatoryLicensees
            .AsNoTracking()
            .Include(licensee => licensee.Snapshot)
            .Include(licensee => licensee.FirmAffiliations)
                .ThenInclude(affiliation => affiliation.Firm)
            .Where(licensee => licensee.Snapshot!.Status == RegulatorySnapshotStatuses.Approved);
        if (!string.IsNullOrWhiteSpace(state))
        {
            query = query.Where(licensee => licensee.State == state.Trim());
        }

        var licensees = await query
            .OrderBy(licensee => licensee.State)
            .ThenBy(licensee => licensee.Name)
            .Take(Math.Clamp(take, 1, 1000))
            .ToListAsync();
        var licenseeIds = licensees.Select(licensee => licensee.Id).ToList();
        var links = await _dbContext.UserRegulatoryLicenseLinks
            .AsNoTracking()
            .Where(link => licenseeIds.Contains(link.RegulatoryLicenseeId))
            .ToDictionaryAsync(link => link.RegulatoryLicenseeId, link => link.UserAccountId);

        return licensees.Select(licensee =>
        {
            var affiliation = licensee.FirmAffiliations.FirstOrDefault();
            return new RegulatoryLicenseeSummary(
                licensee.Id,
                licensee.State,
                licensee.LicenseNumber,
                licensee.Name,
                licensee.LicenseType,
                licensee.LicenseLevel,
                licensee.LicenseStatus,
                licensee.LicenseIssuedDate,
                licensee.LicenseRenewalDate,
                affiliation?.Firm?.LegalName,
                affiliation?.Firm?.LicenseNumber,
                affiliation?.IsBrokerInCharge ?? false,
                links.GetValueOrDefault(licensee.Id));
        }).ToList();
    }

    public async Task<List<CampaignFirmTarget>> GetCampaignFirmTargetsAsync(string? state = null, int take = 100)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var horizon = today.AddYears(1);
        var query = _dbContext.RegulatoryFirms
            .AsNoTracking()
            .Include(firm => firm.Snapshot)
            .Include(firm => firm.LicenseeAffiliations)
                .ThenInclude(affiliation => affiliation.Licensee)
            .Where(firm => firm.Snapshot!.Status == RegulatorySnapshotStatuses.Approved);
        if (!string.IsNullOrWhiteSpace(state))
        {
            query = query.Where(firm => firm.State == state.Trim());
        }

        var firms = await query.ToListAsync();
        return firms
            .Select(firm =>
            {
                var licensees = firm.LicenseeAffiliations
                    .Where(affiliation => affiliation.Licensee is not null)
                    .Select(affiliation => affiliation.Licensee!)
                    .DistinctBy(licensee => licensee.Id)
                    .ToList();
                var upcomingRenewals = licensees
                    .Select(licensee =>
                    {
                        if (licensee.LicenseRenewalDate is DateOnly authoritativeDate)
                        {
                            return (Date: (DateOnly?)authoritativeDate, Source: LicenseDateProvenance.Authoritative);
                        }

                        if (StateLicenseRenewalPolicyCatalog.TryCalculateNextRenewalDate(
                            licensee.State,
                            licensee.LicenseType,
                            today,
                            out var derivedDate)
                            || StateLicenseRenewalPolicyCatalog.TryCalculateNextRenewalDate(
                                licensee.State,
                                licensee.LicenseLevel,
                                today,
                                out derivedDate))
                        {
                            return (Date: (DateOnly?)derivedDate, Source: LicenseDateProvenance.Derived);
                        }

                        return (Date: (DateOnly?)null, Source: (string?)null);
                    })
                    .Where(renewal => renewal.Date.HasValue && renewal.Date.Value >= today)
                    .ToList();
                if (firm.LicenseRenewalDate is DateOnly firmRenewalDate && firmRenewalDate >= today)
                {
                    upcomingRenewals.Add((firmRenewalDate, LicenseDateProvenance.Authoritative));
                }

                upcomingRenewals = upcomingRenewals
                    .OrderBy(renewal => renewal.Date)
                    .ThenBy(renewal => renewal.Source == LicenseDateProvenance.Authoritative ? 0 : 1)
                    .ToList();
                var nextRenewal = upcomingRenewals.FirstOrDefault();
                var brokerName = firm.QualifyingBrokerName
                    ?? firm.LicenseeAffiliations.FirstOrDefault(affiliation => affiliation.IsBrokerInCharge)?.Licensee?.Name
                    ?? firm.LicenseeAffiliations.Select(affiliation => affiliation.BrokerInChargeName).FirstOrDefault(name => name is not null);
                return new CampaignFirmTarget(
                    firm.State,
                    firm.LegalName,
                    firm.LicenseNumber,
                    firm.TradeName,
                    brokerName,
                    firm.Address,
                    licensees.Count,
                    nextRenewal.Date,
                    nextRenewal.Source,
                    upcomingRenewals.Count(renewal => renewal.Date <= horizon));
            })
            .OrderBy(target => target.NextRenewalDate ?? DateOnly.MaxValue)
            .ThenBy(target => target.State)
            .ThenBy(target => target.FirmName)
            .Take(Math.Clamp(take, 1, 10_000))
            .ToList();
    }

    public async Task<bool> LinkLicenseeToUserAsync(Guid userId, Guid licenseeId, Guid actorUserId, string actorEmail)
    {
        var user = await _dbContext.UserAccounts.FirstOrDefaultAsync(candidate => candidate.Id == userId);
        var licensee = await _dbContext.RegulatoryLicensees
            .Include(candidate => candidate.Snapshot)
            .FirstOrDefaultAsync(candidate => candidate.Id == licenseeId);
        if (user is null || licensee?.Snapshot?.Status != RegulatorySnapshotStatuses.Approved)
        {
            return false;
        }

        await ApplyLinkAsync(user, licensee, actorUserId, actorEmail, "regulatory-license.linked");
        return true;
    }

    public async Task<bool> TryAutoLinkLicenseAsync(Guid userId, string? state, string? licenseNumber)
    {
        var normalizedState = Normalize(state);
        var normalizedLicense = Normalize(licenseNumber)?.ToUpperInvariant();
        if (normalizedState is null || normalizedLicense is null)
        {
            return false;
        }

        var user = await _dbContext.UserAccounts.FirstOrDefaultAsync(candidate => candidate.Id == userId);
        if (user is null)
        {
            return false;
        }

        var snapshot = await _dbContext.RegulatoryDataSnapshots
            .Where(candidate => candidate.Status == RegulatorySnapshotStatuses.Approved
                && candidate.State == normalizedState)
            .OrderByDescending(candidate => candidate.RetrievedAtUtc)
            .ThenByDescending(candidate => candidate.ImportedAtUtc)
            .FirstOrDefaultAsync();
        if (snapshot is null)
        {
            return false;
        }

        var candidates = await _dbContext.RegulatoryLicensees
            .Where(candidate => candidate.SnapshotId == snapshot.Id
                && candidate.State == normalizedState
                && candidate.LicenseNumber == normalizedLicense)
            .ToListAsync();

        if (user.LicenseType is not null)
        {
            candidates = candidates
                .Where(candidate => string.Equals(candidate.LicenseType, user.LicenseType, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        if (user.LicenseStatus is not null)
        {
            candidates = candidates
                .Where(candidate => string.Equals(candidate.LicenseStatus, user.LicenseStatus, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        if (user.InitialLicensureDate.HasValue)
        {
            var issuedDate = DateOnly.FromDateTime(user.InitialLicensureDate.Value);
            candidates = candidates.Where(candidate => candidate.LicenseIssuedDate == issuedDate).ToList();
        }

        if (user.LicenseRenewalDueDate.HasValue)
        {
            var renewalDate = DateOnly.FromDateTime(user.LicenseRenewalDueDate.Value);
            candidates = candidates.Where(candidate => candidate.LicenseRenewalDate == renewalDate).ToList();
        }

        if (candidates.Count != 1)
        {
            return false;
        }

        var licensee = candidates[0];
        await ApplyLinkAsync(user, licensee, null, user.Email, "regulatory-license.auto-linked");
        return true;
    }

    private async Task ApplyLinkAsync(UserAccount user, RegulatoryLicensee licensee, Guid? actorUserId, string actorEmail, string auditAction)
    {
        var link = await _dbContext.UserRegulatoryLicenseLinks
            .SingleOrDefaultAsync(candidate => candidate.UserAccountId == user.Id);
        if (link is null)
        {
            _dbContext.UserRegulatoryLicenseLinks.Add(new UserRegulatoryLicenseLink
            {
                UserAccountId = user.Id,
                RegulatorySnapshotId = licensee.SnapshotId,
                RegulatoryLicenseeId = licensee.Id,
                VerifiedAtUtc = DateTime.UtcNow,
                VerifiedByUserId = actorUserId
            });
        }
        else
        {
            link.RegulatorySnapshotId = licensee.SnapshotId;
            link.RegulatoryLicenseeId = licensee.Id;
            link.VerifiedAtUtc = DateTime.UtcNow;
            link.VerifiedByUserId = actorUserId;
        }

        user.State = licensee.State;
        user.LicenseNumber = licensee.LicenseNumber;
        user.LicenseType = licensee.LicenseType;
        user.LicenseStatus = licensee.LicenseStatus;
        user.InitialLicensureDate = licensee.LicenseIssuedDate?.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        user.LicenseRenewalDueDate = licensee.LicenseRenewalDate?.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        user.LicenseRenewalDueDateSource = user.LicenseRenewalDueDate.HasValue
            ? LicenseDateProvenance.Authoritative
            : LicenseDateProvenance.Unknown;
        await _dbContext.SaveChangesAsync();
        await _auditLogService.WriteAsync(actorUserId ?? user.Id, actorEmail, auditAction, "UserAccount", user.Id, $"State={licensee.State};License={licensee.LicenseNumber};Licensee={licensee.Name}");
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}