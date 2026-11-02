using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lms.Application.Services;

namespace Lms.Application.Tests.Services;

internal sealed class CaliforniaArchive : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "california-bulk-test-" + Guid.NewGuid().ToString("N"));
    private readonly string expiration;
    private CaliforniaArchive(string expiration) => this.expiration = expiration;
    public static async Task<CaliforniaArchive> CreateAsync(string associateExpiration = "2027-12-31",
        int additionalLicenseCount = 0)
    {
        var archive = new CaliforniaArchive(associateExpiration);
        Directory.CreateDirectory(archive.Path);
        await archive.RewriteAsync(additionalLicenseCount);
        return archive;
    }
    public async Task RewriteAsync(int additionalLicenseCount = 0)
    {
        const string header = "multiple_license_ind,lastname_primary,firstname_secondary,name_suffix,lic_number,lic_type,lic_status,lic_effective_date,lic_expiration_date,original_date_of_license,related_lic_number,related_lastname_primary,related_firstname_secondary,related_name_suffix,related_lic_type,address_1,address_2,city,state,zip_code,foreign_nation,foreign_postal_info,county_name,restricted_flag,ethics_and_agency_ind";
        static string Row(string number, string type, string last, string first = "", string related = "",
            string relatedType = "", string indicator = "N", string flag = "N") => string.Join(",",
            new[] { indicator, last, first, "", number, type, "Licensed NBA", "20200101", "20271231", "20190101",
                related, related.Length > 0 ? "Related Example" : "", "", "", relatedType, "", "", "", "", "", "", "", "", flag, flag });
        var rows = new[]
        {
            Row("00001234", "Salesperson", "Doe", "María", "00009999", "Broker", flag: "Y"),
            Row("00001234", "Officer", "Doe", "María", "00009012", "Corporation", "Y"),
            Row("00001234", "Officer", "Doe", "María", "00009013", "Corporation", "Y"),
            Row("00005678", "Broker", "Smith", "Robert"),
            Row("00009012", "Corporation", "Example Company", related: "00001234", relatedType: "Officer"),
            Row("00009013", "Corporation", "Second Company", related: "00001234", relatedType: "Officer")
        }.Concat(Enumerable.Range(0, additionalLicenseCount)
            .Select(index => Row((80000000 + index).ToString("D8"), "Salesperson", $"Paging Test {index:D3}")))
            .ToArray();
        var zipPath = System.IO.Path.Combine(Path, "CurrList.zip");
        if (File.Exists(zipPath)) File.Delete(zipPath);
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        using (var writer = new StreamWriter(zip.CreateEntry("CurrList.csv").Open(), Encoding.GetEncoding(1252)))
            writer.WriteLine(header + "\n" + string.Join("\n", rows));
        WriteWorkbook(System.IO.Path.Combine(Path, "broker_associates_list.xls"), expiration);
        var entries = new List<FloridaBulkManifestEntry>();
        foreach (var definition in CaliforniaBulkImportService.Definitions)
        {
            var bytes = await File.ReadAllBytesAsync(System.IO.Path.Combine(Path, definition.Name));
            var count = definition.Name == "CurrList.zip" ? rows.Length : 2;
            entries.Add(new(definition.Name, count, bytes.Length, Convert.ToHexString(SHA256.HashData(bytes)), definition.Url,
                DateTimeOffset.UtcNow, "synthetic test", new() { [definition.Columns.ToString()] = count }));
        }
        await File.WriteAllTextAsync(System.IO.Path.Combine(Path, "manifest.json"), JsonSerializer.Serialize(entries));
    }
    private static void WriteWorkbook(string path, string expiration)
    {
        var headers = new[] { "rb_license_id", "ba_license_id", "affiliation_start_date", "rb_name", "rb_addresss_line_1",
            "rb_addresss_line_2", "rb_city", "rb_state", "rb_zip_code", "rb_license_type", "rb_expiration_date", "ba_name",
            "ba_addresss_line_1", "ba_addresss_line_2", "ba_city", "ba_state", "ba_zip_code", "ba_expiration_date" };
        using var writer = new BinaryWriter(File.Create(path), Encoding.ASCII);
        writer.Write((ushort)0x0009); writer.Write((ushort)4); writer.Write((ushort)0x0002); writer.Write((ushort)0x0010);
        var rows = new[]
        {
            headers,
            new[] { "00009012", "00005678", "2023-01-10", "Example Company", "", "", "", "", "", "Corporation", "2027-12-31", "Robert Smith", "", "", "", "", "", expiration },
            new[] { "00009998", "00007890", "2024-01-10", "Missing Broker", "", "", "", "", "", "Broker", "2027-12-31", "Unknown Associate", "", "", "", "", "", "2027-12-31" }
        };
        for (var row = 0; row < rows.Length; row++)
            for (var col = 0; col < rows[row].Length; col++)
            {
                var bytes = Encoding.ASCII.GetBytes(rows[row][col]);
                writer.Write((ushort)0x0004); writer.Write((ushort)(8 + bytes.Length));
                writer.Write((ushort)row); writer.Write((ushort)col);
                writer.Write(new byte[3]); writer.Write((byte)bytes.Length); writer.Write(bytes);
            }
        writer.Write((ushort)0x000A); writer.Write((ushort)0);
    }
    public void Dispose() => Directory.Delete(Path, true);
}
