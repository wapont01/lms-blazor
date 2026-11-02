using Lms.Application.Data;
using Lms.Domain.Entities;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Lms.Application.Services;

public sealed record DevelopmentCertificate(
    Guid UserId, string Email, string Name, string LicenseNumber, string State,
    string CourseTitle, string Hours, DateTimeOffset CreatedAtUtc)
{
    public string Number => $"DEV-{UserId:N}";
    public string FileName => $"DEV_{FilePart(State)}_{FilePart(Name)}_{FilePart(CourseTitle)}_{CreatedAtUtc:yyyy-MM-dd}_{UserId:N}.pdf";

    private static string FilePart(string value)
    {
        var part = Regex.Replace(value, "[^A-Za-z0-9]+", "-").Trim('-');
        return part[..Math.Min(60, part.Length)];
    }
}

public sealed record DevelopmentCertificateCoverage(string State, int AccountCount, string Status);

public interface IDevelopmentCertificateService
{
    Task InitializeAsync(string? initialPassword, string outputDirectory);
    Task<List<DevelopmentCertificate>> GetAsync(Guid? requesterId, bool isAdmin, CancellationToken cancellationToken = default);
    Task<List<DevelopmentCertificateCoverage>> GetCoverageAsync(CancellationToken cancellationToken = default);
    Task<CertificatePage<DevelopmentCertificate>> GetPageAsync(string state, string search, string course, int page);
}

public sealed class DevelopmentCertificateService(ApplicationDbContext db, IUserAccountService users) : IDevelopmentCertificateService
{
    private const string EmailDomain = "@certificate-dev.invalid";
    private static readonly string[] EligibleStatuses = ["active", "current", "licensed"];
    private static readonly string[] NonIndividualTypes = ["corporation", "corporate broker", "limited liability company broker", "trade name broker"];

    public async Task InitializeAsync(string? initialPassword, string outputDirectory)
    {
        var selections = new List<(string State, string Name, string License, string Email)>();
        foreach (var state in DevelopmentCourseCatalog.States)
        {
            var existing = await db.UserAccounts.AsNoTracking()
                .Where(user => user.State == state && user.Role == "Learner" && user.Email.EndsWith(EmailDomain))
                .Select(user => user.LicenseNumber).ToListAsync();
            var needed = Math.Max(0, 2 - existing.Count);
            if (needed == 0)
            {
                continue;
            }
            var roster = await db.RegulatoryLicensees.AsNoTracking()
                .Where(person => person.State == state && person.Name.Trim() != "" && person.LicenseNumber.Trim() != ""
                    && person.Name.Length <= 120 && person.LicenseStatus != null
                    && EligibleStatuses.Contains(person.LicenseStatus.Trim().ToLower())
                    && !person.IsRestricted && person.LicenseType != null
                    && !NonIndividualTypes.Contains(person.LicenseType.Trim().ToLower())
                    && !existing.Contains(person.LicenseNumber))
                .GroupBy(person => person.LicenseNumber)
                .Select(group => new { Name = group.Min(person => person.Name)!, LicenseNumber = group.Key })
                .OrderBy(person => person.Name).ThenBy(person => person.LicenseNumber)
                .Take(needed).ToListAsync();

            foreach (var person in roster)
            {
                var stateCode = state switch
                {
                    "Florida" => "fl",
                    "North Carolina" => "nc",
                    _ => state.ToLowerInvariant().Replace(' ', '-')
                };
                var licenseKey = Convert.ToHexString(System.Text.Encoding.UTF8.GetBytes(person.LicenseNumber)).ToLowerInvariant();
                selections.Add((state, person.Name, person.LicenseNumber, $"{stateCode}-{licenseKey}{EmailDomain}"));
            }
        }

        await using var transaction = await db.Database.BeginTransactionAsync();
        foreach (var selection in selections)
        {
            var user = await db.UserAccounts.SingleOrDefaultAsync(account => account.Email == selection.Email);
            if (user is not null)
            {
                if (user.Role != "Learner" || user.LicenseNumber != selection.License || user.State != selection.State)
                {
                    throw new InvalidOperationException($"Development account identity conflict: {selection.Email}");
                }
                continue;
            }
            if (string.IsNullOrWhiteSpace(initialPassword))
            {
                throw new InvalidOperationException("DevelopmentCertificates:InitialPassword is required to create test logins.");
            }

            user = await users.CreateAsync(selection.Email, selection.Name, initialPassword, "Learner",
                actorEmail: "system@lms.local");
            user.LegalName = selection.Name;
            user.LicenseNumber = selection.License;
            user.State = selection.State;
            user.MarketingEmailOptOutAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }
        await transaction.CommitAsync();

        Directory.CreateDirectory(outputDirectory);
        foreach (var certificate in await GetAsync(null, true))
        {
            await File.WriteAllBytesAsync(Path.Combine(outputDirectory, certificate.FileName), GeneratePdf(certificate));
            var oldPath = Path.Combine(outputDirectory, $"{certificate.Number}.pdf");
            if (File.Exists(oldPath))
            {
                File.Delete(oldPath);
            }
        }
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "coverage.json"),
            System.Text.Json.JsonSerializer.Serialize(await GetCoverageAsync(),
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }

