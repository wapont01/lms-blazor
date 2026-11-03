using System.Net;
using System.Net.Http.Headers;
using Lms.Application.Services;
using Lms.Web.Services;
using Xunit;

namespace Lms.Web.UITests.Tests;

public sealed class MichiganBulkRosterProviderTests
{
    [Fact]
    public async Task Downloader_FollowsPublishedReportWorkflowWithAnIsolatedSession()
    {
        using var handler = new ReportHandler();
        using var client = new HttpClient(handler);
        var path = Path.Combine(Path.GetTempPath(), "michigan-download-" + Guid.NewGuid().ToString("N"));
        try
        {
            var provider = new MichiganLaraBulkRosterProvider();
            var result = await provider.DownloadFileAsync(client, path, provider.Files[0], CancellationToken.None, (_, _) => { });
            Assert.Equal(4, result.Bytes);
            Assert.Equal(new byte[] { 80, 75, 3, 4 }, await File.ReadAllBytesAsync(path));
            Assert.Equal(2, handler.Calls);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Downloader_RejectsChangedWorkflowOrHtmlInPlaceOfWorkbook(bool wrongWorkflow, bool htmlReport)
    {
        using var handler = new ReportHandler(wrongWorkflow, htmlReport);
        using var client = new HttpClient(handler);
        var path = Path.Combine(Path.GetTempPath(), "michigan-download-" + Guid.NewGuid().ToString("N"));
        var provider = new MichiganLaraBulkRosterProvider();
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            provider.DownloadFileAsync(client, path, provider.Files[0], CancellationToken.None, (_, _) => { }));
        Assert.False(File.Exists(path));
    }

    private sealed class ReportHandler(bool wrongWorkflow = false, bool htmlReport = false) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Assert.StartsWith(MichiganLaraBulkImportService.ReportBase, request.RequestUri!.AbsoluteUri);
            var response = new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = request };
            if (Calls == 1)
            {
                Assert.False(request.Headers.Contains("Cookie"));
                response.Headers.Add("Set-Cookie", "ASP.NET_SessionId=synthetic; Path=/MILARA; Secure; HttpOnly");
                response.Content = new StringContent(wrongWorkflow ? "<html>Workflow changed</html>"
                    : "<script>location.href='ShowReport.aspx?module=Licenses&reportID=32411&reportType=LINK_REPORT_LIST';</script>");
            }
            else
            {
                Assert.Equal("ASP.NET_SessionId=synthetic", Assert.Single(request.Headers.GetValues("Cookie")));
                response.Content = new ByteArrayContent([80, 75, 3, 4]);
                response.Content.Headers.ContentType = new MediaTypeHeaderValue(htmlReport ? "text/html"
                    : "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
            }
            return Task.FromResult(response);
        }
    }
}
