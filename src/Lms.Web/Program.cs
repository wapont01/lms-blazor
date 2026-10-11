using Lms.Application.Services;
using Lms.Application.Data;
using Lms.Domain.Entities;
using Lms.Web.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Lms.Web.Components;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using System.Data.Common;

var builder = WebApplication.CreateBuilder(args);

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

builder.Services.AddCascadingAuthenticationState();
builder.Services.AddAuthorization();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/login";
        options.AccessDeniedPath = "/access-denied";
        options.Cookie.Name = "Lms.Web.Auth";
    });

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
builder.Services.AddScoped<IRegulatoryDirectoryService, RegulatoryDirectoryService>();
builder.Services.AddScoped<IRegulatoryComplianceService, RegulatoryComplianceService>();
builder.Services.AddSingleton<IBackgroundJobMonitor, BackgroundJobMonitor>();
builder.Services.AddHostedService<DueDateReminderBackgroundService>();
builder.Services.AddScoped<ILearnerDashboardService, LearnerDashboardService>();
builder.Services.AddScoped<IModuleCheckpointService, ModuleCheckpointService>();
builder.Services.AddScoped<ICourseActivityService, CourseActivityService>();
builder.Services.AddScoped<IShoppingCartService, ShoppingCartService>();
builder.Services.AddScoped<IPolicyDisclosureService, PolicyDisclosureService>();
builder.Services.AddScoped<ISchoolProfileService, SchoolProfileService>();
builder.Services.AddScoped<IComplianceReportingService, ComplianceReportingService>();
builder.Services.AddHostedService<ComplianceReminderBackgroundService>();
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddScoped<IPDFInvoiceService, PDFInvoiceService>();
builder.Services.AddScoped<IPaymentService, PaymentService>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var userAccountService = scope.ServiceProvider.GetRequiredService<IUserAccountService>();
    await EnsureLegacyDatabaseMigrationBaselineAsync(dbContext);
    await EnsureEnrollmentProvenanceSchemaAsync(dbContext);
    await EnsureRegulatoryAffiliationEmailSchemaAsync(dbContext);
    await EnsureUserLicenseTypeSchemaAsync(dbContext);
    await EnsureInstructorProfileSchemaAsync(dbContext);
    await EnsureSchoolProfileStateSchemaAsync(dbContext);
    await EnsureUserAndCourseStateSchemaAsync(dbContext);
    await dbContext.Database.MigrateAsync();
    await EnsureEnrollmentProvenanceSchemaAsync(dbContext);
    await EnsureRegulatoryAffiliationEmailSchemaAsync(dbContext);
    await EnsureUserLicenseTypeSchemaAsync(dbContext);
    await EnsureInstructorProfileSchemaAsync(dbContext);
    await EnsureSchoolProfileStateSchemaAsync(dbContext);
    await EnsureUserAndCourseStateSchemaAsync(dbContext);
    await userAccountService.EnsureSeedUsersAsync();
    await CourseSeed.SeedAsync(dbContext);
    await FloridaDbprCaptureSeed.SeedAsync(dbContext);
    await NcRecCaptureSeed.SeedAsync(dbContext);
    await NcRecCharlotteCaptureSeed.SeedAsync(dbContext);
    await NcRecNewLicenseCaptureSeed.SeedAsync(dbContext);
    await StateRosterCaptureSeed.SeedAsync(dbContext);
    await TexasTrecCaptureSeed.SeedAsync(dbContext);
    await CaliforniaDreCaptureSeed.SeedAsync(dbContext);
    await AlaskaCbplCaptureSeed.SeedAsync(dbContext);
    await NevadaNredCaptureSeed.SeedAsync(dbContext);
    await KansasKrecCaptureSeed.SeedAsync(dbContext);
    await UtahDreCaptureSeed.SeedAsync(dbContext);
    await NewHampshireOplcCaptureSeed.SeedAsync(dbContext);
    await OregonOreaCaptureSeed.SeedAsync(dbContext);
    await SouthDakotaCitySampleCaptureSeed.SeedAsync(dbContext);
    await NorthDakotaFargoCaptureSeed.SeedAsync(dbContext);
    await NorthDakotaBismarckCaptureSeed.SeedAsync(dbContext);
    await IowaPlbCaptureSeed.SeedAsync(dbContext);
    await MinnesotaPulseCaptureSeed.SeedAsync(dbContext);
    await TennesseeCaptureSeed.SeedAsync(dbContext);
    await CourseSeed.SeedEnrollmentsAsync(dbContext);
    var floridaProfile = await dbContext.SchoolProfiles
        .Include(profile => profile.StaffMembers)
        .FirstOrDefaultAsync(profile => profile.State == "Florida");
    if (floridaProfile is not null)
    {
        ProductionReadinessGuard.Validate(app.Environment.EnvironmentName, floridaProfile);
    }
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

