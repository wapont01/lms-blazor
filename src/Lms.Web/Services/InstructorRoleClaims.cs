using System.Security.Claims;
using Lms.Application.Data;
using Lms.Application.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;

namespace Lms.Web.Services;

public sealed class InstructorRoleClaims(ApplicationDbContext db, IWebHostEnvironment environment,
    EmailJourneyOptions options) : IClaimsTransformation
{
    public async Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        if (principal.Identity?.IsAuthenticated != true
            || !Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id))
            return principal;

        var account = await db.UserAccounts.AsNoTracking().SingleOrDefaultAsync(user => user.Id == id);
        var result = principal.Clone();
        foreach (var identity in result.Identities)
            foreach (var claim in identity.FindAll(identity.RoleClaimType).ToList())
                identity.RemoveClaim(claim);

        if (account is not { IsActive: true }) return result;
        var target = result.Identities.First(identity => identity.IsAuthenticated);
        target.AddClaim(new Claim(target.RoleClaimType, account.Role));
        if (environment.IsDevelopment() && options.Enabled && (account.Role is "Broker" or "Learner")
            && await InstructorAccess.Accounts(db).AnyAsync(user => user.Id == id))
            target.AddClaim(new Claim(target.RoleClaimType, "Instructor"));
        return result;
    }
}
