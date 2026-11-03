using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lms.Application.Data;
using Lms.Application.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Lms.Application.Tests.Services;

public sealed class NewYorkDosBulkImportServiceTests
{
    private const string Csv =
        "\"business_name\",\"business_address_1\",\"business_address_2\",\"business_city\",\"business_state\",\"business_zip\",\"county\",\"license_holder_name\",\"license_expiration_date\",\"license_number\",\"license_type\"\n"
        + "\"DOUGLAS ELLIMAN REAL ESTATE\",\"71 FOREST AVE\",,\"LOCUST VALLEY\",\"NY\",\"11560\",\"NASSAU\",\"ALYSKEWYCZ ANNA M\",\"2028-04-07T00:00:00.000\",\"10301200000\",\"ASSOCIATE BROKER\"\n"
        + "\"Bond New York Properties LLC\",\"300 Kent Avenue\",\"Suite 608\",\"BROOKLYN\",\"NY\",\"11249\",\"KINGS\",\"BEBENEK KASJAN H\",\"2028-04-08T00:00:00.000\",\"10401238008\",\"REAL ESTATE SALESPERSON\"\n"
        + "\"BOND NEW YORK PROPERTIES LLC\",\"810  Seventh Avenue\",\"39TH FLOOR\",\"NEW YORK\",\"NY\",\"10019\",\"NEW YORK\",\"BEBENEK KASJAN H\",\"2028-04-08T00:00:00.000\",\"10401238008\",\"REAL ESTATE SALESPERSON\"\n"
        + "\"A T WHITE REALTY LTD\",\"1 MAIN ST\",,\"ALBANY\",\"NY\",\"12207\",\"ALBANY\",,,\"109913389\",\"REAL ESTATE PRINCIPAL OFFICE\"\n"
        + "\"A T WHITE REALTY LTD\",\"1 MAIN ST\",,\"ALBANY\",\"NY\",\"12207\",\"ALBANY\",\"WHITE ALAN T\",\"2027-06-30T00:00:00.000\",\"109913389\",\"CORPORATE BROKER\"\n"
        + ",\"2105 2ND AVENUE\",,\"MERRICK\",\"NY\",\"11566\",\"NASSAU\",,\"2028-08-02T00:00:00.000\",\"10391201894\",\"REAL ESTATE BRANCH OFFICE\"\n";

    [Fact]
    public void ReadRecords_ParsesPublishedSchemaNamesTypesAndDates()
    {
        using var archive = Archive.Create(Csv);

        var records = NewYorkDosBulkImportService.ReadRecords(archive.CsvPath);

        Assert.Equal(6, records.Count);
        var salesperson = records[1];
        Assert.Equal(3, salesperson.SourceRowNumber);
        Assert.Equal("BEBENEK KASJAN H", salesperson.Name);
        Assert.Equal("Real Estate Salesperson", salesperson.LicenseType);
        Assert.Equal("REAL ESTATE SALESPERSON", salesperson.SourceLicenseType);
        Assert.Equal("Bond New York Properties LLC", salesperson.BusinessName);
        Assert.Equal(new DateOnly(2028, 4, 8), salesperson.ExpirationDate);
        Assert.Equal(11, salesperson.SourceFields.Count);

        var office = records[3];
        Assert.Equal("A T WHITE REALTY LTD", office.Name);
        Assert.Null(office.BusinessName);
        Assert.Null(office.ExpirationDate);
        Assert.Equal("Name not published (10391201894)", records[5].Name);
    }

    [Fact]
    public void ReadRecords_RejectsChangedSchema()
    {
        using var archive = Archive.Create(Csv.Replace("\"license_type\"", "\"license_class\"", StringComparison.Ordinal));

        Assert.Throws<InvalidDataException>(() => NewYorkDosBulkImportService.ReadRecords(archive.CsvPath));
    }

    [Fact]
    public async Task ImportArchive_StoresEveryRowAndOneLicenseePerTypeAndNumberIdempotently()
    {
        using var archive = Archive.Create(Csv);
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var importer = new NewYorkDosBulkImportService(db);

        var id = await importer.ImportArchiveAsync(archive.Root);

        Assert.Equal(id, await importer.ImportArchiveAsync(archive.Root));
        var snapshot = await db.RegulatoryDataSnapshots.SingleAsync();
        Assert.Equal("New York", snapshot.State);
        Assert.Equal(NewYorkDosBulkImportService.ParserVersion, snapshot.ParserVersion);
        Assert.Equal(6, await db.RegulatorySourceRecords.CountAsync());
        var licensees = await db.RegulatoryLicensees.ToListAsync();
        Assert.Equal(5, licensees.Count);
        Assert.All(licensees, licensee => Assert.Equal("Active", licensee.LicenseStatus));
        Assert.Equal(2, licensees.Count(licensee => licensee.LicenseNumber == "109913389"));
        var broker = licensees.Single(licensee => licensee.SourceLicenseType == "CORPORATE BROKER");
        Assert.Equal("WHITE ALAN T", broker.Name);
        Assert.Equal("A T WHITE REALTY LTD", broker.RelatedLicenseName);
        Assert.Equal("Company", broker.RelatedLicenseType);
        Assert.Equal(new DateOnly(2027, 6, 30), broker.LicenseRenewalDate);
        Assert.True(RegulatoryRoster.HasBulkProvider("New York"));
        Assert.Equal(NewYorkDosBulkImportService.ParserVersion, RegulatoryRoster.BulkParser("New York"));
    }

    [Fact]
    public async Task ValidateArchive_RejectsManifestRowCountMismatch()
    {
        using var archive = Archive.Create(Csv, declaredRows: 5);

        await Assert.ThrowsAsync<InvalidDataException>(() => NewYorkDosBulkImportService.ValidateArchiveAsync(archive.Root));
    }

    private sealed class Archive : IDisposable
    {
        private Archive(string root) => Root = root;
        public string Root { get; }
        public string CsvPath => Path.Combine(Root, NewYorkDosBulkImportService.FileName);

        public static Archive Create(string csv, long? declaredRows = null)
        {
            var archive = new Archive(Path.Combine(Path.GetTempPath(), $"ny-dos-test-{Guid.NewGuid():N}"));
            Directory.CreateDirectory(archive.Root);
            var bytes = new UTF8Encoding(false).GetBytes(csv);
            File.WriteAllBytes(archive.CsvPath, bytes);
            var rows = declaredRows ?? csv.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length - 1;
            var manifest = new[]
            {
                new FloridaBulkManifestEntry(NewYorkDosBulkImportService.FileName, rows, bytes.Length,
                    Convert.ToHexString(SHA256.HashData(bytes)), NewYorkDosBulkImportService.SourceUrl,
                    DateTimeOffset.UtcNow, "test", new() { ["11"] = rows })
            };
            File.WriteAllText(Path.Combine(archive.Root, "manifest.json"), JsonSerializer.Serialize(manifest));
            return archive;
        }

        public void Dispose()
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
    }
}
