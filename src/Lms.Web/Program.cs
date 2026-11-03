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
using System.Threading.RateLimiting;
using System.Data.Common;

var builder = WebApplication.CreateBuilder(args);

if (builder.Environment.IsEnvironment("Testing"))
{
    builder.WebHost.UseStaticWebAssets();
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
var databasePathSource = !string.IsNullOrWhiteSpace(configuredDatabasePath)
    ? "Lms:DatabasePath"
    : !string.IsNullOrWhiteSpace(environmentDatabasePath)
        ? "LMS_DATABASE_PATH"
        : "default (App_Data under content root)";

var databaseDirectory = Path.GetDirectoryName(databasePath);
if (!string.IsNullOrWhiteSpace(databaseDirectory))
{
    Directory.CreateDirectory(databaseDirectory);
}

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<IClaimsTransformation, InstructorRoleClaims>();
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

builder.AddMicrosoftSignIn();
builder.AddGoogleSignIn();

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlite($"Data Source={databasePath}"));

builder.Services.AddScoped<ICourseService, CourseService>();
builder.Services.AddScoped<IAuditLogService, AuditLogService>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<IUserAccountService, UserAccountService>();
builder.Services.AddScoped<IExamService, ExamService>();
builder.Services.AddScoped<IEnrollmentService, EnrollmentService>();
builder.Services.AddScoped<IDueDateReminderService, DueDateReminderService>();
builder.Services.AddScoped<IRegulatoryDirectoryService, RegulatoryDirectoryService>();
builder.Services.AddSingleton<RegulatoryStatisticsCache>();
builder.Services.AddSingleton<IRegulatoryLastRunSource, BulkCollectionLastRunSource>();
builder.Services.AddScoped<RegulatoryStatisticsService>();
builder.Services.AddHostedService<RegulatoryStatisticsWarmupService>();
var regulatoryCollections = new RegulatoryCollectionOptions();
builder.Configuration.GetSection("RegulatoryCollections").Bind(regulatoryCollections);
regulatoryCollections.Validate();
builder.Services.AddSingleton(regulatoryCollections);
var scheduleFilePath = builder.Configuration["BulkCollectionSchedules:FilePath"];
builder.Services.AddSingleton(_ => new BulkCollectionScheduleStore(string.IsNullOrWhiteSpace(scheduleFilePath)
    ? Path.Combine(builder.Environment.ContentRootPath, "App_Data", "bulk-collection-schedules.json")
    : Path.GetFullPath(scheduleFilePath)));
builder.Services.AddSingleton<ColoradoDoraDisplaySelectionStore>();
builder.Services.AddSingleton<IColoradoDoraLicenseTypeSelection>(services =>
    services.GetRequiredService<ColoradoDoraDisplaySelectionStore>());
builder.Services.AddHttpClient<IRegulatoryCollectionClient, RegulatoryCollectionClient>(client =>
    client.Timeout = TimeSpan.FromSeconds(15))
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
var floridaBulk = new FloridaBulkCollectionOptions();
builder.Configuration.GetSection("FloridaBulk").Bind(floridaBulk);
if (floridaBulk.RefreshDays is < 1 or > 365)
    throw new InvalidOperationException("Florida bulk refresh days must be between 1 and 365.");
builder.Services.AddSingleton(floridaBulk);
builder.Services.AddSingleton<Microsoft.Extensions.Options.IOptions<FloridaBulkCollectionOptions>>(
    Microsoft.Extensions.Options.Options.Create(floridaBulk));
builder.Services.AddScoped<FloridaBulkImportService>();
builder.Services.AddSingleton<FloridaBulkCollectionService>();
builder.Services.AddSingleton<BulkCollectionService>(services => services.GetRequiredService<FloridaBulkCollectionService>());
builder.Services.AddScoped<FloridaBulkBrowseService>();
builder.Services.AddScoped<FloridaBulkAlertService>();
var texasBulk = new TexasBulkCollectionOptions();
builder.Configuration.GetSection("TexasBulk").Bind(texasBulk);
builder.Services.AddSingleton(texasBulk);
builder.Services.AddSingleton<Microsoft.Extensions.Options.IOptions<TexasBulkCollectionOptions>>(
    Microsoft.Extensions.Options.Options.Create(texasBulk));
builder.Services.AddScoped<TexasBulkImportService>();
builder.Services.AddScoped<TexasBulkBrowseService>();
builder.Services.AddScoped<NevadaNredBulkImportService>();
builder.Services.AddScoped<NevadaBulkBrowseService>();
var nevadaBulk = new NevadaBulkCollectionOptions { WeeklyEnabled = false, RefreshDays = 30, StaleAfterDays = 32 };
builder.Configuration.GetSection("NevadaBulk").Bind(nevadaBulk);
builder.Services.AddSingleton(nevadaBulk);
builder.Services.AddSingleton<Microsoft.Extensions.Options.IOptions<NevadaBulkCollectionOptions>>(
    Microsoft.Extensions.Options.Options.Create(nevadaBulk));
