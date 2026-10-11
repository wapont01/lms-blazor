using System.Globalization;
using System.IO.Compression;
using System.Text;
using Lms.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Lms.Application.Data;

public static class CaliforniaDreCaptureSeed
{
    private const string State = "California";
    private const string SourceUrl = "https://dre.ca.gov/Licensees/ExamineeLicenseeListDataFiles.html";
    private const string CurrentListFileName = "CurrList_20261009.zip";
    private const string BrokerAssociateFileName = "broker_associates_20261009.csv";
    private const string SourceName =
        "California DRE current licensee and broker-associate files (education information only)";
    private static readonly string[] DreDateFormats = ["yyyyMMdd"];
    private static readonly string[] AffiliationDateFormats = ["yyyy-MM-dd", "M/d/yyyy"];
    private static readonly string[] BrokerAssociateDateFormats = ["yyyy-MM-dd", "yyyyMMdd", "M/d/yyyy"];
    private static readonly DateTime RetrievedAtUtc =
        new DateTime(2026, 10, 10, 0, 32, 13, DateTimeKind.Utc).AddTicks(1_852_840);

    public static async Task SeedAsync(ApplicationDbContext dbContext, string? workspaceRoot = null)
    {
        ArgumentNullException.ThrowIfNull(dbContext);

        var root = CaptureSeedRoot.Resolve(workspaceRoot);
        if (root is null)
        {
            return;
        }

        var sourceDirectory = Path.Combine(root, "docs", "CALIFORNIA", "LICENSING", "XLXS");
        if (!Directory.Exists(sourceDirectory))
        {
            return;
        }

        var currentListPath = Path.Combine(sourceDirectory, CurrentListFileName);
        var brokerAssociatePath = Path.Combine(sourceDirectory, BrokerAssociateFileName);
        if (!File.Exists(currentListPath))
        {
            throw new FileNotFoundException("The dated California DRE current-licensee archive is required.", currentListPath);
        }

        if (!File.Exists(brokerAssociatePath))
        {
            throw new FileNotFoundException("The converted California DRE broker-associate CSV is required.", brokerAssociatePath);
        }

        if (await dbContext.RegulatoryDataSnapshots.AnyAsync(snapshot =>
                snapshot.State == State
                && snapshot.SourceUrl == SourceUrl
                && snapshot.RetrievedAtUtc == RetrievedAtUtc))
        {
            return;
        }

        var snapshot = new RegulatoryDataSnapshot
        {
            State = State,
            SourceName = SourceName,
            SourceUrl = SourceUrl,
            ParserVersion = "ca-dre-current-list-v1-2026-10-09",
            RetrievedAtUtc = RetrievedAtUtc,
            Status = RegulatorySnapshotStatuses.PendingReview
        };
        var firmsByLicenseNumber = new Dictionary<string, RegulatoryFirm>(StringComparer.Ordinal);
        var licenseesByIdentity = new Dictionary<LicenseeIdentity, RegulatoryLicensee>();
        var licenseesByNumber = new Dictionary<string, List<RegulatoryLicensee>>(StringComparer.Ordinal);
        var licensees = new List<RegulatoryLicensee>();
        var affiliations = new Dictionary<AffiliationIdentity, RegulatoryFirmLicenseeAffiliation>();
        var logProgress = string.Equals(
            Environment.GetEnvironmentVariable("LMS_CA_IMPORT_DIAGNOSTICS"),
            "1",
            StringComparison.Ordinal);
        var timer = System.Diagnostics.Stopwatch.StartNew();
        var scannedRows = 0;

        foreach (var row in ReadCurrentList(currentListPath))
        {
            scannedRows++;
            var record = ParseCurrentLicensee(row, currentListPath);
            var identity = new LicenseeIdentity(
                record.LicenseNumber,
                record.LicenseType,
                record.IsAdditionalLicenseRecord,
                record.LicenseStatus,
                record.LicenseIssuedDate,
                record.LicenseRenewalDate,
                record.OriginalLicenseDate,
                record.RelatedLicenseNumber,
                record.RelatedLicenseType,
                record.RelatedLicenseName,
                record.Name,
                record.IsRestricted,
                record.EthicsAndAgencyOnly);

            if (!licenseesByIdentity.TryGetValue(identity, out var licensee))
            {
                licensee = new RegulatoryLicensee
                {
                    SnapshotId = snapshot.Id,
                    State = State,
                    LicenseNumber = record.LicenseNumber,
                    Name = record.Name,
                    LicenseType = record.LicenseType,
                    LicenseLevel = record.LicenseType,
                    LicenseStatus = record.LicenseStatus,
                    LicenseIssuedDate = record.LicenseIssuedDate,
                    OriginalLicenseDate = record.OriginalLicenseDate,
                    LicenseRenewalDate = record.LicenseRenewalDate,
                    RelatedLicenseNumber = record.RelatedLicenseNumber,
                    RelatedLicenseType = record.RelatedLicenseType,
                    RelatedLicenseName = record.RelatedLicenseName,
                    IsRestricted = record.IsRestricted,
                    EthicsAndAgencyOnly = record.EthicsAndAgencyOnly,
                    IsAdditionalLicenseRecord = record.IsAdditionalLicenseRecord
                };
                licenseesByIdentity.Add(identity, licensee);
                licensees.Add(licensee);

                if (!licenseesByNumber.TryGetValue(record.LicenseNumber, out var recordsForNumber))
                {
                    recordsForNumber = [];
                    licenseesByNumber.Add(record.LicenseNumber, recordsForNumber);
                }

                recordsForNumber.Add(licensee);
            }

            if (record.LicenseType == "Corporation")
            {
                GetOrAddFirm(firmsByLicenseNumber, snapshot.Id, record.LicenseNumber, record.Name, record.LicenseStatus);
            }

            if (logProgress && scannedRows % 100_000 == 0)
            {
                Console.Error.WriteLine(
                    $"California DRE import parsed {scannedRows:N0} rows in {timer.Elapsed}.");
            }
        }

        if (logProgress)
        {
            Console.Error.WriteLine($"California DRE primary list parsed in {timer.Elapsed}.");
        }
        foreach (var licensee in licensees)
        {
            if (licensee.RelatedLicenseNumber is not null
                && licensee.RelatedLicenseType is "Broker" or "Corporation")
            {
                if (!firmsByLicenseNumber.TryGetValue(licensee.RelatedLicenseNumber, out var relatedFirm))
                {
                    var relatedLicensee = licenseesByNumber
                        .GetValueOrDefault(licensee.RelatedLicenseNumber)?
                        .FirstOrDefault(candidate => candidate.LicenseType == licensee.RelatedLicenseType);
                    var relatedName = licensee.RelatedLicenseName ?? relatedLicensee?.Name;
                    if (relatedName is not null)
                    {
                        relatedFirm = GetOrAddFirm(
                            firmsByLicenseNumber,
                            snapshot.Id,
                            licensee.RelatedLicenseNumber,
                            relatedName,
                            relatedLicensee?.LicenseStatus);
                    }
                }

                if (relatedFirm is not null)
                {
                    AddAffiliation(affiliations, snapshot.Id, relatedFirm, licensee);
                }
            }

            if (licensee.LicenseType == "Corporation"
                && licensee.RelatedLicenseNumber is not null
                && licensee.RelatedLicenseType == "Officer"
                && firmsByLicenseNumber.TryGetValue(licensee.LicenseNumber, out var corporationFirm)
                && licenseesByNumber.TryGetValue(licensee.RelatedLicenseNumber, out var relatedOfficers))
            {
                foreach (var officer in relatedOfficers.Where(candidate => candidate.LicenseType == "Officer"))
                {
                    AddAffiliation(affiliations, snapshot.Id, corporationFirm, officer);
                }
            }
        }

        if (logProgress)
        {
            Console.Error.WriteLine(
                $"California DRE relationships prepared in {timer.Elapsed}; {licensees.Count:N0} licensee records, {firmsByLicenseNumber.Count:N0} firms.");
        }
        var brokerAssociateRows = 0;
        foreach (var row in ReadBrokerAssociateList(brokerAssociatePath))
        {
            brokerAssociateRows++;
            if (logProgress && brokerAssociateRows % 5_000 == 0)
            {
                Console.Error.WriteLine(
                    $"California DRE import processing broker-associate row {brokerAssociateRows:N0} in {timer.Elapsed}.");
            }

            var responsibleBrokerNumber = NormalizeLicenseNumber(
                RequireValue(row, "rb_license_id", brokerAssociatePath),
                brokerAssociatePath,
                row.LineNumber);
            var associateNumber = NormalizeLicenseNumber(
                RequireValue(row, "ba_license_id", brokerAssociatePath),
                brokerAssociatePath,
                row.LineNumber);
            var responsibleBrokerName = RequireValue(row, "rb_name", brokerAssociatePath);
            var responsibleBrokerType = RequireValue(row, "rb_license_type", brokerAssociatePath);
            var associateName = RequireValue(row, "ba_name", brokerAssociatePath);
            var affiliationStartDate = ParseDate(
                row.Get("affiliation_start_date"),
                AffiliationDateFormats,
                brokerAssociatePath,
                row.LineNumber);
            var responsibleBrokerExpiration = ParseDate(
                row.Get("rb_expiration_date"),
                BrokerAssociateDateFormats,
                brokerAssociatePath,
                row.LineNumber);
            var associateExpiration = ParseDate(
                row.Get("ba_expiration_date"),
                BrokerAssociateDateFormats,
                brokerAssociatePath,
                row.LineNumber);

            if (responsibleBrokerType is not ("Broker" or "Corporation"))
            {
                throw new InvalidDataException(
                    $"Unexpected responsible broker type '{responsibleBrokerType}' at {brokerAssociatePath}, row {row.LineNumber}.");
            }

            var firm = GetOrAddFirm(
                firmsByLicenseNumber,
                snapshot.Id,
                responsibleBrokerNumber,
                responsibleBrokerName,
                null);
            if (licenseesByNumber.TryGetValue(responsibleBrokerNumber, out var responsibleBrokerRecords))
            {
                foreach (var responsibleBroker in responsibleBrokerRecords.Where(
                             candidate => candidate.LicenseType == responsibleBrokerType))
                {
                    EnsureMatchingRenewalDate(
                        responsibleBroker.LicenseRenewalDate,
                        responsibleBrokerExpiration,
                        responsibleBrokerNumber,
                        brokerAssociatePath,
                        row.LineNumber);
                }
            }

            if (licenseesByNumber.TryGetValue(associateNumber, out var existingAssociates))
            {
                var brokerRecords = existingAssociates
                    .Where(candidate => candidate.LicenseType == "Broker")
                    .ToArray();
                if (brokerRecords.Length > 0)
                {
                    foreach (var associate in brokerRecords)
                    {
                        EnsureMatchingRenewalDate(
                            associate.LicenseRenewalDate,
                            associateExpiration,
                            associateNumber,
                            brokerAssociatePath,
                            row.LineNumber);
                        associate.LicenseLevel = "Broker Associate";
                        AddAffiliation(affiliations, snapshot.Id, firm, associate, affiliationStartDate);
                    }

                    continue;
                }
            }

            var newAssociate = new RegulatoryLicensee
            {
                SnapshotId = snapshot.Id,
                State = State,
                LicenseNumber = associateNumber,
                Name = associateName,
                LicenseType = "Broker",
                LicenseLevel = "Broker Associate",
                LicenseStatus = "Licensed",
                    LicenseRenewalDate = associateExpiration,
                    IsAdditionalLicenseRecord = null
            };
            if (!licenseesByNumber.TryGetValue(associateNumber, out var associatesForNumber))
            {
                associatesForNumber = [];
                licenseesByNumber.Add(associateNumber, associatesForNumber);
            }

            associatesForNumber.Add(newAssociate);
            licensees.Add(newAssociate);
            AddAffiliation(affiliations, snapshot.Id, firm, newAssociate, affiliationStartDate);

        }

        if (logProgress)
        {
            Console.Error.WriteLine(
                $"California DRE data prepared in {timer.Elapsed}; {firmsByLicenseNumber.Count:N0} firms, {licensees.Count:N0} licensees, {affiliations.Count:N0} affiliations.");
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        dbContext.RegulatoryDataSnapshots.Add(snapshot);
        await dbContext.SaveChangesAsync();
        if (logProgress)
        {
            Console.Error.WriteLine($"California DRE snapshot saved in {timer.Elapsed}.");
        }

        await PersistBatchesAsync(dbContext, dbContext.RegulatoryFirms, firmsByLicenseNumber.Values.ToList());
        if (logProgress)
        {
            Console.Error.WriteLine($"California DRE firms saved in {timer.Elapsed}.");
        }
        await PersistBatchesAsync(dbContext, dbContext.RegulatoryLicensees, licensees);
        if (logProgress)
        {
            Console.Error.WriteLine($"California DRE licensees saved in {timer.Elapsed}.");
        }
        await PersistBatchesAsync(dbContext, dbContext.RegulatoryFirmLicenseeAffiliations, affiliations.Values.ToList());
        if (logProgress)
        {
            Console.Error.WriteLine($"California DRE affiliations saved in {timer.Elapsed}.");
        }

        snapshot.Status = RegulatorySnapshotStatuses.Approved;
        snapshot.ReviewedAtUtc = RetrievedAtUtc;
        await dbContext.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    private static IEnumerable<CsvRecord> ReadCurrentList(string archivePath)
    {
        using var archive = ZipFile.OpenRead(archivePath);
        var entry = archive.GetEntry("CurrList.csv")
            ?? archive.Entries.FirstOrDefault(candidate => candidate.FullName.EndsWith("/CurrList.csv", StringComparison.OrdinalIgnoreCase));
        if (entry is null)
        {
            throw new InvalidDataException($"The California DRE archive does not contain CurrList.csv: {archivePath}");
        }

        using var stream = entry.Open();
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        using var csv = new CsvRecordReader(reader, entry.FullName);
        csv.RequireColumns(
            "lic_number",
            "multiple_license_ind",
            "lic_type",
            "lic_status",
            "lic_effective_date",
            "lic_expiration_date",
            "original_date_of_license",
            "related_lic_number",
            "related_lic_type",
            "lastname_primary",
            "firstname_secondary",
            "name_suffix",
            "related_lastname_primary",
            "related_firstname_secondary",
            "related_name_suffix",
            "restricted_flag",
            "ethics_and_agency_ind");

        foreach (var row in csv.ReadRows())
        {
            yield return row;
        }
    }

    private static DreLicenseeRecord ParseCurrentLicensee(CsvRecord row, string sourcePath)
    {
        var licenseNumber = NormalizeLicenseNumber(
            RequireValue(row, "lic_number", sourcePath),
            sourcePath,
            row.LineNumber);
        var isAdditionalLicenseRecord = ParseMultipleLicenseIndicator(
            RequireValue(row, "multiple_license_ind", sourcePath),
            sourcePath,
            row.LineNumber);
        var licenseType = RequireValue(row, "lic_type", sourcePath);
        var licenseStatus = RequireValue(row, "lic_status", sourcePath);
        var name = BuildName(
            row.Get("lastname_primary"),
            row.Get("firstname_secondary"),
            row.Get("name_suffix"),
            licenseType == "Corporation");
        if (name is null)
        {
            throw new InvalidDataException($"Missing licensee name in {sourcePath}, row {row.LineNumber}.");
        }

        var relatedLicenseNumberValue = row.Get("related_lic_number");
        var relatedLicenseNumber = string.IsNullOrWhiteSpace(relatedLicenseNumberValue)
            ? null
            : NormalizeLicenseNumber(relatedLicenseNumberValue, sourcePath, row.LineNumber);
        var relatedLicenseType = Normalize(row.Get("related_lic_type"));
        var relatedLicenseName = BuildName(
            row.Get("related_lastname_primary"),
            row.Get("related_firstname_secondary"),
            row.Get("related_name_suffix"),
            relatedLicenseType == "Corporation");

        return new DreLicenseeRecord(
            licenseNumber,
            licenseType,
            isAdditionalLicenseRecord,
            licenseStatus,
            name,
            ParseDate(row.Get("lic_effective_date"), DreDateFormats, sourcePath, row.LineNumber),
            ParseDate(row.Get("lic_expiration_date"), DreDateFormats, sourcePath, row.LineNumber),
            ParseDate(row.Get("original_date_of_license"), DreDateFormats, sourcePath, row.LineNumber),
            relatedLicenseNumber,
            relatedLicenseType,
            relatedLicenseName,
            ParseFlag(row.Get("restricted_flag"), sourcePath, row.LineNumber),
            ParseFlag(row.Get("ethics_and_agency_ind"), sourcePath, row.LineNumber));
    }

    private static IEnumerable<CsvRecord> ReadBrokerAssociateList(string csvPath)
    {
        using var reader = new StreamReader(csvPath, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        using var csv = new CsvRecordReader(reader, csvPath);
        csv.RequireColumns(
            "rb_license_id",
            "ba_license_id",
            "affiliation_start_date",
            "rb_name",
            "rb_license_type",
            "rb_expiration_date",
            "ba_name",
            "ba_expiration_date");

        foreach (var row in csv.ReadRows())
        {
            yield return row;
        }
    }

    private static RegulatoryFirm GetOrAddFirm(
        Dictionary<string, RegulatoryFirm> firmsByLicenseNumber,
        Guid snapshotId,
        string licenseNumber,
        string legalName,
        string? licenseStatus)
    {
        if (firmsByLicenseNumber.TryGetValue(licenseNumber, out var firm))
        {
            return firm;
        }

        firm = new RegulatoryFirm
        {
            SnapshotId = snapshotId,
            State = State,
            LicenseNumber = licenseNumber,
            LegalName = legalName,
            LicenseStatus = licenseStatus
        };
        firmsByLicenseNumber.Add(licenseNumber, firm);
        return firm;
    }

    private static void AddAffiliation(
        Dictionary<AffiliationIdentity, RegulatoryFirmLicenseeAffiliation> affiliations,
        Guid snapshotId,
        RegulatoryFirm firm,
        RegulatoryLicensee licensee,
        DateOnly? relationshipEffectiveDate = null)
    {
        var identity = new AffiliationIdentity(firm.LicenseNumber, licensee.Id);
        if (affiliations.TryGetValue(identity, out var existing))
        {
            if (relationshipEffectiveDate.HasValue)
            {
                if (existing.RelationshipEffectiveDate.HasValue
                    && existing.RelationshipEffectiveDate != relationshipEffectiveDate)
                {
                    throw new InvalidDataException(
                        $"Conflicting California broker-associate affiliation dates for licenses {firm.LicenseNumber} and {licensee.LicenseNumber}.");
                }

                existing.RelationshipEffectiveDate = relationshipEffectiveDate;
            }

            return;
        }

        affiliations.Add(identity, new RegulatoryFirmLicenseeAffiliation
        {
            SnapshotId = snapshotId,
            FirmId = firm.Id,
            LicenseeId = licensee.Id,
            RelationshipEffectiveDate = relationshipEffectiveDate
        });
    }

    private static async Task PersistBatchesAsync<TEntity>(
        ApplicationDbContext dbContext,
        DbSet<TEntity> set,
        IReadOnlyList<TEntity> entities)
        where TEntity : class
    {
        if (dbContext.Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite")
        {
            await PersistSqliteBatchesAsync(dbContext, entities);
            return;
        }

        const int batchSize = 5_000;
        var autoDetectChangesEnabled = dbContext.ChangeTracker.AutoDetectChangesEnabled;
        dbContext.ChangeTracker.AutoDetectChangesEnabled = false;
        try
        {
            for (var offset = 0; offset < entities.Count; offset += batchSize)
            {
                var batch = entities.Skip(offset).Take(batchSize).ToArray();
                set.AddRange(batch);
                await dbContext.SaveChangesAsync();

                foreach (var entity in batch)
                {
                    dbContext.Entry(entity).State = EntityState.Detached;
                }
            }
        }
        finally
        {
            dbContext.ChangeTracker.AutoDetectChangesEnabled = autoDetectChangesEnabled;
        }
    }

    private static async Task PersistSqliteBatchesAsync<TEntity>(
        ApplicationDbContext dbContext,
        IReadOnlyList<TEntity> entities)
        where TEntity : class
    {
        if (entities.Count == 0)
        {
            return;
        }

        var entityType = dbContext.Model.FindEntityType(typeof(TEntity))
            ?? throw new InvalidOperationException($"No EF model metadata found for {typeof(TEntity).Name}.");
        var tableName = entityType.GetTableName()
            ?? throw new InvalidOperationException($"No table mapping found for {typeof(TEntity).Name}.");
        var storeObject = StoreObjectIdentifier.Table(tableName, entityType.GetSchema());
        var properties = entityType.GetProperties()
            .Select(property => (Property: property, ColumnName: property.GetColumnName(storeObject)))
            .Where(mapping => mapping.ColumnName is not null)
            .ToArray();
        var columns = string.Join(", ", properties.Select(mapping => QuoteSqliteIdentifier(mapping.ColumnName!)));
        var getters = properties.Select(mapping => CreateGetter<TEntity>(mapping.Property)).ToArray();
        var connection = dbContext.Database.GetDbConnection();
        var transaction = dbContext.Database.CurrentTransaction?.GetDbTransaction();
        const int maxParametersPerCommand = 900;
        var batchSize = Math.Max(1, maxParametersPerCommand / properties.Length);

        for (var offset = 0; offset < entities.Count; offset += batchSize)
        {
            var count = Math.Min(batchSize, entities.Count - offset);
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;

            var valueGroups = new string[count];
            for (var row = 0; row < count; row++)
            {
                var placeholders = new string[properties.Length];
                for (var column = 0; column < properties.Length; column++)
                {
                    var parameterName = $"p{row * properties.Length + column}";
                    placeholders[column] = $"@{parameterName}";
                    var parameter = command.CreateParameter();
                    parameter.ParameterName = parameterName;
                    var value = getters[column](entities[offset + row]);
                    var converter = properties[column].Property.GetTypeMapping().Converter;
                    parameter.Value = value is null
                        ? DBNull.Value
                        : converter?.ConvertToProvider(value) ?? value;
                    command.Parameters.Add(parameter);
                }

                valueGroups[row] = $"({string.Join(", ", placeholders)})";
            }

            command.CommandText =
                $"INSERT INTO {QuoteSqliteIdentifier(tableName)} ({columns}) VALUES {string.Join(", ", valueGroups)};";
            await command.ExecuteNonQueryAsync();
        }
    }

    private static Func<TEntity, object?> CreateGetter<TEntity>(IProperty property)
        where TEntity : class
    {
        var parameter = System.Linq.Expressions.Expression.Parameter(typeof(TEntity), "entity");
        var member = property.PropertyInfo is not null
            ? System.Linq.Expressions.Expression.Property(parameter, property.PropertyInfo)
            : property.FieldInfo is not null
                ? System.Linq.Expressions.Expression.Field(parameter, property.FieldInfo)
                : throw new InvalidOperationException($"No CLR member found for {property.Name}.");
        var body = System.Linq.Expressions.Expression.Convert(member, typeof(object));
        return System.Linq.Expressions.Expression.Lambda<Func<TEntity, object?>>(body, parameter).Compile();
    }

    private static string QuoteSqliteIdentifier(string identifier) =>
        $"\"{identifier.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";

    private static string? BuildName(string? lastName, string? firstName, string? suffix, bool isCorporation)
    {
        var last = Normalize(lastName);
        var first = Normalize(firstName);
        var name = isCorporation
            ? string.Join(" ", new[] { last, first }.Where(value => value is not null))
            : string.Join(" ", new[] { first, last }.Where(value => value is not null));
        var normalizedSuffix = Normalize(suffix);
        if (normalizedSuffix is not null && !isCorporation)
        {
            name = string.IsNullOrEmpty(name) ? normalizedSuffix : $"{name}, {normalizedSuffix}";
        }

        return string.IsNullOrWhiteSpace(name) ? null : name;
    }

    private static string NormalizeLicenseNumber(string value, string sourcePath, int lineNumber)
    {
        var licenseNumber = value.Trim();
        if (licenseNumber.Length is 0 or > 8)
        {
            throw new InvalidDataException(
                $"Invalid California license number '{value}' in {sourcePath}, row {lineNumber}.");
        }

        foreach (var character in licenseNumber)
        {
            if (!char.IsAsciiDigit(character))
            {
                throw new InvalidDataException(
                    $"Invalid California license number '{value}' in {sourcePath}, row {lineNumber}.");
            }
        }

        return licenseNumber.PadLeft(8, '0');
    }

    private static DateOnly? ParseDate(
        string? value,
        string[] formats,
        string sourcePath,
        int lineNumber)
    {
        var normalized = Normalize(value);
        if (normalized is null)
        {
            return null;
        }

        if (DateOnly.TryParseExact(
                normalized,
                formats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var date))
        {
            return date;
        }

        throw new InvalidDataException($"Invalid date '{value}' in {sourcePath}, row {lineNumber}.");
    }

    private static bool ParseFlag(string? value, string sourcePath, int lineNumber) =>
        Normalize(value)?.ToUpperInvariant() switch
        {
            null or "" or "N" => false,
            "Y" => true,
            _ => throw new InvalidDataException($"Invalid Y/N flag '{value}' in {sourcePath}, row {lineNumber}.")
        };

    private static bool ParseMultipleLicenseIndicator(string value, string sourcePath, int lineNumber) =>
        value.Trim().ToUpperInvariant() switch
        {
            "N" => false,
            "Y" => true,
            _ => throw new InvalidDataException(
                $"Invalid Multiple_License_Ind value '{value}' in {sourcePath}, row {lineNumber}.")
        };

    private static string RequireValue(CsvRecord row, string column, string sourcePath) =>
        Normalize(row.Get(column))
        ?? throw new InvalidDataException($"Missing required field '{column}' in {sourcePath}, row {row.LineNumber}.");

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static void EnsureMatchingRenewalDate(
        DateOnly? currentListDate,
        DateOnly? affiliationListDate,
        string licenseNumber,
        string sourcePath,
        int lineNumber)
    {
        if (currentListDate.HasValue
            && affiliationListDate.HasValue
            && currentListDate != affiliationListDate)
        {
            throw new InvalidDataException(
                $"California DRE current-list and broker-associate expiration dates conflict for license {licenseNumber} in {sourcePath}, row {lineNumber}.");
        }
    }

    private sealed class CsvRecordReader : IDisposable
    {
        private readonly TextReader _reader;
        private readonly Dictionary<string, int> _columnIndexes;
        private readonly bool[] _selectedColumns;
        private readonly int _columnCount;
        private readonly string _sourceName;

        public CsvRecordReader(TextReader reader, string sourceName)
        {
            _sourceName = sourceName;
            _reader = reader;
            var headerLine = _reader.ReadLine();
            var headers = headerLine is null
                ? null
                : ParseCsvLine(headerLine, Enumerable.Repeat(true, headerLine.Count(character => character == ',') + 1).ToArray(), sourceName, 1);
            if (headers is null)
            {
                throw new InvalidDataException($"The CSV source has no header row: {sourceName}");
            }

            _columnCount = headers.Length;
            _columnIndexes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < headers.Length; index++)
            {
                var header = headers[index]?.Trim().Trim('\uFEFF');
                if (string.IsNullOrWhiteSpace(header) || !_columnIndexes.TryAdd(header, index))
                {
                    throw new InvalidDataException($"Invalid or duplicate CSV column '{header}' in {sourceName}.");
                }
            }

            _selectedColumns = new bool[_columnCount];
        }

        public void RequireColumns(params string[] columns)
        {
            var missingColumns = columns.Where(column => !_columnIndexes.ContainsKey(column)).ToArray();
            if (missingColumns.Length > 0)
            {
                throw new InvalidDataException(
                    $"The CSV source {_sourceName} is missing required columns: {string.Join(", ", missingColumns)}.");
            }

            foreach (var column in columns)
            {
                _selectedColumns[_columnIndexes[column]] = true;
            }
        }

        public IEnumerable<CsvRecord> ReadRows()
        {
            var lineNumber = 1;
            while (_reader.ReadLine() is { } line)
            {
                lineNumber++;
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                var fields = ParseCsvLine(line, _selectedColumns, _sourceName, lineNumber);
                if (fields.Length != _columnCount)
                {
                    throw new InvalidDataException(
                        $"Expected {_columnCount} columns but found {fields.Length} in {_sourceName}, row {lineNumber}.");
                }

                yield return new CsvRecord(fields, lineNumber, _columnIndexes);
            }
        }

        public void Dispose() => _reader.Dispose();

        private string?[] ParseCsvLine(string line, bool[] selectedColumns, string sourceName, int lineNumber)
        {
            var fields = new string?[selectedColumns.Length];
            var fieldIndex = 0;
            var position = 0;

            while (true)
            {
                if (fieldIndex >= fields.Length)
                {
                    throw new InvalidDataException($"Too many CSV columns in {sourceName}, row {lineNumber}.");
                }

                if (position < line.Length && line[position] == '"')
                {
                    position++;
                    var contentStart = position;
                    StringBuilder? decodedValue = null;
                    var closed = false;

                    while (position < line.Length)
                    {
                        if (line[position] != '"')
                        {
                            position++;
                            continue;
                        }

                        if (position + 1 < line.Length && line[position + 1] == '"')
                        {
                            if (selectedColumns[fieldIndex])
                            {
                                decodedValue ??= new StringBuilder();
                                decodedValue.Append(line, contentStart, position - contentStart);
                                decodedValue.Append('"');
                            }

                            position += 2;
                            contentStart = position;
                            continue;
                        }

                        if (selectedColumns[fieldIndex])
                        {
                            fields[fieldIndex] = decodedValue is null
                                ? line[contentStart..position]
                                : decodedValue.Append(line, contentStart, position - contentStart).ToString();
                        }

                        position++;
                        closed = true;
                        break;
                    }

                    if (!closed || position < line.Length && line[position] != ',')
                    {
                        throw new InvalidDataException($"Malformed quoted CSV field in {sourceName}, row {lineNumber}.");
                    }
                }
                else
                {
                    var contentStart = position;
                    while (position < line.Length && line[position] != ',')
                    {
                        position++;
                    }

                    if (selectedColumns[fieldIndex])
                    {
                        fields[fieldIndex] = line[contentStart..position];
                    }
                }

                if (position == line.Length)
                {
                    if (fieldIndex != fields.Length - 1)
                    {
                        throw new InvalidDataException(
                            $"Expected {fields.Length} columns but found {fieldIndex + 1} in {sourceName}, row {lineNumber}.");
                    }

                    return fields;
                }

                position++;
                fieldIndex++;
            }
        }
    }

    private sealed record CsvRecord(
        string?[] Fields,
        int LineNumber,
        Dictionary<string, int> ColumnIndexes)
    {
        public string? Get(string column) =>
            ColumnIndexes.TryGetValue(column, out var index) ? Fields[index] : null;
    }

    private sealed record DreLicenseeRecord(
        string LicenseNumber,
        string LicenseType,
        bool IsAdditionalLicenseRecord,
        string LicenseStatus,
        string Name,
        DateOnly? LicenseIssuedDate,
        DateOnly? LicenseRenewalDate,
        DateOnly? OriginalLicenseDate,
        string? RelatedLicenseNumber,
        string? RelatedLicenseType,
        string? RelatedLicenseName,
        bool IsRestricted,
        bool EthicsAndAgencyOnly);

    private sealed record LicenseeIdentity(
        string LicenseNumber,
        string LicenseType,
        bool IsAdditionalLicenseRecord,
        string LicenseStatus,
        DateOnly? LicenseIssuedDate,
        DateOnly? LicenseRenewalDate,
        DateOnly? OriginalLicenseDate,
        string? RelatedLicenseNumber,
        string? RelatedLicenseType,
        string? RelatedLicenseName,
        string Name,
        bool IsRestricted,
        bool EthicsAndAgencyOnly);

    private readonly record struct AffiliationIdentity(string FirmLicenseNumber, Guid LicenseeId);
}
