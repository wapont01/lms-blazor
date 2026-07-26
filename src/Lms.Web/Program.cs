using Lms.Application.Services;
using Lms.Application.Data;
using Lms.Web.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Lms.Web.Components;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;
using System.Threading.RateLimiting;
using System.Data.Common;

var builder = WebApplication.CreateBuilder(args);

var emailDeliveryMode = builder.Configuration["Email:DeliveryMode"];
var emailSmtpHost = builder.Configuration["Email:SmtpHost"];
var emailSmtpPort = builder.Configuration["Email:SmtpPort"];
var emailEnableSsl = builder.Configuration["Email:EnableSsl"];
var emailUsername = builder.Configuration["Email:Username"];
var emailPassword = builder.Configuration["Email:Password"];
var emailFromAddress = builder.Configuration["Email:FromEmail"];
var emailFromName = builder.Configuration["Email:FromName"];
var emailTimeoutMs = builder.Configuration["Email:TimeoutMs"];

if (!string.IsNullOrWhiteSpace(emailDeliveryMode) && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("EMAIL_DELIVERY_MODE")))
{
    Environment.SetEnvironmentVariable("EMAIL_DELIVERY_MODE", emailDeliveryMode);
}

if (!string.IsNullOrWhiteSpace(emailSmtpHost) && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SMTP_HOST")))
{
    Environment.SetEnvironmentVariable("SMTP_HOST", emailSmtpHost);
}

if (!string.IsNullOrWhiteSpace(emailSmtpPort) && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SMTP_PORT")))
{
    Environment.SetEnvironmentVariable("SMTP_PORT", emailSmtpPort);
}

if (!string.IsNullOrWhiteSpace(emailEnableSsl) && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SMTP_ENABLE_SSL")))
{
    Environment.SetEnvironmentVariable("SMTP_ENABLE_SSL", emailEnableSsl);
}

if (!string.IsNullOrWhiteSpace(emailUsername) && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SMTP_USERNAME")))
{
    Environment.SetEnvironmentVariable("SMTP_USERNAME", emailUsername);
}

if (!string.IsNullOrWhiteSpace(emailPassword) && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SMTP_PASSWORD")))
{
    Environment.SetEnvironmentVariable("SMTP_PASSWORD", emailPassword);
}

if (!string.IsNullOrWhiteSpace(emailFromAddress) && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SMTP_FROM_EMAIL")))
{
    Environment.SetEnvironmentVariable("SMTP_FROM_EMAIL", emailFromAddress);
}

if (!string.IsNullOrWhiteSpace(emailFromName) && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SMTP_FROM_NAME")))
{
    Environment.SetEnvironmentVariable("SMTP_FROM_NAME", emailFromName);
}

if (!string.IsNullOrWhiteSpace(emailTimeoutMs) && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SMTP_TIMEOUT_MS")))
{
    Environment.SetEnvironmentVariable("SMTP_TIMEOUT_MS", emailTimeoutMs);
}

var stripeSecretFromConfig = builder.Configuration["Stripe:SecretKey"];
var stripeWebhookSecretFromConfig = builder.Configuration["Stripe:WebhookSecret"];
var stripeDirectApiEnabled = builder.Configuration.GetValue<bool>("Stripe:EnableDirectApi");
if (!string.IsNullOrWhiteSpace(stripeSecretFromConfig) && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("STRIPE_SECRET_KEY")))
{
    Environment.SetEnvironmentVariable("STRIPE_SECRET_KEY", stripeSecretFromConfig);
}

if (!string.IsNullOrWhiteSpace(stripeWebhookSecretFromConfig) && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("STRIPE_WEBHOOK_SECRET")))
{
    Environment.SetEnvironmentVariable("STRIPE_WEBHOOK_SECRET", stripeWebhookSecretFromConfig);
}

if (stripeDirectApiEnabled && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("STRIPE_ENABLE_DIRECT_API")))
{
    Environment.SetEnvironmentVariable("STRIPE_ENABLE_DIRECT_API", "true");
}

var appDataDirectory = Path.Combine(builder.Environment.ContentRootPath, "App_Data");
Directory.CreateDirectory(appDataDirectory);
var configuredDatabasePath = builder.Configuration["Lms:DatabasePath"];
var environmentDatabasePath = Environment.GetEnvironmentVariable("LMS_DATABASE_PATH");
var databasePath = string.IsNullOrWhiteSpace(configuredDatabasePath)
    ? string.IsNullOrWhiteSpace(environmentDatabasePath)
        ? Path.Combine(appDataDirectory, "lms.db")
        : Path.GetFullPath(environmentDatabasePath)
    : Path.GetFullPath(configuredDatabasePath);

var databaseDirectory = Path.GetDirectoryName(databasePath);
if (!string.IsNullOrWhiteSpace(databaseDirectory))
{
    Directory.CreateDirectory(databaseDirectory);
}

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddControllers();

builder.Services.AddCascadingAuthenticationState();
builder.Services.AddAuthorization();
var ssoEnabled = builder.Configuration.GetValue<bool>("Authentication:Sso:Enabled");
var ssoAuthority = builder.Configuration["Authentication:Sso:Authority"];
var ssoClientId = builder.Configuration["Authentication:Sso:ClientId"];
var ssoClientSecret = builder.Configuration["Authentication:Sso:ClientSecret"];
var ssoCallbackPath = builder.Configuration["Authentication:Sso:CallbackPath"];
var ssoTestModeEnabled = builder.Configuration.GetValue<bool>("Authentication:Sso:TestModeEnabled");
var ssoTestUserEmail = builder.Configuration["Authentication:Sso:TestUserEmail"];
var ssoTestUserName = builder.Configuration["Authentication:Sso:TestUserName"];

var authenticationBuilder = builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/login";
        options.AccessDeniedPath = "/access-denied";
        options.Cookie.Name = "Lms.Web.Auth";
    });

if (ssoEnabled)
{
    if (string.IsNullOrWhiteSpace(ssoAuthority) || string.IsNullOrWhiteSpace(ssoClientId))
    {
        throw new InvalidOperationException("SSO is enabled but Authentication:Sso:Authority or Authentication:Sso:ClientId is not configured.");
    }

    authenticationBuilder.AddOpenIdConnect(OpenIdConnectDefaults.AuthenticationScheme, options =>
    {
        options.SignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.Authority = ssoAuthority;
        options.ClientId = ssoClientId;
        options.ClientSecret = ssoClientSecret;
        options.ResponseType = "code";
        options.SaveTokens = true;
        options.GetClaimsFromUserInfoEndpoint = true;
        options.CallbackPath = string.IsNullOrWhiteSpace(ssoCallbackPath) ? "/signin-oidc" : ssoCallbackPath;
        options.Scope.Add("openid");
        options.Scope.Add("profile");
        options.Scope.Add("email");
        options.TokenValidationParameters = new TokenValidationParameters
        {
            NameClaimType = ClaimTypes.Name,
            RoleClaimType = ClaimTypes.Role
        };
        options.Events = new OpenIdConnectEvents
        {
            OnTokenValidated = async context =>
            {
                var email = context.Principal?.FindFirst(ClaimTypes.Email)?.Value
                    ?? context.Principal?.FindFirst("email")?.Value
                    ?? context.Principal?.FindFirst("preferred_username")?.Value
                    ?? context.Principal?.FindFirst("upn")?.Value;

                if (string.IsNullOrWhiteSpace(email))
                {
                    context.Fail("SSO login requires an email claim.");
                    return;
                }

                if (!IsAllowedSsoEmailDomain(email, builder.Configuration))
                {
                    context.Fail("Your email domain is not allowed for SSO access.");
                    return;
                }

                var displayName = context.Principal?.FindFirst(ClaimTypes.Name)?.Value
                    ?? context.Principal?.FindFirst("name")?.Value
                    ?? email;

                var userAccountService = context.HttpContext.RequestServices.GetRequiredService<IUserAccountService>();
                var localUser = await userAccountService.UpsertExternalUserAsync(email, displayName);

                var mappedRole = ResolveSsoRole(context.Principal!, builder.Configuration);
                var applyMappedRoleOnSignIn = builder.Configuration.GetValue<bool>("Authentication:Sso:ApplyMappedRoleOnSignIn");
                if (applyMappedRoleOnSignIn && !string.Equals(localUser.Role, mappedRole, StringComparison.OrdinalIgnoreCase))
                {
                    await userAccountService.UpdateRoleAsync(localUser.Id, mappedRole, null, "sso@lms.local");
                    localUser = await userAccountService.GetByIdAsync(localUser.Id) ?? localUser;
                }

                if (!localUser.IsActive)
                {
                    context.Fail("Your account is disabled.");
                    return;
                }

                if (context.Principal?.Identity is not ClaimsIdentity identity)
                {
                    context.Fail("Unable to map SSO claims.");
                    return;
                }

                var claimsToRemove = identity.Claims
                    .Where(claim =>
                        claim.Type == ClaimTypes.NameIdentifier ||
                        claim.Type == ClaimTypes.Name ||
                        claim.Type == ClaimTypes.Email ||
                        claim.Type == ClaimTypes.Role)
                    .ToList();

                foreach (var claim in claimsToRemove)
                {
                    identity.RemoveClaim(claim);
                }

                identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, localUser.Id.ToString()));
                identity.AddClaim(new Claim(ClaimTypes.Name, localUser.DisplayName));
                identity.AddClaim(new Claim(ClaimTypes.Email, localUser.Email));
                identity.AddClaim(new Claim(ClaimTypes.Role, localUser.Role));
            }
        };
    });
}

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("login", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 8,
                Window = TimeSpan.FromMinutes(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0,
                AutoReplenishment = true
            }));
});

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlite($"Data Source={databasePath}"));

