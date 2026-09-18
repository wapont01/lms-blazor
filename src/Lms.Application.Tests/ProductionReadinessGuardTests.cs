using Lms.Application.Services;
using Lms.Domain.Entities;
using Xunit;

namespace Lms.Application.Tests;

public sealed class ProductionReadinessGuardTests
{
    [Fact]
    public void Validate_AllowsMockProfileInTesting()
    {
        ProductionReadinessGuard.Validate("Testing", CreateFloridaProfile());
    }

    [Fact]
    public void Validate_RejectsTemporaryFloridaCredentialsInProduction()
    {
        var previousShortcut = Environment.GetEnvironmentVariable("LMS_TEST_SHORTCUT");
        Environment.SetEnvironmentVariable("LMS_TEST_SHORTCUT", null);
        try
        {
            var exception = Assert.Throws<InvalidOperationException>(() =>
                ProductionReadinessGuard.Validate("Production", CreateFloridaProfile()));

            Assert.Contains("provider number", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Environment.SetEnvironmentVariable("LMS_TEST_SHORTCUT", previousShortcut);
        }
    }

    private static SchoolProfile CreateFloridaProfile() => new()
    {
        State = "Florida",
        ProviderLicenseNumber = SchoolProfile.TemporaryProviderLicenseNumber,
        StaffMembers =
        {
            new SchoolStaffMember { Name = "Milena", LicenseNumber = "FL-INSTRUCTOR-PLACEHOLDER" }
        }
    };
}
