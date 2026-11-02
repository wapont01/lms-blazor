using Lms.Application.Data;
using Lms.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Lms.Application.Services;

public sealed record CertificatePage<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);

public sealed class AdminCertificateService(ApplicationDbContext db)
{
    public const int PageSize = 20;

    public async Task<CertificatePage<CertificateComplianceRow>> GetOfficialAsync(
        string state, string search, string course, string status, int page)
    {
        if (!DevelopmentCourseCatalog.States.Contains(state))
        {
            return new([], 0, 1, PageSize);
        }
        var now = DateTime.UtcNow;
        var soon = now.AddDays(30);
        var query = db.CompletionCertificates.AsNoTracking()
            .Where(item => item.UserAccount != null && item.UserAccount.IsActive && item.UserAccount.Role == "Learner"
                && (item.CertificationState ?? item.Course!.State) == state);
        if (!string.IsNullOrEmpty(course))
            query = query.Where(item => item.Course!.Title == course);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(item => item.UserAccount!.DisplayName.ToLower().Contains(term)
                || item.UserAccount.Email.ToLower().Contains(term) || item.CertificateNumber.ToLower().Contains(term)
                || (item.CertificationLicenseNumber != null && item.CertificationLicenseNumber.ToLower().Contains(term)));
        }
        query = status switch
        {
            "Revoked" => query.Where(item => item.IsRevoked),
            "Expired" => query.Where(item => !item.IsRevoked && item.ExpiresAt <= now),
            "Expiring Soon" => query.Where(item => !item.IsRevoked && item.ExpiresAt > now && item.ExpiresAt <= soon),
            "Active" => query.Where(item => !item.IsRevoked && item.ExpiresAt > soon),
            _ => query
        };
        var count = await query.CountAsync();
        page = Math.Clamp(page, 1, Math.Max(1, (count + PageSize - 1) / PageSize));
        var items = await query.OrderByDescending(item => item.IssuedAt).ThenBy(item => item.Id)
            .Skip((page - 1) * PageSize).Take(PageSize)
            .Select(item => new CertificateComplianceRow(item.Id, item.UserAccount!.DisplayName,
                item.UserAccount.Email, item.Course!.Title, item.CertificateNumber, item.VerificationCode,
                item.IssuedAt, item.ExpiresAt,
                item.IsRevoked ? "Revoked" : item.ExpiresAt <= now ? "Expired" : item.ExpiresAt <= soon ? "Expiring Soon" : "Active",
                item.IsRevoked || item.ExpiresAt <= now)).ToListAsync();
        return new(items, count, page, PageSize);
    }

    public async Task<List<string>> GetOfficialCoursesAsync(string state) =>
        await db.CompletionCertificates.AsNoTracking()
            .Where(item => item.UserAccount != null && item.UserAccount.IsActive && item.UserAccount.Role == "Learner"
                && (item.CertificationState ?? item.Course!.State) == state)
            .Select(item => item.Course!.Title).Distinct().OrderBy(title => title).ToListAsync();
}