builder.Services.AddScoped<ICourseService, CourseService>();
builder.Services.AddScoped<IAuditLogService, AuditLogService>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<IUserAccountService, UserAccountService>();
builder.Services.AddScoped<IAssessmentService, AssessmentService>();
builder.Services.AddScoped<IEnrollmentService, EnrollmentService>();
builder.Services.AddScoped<IDueDateReminderService, DueDateReminderService>();
builder.Services.AddScoped(typeof(Lms.Application.Services.IShoppingCartService), typeof(Lms.Application.Services.ShoppingCartService));
builder.Services.AddScoped(typeof(Lms.Application.Services.IPaymentService), typeof(Lms.Application.Services.PaymentService));
builder.Services.AddScoped(typeof(Lms.Application.Services.IEmailService), typeof(Lms.Application.Services.EmailService));
builder.Services.AddScoped(typeof(Lms.Application.Services.IPDFInvoiceService), typeof(Lms.Application.Services.PDFInvoiceService));
builder.Services.AddScoped(typeof(Lms.Application.Services.IRefundService), typeof(Lms.Application.Services.RefundService));
builder.Services.AddScoped(typeof(Lms.Application.Services.IFraudDetectionService), typeof(Lms.Application.Services.FraudDetectionService));
builder.Services.AddScoped(typeof(Lms.Application.Services.IPaymentReportingService), typeof(Lms.Application.Services.PaymentReportingService));
builder.Services.AddScoped(typeof(Lms.Application.Services.IPayoutService), typeof(Lms.Application.Services.PayoutService));
builder.Services.AddScoped(typeof(Lms.Application.Services.ISubscriptionService), typeof(Lms.Application.Services.SubscriptionService));
builder.Services.AddScoped<ILearnerDashboardService, LearnerDashboardService>();
builder.Services.AddScoped<IModuleCheckpointService, ModuleCheckpointService>();
builder.Services.AddSingleton<IBackgroundJobMonitor, BackgroundJobMonitor>();
builder.Services.AddHostedService<DueDateReminderBackgroundService>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var userAccountService = scope.ServiceProvider.GetRequiredService<IUserAccountService>();
    await EnsureLegacyDatabaseMigrationBaselineAsync(dbContext);
    await EnsureEnrollmentProvenanceSchemaAsync(dbContext);
    await EnsureEnrollmentDueDateSchemaAsync(dbContext);
    await dbContext.Database.MigrateAsync();
    await EnsureEnrollmentProvenanceSchemaAsync(dbContext);
    await EnsureEnrollmentDueDateSchemaAsync(dbContext);
    await userAccountService.EnsureSeedUsersAsync();
    await CourseSeed.SeedAsync(dbContext);
    await CourseSeed.SeedEnrollmentsAsync(dbContext);
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

