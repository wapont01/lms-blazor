using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lms.Application.Data;
using Lms.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.VisualBasic.FileIO;

namespace Lms.Application.Services;

public sealed record OregonOreaIndividual(
    long SourceRowNumber,
    string LicenseNumber,
    string Name,
    string LicenseTypeCode,
    string LicenseStatus,
    DateOnly? ExpirationDate,
    string? BusinessName,
    string? BusinessLicenseNumber,
    DateOnly? AssociatedDate,
    string? KeyManagerName,
    string? KeyManagerLicenseNumber);

public sealed record OregonOreaBusiness(
    long SourceRowNumber,
    string LicenseNumber,
    string Name,
    string RegistrationType,
    DateOnly? ExpirationDate,
    string? Address,
    string? City = null);

public sealed record OregonOreaLocation(OregonOreaBusiness Branch, string? FirmLicenseNumber, string? MatchMethod);

public sealed record OregonOreaLicensee(
    OregonOreaIndividual Record,
    string LicenseTypeCodes,
    OregonOreaIndividual? Association);

public sealed class OregonOreaBulkImportService(ApplicationDbContext db)
{
    public const string State = "Oregon";
    public const string ParserVersion = "oregon-orea-roster-v3";
    public const string BranchOfficeKind = "Registered branch office";
    public const string SourcePage = "https://orea.elicense.micropact.com/Lookup/GenerateRoster.aspx";
    public const string DownloadPage = "https://orea.elicense.micropact.com/Lookup/DownloadRoster.aspx";
    public const string FileDownloadUrl = "https://orea.elicense.micropact.com/Lookup/FileDownload.aspx";
    public const string BusinessesFile = "Active_Businesses.csv";
    public const string ActiveIndividualsFile = "Active_Individuals.csv";
    public const string InactiveIndividualsFile = "Inactive_Individuals.csv";
    public const string BusinessesRoster = "Active Businesses";
    public const string ActiveIndividualsRoster = "Active Individuals";
    public const string InactiveIndividualsRoster = "Inactive Individuals";
    public const string RegisteredBusinessName = "RBN";
    public const string RegisteredBranchOffice = "RBO";
    public const string EscrowAgent = "EA";

    public static IReadOnlyList<FloridaBulkFileDefinition> Definitions { get; } =
    [
        new(BusinessesFile, BusinessesFile, 19) { SourceUrl = SourcePage },
        new(ActiveIndividualsFile, ActiveIndividualsFile, 32) { SourceUrl = SourcePage },
        new(InactiveIndividualsFile, InactiveIndividualsFile, 24) { SourceUrl = SourcePage }
    ];

    /// <summary>Roster names exactly as listed on the OREA "Download List(s)" page.</summary>
    public static string RosterName(string fileName) => fileName switch
    {
        BusinessesFile => BusinessesRoster,
        ActiveIndividualsFile => ActiveIndividualsRoster,
        InactiveIndividualsFile => InactiveIndividualsRoster,
        _ => throw new InvalidDataException("Unknown Oregon OREA source file.")
    };

    // OREA's generated headers split each label across a value column and an empty spacer column,
    // so the printed header text does not line up with the values. Values are read by position.
    public static IReadOnlyList<string> BusinessHeaders { get; } = Array.AsReadOnly(new[]
    {
        "Business", "Name", "Address", "Line 1", "Address Line", "2", "Name of", "City", "State", "Zip", "Code", "County",
        "Business Phone", "Number(s)", "Registration Type", "Business License", "Number", "Expiration Date", ""
    });

    public static IReadOnlyList<string> InactiveIndividualHeaders { get; } = Array.AsReadOnly(new[]
    {
        "Last Name", "-", "First Name", "--", "Middle", "---", "Street", "Address Line 1", "Street Address", "Line 2", "City",
        "----", "State", "Zip", "-----", "County Name", "-------", "License Type", "License Number", "--------",
        "License Expiration", "-----=---", "License Status", ""
    });

    public static IReadOnlyList<string> ActiveIndividualHeaders { get; } = Array.AsReadOnly(InactiveIndividualHeaders
        .Take(InactiveIndividualHeaders.Count - 1)
        .Concat(new[]
        {
            "Business Name", "--------------", "Business License", "-----------------", "Licensee Associated to RBN Date",
            "Key Manager", "-------------------------", "Key Manager Credential Number", ""
        }).ToArray());

