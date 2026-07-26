# SSO Provider Profiles (Entra ID, Auth0, Okta)

This guide provides production-ready SSO profile settings for the LMS app.

## Quick Start (PowerShell)

1. Pick one provider block below.
2. Replace placeholder values.
3. Run the block in PowerShell from the workspace root.
4. Start the app from `src`:

```powershell
Set-Location .\src
& 'C:\Program Files\dotnet\dotnet.exe' run --project .\Lms.Web\Lms.Web.csproj --no-launch-profile
```

5. Browse to `/login` and use the SSO button.

To clear SSO variables in the current terminal session:

```powershell
Get-ChildItem Env:Authentication__Sso__* | ForEach-Object { Remove-Item "Env:$($_.Name)" }
```

## Core Settings

Set these keys under `Authentication:Sso`:

- `Enabled`
- `DisplayName`
- `Authority`
- `ClientId`
- `ClientSecret`
- `CallbackPath` (default `/signin-oidc`)
- `RoleClaimType` (often `roles`)
- `GroupClaimType` (often `groups`)
- `DefaultRole` (recommended `Learner`)
- `ApplyMappedRoleOnSignIn` (`true` to sync local role from token/group mapping)
- `AllowedEmailDomains` (optional domain allowlist)
- `RoleMapping:AdminGroupIds`
- `RoleMapping:InstructorGroupIds`
- `RoleMapping:BrokerGroupIds`
- `RoleMapping:LearnerGroupIds`

## Microsoft Entra ID Profile

Suggested values:

- `DisplayName`: `Microsoft Entra ID`
- `Authority`: `https://login.microsoftonline.com/<tenant-id>/v2.0`
- `RoleClaimType`: `roles`
- `GroupClaimType`: `groups`

Example environment variables:

- `Authentication__Sso__Enabled=true`
- `Authentication__Sso__DisplayName=Microsoft Entra ID`
- `Authentication__Sso__Authority=https://login.microsoftonline.com/<tenant-id>/v2.0`
- `Authentication__Sso__ClientId=<client-id>`
- `Authentication__Sso__ClientSecret=<client-secret>`
- `Authentication__Sso__CallbackPath=/signin-oidc`
- `Authentication__Sso__RoleClaimType=roles`
- `Authentication__Sso__GroupClaimType=groups`
- `Authentication__Sso__DefaultRole=Learner`
- `Authentication__Sso__ApplyMappedRoleOnSignIn=true`
- `Authentication__Sso__AllowedEmailDomains__0=contoso.com`
- `Authentication__Sso__RoleMapping__AdminGroupIds__0=<entra-admin-group-object-id>`
- `Authentication__Sso__RoleMapping__InstructorGroupIds__0=<entra-instructor-group-object-id>`
- `Authentication__Sso__RoleMapping__BrokerGroupIds__0=<entra-broker-group-object-id>`
- `Authentication__Sso__RoleMapping__LearnerGroupIds__0=<entra-learner-group-object-id>`

PowerShell profile block:

```powershell
$env:Authentication__Sso__Enabled = 'true'
$env:Authentication__Sso__Provider = 'Entra'
$env:Authentication__Sso__DisplayName = 'Microsoft Entra ID'
$env:Authentication__Sso__Authority = 'https://login.microsoftonline.com/<tenant-id>/v2.0'
$env:Authentication__Sso__ClientId = '<client-id>'
$env:Authentication__Sso__ClientSecret = '<client-secret>'
$env:Authentication__Sso__CallbackPath = '/signin-oidc'
$env:Authentication__Sso__RoleClaimType = 'roles'
$env:Authentication__Sso__GroupClaimType = 'groups'
$env:Authentication__Sso__DefaultRole = 'Learner'
$env:Authentication__Sso__ApplyMappedRoleOnSignIn = 'true'
$env:Authentication__Sso__AllowedEmailDomains__0 = 'contoso.com'

# Optional group mapping
$env:Authentication__Sso__RoleMapping__AdminGroupIds__0 = '<entra-admin-group-object-id>'
$env:Authentication__Sso__RoleMapping__InstructorGroupIds__0 = '<entra-instructor-group-object-id>'
$env:Authentication__Sso__RoleMapping__BrokerGroupIds__0 = '<entra-broker-group-object-id>'
$env:Authentication__Sso__RoleMapping__LearnerGroupIds__0 = '<entra-learner-group-object-id>'

# Keep test callback disabled in production-like runs
$env:Authentication__Sso__TestModeEnabled = 'false'
```

## Auth0 Profile

Suggested values:

- `DisplayName`: `Auth0`
- `Authority`: `https://<tenant>.auth0.com`
- `RoleClaimType`: `roles` (or your custom role claim namespace)
- `GroupClaimType`: `groups` (or custom groups claim namespace)