if (!app.Environment.IsEnvironment("Testing"))
{
    app.UseHttpsRedirection();
}
app.UseStaticFiles();
app.UseAntiforgery();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.Use(async (httpContext, next) =>
{
    if (httpContext.User.Identity?.IsAuthenticated != true)
    {
        await next();
        return;
    }

    var path = httpContext.Request.Path;
    if (path.StartsWithSegments("/account") ||
        path.StartsWithSegments("/auth/logout") ||
        path.StartsWithSegments("/_blazor") ||
        path.StartsWithSegments("/_framework") ||
        path.StartsWithSegments("/_content") ||
        path.StartsWithSegments("/css") ||
        path.StartsWithSegments("/js") ||
        path.StartsWithSegments("/lib") ||
        path.StartsWithSegments("/favicon"))
    {
        await next();
        return;
    }

    var userIdClaim = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    if (!Guid.TryParse(userIdClaim, out var userId))
    {
        await next();
        return;
    }

    var dbContext = httpContext.RequestServices.GetRequiredService<ApplicationDbContext>();
    var userExists = await dbContext.UserAccounts
        .AsNoTracking()
        .AnyAsync(user => user.Id == userId);

    if (!userExists)
    {
        await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

        var returnUrl = httpContext.Request.Path + httpContext.Request.QueryString;
        httpContext.Response.Redirect($"/login?returnUrl={Uri.EscapeDataString(returnUrl)}");
        return;
    }

    var mustChangePassword = await dbContext.UserAccounts
        .AsNoTracking()
        .Where(user => user.Id == userId)
        .Select(user => user.ForcePasswordChange)
        .FirstOrDefaultAsync();

    if (mustChangePassword)
    {
        httpContext.Response.Redirect("/account?forceChange=1");
        return;
    }

    await next();
});

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapPost("/auth/login", async (HttpContext httpContext, [FromForm] LoginForm form, IUserAccountService userAccountService, IAuditLogService auditLogService) =>
{
    var loginResult = await userAccountService.AuthenticateAsync(form.Email, form.Password);

    if (loginResult.Status != LoginStatus.Succeeded || loginResult.User is null)
    {
        var details = loginResult.Status switch
        {
            LoginStatus.Inactive => "Disabled account",
            LoginStatus.LockedOut => $"Locked out for {Math.Ceiling((loginResult.LockoutRemaining ?? TimeSpan.FromMinutes(15)).TotalMinutes)} minutes",
            LoginStatus.PasswordExpired => "Password expired",
            _ => "Invalid credentials"
        };

        await auditLogService.WriteAsync(null, form.Email, "auth.login.failed", "UserAccount", null, details);

        var errorCode = loginResult.Status switch
        {
            LoginStatus.Inactive => "inactive",
            LoginStatus.LockedOut => "locked",
            LoginStatus.PasswordExpired => "expired",
            _ => "1"
        };

        var lockoutMinutes = loginResult.Status == LoginStatus.LockedOut
            ? (int)Math.Ceiling((loginResult.LockoutRemaining ?? TimeSpan.FromMinutes(15)).TotalMinutes)
            : 0;

        var failedReturnUrl = string.IsNullOrWhiteSpace(form.ReturnUrl) ? "/" : form.ReturnUrl;
        var baseRedirect = $"/login?returnUrl={Uri.EscapeDataString(failedReturnUrl)}&error={Uri.EscapeDataString(errorCode)}";
        var finalRedirect = lockoutMinutes > 0 ? $"{baseRedirect}&lockoutMinutes={lockoutMinutes}" : baseRedirect;
        return Results.LocalRedirect(finalRedirect);
    }

    var authenticatedUser = loginResult.User;

    var displayName = authenticatedUser.DisplayName ?? string.Empty;
    if (displayName.EndsWith(" ADMIN", StringComparison.OrdinalIgnoreCase))
    {
        displayName = displayName[..^6].TrimEnd();
    }
    else if (displayName.EndsWith(" BROKER", StringComparison.OrdinalIgnoreCase))
    {
        displayName = displayName[..^7].TrimEnd();
    }
    else if (displayName.EndsWith(" INSTRUCTOR", StringComparison.OrdinalIgnoreCase))
    {
        displayName = displayName[..^11].TrimEnd();
    }
    else if (displayName.EndsWith(" LEARNER", StringComparison.OrdinalIgnoreCase))
    {
        displayName = displayName[..^8].TrimEnd();
    }

    var claims = new List<Claim>
    {
        new(ClaimTypes.NameIdentifier, authenticatedUser.Id.ToString()),
        new(ClaimTypes.Name, displayName),
        new(ClaimTypes.Email, authenticatedUser.Email),
        new(ClaimTypes.Role, authenticatedUser.Role)
    };

    var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme, ClaimTypes.Name, ClaimTypes.Role);
    var principal = new ClaimsPrincipal(identity);

    await httpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);
    await auditLogService.WriteAsync(authenticatedUser.Id, authenticatedUser.Email, "auth.login.succeeded", "UserAccount", authenticatedUser.Id);

    if (authenticatedUser.ForcePasswordChange)
    {
        return Results.LocalRedirect("/account?forceChange=1");
    }

    var returnUrl = string.IsNullOrWhiteSpace(form.ReturnUrl) ? "/" : form.ReturnUrl;
    return Results.LocalRedirect(returnUrl);
}).DisableAntiforgery().RequireRateLimiting("login");