builder.Services.AddSingleton<NevadaBulkCollectionService>();
builder.Services.AddSingleton<BulkCollectionService>(services => services.GetRequiredService<NevadaBulkCollectionService>());
builder.Services.AddHttpClient("NevadaBulk", client => client.Timeout = TimeSpan.FromMinutes(10))
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddHostedService<NevadaBulkRefreshBackgroundService>();
// NY DOS posts the dataset daily.
var newYorkBulk = new NewYorkBulkCollectionOptions { RefreshDays = 1, StaleAfterDays = 2 };
builder.Configuration.GetSection("NewYorkBulk").Bind(newYorkBulk);
builder.Services.AddSingleton(newYorkBulk);
builder.Services.AddSingleton<Microsoft.Extensions.Options.IOptions<NewYorkBulkCollectionOptions>>(
    Microsoft.Extensions.Options.Options.Create(newYorkBulk));
builder.Services.AddScoped<NewYorkDosBulkImportService>();
builder.Services.AddSingleton<NewYorkBulkCollectionService>();
builder.Services.AddSingleton<BulkCollectionService>(services => services.GetRequiredService<NewYorkBulkCollectionService>());
builder.Services.AddHttpClient("New YorkBulk", client => client.Timeout = TimeSpan.FromMinutes(20))
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddHostedService<NewYorkBulkRefreshBackgroundService>();
// ADRE generates its lists on demand from the live license database.
var arizonaBulk = new ArizonaBulkCollectionOptions { RefreshDays = 7, StaleAfterDays = 8 };
builder.Configuration.GetSection("ArizonaBulk").Bind(arizonaBulk);
builder.Services.AddSingleton(arizonaBulk);
builder.Services.AddSingleton<Microsoft.Extensions.Options.IOptions<ArizonaBulkCollectionOptions>>(
    Microsoft.Extensions.Options.Options.Create(arizonaBulk));
builder.Services.AddScoped<ArizonaAdreBulkImportService>();
builder.Services.AddSingleton<ArizonaBulkCollectionService>();
builder.Services.AddSingleton<BulkCollectionService>(services => services.GetRequiredService<ArizonaBulkCollectionService>());
builder.Services.AddHttpClient("ArizonaBulk", client => client.Timeout = TimeSpan.FromMinutes(20))
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddHostedService<ArizonaBulkRefreshBackgroundService>();
// CT DCP posts its open-data license datasets daily.
var connecticutBulk = new ConnecticutBulkCollectionOptions { RefreshDays = 1, StaleAfterDays = 2 };
builder.Configuration.GetSection("ConnecticutBulk").Bind(connecticutBulk);
builder.Services.AddSingleton(connecticutBulk);
builder.Services.AddSingleton<Microsoft.Extensions.Options.IOptions<ConnecticutBulkCollectionOptions>>(
    Microsoft.Extensions.Options.Options.Create(connecticutBulk));
builder.Services.AddScoped<ConnecticutDcpBulkImportService>();
builder.Services.AddSingleton<ConnecticutBulkCollectionService>();
builder.Services.AddSingleton<BulkCollectionService>(services => services.GetRequiredService<ConnecticutBulkCollectionService>());
builder.Services.AddHttpClient("ConnecticutBulk", client => client.Timeout = TimeSpan.FromMinutes(20))
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddHostedService<ConnecticutBulkRefreshBackgroundService>();
// OREA generates its free licensee rosters on request through an ASP.NET form; cookies are handled per download.
var oregonBulk = new OregonBulkCollectionOptions { RefreshDays = 7, StaleAfterDays = 8 };
builder.Configuration.GetSection("OregonBulk").Bind(oregonBulk);
builder.Services.AddSingleton(oregonBulk);
builder.Services.AddSingleton<Microsoft.Extensions.Options.IOptions<OregonBulkCollectionOptions>>(
    Microsoft.Extensions.Options.Options.Create(oregonBulk));
builder.Services.AddScoped<OregonOreaBulkImportService>();
builder.Services.AddSingleton<OregonBulkCollectionService>();
builder.Services.AddSingleton<BulkCollectionService>(services => services.GetRequiredService<OregonBulkCollectionService>());
builder.Services.AddHttpClient("OregonBulk", client =>
    {
        client.Timeout = TimeSpan.FromMinutes(20);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (compatible; LMS-RosterCollector/1.0)");
    })
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false });
builder.Services.AddHostedService<OregonBulkRefreshBackgroundService>();
// DE DPR's statewide professional licensing dataset is published daily; only real estate and appraiser rows are requested.
var delawareBulk = new DelawareBulkCollectionOptions { RefreshDays = 1, StaleAfterDays = 2 };
builder.Configuration.GetSection("DelawareBulk").Bind(delawareBulk);
builder.Services.AddSingleton(delawareBulk);
builder.Services.AddSingleton<Microsoft.Extensions.Options.IOptions<DelawareBulkCollectionOptions>>(
    Microsoft.Extensions.Options.Options.Create(delawareBulk));
