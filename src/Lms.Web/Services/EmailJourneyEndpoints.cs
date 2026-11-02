using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Lms.Application.Services;
using Lms.Domain.Entities;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;

namespace Lms.Web.Services;

public static class EmailJourneyEndpoints
{
    private const string PendingCookie = "Lms.EmailStep";

    public static void MapEmailJourney(this WebApplication app)
    {
        if (!app.Environment.IsDevelopment() || !app.Services.GetRequiredService<EmailJourneyOptions>().Enabled)
            return;
        var group = app.MapGroup("/auth/email-test").RequireRateLimiting("login");
        group.MapPost("/roster-register", async Task<IResult> (HttpContext context, [FromForm] RegisterRosterRequest form,
            IRosterRegistrationService roster, IEmailJourneyService journey,
            IAntiforgery antiforgery, CancellationToken ct) =>
        {
            if (!await ValidFormAsync(context, antiforgery)) return TypedResults.BadRequest();
            if (context.User.Identity?.IsAuthenticated == true || form.Password != form.ConfirmPassword)
                return TypedResults.LocalRedirect("/roster-registration?error=registration");
            try
            {
                var user = await roster.RegisterAsync(form.LicenseeId, form.Email, form.Password, ct);
                var id = await journey.SendVerificationAsync(user, ct);
                return TypedResults.LocalRedirect(id.HasValue
                    ? "/email-test?step=sent" : "/roster-registration?error=delivery");
            }
            catch (RosterRequestException ex)
            {
                app.Logger.LogWarning("Roster registration rejected: {Reason}", ex.Message);
                return TypedResults.LocalRedirect("/roster-registration?error=registration");
            }
        }).DisableAntiforgery().WithSummary("Register the test inbox with a student-selected roster record awaiting email verification.");

        group.MapPost("/roster-submit", async Task<IResult> (HttpContext context, [FromForm] SubmitRosterRequest form,
            IRosterRegistrationService roster, IAntiforgery antiforgery, CancellationToken ct) =>
        {
            if (!await ValidFormAsync(context, antiforgery)) return TypedResults.BadRequest();
            if (!Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
                return TypedResults.Forbid();
            try { await roster.SubmitAsync(userId, form.LicenseeId, ct); }
            catch (RosterRequestException ex)
            {
                app.Logger.LogWarning("Roster selection rejected: {Reason}", ex.Message);
                return TypedResults.LocalRedirect(ex.Message == "Verify your account email before saving a license record."
                    ? "/roster-registration?error=email-verification"
                    : "/roster-registration?error=selection");
            }
            return TypedResults.LocalRedirect("/account");
        }).RequireAuthorization().DisableAntiforgery().WithSummary("Confirm a student's roster selection with verified email, without asserting verified license ownership.");

        group.MapPost("/register", async Task<IResult> (HttpContext context, [FromForm] RegisterEmailTestRequest form,
            IUserAccountService accounts, IEmailJourneyService journey, EmailJourneyOptions options,
            IAntiforgery antiforgery, CancellationToken ct) =>
        {
            if (!await ValidFormAsync(context, antiforgery)) return TypedResults.BadRequest();
            if (!options.AppliesTo(form.Email) || form.Password != form.ConfirmPassword)
                return TypedResults.LocalRedirect("/email-test?error=registration");
            UserAccount user;
            try { user = await accounts.RegisterLearnerAsync(form.Email, form.Password, marketingOptIn: false); }
            catch (InvalidOperationException ex)
            {
                app.Logger.LogWarning(ex, "Synthetic learner registration rejected.");
                return TypedResults.LocalRedirect("/email-test?error=registration");
            }
            var id = await journey.SendVerificationAsync(user, ct);
            return TypedResults.LocalRedirect(id.HasValue ? "/email-test?step=sent" : "/email-test?error=delivery");
        }).DisableAntiforgery().WithSummary("Register the allowlisted synthetic learner and send email verification.");

        group.MapPost("/verify", async Task<IResult> (HttpContext context, [FromForm] VerifyEmailTestRequest form,
            IEmailJourneyService journey, IAntiforgery antiforgery, CancellationToken ct) =>
        {
            if (!await ValidFormAsync(context, antiforgery)) return TypedResults.BadRequest();
            var user = await journey.ConsumeAsync(form.Id, EmailJourneyService.VerifyPurpose, form.Token, ct);
            return TypedResults.LocalRedirect(user is null ? "/email-test?error=verification" : "/email-test?step=verified");
        }).DisableAntiforgery().WithSummary("Consume a single-use email verification token after explicit confirmation.");

        group.MapPost("/code", async Task<IResult> (HttpContext context, [FromForm] LoginEmailCodeRequest form,
            IEmailJourneyService journey, IDataProtectionProvider protection, IAuditLogService audit,
            IAntiforgery antiforgery, CancellationToken ct) =>
        {
            if (!await ValidFormAsync(context, antiforgery)) return TypedResults.BadRequest();
            var pending = ReadPending(context, protection);
            if (pending is null)
                return TypedResults.LocalRedirect("/email-test?error=expired");
            var user = await journey.ConsumeAsync(pending.Id, EmailJourneyService.LoginPurpose, form.Code, ct);
            if (user is null)
                return TypedResults.LocalRedirect("/email-test?step=code&error=code");
            context.Response.Cookies.Delete(PendingCookie);
            return await SignInAsync(context, user, pending.ReturnUrl, audit);
        }).DisableAntiforgery().WithSummary("Complete password-plus-email-code sign-in using a browser-bound pending challenge.");

        group.MapPost("/certificate", async Task<IResult> (HttpContext context, IUserAccountService accounts,
            IEmailJourneyService journey, EmailJourneyOptions options, IAntiforgery antiforgery, CancellationToken ct) =>
        {
            if (!await ValidFormAsync(context, antiforgery)) return TypedResults.BadRequest();
            var user = await CurrentUserAsync(context, accounts);
            if (user is null || !options.AppliesTo(user.Email) || user.EmailVerifiedAtUtc is null)
                return TypedResults.Forbid();
            var sent = await journey.SendCertificateAsync(user, ct);
            return TypedResults.LocalRedirect(sent ? "/email-test?step=certificate-sent" : "/email-test?error=delivery");
        }).RequireAuthorization().DisableAntiforgery().WithSummary("Email a synthetic PDF and authenticated download link to the verified test learner.");

        group.MapGet("/certificate", async Task<IResult> (HttpContext context,
            IEmailJourneyService journey, CancellationToken ct) =>
        {
            if (!Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
                return TypedResults.NotFound();
            var certificate = await journey.GetCertificateAsync(userId, ct);
            if (certificate is null) return TypedResults.NotFound();
            context.Response.Headers.CacheControl = "no-store";
            return TypedResults.File(DevelopmentCertificateService.GeneratePdf(certificate), "application/pdf", certificate.FileName);
        }).RequireAuthorization().WithSummary("Download the signed-in synthetic learner's non-credit sample certificate.");
    }

    public static void SetPending(HttpContext context, IDataProtectionProvider provider, Guid id, string? returnUrl)
    {
        var pending = new PendingEmailLogin(id, DateTime.UtcNow.AddMinutes(10), SafeReturnUrl(returnUrl));
        context.Response.Cookies.Append(PendingCookie,
            provider.CreateProtector(PendingCookie).Protect(JsonSerializer.Serialize(pending)), new CookieOptions
            {
                HttpOnly = true, Secure = context.Request.IsHttps, SameSite = SameSiteMode.Strict,
                MaxAge = TimeSpan.FromMinutes(10), IsEssential = true
            });
    }

    private static PendingEmailLogin? ReadPending(HttpContext context, IDataProtectionProvider provider)
    {
        if (!context.Request.Cookies.TryGetValue(PendingCookie, out var value)) return null;
        try
        {
            var pending = JsonSerializer.Deserialize<PendingEmailLogin>(provider.CreateProtector(PendingCookie).Unprotect(value));
            return pending?.ExpiresAtUtc > DateTime.UtcNow ? pending : null;
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException)
        {
            context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("EmailJourney")
                .LogWarning("Rejected invalid pending email-login cookie.");
            return null;
        }
    }

    public static async Task<IResult> SignInAsync(HttpContext context, UserAccount user, string? returnUrl, IAuditLogService audit)
    {
        var displayName = user.DisplayName;
        foreach (var suffix in new[] { " ADMIN", " BROKER", " INSTRUCTOR", " LEARNER", " STUDENT" })
        {
            if (displayName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                displayName = displayName[..^suffix.Length].TrimEnd();
                break;
            }
        }
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, displayName),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(ClaimTypes.Role, user.Role)
        };
        await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme)));
        await audit.WriteAsync(user.Id, user.Email, "auth.login.succeeded", "UserAccount", user.Id);
        return TypedResults.LocalRedirect(user.ForcePasswordChange ? "/account?forceChange=1" : SafeReturnUrl(returnUrl));
    }

    private static string SafeReturnUrl(string? url) =>
        !string.IsNullOrWhiteSpace(url) && url.StartsWith('/') && !url.StartsWith("//")
            && !url.Contains('\\') && !url.Any(char.IsControl) ? url : "/";

    private static async Task<bool> ValidFormAsync(HttpContext context, IAntiforgery antiforgery)
    {
        try { await antiforgery.ValidateRequestAsync(context); return true; }
        catch (AntiforgeryValidationException)
        {
            context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("EmailJourney")
                .LogWarning("Rejected email journey request with invalid antiforgery token.");
            return false;
        }
    }

    private static Task<UserAccount?> CurrentUserAsync(HttpContext context, IUserAccountService accounts) =>
        Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
            ? accounts.GetByIdAsync(id) : Task.FromResult<UserAccount?>(null);

    private sealed record PendingEmailLogin(Guid Id, DateTime ExpiresAtUtc, string ReturnUrl);
}

/// <summary>Registers the configured synthetic test inbox as a learner.</summary>
public sealed record RegisterEmailTestRequest(string Email, string Password, string ConfirmPassword);
/// <summary>Confirms a single-use email verification link.</summary>
public sealed record VerifyEmailTestRequest(Guid Id, string Token);
/// <summary>Submits the email code for this browser's pending password sign-in.</summary>
public sealed record LoginEmailCodeRequest(string Code);
/// <summary>Registers a synthetic account and locally records its roster selection for review.</summary>
public sealed record RegisterRosterRequest(Guid LicenseeId, string Email, string Password, string ConfirmPassword);
/// <summary>Submits the signed-in synthetic learner's roster selection.</summary>
public sealed record SubmitRosterRequest(Guid LicenseeId);