app.MapPost("/auth/logout", async (HttpContext httpContext, IAuditLogService auditLogService) =>
{
    var userIdClaim = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    var email = httpContext.User.FindFirst(ClaimTypes.Email)?.Value;
    Guid? userId = Guid.TryParse(userIdClaim, out var parsedUserId) ? parsedUserId : null;

    await auditLogService.WriteAsync(userId, email, "auth.logout", "UserAccount", userId);
    await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.LocalRedirect("/");
}).DisableAntiforgery();

app.MapGet("/auth/sso/login", (string? returnUrl) =>
{
    if (!ssoEnabled)
    {
        return Results.NotFound();
    }

    var safeReturnUrl = BuildSafeReturnUrl(returnUrl);

    if (ssoTestModeEnabled && app.Environment.IsEnvironment("Testing"))
    {
        var testEmail = Uri.EscapeDataString(string.IsNullOrWhiteSpace(ssoTestUserEmail) ? "sso-learner@lms.com" : ssoTestUserEmail);
        var testName = Uri.EscapeDataString(string.IsNullOrWhiteSpace(ssoTestUserName) ? "SSO Learner" : ssoTestUserName);
        var encodedReturnUrl = Uri.EscapeDataString(safeReturnUrl);
        return Results.LocalRedirect($"/auth/sso/test-callback?email={testEmail}&name={testName}&returnUrl={encodedReturnUrl}");
    }

    var authProperties = new AuthenticationProperties
    {
        RedirectUri = safeReturnUrl
    };

    return Results.Challenge(authProperties, new[] { OpenIdConnectDefaults.AuthenticationScheme });
}).AllowAnonymous();

