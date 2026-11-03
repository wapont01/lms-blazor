using System.Collections.Concurrent;
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
    Task<List<string>> GetSnapshotStatesAsync();
    Task<RegulatorySnapshotPage> GetSnapshotPageAsync(string state, DateOnly? fromDate = null, DateOnly? toDate = null, int page = 1, int pageSize = 20);
    Task<List<RegulatorySnapshotSummary>> GetFloridaRosterHistoryAsync(int page = 1, string state = "Florida");
    Task<List<RosterSelection>> GetFloridaRosterRecordsAsync(Guid snapshotId, int page = 1, string state = "Florida");
    Task RestoreFloridaRosterAsync(Guid snapshotId, Guid expectedCurrentId, Guid actorUserId, string reason, string state = "Florida");
    Task<List<string>> GetCampaignStateOptionsAsync();
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
    string SourceUrl,
    DateTime RetrievedAtUtc,
    decimal? AcquisitionCost,
    string Status,
    int FirmCount,
    int LicenseeCount,
    DateTime? ReviewedAtUtc,
    string? ParserVersion = null,
    DateTime? ActivatedAtUtc = null);

public sealed record RegulatorySnapshotPage(
    IReadOnlyList<RegulatorySnapshotSummary> Items,
    int TotalCount,
    int Page,
    int PageSize)
{
    public int PageCount => Math.Max(1, (TotalCount + PageSize - 1) / PageSize);
}

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
    Guid? LinkedUserId,
    string? SourceLicenseType = null);

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
    private static readonly string[] UnitedStatesStates =
    [
        "Alabama", "Alaska", "Arizona", "Arkansas", "California", "Colorado", "Connecticut", "Delaware", "Florida", "Georgia",
        "Hawaii", "Idaho", "Illinois", "Indiana", "Iowa", "Kansas", "Kentucky", "Louisiana", "Maine", "Maryland",
        "Massachusetts", "Michigan", "Minnesota", "Mississippi", "Missouri", "Montana", "Nebraska", "Nevada", "New Hampshire", "New Jersey",
        "New Mexico", "New York", "North Carolina", "North Dakota", "Ohio", "Oklahoma", "Oregon", "Pennsylvania", "Rhode Island", "South Carolina",
        "South Dakota", "Tennessee", "Texas", "Utah", "Vermont", "Virginia", "Washington", "West Virginia", "Wisconsin", "Wyoming"
    ];

    private readonly ApplicationDbContext _dbContext;
    private readonly IAuditLogService _auditLogService;
    private readonly IColoradoDoraLicenseTypeSelection? _coloradoDoraTypeSelection;

    public RegulatoryDirectoryService(ApplicationDbContext dbContext, IAuditLogService auditLogService,
        IColoradoDoraLicenseTypeSelection? coloradoDoraTypeSelection = null)
    {
        _dbContext = dbContext;
        _auditLogService = auditLogService;
        _coloradoDoraTypeSelection = coloradoDoraTypeSelection;
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

        return await RegulatoryRoster.Published(_dbContext.RegulatoryDataSnapshots.AsNoTracking()
            .Where(snapshot => snapshot.State == state.Trim()))
            .FirstOrDefaultAsync();
    }

    public async Task<List<RegulatorySnapshotSummary>> GetRecentSnapshotsAsync(int take = 25) =>
        await WithCountsAsync(_dbContext.RegulatoryDataSnapshots
            .AsNoTracking()
            .OrderByDescending(snapshot => snapshot.RetrievedAtUtc)
            .ThenByDescending(snapshot => snapshot.ImportedAtUtc)
            .Take(Math.Clamp(take, 1, 100)));

    public Task<List<string>> GetSnapshotStatesAsync() =>
        _dbContext.RegulatoryDataSnapshots.AsNoTracking()
            .Select(snapshot => snapshot.State).Distinct().OrderBy(state => state).ToListAsync();

    public async Task<RegulatorySnapshotPage> GetSnapshotPageAsync(string state, DateOnly? fromDate = null, DateOnly? toDate = null, int page = 1, int pageSize = 20)
    {
        pageSize = Math.Clamp(pageSize, 1, 100);
        var query = _dbContext.RegulatoryDataSnapshots.AsNoTracking().Where(snapshot => snapshot.State == state);
        if (fromDate is DateOnly from)
        {
            var fromUtc = from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            query = query.Where(snapshot => snapshot.RetrievedAtUtc >= fromUtc);
        }
        if (toDate is DateOnly to)
        {
            var beforeUtc = to.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            query = query.Where(snapshot => snapshot.RetrievedAtUtc < beforeUtc);
        }

        var total = await query.CountAsync();
        var pageCount = Math.Max(1, (total + pageSize - 1) / pageSize);
        page = Math.Clamp(page, 1, pageCount);
        var items = await WithCountsAsync(query
            .OrderByDescending(snapshot => snapshot.RetrievedAtUtc)
            .ThenByDescending(snapshot => snapshot.ImportedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize));
        return new RegulatorySnapshotPage(items, total, page, pageSize);
    }

    public Task<List<RegulatorySnapshotSummary>> GetFloridaRosterHistoryAsync(int page = 1, string state = "Florida") =>
        WithCountsAsync(RegulatoryRoster.Published(_dbContext.RegulatoryDataSnapshots.AsNoTracking().Where(snapshot =>
            snapshot.State == state && snapshot.ParserVersion == RegulatoryRoster.BulkParser(state)))
            .Skip((Math.Clamp(page, 1, 10000) - 1) * 20).Take(21));

    // Counting rows in multi-hundred-thousand-row bulk snapshots dominated directory load time. Approved bulk
    // snapshots are written in one transaction and never change afterwards, so their counts are cached per process.
    private static readonly ConcurrentDictionary<Guid, (int Firms, int Licensees)> ImmutableSnapshotCounts = new();

    private async Task<List<RegulatorySnapshotSummary>> WithCountsAsync(IQueryable<RegulatoryDataSnapshot> snapshots)
    {
        var rows = await snapshots.Select(snapshot => new RegulatorySnapshotSummary(snapshot.Id, snapshot.State,
            snapshot.SourceName, snapshot.SourceUrl, snapshot.RetrievedAtUtc, snapshot.AcquisitionCost, snapshot.Status,
            0, 0, snapshot.ReviewedAtUtc, snapshot.ParserVersion, snapshot.ActivatedAtUtc)).ToListAsync();
        var missing = rows.Where(row => !ImmutableSnapshotCounts.ContainsKey(row.SnapshotId)).Select(row => row.SnapshotId).ToList();
        var counted = missing.Count == 0
            ? new Dictionary<Guid, (int Firms, int Licensees)>()
            : (await _dbContext.RegulatoryDataSnapshots.AsNoTracking().Where(snapshot => missing.Contains(snapshot.Id))
                .Select(snapshot => new { snapshot.Id, Firms = snapshot.Firms.Count, Licensees = snapshot.Licensees.Count })
                .ToListAsync()).ToDictionary(item => item.Id, item => (item.Firms, item.Licensees));
        return rows.Select(row =>
        {
            if (!ImmutableSnapshotCounts.TryGetValue(row.SnapshotId, out var counts) && counted.TryGetValue(row.SnapshotId, out counts)
                && row.Status == RegulatorySnapshotStatuses.Approved && RegulatoryRoster.HasBulkProvider(row.State)
                && row.ParserVersion == RegulatoryRoster.BulkParser(row.State))
            {
                ImmutableSnapshotCounts[row.SnapshotId] = counts;
            }
            return row with { FirmCount = counts.Firms, LicenseeCount = counts.Licensees };
        }).ToList();
    }

    public Task<List<RosterSelection>> GetFloridaRosterRecordsAsync(Guid snapshotId, int page = 1, string state = "Florida")
    {
        var query = _dbContext.RegulatoryLicensees.AsNoTracking().Where(record =>
            record.SnapshotId == snapshotId && record.Snapshot != null && record.State == state
            && record.Snapshot.State == state && record.Snapshot.ParserVersion == RegulatoryRoster.BulkParser(state)
            && record.Snapshot.Status == RegulatorySnapshotStatuses.Approved);
        if (state == "Colorado" && _coloradoDoraTypeSelection is not null)
        {
            var enabled = ColoradoDoraLicenseTypes.EnabledRealEstateTypes(_coloradoDoraTypeSelection.GetSelected());
            query = query.Where(record => record.Snapshot!.ParserVersion != ColoradoDoraBulkImportService.ParserVersion
                || record.SourceLicenseType != null && enabled.Contains(record.SourceLicenseType));
        }

        return query.OrderBy(record => record.Name).ThenBy(record => record.LicenseNumber).ThenBy(record => record.Id)
            .Skip((Math.Clamp(page, 1, 100000) - 1) * 25).Take(26)
            .Select(record => new RosterSelection(record.Id, record.State, record.LicenseNumber, record.Name,
                record.LicenseType, record.LicenseStatus, record.Snapshot!.SourceName,
                record.Snapshot.SourceUrl, record.Snapshot.RetrievedAtUtc)).ToListAsync();
    }

    public async Task RestoreFloridaRosterAsync(Guid snapshotId, Guid expectedCurrentId, Guid actorUserId, string reason, string state = "Florida")
    {
        var parser = RegulatoryRoster.BulkParser(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        reason = reason.Trim();
        if (reason.Length > 1000) throw new InvalidOperationException("Restore reason must be at most 1,000 characters.");
        var actor = await _dbContext.UserAccounts.AsNoTracking()
            .SingleOrDefaultAsync(user => user.Id == actorUserId && user.IsActive && user.Role == "Admin")
            ?? throw new InvalidOperationException("An active administrator is required to restore a roster.");
        await using var transaction = await _dbContext.Database.BeginTransactionAsync();
        var current = await GetLatestApprovedSnapshotAsync(state);
        if (current?.Id != expectedCurrentId)
            throw new InvalidOperationException("The current roster changed. Refresh history before restoring.");
        var target = await _dbContext.RegulatoryDataSnapshots.SingleOrDefaultAsync(snapshot =>
            snapshot.Id == snapshotId && snapshot.State == state
            && snapshot.ParserVersion == parser
            && snapshot.Status == RegulatorySnapshotStatuses.Approved)
            ?? throw new InvalidOperationException("Only a validated, synchronized bulk roster for this state can be restored.");
        if (target.Id == current.Id) throw new InvalidOperationException("This roster is already current.");
        if (!await _dbContext.RegulatoryLicensees.AnyAsync(record => record.SnapshotId == target.Id))
            throw new InvalidOperationException("An empty roster cannot be restored.");
        target.ActivatedAtUtc = DateTime.UtcNow;
        await _auditLogService.WriteAsync(actor.Id, actor.Email, "regulatory-roster.restored",
            "RegulatoryDataSnapshot", target.Id, $"Previous={current.Id};Reason={reason}");
        await _dbContext.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    public async Task<List<string>> GetCampaignStateOptionsAsync()
    {
        var approvedStates = await _dbContext.RegulatoryDataSnapshots
            .AsNoTracking()
            .Where(snapshot => snapshot.Status == RegulatorySnapshotStatuses.Approved)
            .Select(snapshot => snapshot.State)
            .Distinct()
            .ToListAsync();
        var states = new HashSet<string>(UnitedStatesStates, StringComparer.OrdinalIgnoreCase);
        foreach (var state in approvedStates.Where(state => !string.IsNullOrWhiteSpace(state)))
        {
            states.Add(state.Trim());
        }

        return states.OrderBy(state => state, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public async Task<List<RegulatoryLicenseeSummary>> GetApprovedLicenseesAsync(string? state = null, int take = 250)
    {
        var query = _dbContext.RegulatoryLicensees
            .AsNoTracking()
            .Include(licensee => licensee.Snapshot)
            .Include(licensee => licensee.FirmAffiliations)
                .ThenInclude(affiliation => affiliation.Firm)
            .Where(licensee => licensee.Snapshot!.Status == RegulatorySnapshotStatuses.Approved
                && (licensee.State != "Florida" && licensee.State != "Texas" && licensee.State != "California" && licensee.State != "Colorado" && licensee.State != "Nevada" && licensee.State != "New York" && licensee.State != "Arizona" && licensee.State != "Connecticut" && licensee.State != "Oregon" || licensee.SnapshotId == _dbContext.RegulatoryDataSnapshots
                    .Where(snapshot => snapshot.State == licensee.State && snapshot.Status == RegulatorySnapshotStatuses.Approved)
                    .OrderByDescending(snapshot => snapshot.ActivatedAtUtc ?? snapshot.RetrievedAtUtc).ThenByDescending(snapshot => snapshot.ImportedAtUtc).ThenBy(snapshot => snapshot.Id)
                    .Select(snapshot => snapshot.Id).First()));
        if (!string.IsNullOrWhiteSpace(state))
        {
            query = query.Where(licensee => licensee.State == state.Trim());
        }
        var enabledColoradoLicenseTypes = _coloradoDoraTypeSelection is null
            ? null
            : ColoradoDoraLicenseTypes.EnabledRealEstateTypes(_coloradoDoraTypeSelection.GetSelected());
        if (enabledColoradoLicenseTypes is not null)
        {
            query = query.Where(licensee => licensee.State != "Colorado"
                || licensee.Snapshot!.ParserVersion != ColoradoDoraBulkImportService.ParserVersion
                || licensee.SourceLicenseType != null && enabledColoradoLicenseTypes.Contains(licensee.SourceLicenseType));
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
                links.GetValueOrDefault(licensee.Id),
                licensee.SourceLicenseType);
        }).ToList();
    }

    public async Task<List<CampaignFirmTarget>> GetCampaignFirmTargetsAsync(string? state = null, int take = 100)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var horizon = today.AddYears(1);
        var maxTargets = Math.Clamp(take, 1, 100_000);
        var query = _dbContext.RegulatoryFirms
            .AsNoTracking()
            .Include(firm => firm.Snapshot)
            .Include(firm => firm.LicenseeAffiliations)
                .ThenInclude(affiliation => affiliation.Licensee)
            .Where(firm => firm.Snapshot!.Status == RegulatorySnapshotStatuses.Approved
                && firm.Industry == RegulatoryIndustries.RealEstate
                && (firm.State != "Florida" && firm.State != "Texas" && firm.State != "California" && firm.State != "Colorado" && firm.State != "Nevada" && firm.State != "New York" && firm.State != "Arizona" && firm.State != "Connecticut" && firm.State != "Oregon" || firm.SnapshotId == _dbContext.RegulatoryDataSnapshots
                    .Where(snapshot => snapshot.State == firm.State && snapshot.Status == RegulatorySnapshotStatuses.Approved)
                    .OrderByDescending(snapshot => snapshot.ActivatedAtUtc ?? snapshot.RetrievedAtUtc).ThenByDescending(snapshot => snapshot.ImportedAtUtc).ThenBy(snapshot => snapshot.Id)
                    .Select(snapshot => snapshot.Id).First()));
        if (!string.IsNullOrWhiteSpace(state))
        {
            query = query.Where(firm => firm.State == state.Trim());
        }

        const int batchSize = 1_000;
        var targets = new List<CampaignFirmTarget>(maxTargets);
        var offset = 0;
        while (true)
        {
            var firms = await query
                .OrderBy(firm => firm.Id)
                .Skip(offset)
                .Take(batchSize)
                .ToListAsync();
            if (firms.Count == 0)
            {
                break;
            }

            offset += firms.Count;
            targets.AddRange(firms.Select(firm => BuildCampaignFirmTarget(firm, today, horizon)));
            targets = targets
            .OrderBy(target => target.NextRenewalDate ?? DateOnly.MaxValue)
            .ThenBy(target => target.State)
            .ThenBy(target => target.FirmName)
                .Take(maxTargets)
                .ToList();
        }

        return targets;
    }

    private static CampaignFirmTarget BuildCampaignFirmTarget(RegulatoryFirm firm, DateOnly today, DateOnly horizon)
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
    }

    public async Task<bool> LinkLicenseeToUserAsync(Guid userId, Guid licenseeId, Guid actorUserId, string actorEmail)
    {
        var user = await _dbContext.UserAccounts.FirstOrDefaultAsync(candidate => candidate.Id == userId);
        var licensee = await _dbContext.RegulatoryLicensees
            .Include(candidate => candidate.Snapshot)
            .FirstOrDefaultAsync(candidate => candidate.Id == licenseeId);
        if (user is null || licensee is null
            || licensee.Snapshot?.Status != RegulatorySnapshotStatuses.Approved
            || IsColoradoDoraLicenseeUnavailable(licensee))
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

        var snapshot = await RegulatoryRoster.Published(_dbContext.RegulatoryDataSnapshots
            .Where(candidate => candidate.State == normalizedState))
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

        if (normalizedState == "Colorado" && snapshot.ParserVersion == ColoradoDoraBulkImportService.ParserVersion)
            candidates = candidates.Where(candidate => !IsColoradoDoraLicenseeUnavailable(candidate, isDoraSnapshot: true)).ToList();

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

    private bool IsColoradoDoraLicenseeUnavailable(RegulatoryLicensee licensee, bool isDoraSnapshot = false)
    {
        if (licensee.State != "Colorado"
            || (!isDoraSnapshot && licensee.Snapshot?.ParserVersion != ColoradoDoraBulkImportService.ParserVersion))
            return false;

        var sourceType = licensee.SourceLicenseType;
        if (sourceType is null)
            return true;
        var option = ColoradoDoraLicenseTypes.All.FirstOrDefault(candidate => candidate.Name == sourceType);
        if (option is not { IsRealEstate: true, IsIndividual: true })
            return true;
        return _coloradoDoraTypeSelection is not null
            && !_coloradoDoraTypeSelection.GetSelected().Contains(sourceType);
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