    private static readonly int[] BusinessSpacers = [1, 3, 5, 7, 10, 13, 16, 18];
    private static readonly int[] IndividualSpacers = [1, 3, 5, 7, 9, 11, 14, 16, 19, 21];
    private static readonly int[] AssociationSpacers = [24, 26, 29, 31];

    public static string LicenseTypeName(string code) => code switch
    {
        "B" => "Broker",
        "PB" => "Principal Broker",
        "PM" => "Property Manager",
        _ => code
    };

    /// <summary>Counts every source row after validating the published layout.</summary>
    public static long CheckFile(string path, string fileName, CancellationToken ct = default, Action<long>? progress = null) =>
        fileName switch
        {
            BusinessesFile => ReadRows(path, BusinessHeaders, BusinessSpacers, "OREA business", ct, progress, (_, _) => { }),
            ActiveIndividualsFile => ReadRows(path, ActiveIndividualHeaders, IndividualSpacers.Concat(AssociationSpacers).ToArray(),
                "OREA active individual", ct, progress, (_, _) => { }),
            InactiveIndividualsFile => ReadRows(path, InactiveIndividualHeaders, [.. IndividualSpacers, 23],
                "OREA inactive individual", ct, progress, (_, _) => { }),
            _ => throw new InvalidDataException("Unknown Oregon OREA source file.")
        };

    public static List<OregonOreaBusiness> ReadBusinesses(string path, CancellationToken ct = default, Action<long>? progress = null)
    {
        var records = new List<OregonOreaBusiness>();
        ReadRows(path, BusinessHeaders, BusinessSpacers, "OREA business", ct, progress, (fields, line) =>
        {
            var number = Number(fields[15], line);
            var name = Optional(fields[0]) ?? $"Name not published ({number})";
            if (name.Length > 200)
                throw new InvalidDataException($"OREA business name exceeds the directory limit at line {line}.");
            var type = Required(fields[14], 10, "registration type", line).ToUpperInvariant();
            var cityLine = string.Join(' ', new[] { fields[8], fields[9] }.Select(part => part.Trim()).Where(part => part.Length > 0));
            var address = string.Join(", ", new[] { fields[2], fields[4], fields[6], cityLine }
                .Select(part => part.Trim()).Where(part => part.Length > 0));
            records.Add(new(line, number, name, type, Date(fields[17]), Truncate(address.Length == 0 ? null : address, 500),
                Optional(fields[6])?.ToUpperInvariant()));
        });
        return records;
    }

