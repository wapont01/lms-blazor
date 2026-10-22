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
        var southCarolina = DistanceLearningProfileCatalog.Get("South Carolina");
        var georgia = DistanceLearningProfileCatalog.Get("Georgia");

        Assert.Equal("Florida", florida.State);
        Assert.True(florida.Requirements.AllowsDistanceEducation);
        Assert.True(florida.Requirements.RequiresProviderDisclosure);
        Assert.True(florida.Requirements.SupportsIndependentStudy);

        Assert.Equal("North Carolina", northCarolina.State);
        Assert.True(northCarolina.Requirements.AllowsDistanceEducation);
        Assert.True(northCarolina.Requirements.RequiresInstructorCameraVisibilityForSynchronousLearning);
        Assert.True(northCarolina.Requirements.RequiresAttendanceTrackingForSynchronousCourses);

        Assert.Equal("South Carolina", southCarolina.State);
        Assert.True(southCarolina.Requirements.AllowsDistanceEducation);
        Assert.True(southCarolina.Requirements.RequiresProviderDisclosure);
        Assert.True(southCarolina.Requirements.RequiresInstructorCameraVisibilityForSynchronousLearning);
        Assert.True(southCarolina.Requirements.RequiresAttendanceTrackingForSynchronousCourses);
        Assert.Equal(70, southCarolina.Requirements.MinimumPassingPercent);

        Assert.Equal("Georgia", georgia.State);
        Assert.True(georgia.Requirements.AllowsDistanceEducation);
        Assert.True(georgia.Requirements.RequiresAttendanceTrackingForSynchronousCourses);
        Assert.Equal(100, georgia.Requirements.MinimumAttendancePercent);
    }

    [Fact]
    public void SouthCarolinaRenewalPolicies_UseBiennialCeRulesByLicenseCategory()
    {
        var associate = StateLicenseRenewalPolicyCatalog.Get("South Carolina", "Associate");
        var broker = StateLicenseRenewalPolicyCatalog.Get("South Carolina", "Broker");
        var bic = StateLicenseRenewalPolicyCatalog.Get("South Carolina", "Broker-in-Charge");

        Assert.Equal(2, associate.RenewalCycleYears);
        Assert.Equal(4, associate.RenewalWindowStartMonth);
        Assert.Equal(6, associate.RenewalWindowEndMonth);
        Assert.False(associate.HasPostlicensingDeadline);
        Assert.Equal(10m, associate.ContinuingEducation.TotalHours);
        Assert.Equal(4m, associate.ContinuingEducation.CoreHours);
        Assert.Equal(6m, associate.ContinuingEducation.ElectiveHours);
        Assert.Equal(0m, associate.ContinuingEducation.BicHours);

        Assert.Equal(associate.ContinuingEducation.TotalHours, broker.ContinuingEducation.TotalHours);
        Assert.Equal(associate.ContinuingEducation.CoreHours, broker.ContinuingEducation.CoreHours);
        Assert.Equal(associate.ContinuingEducation.ElectiveHours, broker.ContinuingEducation.ElectiveHours);
        Assert.False(broker.HasPostlicensingDeadline);

        Assert.Equal(10m, bic.ContinuingEducation.TotalHours);
        Assert.Equal(4m, bic.ContinuingEducation.CoreHours);
        Assert.Equal(2m, bic.ContinuingEducation.ElectiveHours);
        Assert.Equal(4m, bic.ContinuingEducation.BicHours);
        Assert.False(bic.HasPostlicensingDeadline);
    }

    [Fact]
    public void GeorgiaPoliciesAndCourseCatalog_ReflectGrecRequirements()
    {
        var salesperson = StateLicenseRenewalPolicyCatalog.Get("Georgia", "Salesperson");
        var broker = StateLicenseRenewalPolicyCatalog.Get("Georgia", "Broker");

        Assert.Equal(4, salesperson.RenewalCycleYears);
        Assert.Equal(36m, salesperson.ContinuingEducation.TotalHours);
        Assert.Equal(3m, salesperson.ContinuingEducation.LicenseLawHours);
        Assert.True(salesperson.HasPostlicensingDeadline);
        Assert.Equal(18m, broker.ContinuingEducation.BrokerCeHours);

        Assert.Contains(GeorgiaCourseCatalog.All, course => course.Title == "Georgia Salesperson Prelicense" && course.RequiredHours == 75);
        Assert.Contains(GeorgiaCourseCatalog.All, course => course.Title == "Georgia Salesperson Postlicense" && course.RequiredHours == 25 && course.HasFirstYearDeadline);
        Assert.Contains(GeorgiaCourseCatalog.All, course => course.Title == "Georgia Broker Prelicense" && course.RequiredHours == 60);
        Assert.Contains(GeorgiaCourseCatalog.All, course => course.Title == "Georgia Community Association Manager Prelicense" && course.RequiredHours == 25);
        Assert.Contains(GeorgiaCourseCatalog.All, course => course.Title == "Georgia Continuing Education" && course.RequiredHours == 36);
    }

    [Fact]
    public void NewStateProfiles_ExposeConfiguredDistanceDeliverySupport()
    {
        foreach (var state in new[] { "Arkansas", "Connecticut", "Hawaii", "Illinois", "New York", "Texas", "Virginia", "West Virginia" })
        {
            var profile = DistanceLearningProfileCatalog.Get(state);

            Assert.True(profile.Requirements.AllowsDistanceEducation);
            Assert.True(profile.Requirements.RequiresProviderDisclosure);
            Assert.Contains(CourseDeliveryMethods.DistanceEducation, profile.AllowedDeliveryMethods);
        }
    }

    [Fact]
    public void StateRenewalPolicies_PreserveHighConfidenceHourRules()
    {
        var arkansas = StateLicenseRenewalPolicyCatalog.Get("Arkansas", "Salesperson");
        var hawaii = StateLicenseRenewalPolicyCatalog.Get("Hawaii", "Broker");
        var newYork = StateLicenseRenewalPolicyCatalog.Get("New York", "Broker");
        var virginia = StateLicenseRenewalPolicyCatalog.Get("Virginia", "Broker");

        Assert.Equal(7m, arkansas.ContinuingEducation.TotalHours);
        Assert.Equal(20m, hawaii.ContinuingEducation.TotalHours);
        Assert.Equal(6m, hawaii.ContinuingEducation.CoreHours);
        Assert.Equal(14m, hawaii.ContinuingEducation.ElectiveHours);
        Assert.Equal(22.5m, newYork.ContinuingEducation.TotalHours);
        Assert.Equal(24m, virginia.ContinuingEducation.TotalHours);
        Assert.Equal(19m, virginia.ContinuingEducation.CoreHours);
    }

    [Fact]
    public void WestVirginiaRenewalPolicies_UseJune30ForSupportedLicenseCategories()
    {
        var beforeRenewal = new DateOnly(2026, 6, 29);
        var onRenewal = new DateOnly(2026, 6, 30);
        var afterRenewal = new DateOnly(2026, 7, 1);

        Assert.True(StateLicenseRenewalPolicyCatalog.TryCalculateNextRenewalDate(
            "WV", "Salesperson", beforeRenewal, out var beforeDate));
        Assert.Equal(new DateOnly(2026, 6, 30), beforeDate);
        Assert.True(StateLicenseRenewalPolicyCatalog.TryCalculateNextRenewalDate(
            "West Virginia", "Associate Broker", onRenewal, out var onDate));
        Assert.Equal(onRenewal, onDate);
        Assert.True(StateLicenseRenewalPolicyCatalog.TryCalculateNextRenewalDate(
            "West Virginia", "Broker", afterRenewal, out var afterDate));
        Assert.Equal(new DateOnly(2027, 6, 30), afterDate);
        Assert.False(StateLicenseRenewalPolicyCatalog.TryCalculateNextRenewalDate(
            "West Virginia", "Unknown License", afterRenewal, out _));
    }

    [Fact]
    public void StateCourseCatalog_ContainsDocumentedCourseDefinitions()
    {
        Assert.Contains(StateCourseCatalog.ForState("Arkansas"), course => course.RequiredHours == 60 && course.CourseType == CourseComplianceTypes.Prelicensing);
        Assert.Contains(StateCourseCatalog.ForState("Connecticut"), course => course.RequiredHours == 60);
        Assert.Contains(StateCourseCatalog.ForState("Illinois"), course => course.RequiredHours == 75);
        Assert.Contains(StateCourseCatalog.ForState("New York"), course => course.RequiredHours == 30);
        Assert.Contains(StateCourseCatalog.ForState("Texas"), course => course.RequiredHours == 180);
        Assert.Contains(StateCourseCatalog.ForState("Virginia"), course => course.HasFirstYearDeadline && course.RequiredHours == 30);
        Assert.Contains(StateCourseCatalog.ForState("West Virginia"), course => course.RequiredHours == 90);
    }

    [Fact]
    public void Validate_AllowsCompleteFloridaDistanceCourse()
    {
        var course = CreateCourse("Florida");
        course.Units.Add(new Unit
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
