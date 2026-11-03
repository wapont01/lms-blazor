using Lms.Application.Data;
using Lms.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Lms.Application.Services;

public sealed record RosterSelection(
    Guid Id, string State, string LicenseNumber, string Name, string? LicenseType,
    string? LicenseStatus, string SourceName, string? SourceUrl, DateTime RetrievedAtUtc)
{
    public IReadOnlyList<RosterDetail> Details { get; init; } = [];
}

public sealed record RosterDetail(string Label, string Value);

public sealed record RosterSearch(string State, string LicenseNumber = "", string Name = "",
    string LicenseType = "", string Firm = "", int Page = 1);
public sealed record RosterSearchOptions(IReadOnlyList<string> LicenseTypes, bool HasFirmAffiliations)
{
    /// <summary>Imported non-real-estate types, listed but not selectable until those products launch.</summary>
    public IReadOnlyList<string> ComingLaterLicenseTypes { get; init; } = [];
}

public sealed record RosterRequestRow(
    Guid Id, Guid LearnerId, string LearnerEmail, RosterSelection Selection,
    string Status, bool EmailVerified, DateTime SubmittedAtUtc, string ReviewNote);

public sealed class RosterRequestException(string message) : InvalidOperationException(message);

public interface IRosterRegistrationService
{
    Task<List<string>> GetAvailableStatesAsync(CancellationToken ct = default);
    Task<List<RosterSelection>> SearchAsync(string state, string licenseNumber, CancellationToken ct = default, string? captchaToken = null);
    Task<CertificatePage<RosterSelection>> SearchAsync(RosterSearch search, CancellationToken ct = default, string? captchaToken = null);
    Task<RosterSelection> GetSelectionAsync(Guid licenseeId, CancellationToken ct = default);
    Task<RosterSearchOptions> GetSearchOptionsAsync(string state, CancellationToken ct = default);
    Task<UserAccount> RegisterAsync(Guid licenseeId, string email, string password, CancellationToken ct = default);
    Task SubmitAsync(Guid userId, Guid licenseeId, CancellationToken ct = default);
    Task<List<RosterRequestRow>> GetMineAsync(Guid userId, CancellationToken ct = default);
    Task<CertificatePage<RosterRequestRow>> GetReviewPageAsync(Guid actorId, string state, string status, int page, CancellationToken ct = default);
    Task ReviewAsync(Guid actorId, Guid requestId, bool approve, string note, CancellationToken ct = default);
}