    public static List<OregonOreaIndividual> ReadIndividuals(string path, bool active, CancellationToken ct = default,
        Action<long>? progress = null)
    {
        var records = new List<OregonOreaIndividual>();
        var headers = active ? ActiveIndividualHeaders : InactiveIndividualHeaders;
        int[] spacers = active ? [.. IndividualSpacers, .. AssociationSpacers] : [.. IndividualSpacers, 23];
        ReadRows(path, headers, spacers, active ? "OREA active individual" : "OREA inactive individual", ct, progress, (fields, line) =>
        {
            var number = Number(fields[18], line);
            var name = string.Join(' ', new[] { fields[2], fields[4], fields[0] }
                .Select(part => part.Trim()).Where(part => part.Length > 0));
            if (name.Length == 0) name = $"Name not published ({number})";
            if (name.Length > 160)
                throw new InvalidDataException($"OREA individual name exceeds the directory limit at line {line}.");
            var status = Required(fields[22], 40, "license status", line);
            if (!string.Equals(status, active ? "ACTIVE" : "INACTIVE", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Unexpected OREA license status '{status}' at line {line}.");
            string? business = null, businessName = null, keyManager = null, keyManagerName = null;
            DateOnly? associated = null;
            if (active)
            {
                business = Optional(fields[25]);
                keyManager = Optional(fields[30]);
                if (business?.Length > 40 || keyManager?.Length > 40)
                    throw new InvalidDataException($"Overlength OREA business or key manager license number at line {line}.");
                businessName = Truncate(Optional(fields[23]), 160);
                keyManagerName = Truncate(Optional(fields[28]), 160);
                associated = Date(fields[27]);
            }
            records.Add(new(line, number, name, Required(fields[17], 10, "license type", line).ToUpperInvariant(),
                active ? "Active" : "Inactive", Date(fields[20]), businessName, business, associated, keyManagerName, keyManager));
        });
        return records;
    }

    public static async Task<IReadOnlyList<FloridaBulkManifestEntry>> ValidateArchiveAsync(
        string root, CancellationToken ct = default, Action<string, long, long>? progress = null)
    {
        await using var manifest = File.OpenRead(FloridaBulkImportService.ArchiveFile(root, "manifest.json"));
        var entries = await JsonSerializer.DeserializeAsync<List<FloridaBulkManifestEntry>>(manifest, cancellationToken: ct)
            ?? throw new InvalidDataException("Missing Oregon OREA acquisition manifest.");
        if (entries.Count != Definitions.Count)
            throw new InvalidDataException("Oregon OREA archive must contain the active business, active individual and inactive individual rosters.");
        var ordered = new List<FloridaBulkManifestEntry>();
        foreach (var definition in Definitions)
        {
            var matches = entries.Where(entry => entry.File == definition.RelativePath).ToArray();
            if (matches.Length != 1)
                throw new InvalidDataException($"Oregon OREA archive requires one {definition.Name}.");
            var entry = matches[0];
            var columns = definition.Columns.ToString(CultureInfo.InvariantCulture);
            if (entry.SourceUrl != definition.Url || entry.Rows <= 0 || entry.Bytes <= 0 || entry.VerifiedAtUtc == default
                || entry.Columns is null || entry.Columns.Count != 1
                || !entry.Columns.TryGetValue(columns, out var declared) || declared != entry.Rows)
                throw new InvalidDataException($"Invalid Oregon OREA acquisition metadata for {definition.Name}.");
            var path = FloridaBulkImportService.ArchiveFile(root, entry.File);
            await using (var stream = File.OpenRead(path))
            {
                var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, ct));
                if (stream.Length != entry.Bytes || !hash.Equals(entry.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"Oregon OREA archive size/hash mismatch for {definition.Name}.");
            }
            if (CheckFile(path, definition.Name, ct, rows => progress?.Invoke(definition.Name, rows, entry.Bytes)) != entry.Rows)
                throw new InvalidDataException($"Oregon OREA archive row count mismatch for {definition.Name}.");
            progress?.Invoke(definition.Name, entry.Rows, entry.Bytes);
            ordered.Add(entry);
        }
        return ordered;
    }

    /// <summary>
    /// OREA generates each roster on demand and returns the same rows in a different order on every request, so the
    /// snapshot identity hashes the header plus the ordinally sorted data rows instead of the raw file bytes.
    /// </summary>
    public static string RowSetDigest(string path, CancellationToken ct = default)
    {
        var lines = File.ReadAllLines(path, Encoding.UTF8);
        ct.ThrowIfCancellationRequested();
        var rows = lines.Skip(1).Where(line => line.Length > 0).ToArray();
        Array.Sort(rows, StringComparer.Ordinal);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var line in lines.Take(1).Concat(rows))
        {
            hash.AppendData(Encoding.UTF8.GetBytes(line));
            hash.AppendData("\n"u8);
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static int TypeRank(string code) => code switch { "PB" => 0, "B" => 1, "PM" => 2, _ => 3 };

    /// <summary>
    /// OREA lists an active licensee once per registered business association, and a license can also appear on the
    /// inactive roster for a lapsed lower license type. Keep one licensee per number: active before inactive, then the
    /// highest license type; the most recent business association becomes the related firm.
    /// </summary>
    public static List<OregonOreaLicensee> SelectLicensees(IEnumerable<OregonOreaIndividual> active,
        IEnumerable<OregonOreaIndividual> inactive) =>
        active.Concat(inactive)
            .GroupBy(record => record.LicenseNumber, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var activeRows = group.Where(record => record.LicenseStatus == "Active").ToList();
                var rows = activeRows.Count > 0 ? activeRows : group.ToList();
                var primary = rows.OrderBy(record => TypeRank(record.LicenseTypeCode))
                    .ThenByDescending(record => record.ExpirationDate ?? DateOnly.MinValue)
                    .ThenBy(record => record.Name, StringComparer.Ordinal)
                    .ThenBy(record => record.SourceRowNumber).First();
                var codes = string.Join(", ", rows.Select(record => record.LicenseTypeCode).Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(TypeRank));
                var association = activeRows.Where(record => record.BusinessLicenseNumber is not null)
                    .OrderByDescending(record => record.AssociatedDate ?? DateOnly.MinValue)
                    .ThenBy(record => record.BusinessLicenseNumber, StringComparer.Ordinal)
                    .ThenBy(record => record.BusinessName, StringComparer.Ordinal)
                    .ThenBy(record => record.SourceRowNumber).FirstOrDefault();
                return new OregonOreaLicensee(primary, codes, association);
            })
            .OrderBy(licensee => licensee.Record.LicenseStatus == "Active" ? 0 : 1)
            .ThenBy(licensee => licensee.Record.SourceRowNumber)
            .ToList();

    /// <summary>
    /// Registered business names (brokerages) and escrow agents (licensed escrow companies, a non-real-estate industry)
    /// are firms. Registered branch offices are extra locations of a brokerage, see <see cref="SelectLocations"/>.
    /// </summary>
    public static List<OregonOreaBusiness> SelectFirms(IEnumerable<OregonOreaBusiness> records) =>
        Distinct(records.Where(record => record.RegistrationType is RegisteredBusinessName or EscrowAgent));

    public static string FirmIndustry(OregonOreaBusiness firm) =>
        firm.RegistrationType == EscrowAgent ? RegulatoryIndustries.Escrow : RegulatoryIndustries.RealEstate;

    /// <summary>
    /// OREA does not publish which brokerage owns a branch office, so branches are linked by name: exact name, then
    /// normalized name (legal suffixes and punctuation ignored), then the closest name, which is flagged for review.
    /// Several brokerages can share a name; the one in the branch's city wins, then the lowest license number.
    /// </summary>
    public static List<OregonOreaLocation> SelectLocations(IEnumerable<OregonOreaBusiness> records)
    {
        var list = records.ToList();
        var brokerages = Distinct(list.Where(record => record.RegistrationType == RegisteredBusinessName));
        var byName = brokerages.ToLookup(firm => firm.Name.Trim(), StringComparer.OrdinalIgnoreCase);
        var normalized = brokerages.Select(firm => (Firm: firm, Key: NormalizeName(firm.Name))).ToList();
        var byKey = normalized.Where(item => item.Key.Length > 0).ToLookup(item => item.Key, item => item.Firm, StringComparer.Ordinal);

        OregonOreaBusiness Pick(IEnumerable<OregonOreaBusiness> candidates, OregonOreaBusiness branch) =>
            candidates.OrderBy(firm => branch.City is not null && string.Equals(firm.City, branch.City, StringComparison.Ordinal) ? 0 : 1)
                .ThenBy(firm => firm.LicenseNumber, StringComparer.Ordinal).First();

        return Distinct(list.Where(record => record.RegistrationType == RegisteredBranchOffice)).Select(branch =>
        {
            if (byName.Contains(branch.Name.Trim()))
                return new OregonOreaLocation(branch, Pick(byName[branch.Name.Trim()], branch).LicenseNumber,
                    RegulatoryLocationMatchMethods.ExactName);
            var key = NormalizeName(branch.Name);
            if (key.Length > 0 && byKey.Contains(key))
                return new OregonOreaLocation(branch, Pick(byKey[key], branch).LicenseNumber,
                    RegulatoryLocationMatchMethods.NormalizedName);
            if (normalized.Count == 0 || key.Length == 0)
                return new OregonOreaLocation(branch, null, null);
            var scored = normalized.Select(item => (item.Firm, Score: Similarity(key, item.Key))).ToList();
            var best = scored.Max(item => item.Score);
            var closest = Pick(scored.Where(item => item.Score == best).Select(item => item.Firm), branch);
            return new OregonOreaLocation(branch, closest.LicenseNumber, RegulatoryLocationMatchMethods.ClosestName);
        }).ToList();
    }

    private static readonly HashSet<string> NameNoise = new(StringComparer.Ordinal)
    {
        "LLC", "L", "C", "INC", "INCORPORATED", "CORP", "CORPORATION", "CO", "COMPANY", "LTD", "LLP", "PC", "PLLC", "THE", "RBO"
    };

    public static string NormalizeName(string name)
    {
        var builder = new StringBuilder(name.Length);
        foreach (var character in name.ToUpperInvariant().Replace("&", " AND ", StringComparison.Ordinal))
            builder.Append(char.IsLetterOrDigit(character) ? character : ' ');
        return string.Join(' ', builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(token => !NameNoise.Contains(token)));
    }

    /// <summary>Levenshtein similarity in [0, 1] between two normalized names.</summary>
    private static double Similarity(string left, string right)
    {
        if (left.Length == 0 || right.Length == 0) return 0;
        var previous = new int[right.Length + 1];
        var current = new int[right.Length + 1];
        for (var j = 0; j <= right.Length; j++) previous[j] = j;
        for (var i = 1; i <= left.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= right.Length; j++)
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1),
                    previous[j - 1] + (left[i - 1] == right[j - 1] ? 0 : 1));
            (previous, current) = (current, previous);
        }
        return 1 - (double)previous[right.Length] / Math.Max(left.Length, right.Length);
    }

    private static List<OregonOreaBusiness> Distinct(IEnumerable<OregonOreaBusiness> records) =>
        records.GroupBy(record => record.LicenseNumber, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderBy(record => record.Name, StringComparer.Ordinal)
                .ThenBy(record => record.Address, StringComparer.Ordinal)
                .ThenBy(record => record.SourceRowNumber).First())
            .OrderBy(record => record.SourceRowNumber)
            .ToList();

    public async Task<Guid> ImportArchiveAsync(string root, CancellationToken ct = default,
        Action<string, long, long>? progress = null, Action<long>? staged = null)
    {
        var entries = await ValidateArchiveAsync(root, ct, progress);
        var businesses = ReadBusinesses(FloridaBulkImportService.ArchiveFile(root, entries[0].File), ct);
        var active = ReadIndividuals(FloridaBulkImportService.ArchiveFile(root, entries[1].File), true, ct);
        var inactive = ReadIndividuals(FloridaBulkImportService.ArchiveFile(root, entries[2].File), false, ct);
        var people = SelectLicensees(active, inactive);
        var firms = SelectFirms(businesses);
        var locations = SelectLocations(businesses);
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(ParserVersion
            + string.Concat(entries.Select(entry => RowSetDigest(FloridaBulkImportService.ArchiveFile(root, entry.File), ct)))));
        var id = new Guid(digest.AsSpan(0, 16));

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var existing = await db.RegulatoryDataSnapshots.SingleOrDefaultAsync(snapshot => snapshot.Id == id, ct);
        if (existing is not null)
        {
            if (existing.State != State || existing.ParserVersion != ParserVersion || existing.Status != RegulatorySnapshotStatuses.Approved)
                throw new InvalidDataException("Oregon OREA archive conflicts with an existing snapshot.");
            if (await db.RegulatoryLicensees.CountAsync(record => record.SnapshotId == id, ct) != people.Count
                || await db.RegulatoryFirms.CountAsync(record => record.SnapshotId == id, ct) != firms.Count
                || await db.RegulatoryFirmLocations.CountAsync(record => record.SnapshotId == id, ct) != locations.Count)
                throw new InvalidDataException("Existing Oregon OREA snapshot coverage does not match the validated archive.");
            var current = await RegulatoryRoster.Published(db.RegulatoryDataSnapshots.Where(snapshot => snapshot.State == State))
                .Select(snapshot => snapshot.Id).FirstAsync(ct);
            if (current != id) { existing.ActivatedAtUtc = DateTime.UtcNow; await db.SaveChangesAsync(ct); }
            await transaction.CommitAsync(ct);
            return id;
        }

        var connection = db.Database.GetDbConnection();
        var sqlTransaction = transaction.GetDbTransaction();
        var importedAt = DateTime.UtcNow;
        await FloridaBulkImportService.InsertBatch(connection, sqlTransaction, "RegulatoryDataSnapshots",
            ["Id", "State", "SourceName", "SourceUrl", "ParserVersion", "RetrievedAtUtc", "ImportedAtUtc", "Status", "ReviewedAtUtc", "ActivatedAtUtc"],
            [[id, State, "Oregon Real Estate Agency licensee rosters (active businesses, active and inactive individuals)",
                SourcePage, ParserVersion, entries.Min(entry => entry.VerifiedAtUtc).UtcDateTime, importedAt,
                RegulatorySnapshotStatuses.Approved, importedAt, importedAt]], ct);

        var personIds = people.ToDictionary(person => person.Record.LicenseNumber, _ => Guid.NewGuid(), StringComparer.OrdinalIgnoreCase);
        var firmIds = firms.ToDictionary(firm => firm.LicenseNumber, _ => Guid.NewGuid(), StringComparer.OrdinalIgnoreCase);
        var keyManagers = active
            .Where(record => record.BusinessLicenseNumber is not null && record.KeyManagerName is not null)
            .GroupBy(record => record.BusinessLicenseNumber!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Select(record => record.KeyManagerName!).Min(StringComparer.Ordinal)!,
                StringComparer.OrdinalIgnoreCase);

        long total = 0;
        foreach (var group in firms.Chunk(100))
        {
            ct.ThrowIfCancellationRequested();
            var rows = group.Select(firm => new object?[]
            {
                firmIds[firm.LicenseNumber], id, State, firm.LicenseNumber, firm.Name, null, "Active",
                null, firm.ExpirationDate, firm.Address, keyManagers.GetValueOrDefault(firm.LicenseNumber), FirmIndustry(firm)
            }).ToList();
            await FloridaBulkImportService.InsertBatch(connection, sqlTransaction, "RegulatoryFirms",
                ["Id", "SnapshotId", "State", "LicenseNumber", "LegalName", "TradeName", "LicenseStatus",
                    "LicenseIssuedDate", "LicenseRenewalDate", "Address", "QualifyingBrokerName", "Industry"], rows, ct);
            total += group.Length;
            staged?.Invoke(total);
        }

        foreach (var group in locations.Chunk(100))
        {
            ct.ThrowIfCancellationRequested();
            var rows = group.Select(location => new object?[]
            {
                Guid.NewGuid(), id,
                location.FirmLicenseNumber is { } owner ? firmIds[owner] : null,
                State, location.Branch.LicenseNumber, location.Branch.Name, location.Branch.Address,
                BranchOfficeKind, location.MatchMethod
            }).ToList();
            await FloridaBulkImportService.InsertBatch(connection, sqlTransaction, "RegulatoryFirmLocations",
                ["Id", "SnapshotId", "FirmId", "State", "RegistrationNumber", "Name", "Address", "Kind", "MatchMethod"], rows, ct);
            total += group.Length;
            staged?.Invoke(total);
        }

        foreach (var group in people.Chunk(100))
        {
            ct.ThrowIfCancellationRequested();
            var rows = group.Select(person =>
            {
                var record = person.Record;
                var association = person.Association;
                return new object?[]
                {
                    personIds[record.LicenseNumber], id, State, record.LicenseNumber, record.Name,
                    LicenseTypeName(record.LicenseTypeCode), null, person.LicenseTypeCodes, record.LicenseStatus,
                    null, null, record.ExpirationDate,
                    association?.BusinessLicenseNumber, association is null ? null : "Registered Business Name",
                    association?.BusinessName, false, false, false
                };
            }).ToList();
            await FloridaBulkImportService.InsertBatch(connection, sqlTransaction, "RegulatoryLicensees",
                ["Id", "SnapshotId", "State", "LicenseNumber", "Name", "LicenseType", "LicenseLevel", "SourceLicenseType",
                    "LicenseStatus", "OriginalLicenseDate", "LicenseIssuedDate", "LicenseRenewalDate",
                    "RelatedLicenseNumber", "RelatedLicenseType", "RelatedLicenseName",
                    "IsRestricted", "EthicsAndAgencyOnly", "IsBicEligible"], rows, ct);
            total += group.Length;
            staged?.Invoke(total);
        }

        // One edge per business association; the business's key manager is marked broker-in-charge.
        var edges = new Dictionary<(Guid Firm, Guid Person), (DateOnly? Since, bool KeyManager)>();
        foreach (var record in active)
        {
            if (record.BusinessLicenseNumber is not { } business || !firmIds.TryGetValue(business, out var firmId)
                || !personIds.TryGetValue(record.LicenseNumber, out var personId))
                continue;
            var keyManager = string.Equals(record.KeyManagerLicenseNumber, record.LicenseNumber, StringComparison.OrdinalIgnoreCase);
            if (edges.TryGetValue((firmId, personId), out var known))
                edges[(firmId, personId)] = (Earliest(known.Since, record.AssociatedDate), known.KeyManager || keyManager);
            else
                edges.Add((firmId, personId), (record.AssociatedDate, keyManager));
        }
        foreach (var group in edges.Chunk(100))
        {
            ct.ThrowIfCancellationRequested();
            var rows = group.Select(edge => new object?[]
                { Guid.NewGuid(), id, edge.Key.Firm, edge.Key.Person, edge.Value.Since, edge.Value.KeyManager }).ToList();
            await FloridaBulkImportService.InsertBatch(connection, sqlTransaction, "RegulatoryFirmLicenseeAffiliations",
                ["Id", "SnapshotId", "FirmId", "LicenseeId", "RelationshipEffectiveDate", "IsBrokerInCharge"], rows, ct);
        }

        ct.ThrowIfCancellationRequested();
        await transaction.CommitAsync(ct);
        staged?.Invoke(total);
        return id;
    }

    private static DateOnly? Earliest(DateOnly? left, DateOnly? right) =>
        left is null ? right : right is null ? left : left < right ? left : right;

    private static long ReadRows(string path, IReadOnlyList<string> headers, IReadOnlyCollection<int> spacers, string label,
        CancellationToken ct, Action<long>? progress, Action<string[], long> handle)
    {
        using var parser = new TextFieldParser(path, new UTF8Encoding(false, true))
        {
            TextFieldType = FieldType.Delimited,
            HasFieldsEnclosedInQuotes = true,
            TrimWhiteSpace = false
        };
        parser.SetDelimiters(",");
        var actual = parser.ReadFields() ?? throw new InvalidDataException($"{label} roster has no header.");
        if (!actual.Select(value => value.TrimStart('\uFEFF').Trim()).SequenceEqual(headers, StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException($"{label} roster layout changed; expected the published {headers.Count}-column schema.");

        long rows = 0;
        while (!parser.EndOfData)
        {
            ct.ThrowIfCancellationRequested();
            var line = parser.LineNumber;
            var fields = parser.ReadFields();
            if (fields is null || fields.Length == 1 && fields[0].Length == 0) continue;
            if (fields.Length != headers.Count)
                throw new InvalidDataException($"Invalid {label} row shape at line {line}.");
            // A value in a spacer column means OREA shifted its columns; positional parsing would misread every field.
            foreach (var spacer in spacers)
            {
                if (fields[spacer].Trim().Length > 0)
                    throw new InvalidDataException($"{label} roster layout changed; unexpected value in column {spacer + 1} at line {line}.");
            }
            rows++;
            handle(fields, line);
            if (rows % 10_000 == 0) progress?.Invoke(rows);
            if (rows > 2_000_000)
                throw new InvalidDataException($"{label} roster exceeded the two-million-row safety limit.");
        }
        if (rows == 0)
            throw new InvalidDataException($"{label} roster contains no records.");
        progress?.Invoke(rows);
        return rows;
    }

    private static string Number(string value, long line)
    {
        var number = Optional(value);
        if (number is null || number.Length > 40)
            throw new InvalidDataException($"Missing or overlength OREA license number at line {line}.");
        return number;
    }

    private static string Required(string value, int maxLength, string field, long line)
    {
        var normalized = Optional(value);
        if (normalized is null || normalized.Length > maxLength)
            throw new InvalidDataException($"Missing or overlength OREA {field} at line {line}.");
        return normalized;
    }

    private static string? Optional(string value)
    {
        var normalized = value.Trim();
        return normalized.Length == 0 ? null : normalized;
    }

    private static string? Truncate(string? value, int maxLength) =>
        value is null || value.Length <= maxLength ? value : value[..maxLength];

    private static readonly string[] DateFormats = ["MM-dd-yyyy", "M-d-yyyy", "MM/dd/yyyy", "M/d/yyyy"];

    /// <summary>OREA prints 01/01/1900 for registrations without an expiration date.</summary>
    private static DateOnly? Date(string value)
    {
        var normalized = Optional(value);
        if (normalized is null) return null;
        if (!DateOnly.TryParseExact(normalized, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            throw new InvalidDataException($"Invalid OREA date value: {normalized}.");
        return parsed.Year <= 1900 ? null : parsed;
    }
}
