using System.ComponentModel.DataAnnotations;
using Lms.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace Lms.Web.Services;

public static class PasswordRecoveryEndpoints
{
    public static void MapPasswordRecovery(this WebApplication app)
    {
        var group = app.MapGroup("/auth/password").RequireRateLimiting("login");
        group.MapPost("/forgot", async Task<IResult> ([FromForm] ForgotPasswordRequest form,
            IPasswordRecoveryService recovery, PasswordRecoveryOptions options, CancellationToken ct) =>
        {
            if (!options.Enabled)
                return TypedResults.LocalRedirect("/forgot-password?error=unavailable");
            if (string.IsNullOrWhiteSpace(form.Email) || form.Email.Trim().Length > 160
                || !new EmailAddressAttribute().IsValid(form.Email.Trim()))
                return TypedResults.LocalRedirect("/forgot-password?error=email");
            await recovery.RequestAsync(form.Email, ct);
            return TypedResults.LocalRedirect("/forgot-password?sent=true");
        }).WithSummary("Request a single-use password reset without disclosing account existence.");

        group.MapPost("/reset", async Task<IResult> ([FromForm] ResetPasswordRequest form,
            IPasswordRecoveryService recovery, CancellationToken ct) =>
        {
            if (!Guid.TryParse(form.Id, out var id) || string.IsNullOrEmpty(form.Token) || form.Token.Length != 64)
                return TypedResults.LocalRedirect("/reset-password?error=invalid");
            var path = $"/reset-password?id={id}&token={Uri.EscapeDataString(form.Token)}";
            if (form.Password != form.ConfirmPassword)
                return TypedResults.LocalRedirect(path + "&error=mismatch");
            if (string.IsNullOrWhiteSpace(form.Password) || form.Password.Length < 8)
                return TypedResults.LocalRedirect(path + "&error=password");
            var reset = await recovery.ResetAsync(id, form.Token, form.Password, ct);
            return TypedResults.LocalRedirect(reset ? "/login?passwordReset=true" : "/reset-password?error=invalid");
        }).WithSummary("Consume an expiring email token and set a new password without signing in.");
    }
}

/// <summary>Requests password recovery for an email address.</summary>
public sealed record ForgotPasswordRequest
{
    public string? Email { get; init; }
}

/// <summary>Confirms a password reset using the emailed single-use token.</summary>
public sealed record ResetPasswordRequest
{
    public string? Id { get; init; }
    public string? Token { get; init; }
    public string? Password { get; init; }
    public string? ConfirmPassword { get; init; }
}
