using System.Security.Claims;
using Lms.Application.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Lms.Web.Services;

public static class MicrosoftSignIn
{
    private const string Scheme = "Microsoft";

    public static void AddGoogleSignIn(this WebApplicationBuilder builder)
    {
        if (!builder.Configuration.GetValue<bool>("Authentication:Google:Enabled")) return;
        var clientId = builder.Configuration["Authentication:Google:ClientId"];
        var secret = builder.Configuration["Authentication:Google:ClientSecret"];
        if (string.IsNullOrWhiteSpace(clientId) || !clientId.EndsWith(".apps.googleusercontent.com", StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(secret))
            throw new InvalidOperationException("Google sign-in requires a Web client ID and client secret.");
        builder.Services.AddScoped<IFederatedAccountService, FederatedAccountService>();
        builder.Services.AddAuthentication().AddOpenIdConnect("Google", options =>
        {
            options.Authority = "https://accounts.google.com";
            options.ClientId = clientId;
            options.ClientSecret = secret;
            options.CallbackPath = "/signin-google";
            ConfigureFlow(builder, options, "Google");
        });
    }

    public static void AddMicrosoftSignIn(this WebApplicationBuilder builder)
    {
        if (!builder.Configuration.GetValue<bool>("Authentication:Sso:Enabled")) return;
        var clientId = builder.Configuration["Authentication:Sso:ClientId"];
        var secret = builder.Configuration["Authentication:Sso:ClientSecret"];
        if (!Guid.TryParse(clientId, out _) || string.IsNullOrWhiteSpace(secret))
            throw new InvalidOperationException("Microsoft sign-in requires a client ID and client secret.");

        builder.Services.AddScoped<IFederatedAccountService, FederatedAccountService>();
        builder.Services.AddAuthentication().AddOpenIdConnect(Scheme, options =>
        {
            options.Authority = "https://login.microsoftonline.com/common/v2.0";
            options.ClientId = clientId;
            options.ClientSecret = secret;
            options.CallbackPath = "/signin-oidc";
            ConfigureFlow(builder, options, Scheme);
            // Common metadata uses a tenant placeholder; require the exact issuer for the signed tenant claim.
            options.TokenValidationParameters.IssuerValidator = (issuer, token, _) =>
            {
                var tenant = token switch
                {
                    Microsoft.IdentityModel.JsonWebTokens.JsonWebToken jwt => jwt.GetClaim("tid")?.Value,
                    System.IdentityModel.Tokens.Jwt.JwtSecurityToken jwt => jwt.Claims.FirstOrDefault(c => c.Type == "tid")?.Value,
                    _ => null
                };
                if (!Guid.TryParse(tenant, out var tenantId)
                    || issuer != $"https://login.microsoftonline.com/{tenantId:D}/v2.0")
                    throw new SecurityTokenInvalidIssuerException("Unexpected Microsoft issuer.");
                return issuer;
            };
        });
    }

    private static void ConfigureFlow(WebApplicationBuilder builder, OpenIdConnectOptions options, string scheme)
    {
            options.SignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            options.ResponseType = "code";
            options.ResponseMode = "query";
            options.UsePkce = true;
            options.MapInboundClaims = false;
            options.SaveTokens = false;
            options.Scope.Clear();
            options.Scope.Add("openid");
            options.Scope.Add("profile");
            options.Scope.Add("email");
            options.NonceCookie.SameSite = SameSiteMode.Lax;
            options.CorrelationCookie.SameSite = SameSiteMode.Lax;
            var securePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
            options.NonceCookie.SecurePolicy = securePolicy;
            options.CorrelationCookie.SecurePolicy = securePolicy;
            options.Events.OnRedirectToIdentityProvider = context =>
            {
                context.ProtocolMessage.Prompt = "select_account";
                return Task.CompletedTask;
            };
            options.Events.OnTokenValidated = context =>
            {
                context.Properties!.Items["FederatedValidatedIssuer"] = context.SecurityToken.Issuer;
                return Task.CompletedTask;
            };
            options.Events.OnTicketReceived = async context =>
            {
                var principal = context.Principal!;
                try
                {
                    context.Properties!.Items.TryGetValue("FederatedValidatedIssuer", out var issuer);
                    var user = await context.HttpContext.RequestServices.GetRequiredService<IFederatedAccountService>()
                        .ResolveAsync(issuer ?? "",
                            principal.FindFirstValue("sub") ?? "",
                            principal.FindFirstValue("email") ?? principal.FindFirstValue("preferred_username"),
                            principal.FindFirstValue("name"), context.HttpContext.RequestAborted);
                    context.Principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
                    {
                        new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                        new Claim(ClaimTypes.Name, user.DisplayName),
                        new Claim(ClaimTypes.Email, user.Email),
                        new Claim(ClaimTypes.Role, user.Role)
                    }, scheme));
                    context.Properties.Items.Remove("FederatedValidatedIssuer");
                }
                catch (FederatedSignInException ex)
                {
                    context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>()
                        .CreateLogger("FederatedSignIn").LogWarning("{Provider} sign-in rejected: {Reason}", scheme, ex.Message);
                    context.HandleResponse();
                    context.Response.Redirect(scheme == "Google" ? "/login?error=google" : "/login?error=sso");
                }
            };
            options.Events.OnRemoteFailure = context =>
            {
                context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>()
                    .CreateLogger("FederatedSignIn").LogWarning("{Provider} authentication failed. Check provider configuration or account eligibility.", scheme);
                context.HandleResponse();
                context.Response.Redirect(scheme == "Google" ? "/login?error=google" : "/login?error=sso");
                return Task.CompletedTask;
            };
    }

    public static void MapMicrosoftSignIn(this WebApplication app)
    {
        if (app.Configuration.GetValue<bool>("Authentication:Sso:Enabled"))
            MapProvider(app, "/auth/sso/login", Scheme);
        if (app.Configuration.GetValue<bool>("Authentication:Google:Enabled"))
            MapProvider(app, "/auth/google/login", "Google");
    }

    private static void MapProvider(WebApplication app, string path, string scheme)
    {
        app.MapGet(path, (HttpContext context, string? returnUrl) =>
        {
            if (app.Environment.IsDevelopment() && context.Request.Host.Host != "localhost")
                return (IResult)TypedResults.Redirect("http://localhost:5000/login");
            if (!app.Environment.IsDevelopment() && !context.Request.IsHttps)
                return TypedResults.BadRequest();
            var safeUrl = !string.IsNullOrWhiteSpace(returnUrl) && returnUrl.StartsWith('/')
                && !returnUrl.StartsWith("//") && !returnUrl.Contains('\\') && !returnUrl.Any(char.IsControl)
                    ? returnUrl : "/";
            return TypedResults.Challenge(new AuthenticationProperties { RedirectUri = safeUrl }, new[] { scheme });
        }).RequireRateLimiting("login").WithSummary($"Start {scheme} federated sign-in.");
    }
}
