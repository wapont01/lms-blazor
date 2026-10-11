using System.Globalization;
using System.Text;
using Lms.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualBasic.FileIO;

namespace Lms.Application.Data;

public static class TexasTrecCaptureSeed
{
    private const string State = "Texas";
    private const string SourceName = "Texas TREC Broker and Sales Agent License Holder Information";
    private const string SourceUrl = "https://data.texas.gov/api/views/s7ft-44qi/rows.csv?accessType=DOWNLOAD";
    private const string FileName = "Texas_TREC_Broker_and_Sales_Agent_License_Holder_Information_2026-10-10.csv";
    private const string ParserVersion = "trec-license-holder-csv-v1-2026-10-10";
    private static readonly DateTime RetrievedAtUtc = new(2026, 10, 10, 12, 43, 24, DateTimeKind.Utc);
    private static readonly HashSet<string> ActiveStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Active",
        "Probation - Active",
        "Probated Suspension - Active",
        "Probated Revocation - Active"
    };

    public static async Task SeedAsync(ApplicationDbContext dbContext, string? workspaceRoot = null)
    {
        ArgumentNullException.ThrowIfNull(dbContext);

        var root = CaptureSeedRoot.Resolve(workspaceRoot);
        if (root is null)
        {
            return;
        }

        var sourceDirectory = Path.Combine(root, "docs", "TEXAS", "LICENSING", "XLXS");
        if (!Directory.Exists(sourceDirectory))
        {
            return;
        }

        var sourcePath = Path.Combine(sourceDirectory, FileName);
        if (!File.Exists(sourcePath))
        {
            return;
        }

        if (await dbContext.RegulatoryDataSnapshots.AnyAsync(snapshot =>
                snapshot.State == State
                && snapshot.SourceUrl == SourceUrl
                && snapshot.RetrievedAtUtc == RetrievedAtUtc))
        {
            return;
        }

        var records = ReadActiveRecords(sourcePath);
        records = ResolveReferencedNames(records);
        var omittedRecords = records.RemoveAll(record => record.Name is null);

        var snapshot = new RegulatoryDataSnapshot
        {
            State = State,
            SourceName = omittedRecords == 0
                ? SourceName
                : $"{SourceName} ({omittedRecords} active rows omitted because no name was available)",
            SourceUrl = SourceUrl,
            ParserVersion = ParserVersion,
            RetrievedAtUtc = RetrievedAtUtc,
            Status = RegulatorySnapshotStatuses.PendingReview
        };
        var firmsByLicenseNumber = new Dictionary<string, RegulatoryFirm>(StringComparer.OrdinalIgnoreCase);
        var licenseesByLicenseNumber = new Dictionary<string, RegulatoryLicensee>(StringComparer.OrdinalIgnoreCase);
        var affiliationsByKey = new Dictionary<(string FirmLicense, string LicenseeNumber), RegulatoryFirmLicenseeAffiliation>();

        foreach (var record in records.Where(record => record.LicenseType == "Broker Company"))
        {
            if (record.Name is null)
            {
                continue;
            }

            var firm = new RegulatoryFirm
            {
                SnapshotId = snapshot.Id,
                State = State,
                LicenseNumber = record.LicenseNumber,
                LegalName = record.Name,
                LicenseStatus = record.Status,
                LicenseIssuedDate = record.OriginalLicenseDate,
                LicenseRenewalDate = record.ExpirationDate
            };
            firmsByLicenseNumber.Add(record.LicenseNumber, firm);
        }

        foreach (var record in records.Where(record => record.LicenseType is "Broker Individual" or "Sales Agent"))
        {
            if (record.Name is null)
            {
                continue;
            }

            var licenseType = record.LicenseType == "Broker Individual" ? "Broker" : "Sales Agent";
            var licensee = new RegulatoryLicensee
            {
                SnapshotId = snapshot.Id,
                State = State,
                LicenseNumber = record.LicenseNumber,
                Name = record.Name,
                LicenseType = licenseType,
                LicenseLevel = record.LicenseType == "Broker Individual" ? "Individual Broker" : "Sales Agent",
                LicenseStatus = record.Status,
                LicenseIssuedDate = record.OriginalLicenseDate,
                OriginalLicenseDate = record.OriginalLicenseDate,
                LicenseRenewalDate = record.ExpirationDate,
                RelatedLicenseNumber = record.RelatedLicenseNumber,
                RelatedLicenseType = record.RelatedLicenseType,
                RelatedLicenseName = record.RelatedLicenseName
            };
            if (!licenseesByLicenseNumber.TryAdd(record.LicenseNumber, licensee))
            {
                throw new InvalidDataException($"The Texas TREC CSV contains duplicate active license number '{record.LicenseNumber}'.");
            }
        }

        foreach (var record in records)
        {
            if (record.Name is null)
            {
                continue;
            }

            if (record.LicenseType == "Broker Company")
            {
                if (record.RelatedLicenseType == "Broker Individual"
                    && record.RelatedLicenseNumber is not null
                    && record.RelatedLicenseName is not null)
                {
                    var broker = GetOrAddBroker(record, snapshot.Id, licenseesByLicenseNumber);
                    firmsByLicenseNumber[record.LicenseNumber].QualifyingBrokerName = record.RelatedLicenseName;
                    AddAffiliation(record, firmsByLicenseNumber[record.LicenseNumber], broker, isBrokerInCharge: true);
                }

                continue;
            }

            if (record.RelatedLicenseNumber is null
                || record.RelatedLicenseName is null
                || record.RelatedLicenseType is not ("Broker Company" or "Broker Individual"))
            {
                continue;
            }

            var firm = GetOrAddSponsorFirm(record, snapshot.Id, firmsByLicenseNumber);
            var licensee = licenseesByLicenseNumber[record.LicenseNumber];
            if (firm.QualifyingBrokerName is null && record.RelatedLicenseType == "Broker Individual")
            {
                firm.QualifyingBrokerName = record.RelatedLicenseName;
            }

            AddAffiliation(
                record,
                firm,
                licensee,
                isBrokerInCharge: record.LicenseType == "Broker Individual"
                    && string.Equals(record.DesignatedSupervisorFlag, "1", StringComparison.Ordinal));
        }

        await PersistPendingSnapshotAsync(
            dbContext,
            snapshot,
            firmsByLicenseNumber.Values.ToList(),
            licenseesByLicenseNumber.Values.ToList(),
            affiliationsByKey.Values.ToList());

        void AddAffiliation(
            TrecLicenseRecord record,
            RegulatoryFirm firm,
            RegulatoryLicensee licensee,
            bool isBrokerInCharge)
        {
            var key = (firm.LicenseNumber, licensee.LicenseNumber);
            if (!affiliationsByKey.ContainsKey(key))
            {
                affiliationsByKey.Add(key, new RegulatoryFirmLicenseeAffiliation
                {
                    SnapshotId = snapshot.Id,
                    FirmId = firm.Id,
                    LicenseeId = licensee.Id,
                    RelationshipEffectiveDate = record.RelationshipStartDate,
                    IsBrokerInCharge = isBrokerInCharge,
                    BrokerInChargeName = isBrokerInCharge ? licensee.Name : null
                });
            }
        }
    }

    private static List<TrecLicenseRecord> ReadActiveRecords(string sourcePath)
    {
        using var parser = new TextFieldParser(sourcePath, Encoding.UTF8)
        {
            TextFieldType = FieldType.Delimited,
            HasFieldsEnclosedInQuotes = true,
            TrimWhiteSpace = false
        };
        parser.SetDelimiters(",");

        var headers = parser.ReadFields()
            ?? throw new InvalidDataException($"The Texas TREC CSV has no header row: {sourcePath}");
        var columnIndexes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < headers.Length; index++)
        {
            var header = headers[index].Trim().Trim('\uFEFF');
            if (header.Length == 0 || !columnIndexes.TryAdd(header, index))
            {
                throw new InvalidDataException($"The Texas TREC CSV has an invalid or duplicate column '{header}'.");
            }
        }

        var requiredColumns = new[]
        {
            "License Type",
            "License Number",
            "Full Name",
            "Status",
            "Original License Date",
            "License Expiration Date",
            "Designated Supervisor Flag",
            "Related License Type",
            "Related License Number",
            "Related License Full Name",
            "Related License Start Date"
        };
        var missingColumns = requiredColumns.Where(column => !columnIndexes.ContainsKey(column)).ToArray();
        if (missingColumns.Length > 0)
        {
            throw new InvalidDataException(
                $"The Texas TREC CSV is missing required columns: {string.Join(", ", missingColumns)}.");
        }

        var records = new List<TrecLicenseRecord>();
        while (!parser.EndOfData)
        {
            var fields = parser.ReadFields();
            if (fields is null || fields.All(string.IsNullOrWhiteSpace))
            {
                continue;
            }

            if (fields.Length != headers.Length)
            {
                throw new InvalidDataException(
                    $"Expected {headers.Length} CSV columns but found {fields.Length} in {sourcePath}, row {parser.LineNumber}.");
            }

            string? Get(string column)
            {
                var value = fields[columnIndexes[column]].Trim();
                return value.Length == 0 ? null : value;
            }

            var status = Get("Status");
            if (status is null || !ActiveStatuses.Contains(status))
            {
                continue;
            }

            var licenseNumber = Get("License Number");
            var licenseType = Get("License Type");
            var name = Get("Full Name");
            if (licenseNumber is null || licenseType is null)
            {
                throw new InvalidDataException(
                    $"A current Texas TREC record is missing its license number or type in {sourcePath}, row {parser.LineNumber}.");
            }

            if (licenseType is not ("Broker Company" or "Broker Individual" or "Sales Agent"))
            {
                throw new InvalidDataException(
                    $"Unexpected Texas license type '{licenseType}' in {sourcePath}, row {parser.LineNumber}.");
            }

            records.Add(new TrecLicenseRecord(
                licenseType,
                licenseNumber,
                name,
                status,
                ParseDate(Get("Original License Date")),
                ParseDate(Get("License Expiration Date")),
                Get("Designated Supervisor Flag"),
                Get("Related License Type"),
                Get("Related License Number"),
                Get("Related License Full Name"),
                ParseDate(Get("Related License Start Date"))));
        }

        return records;
    }

    private static List<TrecLicenseRecord> ResolveReferencedNames(List<TrecLicenseRecord> records)
    {
        var namesByLicenseNumber = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var record in records)
        {
            if (record.RelatedLicenseNumber is not null && record.RelatedLicenseName is not null)
            {
                if (!namesByLicenseNumber.TryGetValue(record.RelatedLicenseNumber, out var names))
                {
                    names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    namesByLicenseNumber.Add(record.RelatedLicenseNumber, names);
                }

                names.Add(record.RelatedLicenseName);
            }
        }

        return records
            .Select(record => record.Name is null
                && namesByLicenseNumber.TryGetValue(record.LicenseNumber, out var names)
                && names.Count == 1
                ? record with { Name = names.Single() }
                : record)
            .ToList();
    }

    private static RegulatoryLicensee GetOrAddBroker(
        TrecLicenseRecord source,
        Guid snapshotId,
        Dictionary<string, RegulatoryLicensee> licenseesByLicenseNumber)
    {
        if (licenseesByLicenseNumber.TryGetValue(source.RelatedLicenseNumber!, out var existing))
        {
            return existing;
        }

        var broker = new RegulatoryLicensee
        {
            SnapshotId = snapshotId,
            State = State,
            LicenseNumber = source.RelatedLicenseNumber!,
            Name = source.RelatedLicenseName!,
            LicenseType = "Broker",
            LicenseLevel = "Individual Broker"
        };
        licenseesByLicenseNumber.Add(broker.LicenseNumber, broker);
        return broker;
    }

    private static RegulatoryFirm GetOrAddSponsorFirm(
        TrecLicenseRecord source,
        Guid snapshotId,
        Dictionary<string, RegulatoryFirm> firmsByLicenseNumber)
    {
        if (firmsByLicenseNumber.TryGetValue(source.RelatedLicenseNumber!, out var existing))
        {
            return existing;
        }

        var firm = new RegulatoryFirm
        {
            SnapshotId = snapshotId,
            State = State,
            LicenseNumber = source.RelatedLicenseNumber!,
            LegalName = source.RelatedLicenseName!,
            TradeName = source.RelatedLicenseType == "Broker Individual" ? "Individual Broker" : null
        };
        firmsByLicenseNumber.Add(firm.LicenseNumber, firm);
        return firm;
    }

    private static async Task PersistPendingSnapshotAsync(
        ApplicationDbContext dbContext,
        RegulatoryDataSnapshot snapshot,
        List<RegulatoryFirm> firms,
        List<RegulatoryLicensee> licensees,
        List<RegulatoryFirmLicenseeAffiliation> affiliations)
    {
        var autoDetectChangesEnabled = dbContext.ChangeTracker.AutoDetectChangesEnabled;
        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        try
        {
            dbContext.ChangeTracker.AutoDetectChangesEnabled = false;
            dbContext.RegulatoryDataSnapshots.Add(snapshot);
            await dbContext.SaveChangesAsync();
            dbContext.Entry(snapshot).State = EntityState.Detached;

            await PersistBatchesAsync(dbContext, dbContext.RegulatoryFirms, firms);
            await PersistBatchesAsync(dbContext, dbContext.RegulatoryLicensees, licensees);
            await PersistBatchesAsync(dbContext, dbContext.RegulatoryFirmLicenseeAffiliations, affiliations);

            await transaction.CommitAsync();
        }
        finally
        {
            dbContext.ChangeTracker.AutoDetectChangesEnabled = autoDetectChangesEnabled;
        }
    }

    private static async Task PersistBatchesAsync<TEntity>(
        ApplicationDbContext dbContext,
        DbSet<TEntity> set,
        IReadOnlyList<TEntity> entities)
        where TEntity : class
    {
        const int batchSize = 500;
        foreach (var batch in entities.Chunk(batchSize))
        {
            set.AddRange(batch);
            await dbContext.SaveChangesAsync();
            foreach (var entity in batch)
            {
                dbContext.Entry(entity).State = EntityState.Detached;
            }
        }
    }

    private static DateOnly? ParseDate(string? value) =>
        DateOnly.TryParseExact(
            value,
            ["MM/dd/yyyy", "M/d/yyyy"],
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var date)
            ? date
            : null;

    private sealed record TrecLicenseRecord(
        string LicenseType,
        string LicenseNumber,
        string? Name,
        string Status,
        DateOnly? OriginalLicenseDate,
        DateOnly? ExpirationDate,
        string? DesignatedSupervisorFlag,
        string? RelatedLicenseType,
        string? RelatedLicenseNumber,
        string? RelatedLicenseName,
        DateOnly? RelationshipStartDate);
}