app.MapGet("/auth/sso/logout", () =>
{
    if (!ssoEnabled)
    {
        return Results.LocalRedirect("/");
    }

    var authProperties = new AuthenticationProperties
    {
        RedirectUri = "/"
    };

    return Results.SignOut(authProperties, new[]
    {
        CookieAuthenticationDefaults.AuthenticationScheme,
        OpenIdConnectDefaults.AuthenticationScheme
    });
}).RequireAuthorization();

app.MapGet("/auth/sso/test-callback", async (
    string email,
    string? name,
    string? returnUrl,
    HttpContext httpContext,
    IUserAccountService userAccountService,
    IAuditLogService auditLogService) =>
{
    if (!(ssoEnabled && ssoTestModeEnabled && app.Environment.IsEnvironment("Testing")))
    {
        return Results.NotFound();
    }

    if (!IsAllowedSsoEmailDomain(email, builder.Configuration))
    {
        return Results.LocalRedirect("/login?error=inactive");
    }

    var localUser = await userAccountService.UpsertExternalUserAsync(email, name ?? email);

    var testClaims = new List<Claim>
    {
        new(ClaimTypes.Email, email),
        new(ClaimTypes.Name, name ?? email)
    };
    var mappedRole = ResolveSsoRole(new ClaimsPrincipal(new ClaimsIdentity(testClaims, "sso-test")), builder.Configuration);
    var applyMappedRoleOnSignIn = builder.Configuration.GetValue<bool>("Authentication:Sso:ApplyMappedRoleOnSignIn");
    if (applyMappedRoleOnSignIn && !string.Equals(localUser.Role, mappedRole, StringComparison.OrdinalIgnoreCase))
    {
        await userAccountService.UpdateRoleAsync(localUser.Id, mappedRole, null, "sso@lms.local");
        localUser = await userAccountService.GetByIdAsync(localUser.Id) ?? localUser;
    }

    if (!localUser.IsActive)
    {
        return Results.LocalRedirect("/login?error=inactive");
    }

    var claims = new List<Claim>
    {
        new(ClaimTypes.NameIdentifier, localUser.Id.ToString()),
        new(ClaimTypes.Name, localUser.DisplayName),
        new(ClaimTypes.Email, localUser.Email),
        new(ClaimTypes.Role, localUser.Role)
    };

    var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme, ClaimTypes.Name, ClaimTypes.Role);
    var principal = new ClaimsPrincipal(identity);

    await httpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);
    await auditLogService.WriteAsync(localUser.Id, localUser.Email, "auth.login.sso.succeeded", "UserAccount", localUser.Id);

    return Results.LocalRedirect(BuildSafeReturnUrl(returnUrl));
}).AllowAnonymous();

