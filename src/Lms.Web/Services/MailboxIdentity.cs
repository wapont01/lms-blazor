using System.Security.Claims;
using Lms.Application.Services;
using Lms.Application.Data;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Lms.Web.Services;

public sealed class MailboxIdentity(AuthenticationStateProvider authentication, ApplicationDbContext db) : IMailboxIdentity
{
    public async Task<Guid> GetInstructorIdAsync()
    {
        var user = (await authentication.GetAuthenticationStateAsync()).User;
        if (user.Identity?.IsAuthenticated != true || (!user.IsInRole("Instructor") && !user.IsInRole("Admin"))
            || !Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var id))
            throw new UnauthorizedAccessException("Sign in as an instructor to use mail.");
        var account = await db.UserAccounts.AsNoTracking().SingleOrDefaultAsync(account => account.Id == id && account.IsActive);
        if (account is null || !user.IsInRole(account.Role)
            || (account.Role != "Admin" && !await InstructorAccess.Accounts(db).AnyAsync(account => account.Id == id)))
            throw new UnauthorizedAccessException("An active instructor account is required to use mail.");
        return id;
    }
}