    public async Task<List<DevelopmentCertificate>> GetAsync(Guid? requesterId, bool isAdmin, CancellationToken cancellationToken = default)
    {
        var accounts = await db.UserAccounts.AsNoTracking()
            .Where(user => user.Email.EndsWith(EmailDomain) && user.Role == "Learner"
                && (isAdmin || user.Id == requesterId)
                && user.State != null && DevelopmentCourseCatalog.States.Contains(user.State)
                && user.LicenseNumber != null)
            .OrderBy(user => user.State).ThenBy(user => user.DisplayName)
            .ToListAsync(cancellationToken);
        return accounts.Select(ToCertificate).ToList();
    }

    private static DevelopmentCertificate ToCertificate(UserAccount user) => new(
            user.Id, user.Email, user.LegalName ?? user.DisplayName, user.LicenseNumber!, user.State!,
            user.State == "North Carolina" ? "Document Diligence: Safeguarding Your Transactions" : "Ethics at Work",
            user.State switch
            {
                "Florida" => "3 Law/Ethics | 0 Specialty (simulated)",
                "North Carolina" => "0 Core | 4 Elective (simulated)",
                _ => "3 simulated instructional hours - no state credit category asserted"
            },
            new DateTimeOffset(DateTime.SpecifyKind(user.CreatedAt, DateTimeKind.Utc)));

    public async Task<List<DevelopmentCertificateCoverage>> GetCoverageAsync(CancellationToken cancellationToken = default)
    {
        var counts = await db.UserAccounts.AsNoTracking()
            .Where(user => user.Email.EndsWith(EmailDomain) && user.Role == "Learner" && user.LicenseNumber != null && user.State != null)
            .GroupBy(user => user.State!)
            .Select(group => new { State = group.Key, Count = group.Count() })
            .ToDictionaryAsync(item => item.State, item => item.Count, cancellationToken);
        var rosterStates = await db.RegulatoryLicensees.AsNoTracking().Select(person => person.State)
            .Distinct().ToListAsync(cancellationToken);
        return DevelopmentCourseCatalog.States.Select(state =>
        {
            var count = counts.GetValueOrDefault(state);
            var status = count >= 2 ? "Ready"
                : !rosterStates.Contains(state) ? "Skipped: no roster records in database"
                : count == 0 ? "Skipped: no usable active individual roster records"
                : "Partial: only one usable active individual roster record";
            return new DevelopmentCertificateCoverage(state, count, status);
        }).ToList();
    }

