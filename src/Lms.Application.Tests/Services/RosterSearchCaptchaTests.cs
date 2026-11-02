using System.Net;
using System.Text;
using Lms.Application.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Lms.Application.Tests.Services;

public class RosterSearchCaptchaTests
{
    [Fact]
    public async Task FirstThreeFree_EveryLaterSearchNeedsFreshVerification()
    {
        var verifier = new SingleUseVerifier();
        var session = new RosterSearchSession(verifier);
        for (var index = 0; index < 3; index++)
        {
            Assert.False(session.RequiresCaptcha);
            (await session.EnterAsync(null, default)).Dispose();
        }
        Assert.True(session.RequiresCaptcha);
        await Assert.ThrowsAsync<RosterRequestException>(() => session.EnterAsync(null, default));
        Assert.Equal(3, session.Submissions);
        (await session.EnterAsync("token-four", default)).Dispose();
        await Assert.ThrowsAsync<RosterRequestException>(() => session.EnterAsync("token-four", default));
        await Assert.ThrowsAsync<RosterRequestException>(() => session.EnterAsync(null, default));
        (await session.EnterAsync("token-five", default)).Dispose();
        Assert.Equal(5, session.Submissions);
    }

    [Fact]
    public async Task OverlappingSearchesCannotConsumeFreeAllowanceConcurrently()
    {
        var session = new RosterSearchSession();
        using var first = await session.EnterAsync(null, default);
        await Assert.ThrowsAsync<RosterRequestException>(() => session.EnterAsync(null, default));
        Assert.Equal(1, session.Submissions);
    }

    [Fact]
    public async Task RestoringOrClearingResultsCannotLowerCount_AndMissingVerifierFailsClosed()
    {
        var session = new RosterSearchSession();
        session.RestoreMinimum(3);
        session.RestoreMinimum(0);
        await Assert.ThrowsAsync<RosterRequestException>(() => session.EnterAsync("anything", default));
        Assert.True(session.RequiresCaptcha);
        Assert.Equal(3, session.Submissions);
    }

    [Theory]
    [InlineData(true, "localhost", "roster-search", true)]
    [InlineData(false, "localhost", "roster-search", false)]
    [InlineData(true, "attacker.invalid", "roster-search", false)]
    [InlineData(true, "localhost", "login", false)]
    [InlineData(true, null, "roster-search", false)]
    public async Task ProviderResponseMustMatchSuccessHostnameAndAction(bool success, string? hostname, string action, bool allowed)
    {
        using var handler = new ProviderHandler(System.Text.Json.JsonSerializer.Serialize(new { success, hostname, action }));
        using var client = new HttpClient(handler);
        var verifier = new TurnstileVerifier(client, Configured(), NullLogger<TurnstileVerifier>.Instance);
        if (allowed) await verifier.VerifyAsync("synthetic-token", default);
        else await Assert.ThrowsAsync<RosterRequestException>(() => verifier.VerifyAsync("synthetic-token", default));
        Assert.Equal("https://challenges.cloudflare.com/turnstile/v0/siteverify", handler.Endpoint);
        Assert.Contains("response=synthetic-token", handler.Body);
    }

    [Theory]
    [InlineData("not-json", HttpStatusCode.OK)]
    [InlineData("{}", HttpStatusCode.ServiceUnavailable)]
    public async Task ProviderFailuresDoNotAuthorizeSearch(string body, HttpStatusCode status)
    {
        using var handler = new ProviderHandler(body, status);
        using var client = new HttpClient(handler);
        var verifier = new TurnstileVerifier(client, Configured(), NullLogger<TurnstileVerifier>.Instance);
        await Assert.ThrowsAsync<RosterRequestException>(() => verifier.VerifyAsync("synthetic-token", default));
    }

    [Fact]
    public async Task MissingConfigurationAndInvalidTokensDoNotCallProvider()
    {
        using var handler = new ProviderHandler("{}");
        using var client = new HttpClient(handler);
        var verifier = new TurnstileVerifier(client, new(), NullLogger<TurnstileVerifier>.Instance);
        await Assert.ThrowsAsync<RosterRequestException>(() => verifier.VerifyAsync("synthetic-token", default));
        verifier = new TurnstileVerifier(client, Configured(), NullLogger<TurnstileVerifier>.Instance);
        await Assert.ThrowsAsync<RosterRequestException>(() => verifier.VerifyAsync(null, default));
        await Assert.ThrowsAsync<RosterRequestException>(() => verifier.VerifyAsync(new string('x', 2049), default));
        Assert.Null(handler.Endpoint);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ExplicitDevelopmentTestModeRequiresTestingMetadata(bool testingKey)
    {
        using var handler = new ProviderHandler(System.Text.Json.JsonSerializer.Serialize(new
        {
            success = true, hostname = "localhost", metadata = new { result_with_testing_key = testingKey }
        }));
        using var client = new HttpClient(handler);
        var options = Configured();
        options.UseTestKeys = true;
        var verifier = new TurnstileVerifier(client, options, NullLogger<TurnstileVerifier>.Instance);
        if (testingKey) await verifier.VerifyAsync("synthetic-token", default);
        else await Assert.ThrowsAsync<RosterRequestException>(() => verifier.VerifyAsync("synthetic-token", default));
    }

    private static TurnstileOptions Configured() => new()
    {
        SiteKey = "synthetic-site-key", SecretKey = "synthetic-secret", AllowedHostnames = ["localhost"]
    };

    private sealed class SingleUseVerifier : IRosterCaptchaVerifier
    {
        private readonly HashSet<string> _used = [];
        public Task VerifyAsync(string? token, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(token) || !_used.Add(token))
                throw new RosterRequestException("Synthetic missing or duplicate token.");
            return Task.CompletedTask;
        }
    }

    private sealed class ProviderHandler(string body, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public string? Endpoint { get; private set; }
        public string? Body { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Endpoint = request.RequestUri!.AbsoluteUri;
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }
}