builder.Services.AddScoped<DelawareDprBulkImportService>();
builder.Services.AddSingleton<DelawareBulkCollectionService>();
builder.Services.AddSingleton<BulkCollectionService>(services => services.GetRequiredService<DelawareBulkCollectionService>());
builder.Services.AddHttpClient("DelawareBulk", client => client.Timeout = TimeSpan.FromMinutes(20))
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddHostedService<DelawareBulkRefreshBackgroundService>();
// IL IDFPR's statewide professional licensing dataset is published daily; real estate and appraisal rows are requested, excluding course approvals.
var illinoisBulk = new IllinoisBulkCollectionOptions { RefreshDays = 1, StaleAfterDays = 2 };
builder.Configuration.GetSection("IllinoisBulk").Bind(illinoisBulk);
builder.Services.AddSingleton(illinoisBulk);
builder.Services.AddSingleton<Microsoft.Extensions.Options.IOptions<IllinoisBulkCollectionOptions>>(
    Microsoft.Extensions.Options.Options.Create(illinoisBulk));
builder.Services.AddScoped<IllinoisIdfprBulkImportService>();
builder.Services.AddSingleton<IllinoisBulkCollectionService>();
builder.Services.AddSingleton<BulkCollectionService>(services => services.GetRequiredService<IllinoisBulkCollectionService>());
builder.Services.AddHttpClient("IllinoisBulk", client => client.Timeout = TimeSpan.FromMinutes(20))
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddHostedService<IllinoisBulkRefreshBackgroundService>();
// DPOR updates its regulant lists every 5 business days (usually Mondays); the site rejects requests without a browser user agent.
var virginiaBulk = new VirginiaBulkCollectionOptions { RefreshDays = 7, StaleAfterDays = 8 };
builder.Configuration.GetSection("VirginiaBulk").Bind(virginiaBulk);
builder.Services.AddSingleton(virginiaBulk);
builder.Services.AddSingleton<Microsoft.Extensions.Options.IOptions<VirginiaBulkCollectionOptions>>(
    Microsoft.Extensions.Options.Options.Create(virginiaBulk));
builder.Services.AddScoped<VirginiaDporBulkImportService>();
builder.Services.AddSingleton<VirginiaBulkCollectionService>();
builder.Services.AddSingleton<BulkCollectionService>(services => services.GetRequiredService<VirginiaBulkCollectionService>());
builder.Services.AddHttpClient("VirginiaBulk", client =>
    {
        client.Timeout = TimeSpan.FromMinutes(20);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (compatible; LMS-RosterCollector/1.0)");
    })
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddHostedService<VirginiaBulkRefreshBackgroundService>();
var michiganBulk = new MichiganBulkCollectionOptions { WeeklyEnabled = false, RefreshDays = 1, StaleAfterDays = 2 };
builder.Configuration.GetSection("MichiganBulk").Bind(michiganBulk);
builder.Services.AddSingleton(michiganBulk);
builder.Services.AddSingleton<Microsoft.Extensions.Options.IOptions<MichiganBulkCollectionOptions>>(
    Microsoft.Extensions.Options.Options.Create(michiganBulk));
builder.Services.AddScoped<MichiganLaraBulkImportService>();
builder.Services.AddSingleton<MichiganBulkCollectionService>();
builder.Services.AddSingleton<BulkCollectionService>(services => services.GetRequiredService<MichiganBulkCollectionService>());
builder.Services.AddHttpClient("MichiganBulk", client => client.Timeout = TimeSpan.FromMinutes(20))
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false });
builder.Services.AddHostedService<MichiganBulkRefreshBackgroundService>();
var westVirginiaBulk = new WestVirginiaBulkCollectionOptions { WeeklyEnabled = false, RefreshDays = 30, StaleAfterDays = 35 };
builder.Configuration.GetSection("WestVirginiaBulk").Bind(westVirginiaBulk);
builder.Services.AddSingleton(westVirginiaBulk);
builder.Services.AddSingleton<Microsoft.Extensions.Options.IOptions<WestVirginiaBulkCollectionOptions>>(
    Microsoft.Extensions.Options.Options.Create(westVirginiaBulk));
builder.Services.AddScoped<WestVirginiaRecBulkImportService>();
builder.Services.AddSingleton<WestVirginiaBulkCollectionService>();
builder.Services.AddSingleton<BulkCollectionService>(services => services.GetRequiredService<WestVirginiaBulkCollectionService>());
builder.Services.AddHttpClient("West VirginiaBulk", client => client.Timeout = TimeSpan.FromMinutes(20))
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddHostedService<WestVirginiaBulkRefreshBackgroundService>();
var maineBulk = new MaineBulkCollectionOptions { WeeklyEnabled = false, RefreshDays = 1, StaleAfterDays = 2 };
builder.Configuration.GetSection("MaineBulk").Bind(maineBulk);
builder.Services.AddSingleton(maineBulk);
builder.Services.AddSingleton<Microsoft.Extensions.Options.IOptions<MaineBulkCollectionOptions>>(
    Microsoft.Extensions.Options.Options.Create(maineBulk));
