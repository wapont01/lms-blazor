using System.Text;
using Lms.Application.Services;
using Xunit;

namespace Lms.Application.Tests.Services;

public sealed class ColoradoDoraBulkImportServiceTests
{
    [Fact]
    public void ReadRecords_ParsesPublishedDoraCsvSchemaAndDates()
    {
        var values = new string[ColoradoDoraBulkImportService.Headers.Count];
        Set("firstName", "Alex");
        Set("lastName", "Example");
        Set("licensePrefix", "FA");
        Set("licenseNumber", "1322849");
        Set("licenseType", "Associate Level Real Estate Broker");
        Set("licenseFirstIssueDate", "09/12/1996");
        Set("licenseExpirationDate", "12/31/2027");
        Set("licenseStatus", "Active");
        var path = Path.Combine(Path.GetTempPath(), $"dora-test-{Guid.NewGuid():N}.csv");
        try
        {
            File.WriteAllText(path,
                string.Join(",", ColoradoDoraBulkImportService.Headers) + Environment.NewLine
                + string.Join(",", values) + Environment.NewLine,
                new UTF8Encoding(false));

            var record = Assert.Single(ColoradoDoraBulkImportService.ReadRecords(path));

            Assert.Equal(2, record.SourceRowNumber);
            Assert.Equal("Alex Example", record.Name);
            Assert.Equal("FA", record.LicensePrefix);
            Assert.Equal("1322849", record.LicenseNumber);
            Assert.Equal("Associate Level Real Estate Broker", record.LicenseType);
            Assert.Equal(new DateOnly(1996, 9, 12), record.IssueDate);
            Assert.Equal(new DateOnly(2027, 12, 31), record.RenewalDate);
            Assert.Equal(20, record.SourceFields.Count);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }

        void Set(string header, string value) =>
            values[Array.IndexOf(ColoradoDoraBulkImportService.Headers.ToArray(), header)] = value;
    }

    [Fact]
    public void LicenseTypeOptionsIncludeAllPublishedCategoriesAndDefaultToRealEstate()
    {
        Assert.Equal(23, ColoradoDoraLicenseTypes.All.Count);
        Assert.Equal(9, ColoradoDoraLicenseTypes.DefaultEnabled.Count);
        Assert.All(ColoradoDoraLicenseTypes.DefaultEnabled, type =>
            Assert.True(ColoradoDoraLicenseTypes.All.Single(option => option.Name == type).IsRealEstate));
    }
}