If using namespaced custom claims, set those names exactly, for example:

- `Authentication__Sso__RoleClaimType=https://lms.example.com/roles`
- `Authentication__Sso__GroupClaimType=https://lms.example.com/groups`

PowerShell profile block:

```powershell
$env:Authentication__Sso__Enabled = 'true'
$env:Authentication__Sso__Provider = 'Auth0'
$env:Authentication__Sso__DisplayName = 'Auth0'
$env:Authentication__Sso__Authority = 'https://<tenant>.auth0.com'
$env:Authentication__Sso__ClientId = '<client-id>'
$env:Authentication__Sso__ClientSecret = '<client-secret>'
$env:Authentication__Sso__CallbackPath = '/signin-oidc'

# Use either default claims...
$env:Authentication__Sso__RoleClaimType = 'roles'
$env:Authentication__Sso__GroupClaimType = 'groups'

# ...or namespaced custom claims
# $env:Authentication__Sso__RoleClaimType = 'https://lms.example.com/roles'
# $env:Authentication__Sso__GroupClaimType = 'https://lms.example.com/groups'

$env:Authentication__Sso__DefaultRole = 'Learner'
$env:Authentication__Sso__ApplyMappedRoleOnSignIn = 'true'
$env:Authentication__Sso__AllowedEmailDomains__0 = 'contoso.com'

# Optional group mapping
$env:Authentication__Sso__RoleMapping__AdminGroupIds__0 = '<auth0-admin-group-id>'
$env:Authentication__Sso__RoleMapping__InstructorGroupIds__0 = '<auth0-instructor-group-id>'
$env:Authentication__Sso__RoleMapping__BrokerGroupIds__0 = '<auth0-broker-group-id>'
$env:Authentication__Sso__RoleMapping__LearnerGroupIds__0 = '<auth0-learner-group-id>'

$env:Authentication__Sso__TestModeEnabled = 'false'
```

## Okta Profile

Suggested values:

- `DisplayName`: `Okta`
- `Authority`: `https://<tenant>.okta.com/oauth2/default`
- `RoleClaimType`: `groups` (if role is group-driven) or custom claim name
- `GroupClaimType`: `groups`

For group-to-role mapping, keep `ApplyMappedRoleOnSignIn=true` and populate `RoleMapping` group lists.

PowerShell profile block:

```powershell
$env:Authentication__Sso__Enabled = 'true'
$env:Authentication__Sso__Provider = 'Okta'
$env:Authentication__Sso__DisplayName = 'Okta'
$env:Authentication__Sso__Authority = 'https://<tenant>.okta.com/oauth2/default'
$env:Authentication__Sso__ClientId = '<client-id>'
$env:Authentication__Sso__ClientSecret = '<client-secret>'
$env:Authentication__Sso__CallbackPath = '/signin-oidc'
$env:Authentication__Sso__RoleClaimType = 'groups'
$env:Authentication__Sso__GroupClaimType = 'groups'
$env:Authentication__Sso__DefaultRole = 'Learner'
$env:Authentication__Sso__ApplyMappedRoleOnSignIn = 'true'
$env:Authentication__Sso__AllowedEmailDomains__0 = 'contoso.com'

# Optional group mapping
$env:Authentication__Sso__RoleMapping__AdminGroupIds__0 = '<okta-admin-group-id>'
$env:Authentication__Sso__RoleMapping__InstructorGroupIds__0 = '<okta-instructor-group-id>'
$env:Authentication__Sso__RoleMapping__BrokerGroupIds__0 = '<okta-broker-group-id>'
$env:Authentication__Sso__RoleMapping__LearnerGroupIds__0 = '<okta-learner-group-id>'

$env:Authentication__Sso__TestModeEnabled = 'false'
```

## Local Validation Checklist

After applying any profile:

1. Open `/login` and confirm provider-specific button text appears.
2. Complete IdP sign-in.
3. Verify post-login role-based navigation (Admin/Instructor/Broker/Learner).
4. Check that a local user row exists and role assignment follows mapping policy.
5. Verify `/auth/sso/logout` signs out cleanly.

## Role Resolution Order

On SSO sign-in, the app resolves role in this order:

1. Role claim values (`RoleClaimType`, then fallback `ClaimTypes.Role`) for known roles: `Admin`, `Instructor`, `Broker`, `Learner`
2. Group mapping lists (`RoleMapping`)
3. `DefaultRole`

If `ApplyMappedRoleOnSignIn=true`, local DB role is updated to the resolved role at sign-in.

## Security Notes

- Use `AllowedEmailDomains` to restrict tenant entry points.
- Keep local role as source-of-truth by setting `ApplyMappedRoleOnSignIn=false` unless your IdP governance is mature.
- Never commit real `ClientSecret` values into source control.
- Keep `TestModeEnabled=false` in production.