builder.Services.AddScoped<MaineAlmsBulkImportService>();
builder.Services.AddSingleton<MaineBulkCollectionService>();
builder.Services.AddSingleton<BulkCollectionService>(services => services.GetRequiredService<MaineBulkCollectionService>());
builder.Services.AddHttpClient("MaineBulk", client => client.Timeout = TimeSpan.FromMinutes(20))
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false });
builder.Services.AddHostedService<MaineBulkRefreshBackgroundService>();
var vermontBulk = new VermontBulkCollectionOptions { WeeklyEnabled = false, RefreshDays = 1, StaleAfterDays = 2 };
builder.Configuration.GetSection("VermontBulk").Bind(vermontBulk);
builder.Services.AddSingleton(vermontBulk);
builder.Services.AddSingleton<Microsoft.Extensions.Options.IOptions<VermontBulkCollectionOptions>>(
    Microsoft.Extensions.Options.Options.Create(vermontBulk));
builder.Services.AddScoped<VermontOprBulkImportService>();
builder.Services.AddSingleton<VermontBulkCollectionService>();
builder.Services.AddSingleton<BulkCollectionService>(services => services.GetRequiredService<VermontBulkCollectionService>());
builder.Services.AddHttpClient("VermontBulk", client => client.Timeout = TimeSpan.FromMinutes(20))
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false });
builder.Services.AddHostedService<VermontBulkRefreshBackgroundService>();
builder.Services.AddScoped<MontanaRreBulkImportService>();
builder.Services.AddSingleton<MontanaBulkCollectionService>();
builder.Services.AddSingleton<BulkCollectionService>(services => services.GetRequiredService<MontanaBulkCollectionService>());
builder.Services.AddSingleton<TexasBulkCollectionService>();
builder.Services.AddSingleton<BulkCollectionService>(services => services.GetRequiredService<TexasBulkCollectionService>());
builder.Services.AddHttpClient("TexasBulk", client => client.Timeout = TimeSpan.FromMinutes(10))
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddHostedService<TexasBulkRefreshBackgroundService>();
var californiaBulk = new CaliforniaBulkCollectionOptions();
builder.Configuration.GetSection("CaliforniaBulk").Bind(californiaBulk);
builder.Services.AddSingleton(californiaBulk);
builder.Services.AddSingleton<Microsoft.Extensions.Options.IOptions<CaliforniaBulkCollectionOptions>>(
    Microsoft.Extensions.Options.Options.Create(californiaBulk));
builder.Services.AddScoped<CaliforniaBulkImportService>();
builder.Services.AddScoped<CaliforniaBulkBrowseService>();
builder.Services.AddSingleton<CaliforniaBulkCollectionService>();
builder.Services.AddSingleton<BulkCollectionService>(services => services.GetRequiredService<CaliforniaBulkCollectionService>());
builder.Services.AddHttpClient("CaliforniaBulk", client => client.Timeout = TimeSpan.FromMinutes(10))
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddHostedService<CaliforniaBulkRefreshBackgroundService>();
var coloradoDoraBulk = new ColoradoDoraBulkCollectionOptions();
builder.Configuration.GetSection("ColoradoDoraBulk").Bind(coloradoDoraBulk);
builder.Services.AddSingleton(coloradoDoraBulk);
builder.Services.AddSingleton<Microsoft.Extensions.Options.IOptions<ColoradoDoraBulkCollectionOptions>>(
    Microsoft.Extensions.Options.Options.Create(coloradoDoraBulk));
