using Lms.Domain.Entities;
using Lms.Application.Services;
using Xunit;

namespace Lms.Application.Tests;

public class StateComplianceProfileCatalogTests
{
    [Fact]
    public void GetProfile_ReturnsStateSpecificDistanceLearningRequirements()
    {
        var florida = DistanceLearningProfileCatalog.Get("Florida");
        var northCarolina = DistanceLearningProfileCatalog.Get("North Carolina");

        Assert.Equal("Florida", florida.State);
        Assert.True(florida.Requirements.AllowsDistanceEducation);
        Assert.True(florida.Requirements.RequiresProviderDisclosure);
        Assert.True(florida.Requirements.SupportsIndependentStudy);

        Assert.Equal("North Carolina", northCarolina.State);
        Assert.True(northCarolina.Requirements.AllowsDistanceEducation);
        Assert.True(northCarolina.Requirements.RequiresInstructorCameraVisibilityForSynchronousLearning);
        Assert.True(northCarolina.Requirements.RequiresAttendanceTrackingForSynchronousCourses);
    }

    [Fact]
    public void Validate_AllowsCompleteFloridaDistanceCourse()
    {
        var course = CreateCourse("Florida");
        course.Modules.Add(new Module
        {
            Lessons =
            {
                new Lesson { Title = "Lesson", TextContent = "Content", DurationMinutes = 60 }
            }
        });

        var result = DistanceLearningComplianceValidator.Validate(course);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_RejectsRegulatedCourseWithoutContentControls()
    {
        var course = CreateCourse("North Carolina");
        course.DeliveryMethod = "UnsupportedDelivery";
        course.CompletionWindowDays = null;

        var result = DistanceLearningComplianceValidator.Validate(course);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("delivery method", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Errors, error => error.Contains("completion window", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Errors, error => error.Contains("lesson", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void MapNcLicenseTypeToFlorida_MapsProvisionalBrokerToSalesAssociate()
    {
        Assert.Equal("Sales Associate", DistanceLearningProfileCatalog.MapNcLicenseTypeToFlorida("PB"));
        Assert.Equal("Broker", DistanceLearningProfileCatalog.MapNcLicenseTypeToFlorida("B"));
        Assert.Equal("Sales Associate", DistanceLearningProfileCatalog.MapNcLicenseTypeToFlorida("SL"));
        Assert.Equal("Broker Associate", DistanceLearningProfileCatalog.MapNcLicenseTypeToFlorida("BL"));
        Assert.Equal("Broker", DistanceLearningProfileCatalog.MapNcLicenseTypeToFlorida("BK"));
    }

    [Fact]
    public void LicenseEducationRules_ClassifiesPostLicenseAndCeFromInitialDate()
    {
        var initialDate = new DateTime(2025, 1, 15);
        var beforePostlicensingDeadline = LicenseEducationRules.Calculate(
            initialDate,
            null,
            null,
            new DateTime(2027, 3, 31),
            new DateTime(2026, 6, 1));
        var overduePostlicensing = LicenseEducationRules.Calculate(
            initialDate,
            null,
            null,
            new DateTime(2027, 3, 31),
            new DateTime(2026, 8, 1));
        var continuingEducation = LicenseEducationRules.Calculate(
            initialDate,
            new DateTime(2026, 5, 1),
            new DateTime(2027, 3, 31),
            null,
            new DateTime(2026, 8, 1));

        Assert.Equal("Post-license", beforePostlicensingDeadline.Phase);
        Assert.False(beforePostlicensingDeadline.IsPostlicensingOverdue);
        Assert.Equal(new DateTime(2026, 7, 15), beforePostlicensingDeadline.ExpectedPostlicensingDate);
        Assert.Equal(502, beforePostlicensingDeadline.ElapsedDays);

        Assert.True(overduePostlicensing.IsPostlicensingOverdue);
        Assert.Equal("Continuing education", continuingEducation.Phase);
        Assert.Equal(new DateTime(2027, 3, 31), continuingEducation.ExpectedContinuingEducationDate);
    }

    private static Course CreateCourse(string state) => new()
    {
        Title = "Test course",
        ComplianceType = CourseComplianceTypes.Prelicensing,
        DeliveryMethod = CourseDeliveryMethods.DistanceEducation,
        State = state,
        CompletionWindowDays = 180,
        RequiredInstructionalMinutes = 60,
        MinimumPassingPercent = 75
    };
}