app.MapPost("/auth/register", async (HttpContext httpContext, [FromForm] RegisterForm form, IUserAccountService userAccountService, IRegulatoryDirectoryService regulatoryDirectoryService, IAuditLogService auditLogService) =>
{
    string Fail(string code) => $"/register?error={Uri.EscapeDataString(code)}";

    if (string.IsNullOrWhiteSpace(form.Email) || string.IsNullOrWhiteSpace(form.Password))
    {
        return Results.LocalRedirect(Fail("missing"));
    }

    if (!string.Equals(form.Password, form.ConfirmPassword, StringComparison.Ordinal))
    {
        return Results.LocalRedirect(Fail("mismatch"));
    }

    UserAccount newUser;
    try
    {
        // unchecked checkboxes are absent from the form, which breaks record binding; read directly
        var marketingOptIn = !string.IsNullOrEmpty(httpContext.Request.Form["marketingEmails"]);
        newUser = await userAccountService.RegisterLearnerAsync(form.Email, form.Password, marketingOptIn);
    }
    catch (InvalidOperationException exception)
    {
        await auditLogService.WriteAsync(null, form.Email, "auth.register.failed", "UserAccount", null, exception.Message);
        var code = exception.Message.Contains("already exists", StringComparison.OrdinalIgnoreCase) ? "exists" : "password";
        return Results.LocalRedirect(Fail(code));
    }

    var claims = new List<Claim>
    {
        new(ClaimTypes.NameIdentifier, newUser.Id.ToString()),
        new(ClaimTypes.Name, newUser.DisplayName),
        new(ClaimTypes.Email, newUser.Email),
        new(ClaimTypes.Role, newUser.Role)
    };
    var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme, ClaimTypes.Name, ClaimTypes.Role);
    await httpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
    await auditLogService.WriteAsync(newUser.Id, newUser.Email, "auth.login.succeeded", "UserAccount", newUser.Id, "Post-registration sign-in");

    var registerReturnUrl = string.IsNullOrWhiteSpace(form.ReturnUrl) ? "/courses" : form.ReturnUrl;
    return Results.LocalRedirect(registerReturnUrl);
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

app.MapGet("/ops/background-jobs/status", (ClaimsPrincipal user, IBackgroundJobMonitor monitor) =>
{
    if (!user.IsInRole("Admin"))
    {
        return Results.Forbid();
    }

    return Results.Ok(monitor.GetStatus());
}).RequireAuthorization();

app.MapGet("/ops/background-jobs/health", async (ClaimsPrincipal user, IBackgroundJobMonitor monitor) =>
{
    if (!user.IsInRole("Admin"))
    {
        return Results.Forbid();
    }

    var health = await monitor.HealthCheckAsync();
    return Results.Ok(health);
}).RequireAuthorization();

app.MapGet("/admin/campaign-targets.csv", async (ClaimsPrincipal user, string? state, IRegulatoryDirectoryService regulatoryDirectoryService) =>
{
    if (!user.IsInRole("Admin"))
    {
        return Results.Forbid();
    }

    var targets = await regulatoryDirectoryService.GetCampaignFirmTargetsAsync(state, take: 10_000);
    var csv = new StringBuilder();
    csv.AppendLine("State,Firm,TradeName,FirmLicense,Broker,Address,Licensees,NextRenewal,RenewalDateSource,RenewalsWithin12Months");
    foreach (var target in targets)
    {
        csv.AppendLine(string.Join(',',
            CsvField(target.State),
            CsvField(target.FirmName),
            CsvField(target.TradeName),
            CsvField(target.FirmLicenseNumber),
            CsvField(target.BrokerName),
            CsvField(target.Address),
            target.LicenseeCount,
            CsvField(target.NextRenewalDate?.ToString("yyyy-MM-dd")),
            CsvField(target.RenewalDateSource),
            target.RenewalsWithinTwelveMonths));
    }

    return Results.File(Encoding.UTF8.GetBytes(csv.ToString()), "text/csv", "campaign-targets.csv");

    static string CsvField(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value.Contains(',') || value.Contains('"') || value.Contains('\n')
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;
    }
}).RequireAuthorization();

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

static async Task EnsureUserLicenseTypeSchemaAsync(ApplicationDbContext dbContext)
{
    if (!await SqliteTableExistsAsync(dbContext, "UserAccounts") ||
        await SqliteColumnExistsAsync(dbContext, "UserAccounts", "LicenseType"))
    {
        return;
    }

    await dbContext.Database.ExecuteSqlRawAsync("ALTER TABLE \"UserAccounts\" ADD COLUMN \"LicenseType\" TEXT NULL;");
}

static async Task EnsureRegulatoryAffiliationEmailSchemaAsync(ApplicationDbContext dbContext)
{
    const string tableName = "RegulatoryFirmLicenseeAffiliations";
    if (!await SqliteTableExistsAsync(dbContext, tableName) ||
        await SqliteColumnExistsAsync(dbContext, tableName, "ContactEmail"))
    {
        return;
    }

    await dbContext.Database.ExecuteSqlRawAsync($"ALTER TABLE \"{tableName}\" ADD COLUMN \"ContactEmail\" TEXT NULL;");
}

static async Task EnsureInstructorProfileSchemaAsync(ApplicationDbContext dbContext)
{
    if (!await SqliteTableExistsAsync(dbContext, "UserAccounts"))
    {
        return;
    }

    if (!await SqliteColumnExistsAsync(dbContext, "UserAccounts", "PhoneNumber"))
    {
        await dbContext.Database.ExecuteSqlRawAsync("ALTER TABLE \"UserAccounts\" ADD COLUMN \"PhoneNumber\" TEXT NULL;");
    }

    if (!await SqliteColumnExistsAsync(dbContext, "UserAccounts", "PermanentAddress"))
    {
        await dbContext.Database.ExecuteSqlRawAsync("ALTER TABLE \"UserAccounts\" ADD COLUMN \"PermanentAddress\" TEXT NULL;");
    }
}

static async Task EnsureSchoolProfileStateSchemaAsync(ApplicationDbContext dbContext)
{
    if (!await SqliteTableExistsAsync(dbContext, "SchoolProfiles"))
    {
        return;
    }

    if (!await SqliteColumnExistsAsync(dbContext, "SchoolProfiles", "State"))
    {
        await dbContext.Database.ExecuteSqlRawAsync("ALTER TABLE \"SchoolProfiles\" ADD COLUMN \"State\" TEXT NOT NULL DEFAULT 'Florida';");
    }

    if (!await SqliteColumnExistsAsync(dbContext, "SchoolProfiles", "AddressState"))
    {
        await dbContext.Database.ExecuteSqlRawAsync("ALTER TABLE \"SchoolProfiles\" ADD COLUMN \"AddressState\" TEXT NOT NULL DEFAULT 'FL';");
    }
}

static async Task EnsureUserAndCourseStateSchemaAsync(ApplicationDbContext dbContext)
{
    if (await SqliteTableExistsAsync(dbContext, "UserAccounts")
        && !await SqliteColumnExistsAsync(dbContext, "UserAccounts", "State"))
    {
        await dbContext.Database.ExecuteSqlRawAsync("ALTER TABLE \"UserAccounts\" ADD COLUMN \"State\" TEXT NULL;");
    }

    if (await SqliteTableExistsAsync(dbContext, "Courses")
        && !await SqliteColumnExistsAsync(dbContext, "Courses", "State"))
    {
        await dbContext.Database.ExecuteSqlRawAsync("ALTER TABLE \"Courses\" ADD COLUMN \"State\" TEXT NULL;");
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

record LoginForm(string Email, string Password, string? ReturnUrl);
record RegisterForm(string Email, string Password, string ConfirmPassword, string? ReturnUrl = null);