builder.Services.AddScoped<ColoradoDoraBulkImportService>();
builder.Services.AddSingleton<ColoradoDoraBulkCollectionService>();
builder.Services.AddSingleton<BulkCollectionService>(services => services.GetRequiredService<ColoradoDoraBulkCollectionService>());
builder.Services.AddHttpClient("ColoradoBulk", client => client.Timeout = TimeSpan.FromMinutes(20))
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddHostedService<ColoradoDoraBulkRefreshBackgroundService>();
builder.Services.AddHostedService<ColoradoDoraBaselineBootstrapService>();
builder.Services.AddHttpClient("FloridaBulk", client => client.Timeout = TimeSpan.FromMinutes(10))
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddHostedService<FloridaBulkRefreshBackgroundService>();
builder.Services.AddScoped<IRegulatoryComplianceService, RegulatoryComplianceService>();
builder.Services.AddSingleton<IBackgroundJobMonitor, BackgroundJobMonitor>();
builder.Services.AddHostedService<DueDateReminderBackgroundService>();
builder.Services.AddScoped<ILearnerDashboardService, LearnerDashboardService>();
builder.Services.AddScoped<IUnitExamService, UnitExamService>();
builder.Services.AddScoped<IShoppingCartService, ShoppingCartService>();
builder.Services.AddScoped<ICourseActivityService, CourseActivityService>();
builder.Services.AddScoped<IPolicyDisclosureService, PolicyDisclosureService>();
builder.Services.AddScoped<ISchoolProfileService, SchoolProfileService>();
builder.Services.AddScoped<IInstructorRecordIdentity, Lms.Web.Services.InstructorRecordIdentity>();
builder.Services.AddScoped<InstructorRecordService>();
builder.Services.AddScoped<IComplianceReportingService, ComplianceReportingService>();
builder.Services.AddHostedService<ComplianceReminderBackgroundService>();
if (builder.Configuration.GetValue<bool>("MailSlurp:Enabled"))
{
    if (!builder.Environment.IsDevelopment())
        throw new InvalidOperationException("MailSlurp test delivery may only be enabled in Development.");
    var mailSlurp = new MailSlurpOptions();
    builder.Configuration.GetSection("MailSlurp").Bind(mailSlurp);
    mailSlurp.Validate();
    builder.Services.AddSingleton(mailSlurp);
    builder.Services.AddHttpClient<MailSlurpEmailTransport>(client => client.Timeout = TimeSpan.FromSeconds(30));
}
builder.Services.AddScoped<IEmailService, EmailService>();
var instructorMailbox = new InstructorMailboxOptions();
builder.Configuration.GetSection("InstructorMailbox").Bind(instructorMailbox);
if (instructorMailbox.Enabled && !builder.Environment.IsDevelopment())
    throw new InvalidOperationException("MailSlurp instructor test mailboxes may only be enabled in Development.");
instructorMailbox.ApiKey = builder.Configuration["MailSlurp:ApiKey"] ?? "";
instructorMailbox.Validate();
if (instructorMailbox.Enabled && Guid.TryParse(builder.Configuration["MailSlurp:SenderInboxId"], out var systemInbox)
    && instructorMailbox.Accounts.Any(a => a.InboxId == systemInbox))
    throw new InvalidOperationException("Instructor mailboxes must not share the system notification sender inbox.");
builder.Services.AddSingleton(instructorMailbox);
builder.Services.AddScoped<IMailboxIdentity, Lms.Web.Services.MailboxIdentity>();
builder.Services.AddHttpClient<IMailboxProvider, MailSlurpMailboxProvider>(client => client.Timeout = TimeSpan.FromSeconds(30));
builder.Services.AddScoped<InstructorMailboxService>();
var emailJourney = new EmailJourneyOptions();
builder.Configuration.GetSection("EmailJourney").Bind(emailJourney);
if (emailJourney.Enabled)
{
    if (!builder.Environment.IsDevelopment() || !builder.Configuration.GetValue<bool>("MailSlurp:Enabled")
        || !Uri.TryCreate(emailJourney.PublicBaseUrl, UriKind.Absolute, out var emailBaseUrl)
        || (emailBaseUrl.Scheme != "http" && emailBaseUrl.Scheme != "https")
        || !builder.Configuration.GetSection("MailSlurp:AllowedRecipients").Get<string[]>()!
            .Contains(emailJourney.TestRecipient, StringComparer.OrdinalIgnoreCase))
        throw new InvalidOperationException("The email journey requires Development, MailSlurp, an allowlisted synthetic inbox, and an absolute public base URL.");
}
builder.Services.AddSingleton(emailJourney);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<IEmailJourneyService, EmailJourneyService>();
var passwordRecovery = new PasswordRecoveryOptions();
builder.Configuration.GetSection("PasswordRecovery").Bind(passwordRecovery);
if (passwordRecovery.Enabled)
{
    if (!Uri.TryCreate(passwordRecovery.PublicBaseUrl, UriKind.Absolute, out var recoveryUrl)
        || !string.IsNullOrEmpty(recoveryUrl.UserInfo) || !string.IsNullOrEmpty(recoveryUrl.Query)
        || !string.IsNullOrEmpty(recoveryUrl.Fragment) || recoveryUrl.AbsolutePath != "/"
        || (recoveryUrl.Scheme != "https" && !(builder.Environment.IsDevelopment()
            && recoveryUrl.Scheme == "http" && recoveryUrl.IsLoopback))
        || (!builder.Configuration.GetValue<bool>("MailSlurp:Enabled")
            && !string.Equals(Environment.GetEnvironmentVariable("EMAIL_DELIVERY_MODE"), "smtp", StringComparison.OrdinalIgnoreCase)))
        throw new InvalidOperationException("Password recovery requires a trusted HTTPS public base URL and real email delivery (SMTP or the Development MailSlurp transport).");
}
builder.Services.AddSingleton(passwordRecovery);
builder.Services.AddScoped<IPasswordRecoveryService, PasswordRecoveryService>();
builder.Services.AddSingleton<RosterSearchProtection>();
var turnstile = new TurnstileOptions();
builder.Configuration.GetSection("Turnstile").Bind(turnstile);
if (!builder.Environment.IsDevelopment() && (turnstile.UseTestKeys
    || turnstile.SiteKey.Contains("x00000000000000000000", StringComparison.Ordinal)
    || turnstile.SecretKey.Contains("x00000000000000000000", StringComparison.Ordinal)))
    throw new InvalidOperationException("Turnstile test keys are allowed only in Development.");
