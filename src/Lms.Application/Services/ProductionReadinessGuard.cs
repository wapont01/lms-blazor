using Lms.Domain.Entities;

namespace Lms.Application.Services;

public static class ProductionReadinessGuard
{
    public static void Validate(string environmentName, SchoolProfile floridaProfile)
    {
        if (string.Equals(environmentName, "Development", StringComparison.OrdinalIgnoreCase)
            || string.Equals(environmentName, "Testing", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (string.Equals(Environment.GetEnvironmentVariable("LMS_TEST_SHORTCUT"), "1", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("LMS_TEST_SHORTCUT must be disabled outside Development and Testing environments.");
        }

        if (string.Equals(floridaProfile.ProviderLicenseNumber, SchoolProfile.TemporaryProviderLicenseNumber, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("A verified Florida provider number is required before production startup.");
        }

        if (floridaProfile.StaffMembers.Any(member => string.Equals(member.LicenseNumber, "FL-INSTRUCTOR-PLACEHOLDER", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("Verified Florida instructor credentials are required before production startup.");
        }
    }
}
