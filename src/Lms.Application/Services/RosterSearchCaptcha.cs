using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace Lms.Application.Services;

public sealed class TurnstileOptions
{
    public string SiteKey { get; set; } = "";
    public string SecretKey { get; set; } = "";
    public string[] AllowedHostnames { get; set; } = [];
    public bool UseTestKeys { get; set; }
    public bool IsConfigured => !string.IsNullOrWhiteSpace(SiteKey)
        && !string.IsNullOrWhiteSpace(SecretKey) && AllowedHostnames.Length > 0;
}

public interface IRosterCaptchaVerifier
{
    Task VerifyAsync(string? token, CancellationToken cancellationToken);
}

public sealed class TurnstileVerifier(HttpClient client, TurnstileOptions options,
    ILogger<TurnstileVerifier> logger) : IRosterCaptchaVerifier
{
    public async Task VerifyAsync(string? token, CancellationToken cancellationToken)
    {
        if (!options.IsConfigured)
        {
            logger.LogError("Roster CAPTCHA verification is not configured.");
            throw new RosterRequestException("Search verification is currently unavailable. Please try again later.");
        }
        if (string.IsNullOrWhiteSpace(token) || token.Length > 2048)
            throw new RosterRequestException("Complete the security check before searching again.");

        try
        {
            using var body = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["secret"] = options.SecretKey, ["response"] = token
            });
            using var response = await client.PostAsync(
                "https://challenges.cloudflare.com/turnstile/v0/siteverify", body, cancellationToken);
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<VerificationResponse>(cancellationToken);
            if (result is null || !result.Success
                || (options.UseTestKeys ? result.Metadata?.TestingKey != true : result.Action != "roster-search")
                || !options.AllowedHostnames.Contains(result.Hostname, StringComparer.OrdinalIgnoreCase))
            {
                logger.LogWarning("Roster CAPTCHA verification was rejected.");
                throw new RosterRequestException("Security verification failed or expired. Complete a new security check.");
            }
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "Roster CAPTCHA provider request failed.");
            throw new RosterRequestException("Search verification is currently unavailable. Please try again later.");
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "Roster CAPTCHA provider returned an invalid response.");
            throw new RosterRequestException("Search verification is currently unavailable. Please try again later.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Roster CAPTCHA provider timed out.");
            throw new RosterRequestException("Search verification timed out. Please try again.");
        }
    }

    private sealed record VerificationResponse(
        [property: JsonPropertyName("success")] bool Success,
        [property: JsonPropertyName("hostname")] string? Hostname,
        [property: JsonPropertyName("action")] string? Action,
        [property: JsonPropertyName("metadata")] VerificationMetadata? Metadata);

    private sealed record VerificationMetadata(
        [property: JsonPropertyName("result_with_testing_key")] bool TestingKey);
}

public sealed class RosterSearchSession(IRosterCaptchaVerifier? verifier = null)
{
    private int _submissions;
    private int _busy;
    public int Submissions => Volatile.Read(ref _submissions);
    public bool RequiresCaptcha => Submissions >= 3;

    // Only raises the count when restoring protected prerender state.
    public void RestoreMinimum(int submissions)
    {
        int current;
        do { current = Submissions; }
        while (submissions > current && Interlocked.CompareExchange(ref _submissions, submissions, current) != current);
    }

    public async Task<IDisposable> EnterAsync(string? token, CancellationToken cancellationToken)
    {
        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
            throw new RosterRequestException("A search is already in progress. Please wait.");
        try
        {
            if (RequiresCaptcha)
            {
                if (verifier is null)
                    throw new RosterRequestException("Search verification is currently unavailable. Please try again later.");
                await verifier.VerifyAsync(token, cancellationToken);
            }
            Interlocked.Increment(ref _submissions);
            return new Lease(this);
        }
        catch
        {
            Volatile.Write(ref _busy, 0);
            throw;
        }
    }

    private sealed class Lease(RosterSearchSession owner) : IDisposable
    {
        private int _disposed;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                Volatile.Write(ref owner._busy, 0);
        }
    }
}