builder.Services.AddSingleton(turnstile);
builder.Services.AddHttpClient<IRosterCaptchaVerifier, TurnstileVerifier>(client =>
    client.Timeout = TimeSpan.FromSeconds(5));
builder.Services.AddScoped<RosterSearchSession>();
builder.Services.AddScoped<IRosterRegistrationService, RosterRegistrationService>();
builder.Services.AddScoped<IPDFInvoiceService, PDFInvoiceService>();
builder.Services.AddScoped<IPaymentService, PaymentService>();
builder.Services.AddScoped<StateLicensingEditorService>();
var developmentCatalog = new DevelopmentCatalogOptions
{
    Enabled = builder.Configuration.GetValue<bool>("DevelopmentCatalog:Enabled")
};
builder.Services.AddSingleton(developmentCatalog);
builder.Services.AddScoped<IDevelopmentCertificateService, DevelopmentCertificateService>();
builder.Services.AddScoped<AdminCertificateService>();
var developmentCertificatesEnabled = builder.Configuration.GetValue<bool>("DevelopmentCertificates:Enabled");
if (developmentCertificatesEnabled && !builder.Environment.IsDevelopment())
{
    throw new InvalidOperationException("Development certificates may only be enabled in the Development environment.");
}

var app = builder.Build();

if (args.Contains("--mailslurp-smoke-test"))
{
    if (!app.Environment.IsDevelopment() || !builder.Configuration.GetValue<bool>("MailSlurp:Enabled"))
        throw new InvalidOperationException("The email smoke test requires Development and MailSlurp:Enabled.");
    using var scope = app.Services.CreateScope();
    var options = scope.ServiceProvider.GetRequiredService<MailSlurpOptions>();
    var email = scope.ServiceProvider.GetRequiredService<IEmailService>();
    var subject = $"LMS email connection test {DateTime.UtcNow:yyyyMMdd-HHmmss}";
    if (!await email.SendAsync(new EmailMessage(options.AllowedRecipients[0], subject,
        "<h1>LMS email connection verified</h1><p>This synthetic message tests development email delivery only. No roster data is included.</p>")))
        throw new InvalidOperationException("MailSlurp email smoke test failed. See the email service error.");
    app.Logger.LogInformation("Smoke test sent: {Subject}. Confirm receipt in the MailSlurp inbox.", subject);
    return;
}

app.Logger.LogInformation(
    "LMS SQLite database: {DatabasePath}. Selection: {DatabasePathSource}. Content root: {ContentRoot}",
    Path.GetFullPath(databasePath), databasePathSource, app.Environment.ContentRootPath);

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var userAccountService = scope.ServiceProvider.GetRequiredService<IUserAccountService>();
    await EnsureLegacyDatabaseMigrationBaselineAsync(dbContext);
    await EnsureEnrollmentProvenanceSchemaAsync(dbContext);
    await EnsureUserLicenseTypeSchemaAsync(dbContext);
    await EnsureInstructorProfileSchemaAsync(dbContext);
    await EnsureSchoolProfileStateSchemaAsync(dbContext);
    await EnsureUserAndCourseStateSchemaAsync(dbContext);
    await EnsureRegulatoryAffiliationEmailSchemaAsync(dbContext);
    await dbContext.Database.MigrateAsync();
    // WAL lets pages keep reading while long roster imports hold the write transaction.
    await dbContext.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");
    await EnsureEnrollmentProvenanceSchemaAsync(dbContext);
    await EnsureUserLicenseTypeSchemaAsync(dbContext);
    await EnsureInstructorProfileSchemaAsync(dbContext);
    await EnsureSchoolProfileStateSchemaAsync(dbContext);
    await EnsureUserAndCourseStateSchemaAsync(dbContext);
    await EnsureRegulatoryAffiliationEmailSchemaAsync(dbContext);
    var nameIndexStarted = System.Diagnostics.Stopwatch.StartNew();
    if (await RegulatoryLicenseeNameIndex.EnsureAsync(dbContext))
        app.Logger.LogInformation("Built roster name search index in {Elapsed}.", nameIndexStarted.Elapsed);
    await userAccountService.EnsureSeedUsersAsync();
    await CourseSeed.SeedAsync(dbContext);
    await StateLicensingEntrySeed.SeedAsync(dbContext);
    if (developmentCatalog.Enabled)
    {
        await DevelopmentStateCourseSeed.SeedAsync(dbContext);
    }
    await CourseSeed.SeedEnrollmentsAsync(dbContext);
    if (developmentCertificatesEnabled)
    {
        await scope.ServiceProvider.GetRequiredService<IDevelopmentCertificateService>().InitializeAsync(
            builder.Configuration["DevelopmentCertificates:InitialPassword"],
            Path.Combine(databaseDirectory!, "development-certificates"));
    }
}