app.MapGet("/certificates/{certificateId:guid}/download", async (Guid certificateId, HttpContext httpContext, IEnrollmentService enrollmentService) =>
{
    if (httpContext.User.Identity?.IsAuthenticated != true)
    {
        return Results.Unauthorized();
    }

    var userIdClaim = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    if (!Guid.TryParse(userIdClaim, out var userId))
    {
        return Results.Unauthorized();
    }

    var isPrivileged = httpContext.User.IsInRole("Admin") || httpContext.User.IsInRole("Broker");
    var payload = await enrollmentService.GetCertificateDownloadPayloadAsync(certificateId, userId, isPrivileged);
    if (payload is null)
    {
        return Results.NotFound();
    }

    var pdf = await enrollmentService.GenerateCertificatePdfAsync(certificateId, userId, isPrivileged);
    if (pdf is null)
    {
        return Results.NotFound();
    }

    return Results.File(pdf, "application/pdf", $"{payload.CertificateNumber}.pdf");
}).RequireAuthorization();

app.MapGet("/api/certificates/verify", async (string certificateNumber, string verificationCode, IEnrollmentService enrollmentService) =>
{
    var payload = await enrollmentService.VerifyCertificateAsync(certificateNumber, verificationCode);
    if (payload is null)
    {
        return Results.NotFound(new { verified = false, message = "Certificate not found or verification code is invalid." });
    }

    return Results.Ok(new
    {
        verified = true,
        payload.CertificateNumber,
        payload.VerificationCode,
        payload.LearnerName,
        payload.CourseTitle,
        payload.IssuedAt,
        payload.ExpiresAt,
        payload.Status,
        payload.IsRevoked,
        payload.RevocationReason
    });
});

app.MapPost("/ops/due-dates/process", async (IDueDateReminderService dueDateReminderService) =>
{
    var result = await dueDateReminderService.ProcessRemindersAsync();
    return Results.Ok(new
    {
        result.DueSoonSent,
        result.OverdueSent,
        result.Processed
    });
}).RequireAuthorization(policy => policy.RequireRole("Admin"));

app.MapGet("/ops/background-jobs/status", (IBackgroundJobMonitor monitor) =>
{
    var status = monitor.GetStatus();
    return Results.Ok(status);
}).RequireAuthorization(policy => policy.RequireRole("Admin"));

app.MapGet("/ops/background-jobs/health", async (IBackgroundJobMonitor monitor) =>
{
    var health = await monitor.HealthCheckAsync();
    return Results.Ok(health);
}).RequireAuthorization(policy => policy.RequireRole("Admin"));

app.MapControllers();

app.Run();