    public async Task<CertificatePage<DevelopmentCertificate>> GetPageAsync(string state, string search, string course, int page)
    {
        if (!DevelopmentCourseCatalog.States.Contains(state))
            return new([], 0, 1, AdminCertificateService.PageSize);
        var title = state == "North Carolina" ? "Document Diligence: Safeguarding Your Transactions" : "Ethics at Work";
        if (!string.IsNullOrEmpty(course) && course != title)
            return new([], 0, 1, AdminCertificateService.PageSize);
        var query = db.UserAccounts.AsNoTracking().Where(user => user.Email.EndsWith(EmailDomain)
            && user.Role == "Learner" && user.State == state && user.LicenseNumber != null);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            var numberTerm = term.StartsWith("dev-", StringComparison.Ordinal) ? term[4..] : term;
            query = query.Where(user => (user.LegalName ?? user.DisplayName).ToLower().Contains(term)
                || user.Email.ToLower().Contains(term) || user.LicenseNumber!.ToLower().Contains(term)
                || user.Id.ToString().Replace("-", "").ToLower().Contains(numberTerm));
        }
        var total = await query.CountAsync();
        page = Math.Clamp(page, 1, Math.Max(1, (total + AdminCertificateService.PageSize - 1) / AdminCertificateService.PageSize));
        var accounts = await query.OrderBy(user => user.DisplayName).ThenBy(user => user.Id)
            .Skip((page - 1) * AdminCertificateService.PageSize).Take(AdminCertificateService.PageSize).ToListAsync();
        var certificates = accounts.Select(ToCertificate).ToList();
        return new(certificates, total, page, AdminCertificateService.PageSize);
    }

    public static byte[] GeneratePdf(DevelopmentCertificate certificate)
    {
        QuestPDF.Settings.License = LicenseType.Community;
        return Document.Create(document => document.Page(page =>
        {
            page.Size(PageSizes.A4.Landscape());
            page.Margin(32);
            page.DefaultTextStyle(style => style.FontSize(12));
            page.Background().AlignCenter().AlignMiddle()
                .Text("DEVELOPMENT SAMPLE").FontSize(54).Bold().FontColor(Colors.Grey.Lighten3);
            page.Content().Border(2).BorderColor(Colors.Blue.Darken3).Padding(22).Column(column =>
            {
                column.Spacing(10);
                column.Item().AlignCenter().Text("DEVELOPMENT / NOT VALID FOR CREDIT")
                    .FontSize(19).Bold().FontColor(Colors.Red.Darken2);
                column.Item().AlignCenter().Text($"{certificate.State} | Certificate of Completion")
                    .FontSize(25).Bold().FontColor(Colors.Blue.Darken3);
                column.Item().AlignCenter().Text("SIMULATED COMPLETION - LOCAL SOFTWARE TEST ONLY").Bold();
                column.Item().PaddingTop(6).AlignCenter().Text(certificate.Name).FontSize(24).Bold();
                column.Item().AlignCenter().Text($"License # {certificate.LicenseNumber} | {certificate.State}");
                column.Item().AlignCenter().Text("is shown as the student for this simulated distance-education course:");
                column.Item().AlignCenter().Text(certificate.CourseTitle).FontSize(18).SemiBold();
                column.Item().AlignCenter().Text(certificate.Hours);
                column.Item().AlignCenter().Text($"Simulated start / completion date: {certificate.CreatedAtUtc:yyyy-MM-dd} (UTC)");
                column.Item().Text(certificate.State == "Florida"
                    ? "Course approval number: DEVELOPMENT ONLY - no approval asserted"
                    : "Education provider / instructor number: DEVELOPMENT ONLY - no approval asserted");
                column.Item().Text("LMS Development School | Administrator: unsigned test specimen");
                column.Item().Text("Uses roster identity data for an authorized development fixture. This document does not attest to actual attendance, exam results, state approval, or earned education credit.")
                    .FontSize(10);
                column.Item().Text($"Test certificate: {certificate.Number}").FontSize(9);
            });
            page.Footer().AlignCenter().Text("NOT A REGULATORY CERTIFICATE | DO NOT SUBMIT FOR LICENSING OR RENEWAL")
                .Bold().FontColor(Colors.Red.Darken2);
        })).GeneratePdf();
    }
}