var archiveImportIndex = Array.IndexOf(args, "--florida-bulk-import-archive");
if (args.Contains("--nevada-nred-import-all", StringComparer.Ordinal))
{
    await using var scope = app.Services.CreateAsyncScope();
    var result = await scope.ServiceProvider.GetRequiredService<NevadaNredBulkImportService>()
        .ImportCaptureAsync();
    app.Logger.LogInformation(
        "Nevada NRED snapshot {SnapshotId} imported: {SourceRows} source rows, {NumberedLicenses} normalized licenses, {RowsWithoutLicenseNumber} rows without license numbers.",
        result.SnapshotId, result.SourceRows, result.NumberedLicenses, result.RowsWithoutLicenseNumber);
    return;
}
var californiaArchiveImportIndex = Array.IndexOf(args, "--california-bulk-import-archive");
if (californiaArchiveImportIndex >= 0 || args.Contains("--california-bulk-collect", StringComparer.Ordinal))
{
    var collection = app.Services.GetRequiredService<CaliforniaBulkCollectionService>();
    if (californiaArchiveImportIndex >= 0)
    {
        if (californiaArchiveImportIndex + 1 >= args.Length)
            throw new ArgumentException("--california-bulk-import-archive requires a local validated archive directory.");
        await collection.ImportArchiveAsync(args[californiaArchiveImportIndex + 1]);
    }
    else await collection.CollectAsync();
    app.Logger.LogInformation("California bulk roster synchronized: {SnapshotId}", (await collection.GetStatusAsync()).SnapshotId);
    return;
}
var texasArchiveImportIndex = Array.IndexOf(args, "--texas-bulk-import-archive");
if (texasArchiveImportIndex >= 0 || args.Contains("--texas-bulk-collect", StringComparer.Ordinal))
{
    var collection = app.Services.GetRequiredService<TexasBulkCollectionService>();
    if (texasArchiveImportIndex >= 0)
    {
        if (texasArchiveImportIndex + 1 >= args.Length)
            throw new ArgumentException("--texas-bulk-import-archive requires a local validated archive directory.");
        await collection.ImportArchiveAsync(args[texasArchiveImportIndex + 1]);
    }
    else await collection.CollectAsync();
    app.Logger.LogInformation("Texas bulk roster synchronized: {SnapshotId}", (await collection.GetStatusAsync()).SnapshotId);
    return;
}
if (archiveImportIndex >= 0)
{
    if (archiveImportIndex + 1 >= args.Length || string.IsNullOrWhiteSpace(args[archiveImportIndex + 1]))
        throw new ArgumentException("--florida-bulk-import-archive requires a local validated archive directory.");
    await app.Services.GetRequiredService<FloridaBulkCollectionService>().ImportArchiveAsync(
        Path.GetFullPath(args[archiveImportIndex + 1]), CancellationToken.None);
    var status = await app.Services.GetRequiredService<FloridaBulkCollectionService>().GetStatusAsync(CancellationToken.None);
    app.Logger.LogInformation("Florida bulk archive synchronized as current roster: {SnapshotId}", status.SnapshotId);
    return;
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
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/forgot-password")
        || context.Request.Path.StartsWithSegments("/reset-password")
        || context.Request.Path.StartsWithSegments("/auth/password"))
    {
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers["Referrer-Policy"] = "no-referrer";
        context.Response.Headers["X-Robots-Tag"] = "noindex, nofollow";
    }
    await next();
});
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

app.MapEmailJourney();
app.MapPasswordRecovery();
app.MapRegulatoryCollectionExports();
app.MapFloridaBulkCollectionExports();
app.MapMicrosoftSignIn();