static async Task EnsureLegacyDatabaseMigrationBaselineAsync(ApplicationDbContext dbContext)
{
    var firstMigrationId = dbContext.Database.GetMigrations().OrderBy(migration => migration).FirstOrDefault();
    if (string.IsNullOrWhiteSpace(firstMigrationId))
    {
        return;
    }

    if (await SqliteTableExistsAsync(dbContext, "__EFMigrationsHistory"))
    {
        return;
    }

    // Detect legacy databases created before EF migrations tracking existed.
    var hasLegacySchema = await SqliteTableExistsAsync(dbContext, "Courses") &&
                          await SqliteTableExistsAsync(dbContext, "UserAccounts") &&
                          await SqliteTableExistsAsync(dbContext, "CompletionCertificates");

    if (!hasLegacySchema)
    {
        return;
    }

    await dbContext.Database.ExecuteSqlRawAsync("""
        CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
            "MigrationId" TEXT NOT NULL CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY,
            "ProductVersion" TEXT NOT NULL
        );
        """);

    await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
        INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
        SELECT {firstMigrationId}, {"8.0.11"}
        WHERE NOT EXISTS (
            SELECT 1
            FROM "__EFMigrationsHistory"
            WHERE "MigrationId" = {firstMigrationId}
        );
        """);
}

static async Task EnsureEnrollmentProvenanceSchemaAsync(ApplicationDbContext dbContext)
{
    if (!await SqliteTableExistsAsync(dbContext, "Enrollments"))
    {
        return;
    }

    if (!await SqliteColumnExistsAsync(dbContext, "Enrollments", "EnrollmentSource"))
    {
        await dbContext.Database.ExecuteSqlRawAsync("ALTER TABLE \"Enrollments\" ADD COLUMN \"EnrollmentSource\" TEXT NOT NULL DEFAULT 'LearnerPurchase';");
    }

    if (!await SqliteColumnExistsAsync(dbContext, "Enrollments", "SponsoredByBrokerUserId"))
    {
        await dbContext.Database.ExecuteSqlRawAsync("ALTER TABLE \"Enrollments\" ADD COLUMN \"SponsoredByBrokerUserId\" TEXT NULL;");
    }

    if (!await SqliteColumnExistsAsync(dbContext, "Enrollments", "ConsentStatus"))
    {
        await dbContext.Database.ExecuteSqlRawAsync("ALTER TABLE \"Enrollments\" ADD COLUMN \"ConsentStatus\" TEXT NOT NULL DEFAULT 'NotRequired';");
    }
}

static async Task EnsureEnrollmentDueDateSchemaAsync(ApplicationDbContext dbContext)
{
    if (!await SqliteTableExistsAsync(dbContext, "Enrollments"))
    {
        return;
    }

    if (!await SqliteColumnExistsAsync(dbContext, "Enrollments", "DueAtUtc"))
    {
        await dbContext.Database.ExecuteSqlRawAsync("ALTER TABLE \"Enrollments\" ADD COLUMN \"DueAtUtc\" TEXT NULL;");
    }

    if (!await SqliteColumnExistsAsync(dbContext, "Enrollments", "DueSoonReminderSentAt"))
    {
        await dbContext.Database.ExecuteSqlRawAsync("ALTER TABLE \"Enrollments\" ADD COLUMN \"DueSoonReminderSentAt\" TEXT NULL;");
    }

    if (!await SqliteColumnExistsAsync(dbContext, "Enrollments", "OverdueReminderSentAt"))
    {
        await dbContext.Database.ExecuteSqlRawAsync("ALTER TABLE \"Enrollments\" ADD COLUMN \"OverdueReminderSentAt\" TEXT NULL;");
    }
}

static async Task<bool> SqliteTableExistsAsync(ApplicationDbContext dbContext, string tableName)
{
    var connection = dbContext.Database.GetDbConnection();
    if (connection.State != System.Data.ConnectionState.Open)
    {
        await connection.OpenAsync();
    }

    await using var command = connection.CreateCommand();
    command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = @tableName;";

    var parameter = command.CreateParameter();
    parameter.ParameterName = "@tableName";
    parameter.Value = tableName;
    command.Parameters.Add(parameter);

    var result = await command.ExecuteScalarAsync();
    return Convert.ToInt32(result) > 0;
}

static async Task<bool> SqliteColumnExistsAsync(ApplicationDbContext dbContext, string tableName, string columnName)
{
    var connection = dbContext.Database.GetDbConnection();
    if (connection.State != System.Data.ConnectionState.Open)
    {
        await connection.OpenAsync();
    }

    await using var command = connection.CreateCommand();
    command.CommandText = $"PRAGMA table_info(\"{tableName}\");";

    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
    }

    return false;
}

static string BuildSafeReturnUrl(string? returnUrl)
{
    if (string.IsNullOrWhiteSpace(returnUrl))
    {
        return "/";
    }

    return returnUrl.StartsWith('/') ? returnUrl : "/";
}

static bool IsAllowedSsoEmailDomain(string email, IConfiguration configuration)
{
    var allowedDomains = configuration
        .GetSection("Authentication:Sso:AllowedEmailDomains")
        .Get<string[]>()
        ?.Where(domain => !string.IsNullOrWhiteSpace(domain))
        .Select(domain => domain.Trim().ToLowerInvariant())
        .Distinct()
        .ToArray() ?? Array.Empty<string>();

    if (allowedDomains.Length == 0)
    {
        return true;
    }

    var atIndex = email.LastIndexOf('@');
    if (atIndex <= 0 || atIndex >= email.Length - 1)
    {
        return false;
    }

    var emailDomain = email[(atIndex + 1)..].Trim().ToLowerInvariant();
    return allowedDomains.Contains(emailDomain, StringComparer.OrdinalIgnoreCase);
}

static string ResolveSsoRole(ClaimsPrincipal principal, IConfiguration configuration)
{
    var roleClaimType = configuration["Authentication:Sso:RoleClaimType"] ?? ClaimTypes.Role;
    var groupClaimType = configuration["Authentication:Sso:GroupClaimType"] ?? "groups";

    var roleClaims = principal.FindAll(roleClaimType)
        .Concat(principal.FindAll(ClaimTypes.Role))
        .Select(claim => claim.Value)
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

    var roleByClaims = ResolveKnownRole(roleClaims);
    if (!string.IsNullOrWhiteSpace(roleByClaims))
    {
        return roleByClaims;
    }

    var groupClaims = principal.FindAll(groupClaimType)
        .Select(claim => claim.Value)
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    if (groupClaims.Count > 0)
    {
        var adminGroups = GetConfiguredValues(configuration, "Authentication:Sso:RoleMapping:AdminGroupIds");
        if (adminGroups.Any(groupClaims.Contains))
        {
            return "Admin";
        }

        var instructorGroups = GetConfiguredValues(configuration, "Authentication:Sso:RoleMapping:InstructorGroupIds");
        if (instructorGroups.Any(groupClaims.Contains))
        {
            return "Instructor";
        }

        var brokerGroups = GetConfiguredValues(configuration, "Authentication:Sso:RoleMapping:BrokerGroupIds");
        if (brokerGroups.Any(groupClaims.Contains))
        {
            return "Broker";
        }

        var learnerGroups = GetConfiguredValues(configuration, "Authentication:Sso:RoleMapping:LearnerGroupIds");
        if (learnerGroups.Any(groupClaims.Contains))
        {
            return "Learner";
        }
    }

    var defaultRole = configuration["Authentication:Sso:DefaultRole"];
    return NormalizeRole(defaultRole) ?? "Learner";
}

static string? ResolveKnownRole(IEnumerable<string> candidates)
{
    foreach (var candidate in candidates)
    {
        var normalized = NormalizeRole(candidate);
        if (!string.IsNullOrWhiteSpace(normalized))
        {
            return normalized;
        }
    }

    return null;
}

static string? NormalizeRole(string? role)
{
    return role?.Trim().ToLowerInvariant() switch
    {
        "admin" => "Admin",
        "instructor" => "Instructor",
        "broker" => "Broker",
        "learner" => "Learner",
        _ => null
    };
}

static string[] GetConfiguredValues(IConfiguration configuration, string path)
{
    return configuration
        .GetSection(path)
        .Get<string[]>()
        ?.Where(value => !string.IsNullOrWhiteSpace(value))
        .Select(value => value.Trim())
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray() ?? Array.Empty<string>();
}

record LoginForm(string Email, string Password, string? ReturnUrl);
