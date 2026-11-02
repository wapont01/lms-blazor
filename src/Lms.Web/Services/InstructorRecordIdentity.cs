using System.Security.Claims;
using Lms.Application.Services;
using Microsoft.AspNetCore.Components.Authorization;

namespace Lms.Web.Services;

public sealed class InstructorRecordIdentity(AuthenticationStateProvider authentication) : IInstructorRecordIdentity
{
    public async Task<ClaimsPrincipal> GetUserAsync() =>
        (await authentication.GetAuthenticationStateAsync()).User;
}