app.MapPost("/auth/register", async Task<IResult> (HttpContext context, [FromForm] RegisterGuestRequest form,
    IUserAccountService accounts, IAuditLogService audit, IEmailJourneyService journey,
    EmailJourneyOptions journeyOptions, CancellationToken ct) =>
{
    if (context.User.Identity?.IsAuthenticated == true)
        return TypedResults.LocalRedirect("/courses");

    var returnUrl = string.IsNullOrWhiteSpace(form.ReturnUrl) ? "/courses" : form.ReturnUrl;
    IResult Reject(string error) => TypedResults.LocalRedirect($"/register?error={error}&returnUrl={Uri.EscapeDataString(returnUrl)}");
    if (string.IsNullOrWhiteSpace(form.Email) || string.IsNullOrWhiteSpace(form.Password)
        || string.IsNullOrWhiteSpace(form.ConfirmPassword))
        return Reject("missing");
    if (form.Email.Trim().Length > 120 || !new System.ComponentModel.DataAnnotations.EmailAddressAttribute().IsValid(form.Email.Trim()))
        return Reject("email");
    if (form.Password != form.ConfirmPassword)
        return Reject("mismatch");

    UserAccount user;
    try
    {
        user = await accounts.RegisterLearnerAsync(form.Email, form.Password, form.MarketingEmails == "true");
    }
    catch (InvalidOperationException ex) when (ex.Message == "A user with this email already exists."
        || ex.Message == "Password must be at least 8 characters.")
    {
        app.Logger.LogWarning("Guest registration rejected: {Reason}", ex.Message);
        return Reject(ex.Message.StartsWith("A user", StringComparison.Ordinal) ? "exists" : "password");
    }

    if (journeyOptions.AppliesTo(user.Email))
    {
        var verification = await journey.SendVerificationAsync(user, ct);
        return TypedResults.LocalRedirect(verification.HasValue ? "/email-test?step=sent" : "/email-test?error=delivery");
    }
    return await EmailJourneyEndpoints.SignInAsync(context, user, returnUrl, audit);
}).RequireRateLimiting("login").WithSummary("Create an individual Guest account and sign in, or start the configured test email verification.");

app.MapPost("/auth/login", async (HttpContext httpContext, [FromForm] LoginForm form, IUserAccountService userAccountService,
    IAuditLogService auditLogService, IEmailJourneyService journey, EmailJourneyOptions journeyOptions,
    Microsoft.AspNetCore.DataProtection.IDataProtectionProvider protection, CancellationToken ct) =>
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
    if (authenticatedUser.EmailTwoStepEnabled || journeyOptions.AppliesTo(authenticatedUser.Email))
    {
        if (!journeyOptions.AppliesTo(authenticatedUser.Email))
            return Results.LocalRedirect("/login?error=email-unavailable");
        if (authenticatedUser.EmailVerifiedAtUtc is null)
        {
            var verification = await journey.SendVerificationAsync(authenticatedUser, ct);
            return Results.LocalRedirect(verification.HasValue ? "/email-test?step=sent" : "/email-test?error=delivery");
        }
        var challenge = await journey.SendLoginCodeAsync(authenticatedUser, ct);
        if (!challenge.HasValue) return Results.LocalRedirect("/email-test?error=delivery");
        EmailJourneyEndpoints.SetPending(httpContext, protection, challenge.Value, form.ReturnUrl);
        return Results.LocalRedirect("/email-test?step=code");
    }
    return await EmailJourneyEndpoints.SignInAsync(httpContext, authenticatedUser, form.ReturnUrl, auditLogService);
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

if (developmentCertificatesEnabled)
{
    app.MapGet("/dev/certificates/{learnerId:guid}/download",
        async Task<Microsoft.AspNetCore.Http.HttpResults.Results<Microsoft.AspNetCore.Http.HttpResults.FileContentHttpResult,
            Microsoft.AspNetCore.Http.HttpResults.NotFound>> (
            Guid learnerId, HttpContext context, IDevelopmentCertificateService certificates, CancellationToken cancellationToken) =>
        {
            if (!Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var requesterId))
            {
                return TypedResults.NotFound();
            }
            var certificate = (await certificates.GetAsync(requesterId, context.User.IsInRole("Admin"), cancellationToken))
                .SingleOrDefault(item => item.UserId == learnerId);
            if (certificate is null)
            {
                return TypedResults.NotFound();
            }
            context.Response.Headers.CacheControl = "no-store";
            return TypedResults.File(DevelopmentCertificateService.GeneratePdf(certificate), "application/pdf", certificate.FileName);
        })
        .RequireAuthorization(policy => policy.RequireRole("Admin", "Learner"))
        .WithName("DownloadDevelopmentCertificate")
        .WithSummary("Download an owner-scoped development certificate specimen.")
        .WithDescription("Development-only simulated certificate. No official credit, verification, or regulatory reporting.");
}

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

static async Task EnsureRegulatoryAffiliationEmailSchemaAsync(ApplicationDbContext dbContext)
{
    const string tableName = "RegulatoryFirmLicenseeAffiliations";
    if (!await SqliteTableExistsAsync(dbContext, tableName)
        || await SqliteColumnExistsAsync(dbContext, tableName, "ContactEmail"))
    {
        return;
    }

    await dbContext.Database.ExecuteSqlRawAsync($"ALTER TABLE \"{tableName}\" ADD COLUMN \"ContactEmail\" TEXT NULL;");
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

/// <summary>Creates an individual Guest account using email and password.</summary>
sealed record RegisterGuestRequest
{
    public string? Email { get; init; }
    public string? Password { get; init; }
    public string? ConfirmPassword { get; init; }
    public string? ReturnUrl { get; init; }
    public string? MarketingEmails { get; init; }
}