public sealed class RosterRegistrationService(
    ApplicationDbContext db, IUserAccountService accounts, EmailJourneyOptions options,
    IAuditLogService audit, RosterSearchProtection? protection = null,
    RosterSearchSession? searchSession = null,
    IColoradoDoraLicenseTypeSelection? coloradoDoraTypeSelection = null) : IRosterRegistrationService
{
    private readonly RosterSearchProtection _protection = protection ?? new();
    private readonly RosterSearchSession _searchSession = searchSession ?? new();
    private readonly IColoradoDoraLicenseTypeSelection? _coloradoDoraTypeSelection = coloradoDoraTypeSelection;
    public async Task<List<string>> GetAvailableStatesAsync(CancellationToken ct = default)
    {
        if (!options.Enabled) return [];
        using var admission = _protection.Enter(this);
        var states = await db.RegulatoryDataSnapshots.AsNoTracking()
            .Where(snapshot => snapshot.Status == RegulatorySnapshotStatuses.Approved
                && db.RegulatoryLicensees.Any(record => record.SnapshotId == snapshot.Id))
            .Select(snapshot => snapshot.State).Distinct().OrderBy(state => state).ToListAsync(ct);
        return states.Where(IsSupportedState).ToList();
    }

    public async Task<RosterSearchOptions> GetSearchOptionsAsync(string state, CancellationToken ct = default)
    {
        using var admission = _protection.Enter(this);
        return await GetSearchOptionsCoreAsync(state, ct);
    }

    private async Task<RosterSearchOptions> GetSearchOptionsCoreAsync(string state, CancellationToken ct)
    {
        if (!options.Enabled || !IsSupportedState(state))
            return new([], false);
        var snapshotId = await LatestSnapshotIdAsync(state, ct);
        if (!snapshotId.HasValue) return new([], false);
        var snapshotQuery = db.RegulatoryLicensees.AsNoTracking()
            .Where(item => item.SnapshotId == snapshotId && item.State == state);
        var isColoradoDora = await IsColoradoDoraSnapshotAsync(snapshotId.Value, ct);
        var allTypes = isColoradoDora
            ? await snapshotQuery
                .Where(item => item.SourceLicenseType != null && item.SourceLicenseType != "")
                .Select(item => item.SourceLicenseType!).Distinct().ToListAsync(ct)
            : await snapshotQuery
            .Where(item => item.LicenseType != null && item.LicenseType != "")
            .Select(item => item.LicenseType!).Distinct().ToListAsync(ct);
        var coloradoSelection = isColoradoDora && _coloradoDoraTypeSelection is not null
            ? ColoradoDoraLicenseTypes.EnabledRealEstateTypes(_coloradoDoraTypeSelection.GetSelected()).ToHashSet(StringComparer.Ordinal)
            : null;
        var types = allTypes
            .Where(type => RegulatoryLicenseTypes.IsRealEstate(state, type)
                && (coloradoSelection is null || coloradoSelection.Contains(type)))
            .Order(StringComparer.Ordinal).ToList();
        var comingLater = allTypes.Where(type => !RegulatoryLicenseTypes.IsRealEstate(state, type))
            .Order(StringComparer.Ordinal).ToList();
        var hasFirms = await db.RegulatoryFirmLicenseeAffiliations.AnyAsync(item =>
            item.SnapshotId == snapshotId && item.Firm != null && item.Firm.State == state, ct);
        return new(types, hasFirms) { ComingLaterLicenseTypes = comingLater };
    }

    public async Task<CertificatePage<RosterSelection>> SearchAsync(RosterSearch search, CancellationToken ct = default, string? captchaToken = null)
    {
        using var admission = _protection.Enter(this);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(5));
        ct = deadline.Token;
        if (!options.Enabled || !IsSupportedState(search.State))
            throw new RosterRequestException("Select a supported state before searching.");
        var license = search.LicenseNumber.Trim().ToUpperInvariant();
        var name = search.Name.Trim().ToUpperInvariant();
        var firm = search.Firm.Trim().ToUpperInvariant();
        var type = search.LicenseType.Trim();
        if (license.Length == 0 && name.Length < 3)
            throw new RosterRequestException("Enter a license number or at least 3 characters of the name.");
        if (license.Length > 40 || name.Length > 160 || firm.Length > 160 || type.Length > 40)
            throw new RosterRequestException("Search values are too long.");
        using var submission = await _searchSession.EnterAsync(captchaToken, ct);
        var snapshotId = await LatestSnapshotIdAsync(search.State, ct);
        if (!snapshotId.HasValue) return new([], 0, 1, 20);
        if (firm.Length > 0 && !(await GetSearchOptionsCoreAsync(search.State, ct)).HasFirmAffiliations)
            throw new RosterRequestException("Firm filtering is not available in this state's current roster.");
        var query = db.RegulatoryLicensees.AsNoTracking()
            .Where(item => item.SnapshotId == snapshotId && item.State == search.State);
        if (license.Length > 0)
        {
            if (license.All(character => character is >= '0' and <= '9'))
            {
                // Preserve the full numeric identity; Nevada also uses dotted real-estate prefixes.
                query = db.RegulatoryLicensees.FromSqlInterpolated($"""
                    SELECT * FROM RegulatoryLicensees
                    WHERE SnapshotId = {snapshotId.Value} AND State = {search.State}
                      AND (LicenseNumber = {license}
                        OR (length(LicenseNumber) > {license.Length}
                          AND substr(LicenseNumber, -{license.Length}) = {license}
                          AND substr(LicenseNumber, 1, length(LicenseNumber) - {license.Length}) NOT GLOB '*[^A-Za-z]*')
                        OR ({search.State} = 'Texas' AND upper(replace(LicenseNumber, ' ', '')) IN ({license + "-B"}, {license + "-SA"}))
                        OR ({search.State} = 'Nevada' AND upper(LicenseNumber) IN ({"B." + license}, {"BS." + license}, {"S." + license})))
                    """).AsNoTracking();
            }
            else
            {
                query = search.State == "Texas"
                    ? query.Where(item => item.LicenseNumber.Replace(" ", "").ToUpper() == license.Replace(" ", ""))
                    : query.Where(item => item.LicenseNumber.ToUpper() == license);
            }
        }
        query = await ApplyDisplaySelectionAsync(query, search.State, snapshotId.Value, ct);
        if (name.Length > 0) query = query.Where(item => item.Name.ToUpper().Contains(name));
        if (type.Length > 0)
        {
            query = await IsColoradoDoraSnapshotAsync(snapshotId.Value, ct)
                ? query.Where(item => item.SourceLicenseType == type)
                : query.Where(item => item.LicenseType == type);
        }
        if (firm.Length > 0) query = query.Where(item => item.FirmAffiliations.Any(affiliation =>
            affiliation.SnapshotId == snapshotId && affiliation.Firm != null
            && (affiliation.Firm.LegalName.ToUpper().Contains(firm)
                || (affiliation.Firm.TradeName != null && affiliation.Firm.TradeName.ToUpper().Contains(firm)))));
        var total = await query.CountAsync(ct);
        var page = Math.Clamp(search.Page, 1, Math.Max(1, (total + 19) / 20));
        var rows = await query.Include(item => item.Snapshot)
            .Include(item => item.FirmAffiliations).ThenInclude(item => item.Firm).AsSplitQuery()
            .OrderBy(item => item.Name).ThenBy(item => item.LicenseNumber).ThenBy(item => item.Id)
            .Skip((page - 1) * 20).Take(20)
            .ToListAsync(ct);
        return new(rows.Select(ToSelection).ToList(), total, page, 20);
    }

    public async Task<List<RosterSelection>> SearchAsync(string state, string licenseNumber, CancellationToken ct = default, string? captchaToken = null)
    {
        if (!options.Enabled || !IsSupportedState(state) || string.IsNullOrWhiteSpace(licenseNumber))
            return [];
        return (await SearchAsync(new RosterSearch(state, licenseNumber), ct, captchaToken)).Items.ToList();
    }

    public async Task<RosterSelection> GetSelectionAsync(Guid licenseeId, CancellationToken ct = default)
    {
        if (!options.Enabled)
            throw new RosterRequestException("The roster registration pilot is not enabled.");
        await ValidateSelectionAsync(licenseeId, ct);
        var selection = await db.RegulatoryLicensees.AsNoTracking().Include(item => item.Snapshot)
            .Include(item => item.FirmAffiliations).ThenInclude(item => item.Firm).AsSplitQuery()
            .SingleOrDefaultAsync(item => item.Id == licenseeId, ct)
            ?? throw new RosterRequestException("Roster record was not found. Search again.");
        return ToSelection(selection);
    }

    public async Task<UserAccount> RegisterAsync(Guid licenseeId, string email, string password, CancellationToken ct = default)
    {
        if (!options.AppliesTo(email))
            throw new RosterRequestException("Registration is limited to the configured test inbox.");
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await ValidateSelectionAsync(licenseeId, ct);
        if (await db.UserAccounts.AnyAsync(item => item.Email == email.Trim().ToLowerInvariant(), ct))
            throw new RosterRequestException("An account already exists. Sign in to submit this selection.");
        UserAccount user;
        try { user = await accounts.RegisterLearnerAsync(email, password, marketingOptIn: false); }
        catch (InvalidOperationException ex)
        {
            throw new RosterRequestException(ex.Message);
        }
        db.RosterAccountRequests.Add(new RosterAccountRequest { UserAccountId = user.Id, RegulatoryLicenseeId = licenseeId });
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user.Id, user.Email, "roster-request.submitted", "UserAccount", user.Id,
            "Student selected the commission roster record; awaiting email verification. No ownership asserted.");
        await transaction.CommitAsync(ct);
        return user;
    }

    public async Task SubmitAsync(Guid userId, Guid licenseeId, CancellationToken ct = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var user = await db.UserAccounts.AsNoTracking().SingleOrDefaultAsync(item => item.Id == userId, ct);
        if (!options.Enabled || user is null || !user.IsActive || user.Role is not ("Learner" or "Broker" or "Instructor"))
            throw new RosterRequestException("Sign in with an active licensee account before saving a license record.");
        if (user.EmailVerifiedAtUtc is null)
            throw new RosterRequestException("Verify your account email before saving a license record.");
        await ValidateSelectionAsync(licenseeId, ct);
        if (await db.RosterAccountRequests.AnyAsync(item => item.UserAccountId == userId && item.RegulatoryLicenseeId == licenseeId, ct))
            throw new RosterRequestException("This record is already saved. View your license profile in Account.");
        db.RosterAccountRequests.Add(new RosterAccountRequest
        {
            UserAccountId = userId, RegulatoryLicenseeId = licenseeId, Status = "Confirmed"
        });
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(userId, user.Email, "roster-request.confirmed", "UserAccount", userId,
            "Confirmed by licensee with verified email; commission data unchanged. No verified ownership or credit asserted.");
        await transaction.CommitAsync(ct);
    }

    public async Task<List<RosterRequestRow>> GetMineAsync(Guid userId, CancellationToken ct = default) =>
        await Rows().Where(item => item.UserAccountId == userId && item.UserAccount!.IsActive)
            .OrderByDescending(item => item.SubmittedAtUtc).ThenBy(item => item.Id).Take(20)
            .Select(item => ToRow(item)).ToListAsync(ct);

    public async Task<CertificatePage<RosterRequestRow>> GetReviewPageAsync(
        Guid actorId, string state, string status, int page, CancellationToken ct = default)
    {
        await RequireAdminAsync(actorId, ct);
        if (!IsSupportedState(state))
            return new([], 0, 1, 20);
        var query = Rows().Where(item => item.RegulatoryLicensee!.State == state);
        if (status is "Pending" or "Confirmed" or "Approved" or "Rejected") query = query.Where(item => item.Status == status);
        var total = await query.CountAsync(ct);
        page = Math.Clamp(page, 1, Math.Max(1, (total + 19) / 20));
        var rows = await query.OrderByDescending(item => item.SubmittedAtUtc).ThenBy(item => item.Id)
            .Skip((page - 1) * 20).Take(20).Select(item => ToRow(item)).ToListAsync(ct);
        return new(rows, total, page, 20);
    }

    public async Task ReviewAsync(Guid actorId, Guid requestId, bool approve, string note, CancellationToken ct = default)
    {
        if (!options.Enabled) throw new RosterRequestException("The roster registration pilot is not enabled.");
        var admin = await RequireAdminAsync(actorId, ct);
        if (approve)
            throw new RosterRequestException("Administrator approval is not required. Students confirm their own license association with verified email.");
        if (string.IsNullOrWhiteSpace(note) || note.Trim().Length > 1000)
            throw new RosterRequestException("Enter a review note between 1 and 1000 characters.");
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var request = await db.RosterAccountRequests.Include(item => item.UserAccount)
            .SingleOrDefaultAsync(item => item.Id == requestId, ct)
            ?? throw new RosterRequestException("Request was not found.");
        if (request.Status is not ("Pending" or "Confirmed" or "Approved"))
            throw new RosterRequestException("This association has already been rejected.");
        request.Status = "Rejected";
        request.ReviewNote = note.Trim();
        request.ReviewedAtUtc = DateTime.UtcNow;
        request.ReviewedByUserId = actorId;
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(actorId, admin.Email, "roster-request.rejected",
            "RosterAccountRequest", requestId, "Association rejected for a correction or dispute; commission record unchanged.");
        await transaction.CommitAsync(ct);
    }

    private IQueryable<RosterAccountRequest> Rows() => db.RosterAccountRequests.AsNoTracking()
        .Include(item => item.UserAccount)
        .Include(item => item.RegulatoryLicensee).ThenInclude(item => item!.Snapshot)
        .Include(item => item.RegulatoryLicensee).ThenInclude(item => item!.FirmAffiliations)
            .ThenInclude(item => item.Firm).AsSplitQuery();

    private static RosterRequestRow ToRow(RosterAccountRequest item) => new(
        item.Id, item.UserAccountId, item.UserAccount!.Email,
        ToSelection(item.RegulatoryLicensee!),
        item.Status, item.UserAccount.EmailVerifiedAtUtc.HasValue, item.SubmittedAtUtc, item.ReviewNote);

    public static string? SafeSourceUrl(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var parsed) && (parsed.Scheme == "https" || parsed.Scheme == "http")
            ? parsed.AbsoluteUri : null;

    private static RosterSelection ToSelection(RegulatoryLicensee item)
    {
        var details = new List<RosterDetail>();
        void Add(string label, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value)) details.Add(new(label, value));
        }
        void Date(string label, DateOnly? value) => Add(label, value?.ToString("yyyy-MM-dd",
            System.Globalization.CultureInfo.InvariantCulture));
        Add("Professional type", item.SourceLicenseType);
        Add("License level", item.LicenseLevel);
        Date("Issued", item.LicenseIssuedDate);
        Date("Original license date", item.OriginalLicenseDate);
        Date("Renewal / expiration", item.LicenseRenewalDate);
        Add("Related license number", item.RelatedLicenseNumber);
        Add("Related license type", item.RelatedLicenseType);
        Add("Related license name", item.RelatedLicenseName);
        if (item.IsRestricted) Add("Restriction", "Restricted");
        if (item.EthicsAndAgencyOnly) Add("Education scope", "Ethics and agency only");
        if (item.IsAdditionalLicenseRecord.HasValue)
            Add("Additional license record", item.IsAdditionalLicenseRecord.Value ? "Yes" : "No");
        if (item.UpdateHours.HasValue) Add("Update hours", item.UpdateHours.Value.ToString());
        if (item.ElectiveHours.HasValue) Add("Elective hours", item.ElectiveHours.Value.ToString());
        if (item.PostlicensingHours.HasValue) Add("Postlicensing hours", item.PostlicensingHours.Value.ToString());
        if (item.IsBicEligible) Add("Broker-in-charge eligibility", "Eligible");
        foreach (var affiliation in item.FirmAffiliations
            .Where(link => link.SnapshotId == item.SnapshotId && link.Firm?.State == item.State
                && link.Firm.SnapshotId == item.SnapshotId)
            .OrderBy(link => link.Firm!.LegalName).ThenBy(link => link.Firm!.LicenseNumber).ThenBy(link => link.Id))
        {
            var firm = affiliation.Firm!;
            Add("Brokerage / firm", firm.LegalName);
            Add("Firm license", firm.LicenseNumber);
            Add("Firm trade name", firm.TradeName);
            Add("Firm status", firm.LicenseStatus);
            var addressIsDate = DateOnly.TryParseExact(firm.Address,
                ["MM/dd/yyyy", "M/d/yyyy", "yyyy-MM-dd"],
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out _);
            Add("Firm address", addressIsDate ? "Unavailable: address field contains a date." : firm.Address);
            Date("Firm issued", firm.LicenseIssuedDate);
            Date("Firm renewal / expiration", firm.LicenseRenewalDate);
            Add("Qualifying broker", firm.QualifyingBrokerName);
            Date("Affiliation effective", affiliation.RelationshipEffectiveDate);
            if (affiliation.IsBrokerInCharge) Add("Affiliation role", "Broker in charge");
            Add("Broker in charge", affiliation.BrokerInChargeName);
        }
        return new(item.Id, item.State, item.LicenseNumber, item.Name, item.LicenseType, item.LicenseStatus,
            item.Snapshot!.SourceName, SafeSourceUrl(item.Snapshot.SourceUrl), item.Snapshot.RetrievedAtUtc)
        { Details = details };
    }

    private async Task ValidateSelectionAsync(Guid id, CancellationToken ct)
    {
        var record = await db.RegulatoryLicensees.AsNoTracking().SingleOrDefaultAsync(item => item.Id == id, ct)
            ?? throw new RosterRequestException("Roster record was not found. Search again.");
        var latest = await LatestSnapshotIdAsync(record.State, ct);
        if (record.SnapshotId != latest)
            throw new RosterRequestException("This roster record is no longer in the current roster. Search again.");
        if (record.State == "Colorado" && _coloradoDoraTypeSelection is not null
            && await db.RegulatoryDataSnapshots.AsNoTracking()
                .Where(snapshot => snapshot.Id == record.SnapshotId)
                .Select(snapshot => snapshot.ParserVersion)
                .SingleAsync(ct) == ColoradoDoraBulkImportService.ParserVersion)
        {
            var selected = _coloradoDoraTypeSelection.GetSelected();
            if (record.SourceLicenseType is null
                || !ColoradoDoraLicenseTypes.EnabledRealEstateTypes(selected).Contains(record.SourceLicenseType, StringComparer.Ordinal))
                throw new RosterRequestException("This Colorado professional type is not enabled for LMS registration.");
            var option = ColoradoDoraLicenseTypes.All.FirstOrDefault(item => item.Name == record.SourceLicenseType);
            if (option is not { IsRealEstate: true, IsIndividual: true })
                throw new RosterRequestException("Select an individual professional license record for registration.");
        }
        if (!RegulatoryLicenseTypes.IsRealEstate(record.State, record.LicenseType)
            || (record.SourceLicenseType is not null && record.State == "Colorado"
                && !RegulatoryLicenseTypes.IsRealEstate(record.State, record.SourceLicenseType)))
            throw new RosterRequestException($"This {record.State} professional type is not enabled for LMS registration.");
        if (record.IsRestricted || string.IsNullOrWhiteSpace(record.LicenseType)
            || new[] { "corporation", "corporate broker", "limited liability company broker", "trade name broker" }
                .Contains(record.LicenseType.Trim().ToLowerInvariant()))
            throw new RosterRequestException("Select an unrestricted individual license record for this student pilot.");
    }

    private static bool IsSupportedState(string state) =>
        DevelopmentCourseCatalog.States.Contains(state) || state == "Puerto Rico";

    private Task<Guid?> LatestSnapshotIdAsync(string state, CancellationToken ct) =>
        RegulatoryRoster.Published(db.RegulatoryDataSnapshots.AsNoTracking().Where(item => item.State == state))
            .Select(item => (Guid?)item.Id).FirstOrDefaultAsync(ct);

    private Task<bool> IsColoradoDoraSnapshotAsync(Guid snapshotId, CancellationToken ct) =>
        db.RegulatoryDataSnapshots.AsNoTracking().Where(snapshot => snapshot.Id == snapshotId)
            .Select(snapshot => snapshot.ParserVersion == ColoradoDoraBulkImportService.ParserVersion)
            .FirstOrDefaultAsync(ct);

    private async Task<IQueryable<RegulatoryLicensee>> ApplyDisplaySelectionAsync(
        IQueryable<RegulatoryLicensee> query, string state, Guid snapshotId, CancellationToken ct)
    {
        var excluded = RegulatoryLicenseTypes.NonRealEstateTypes(state).ToArray();
        if (state != "Colorado" || !await IsColoradoDoraSnapshotAsync(snapshotId, ct))
            return excluded.Length == 0
                ? query
                : query.Where(item => item.LicenseType == null || !excluded.Contains(item.LicenseType));

        if (_coloradoDoraTypeSelection is null)
            return query.Where(item => item.SourceLicenseType != null && !excluded.Contains(item.SourceLicenseType));
        var selectedTypes = ColoradoDoraLicenseTypes.EnabledRealEstateTypes(_coloradoDoraTypeSelection.GetSelected());
        return selectedTypes.Length == 0
            ? query.Where(_ => false)
            : query.Where(item => item.SourceLicenseType != null && selectedTypes.Contains(item.SourceLicenseType));
    }

    private async Task<UserAccount> RequireAdminAsync(Guid actorId, CancellationToken ct) =>
        await db.UserAccounts.AsNoTracking().SingleOrDefaultAsync(item => item.Id == actorId && item.IsActive && item.Role == "Admin", ct)
        ?? throw new RosterRequestException("An active administrator is required.");
}
