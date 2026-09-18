namespace Lms.Domain.Entities;

public sealed class DistanceLearningRequirements
{
    public bool AllowsDistanceEducation { get; init; }
    public bool AllowsBlendedDelivery { get; init; }
    public bool SupportsIndependentStudy { get; init; }
    public bool RequiresProviderDisclosure { get; init; }
    public bool RequiresInstructorCameraVisibilityForSynchronousLearning { get; init; }
    public bool RequiresAttendanceTrackingForSynchronousCourses { get; init; }
    public int MinimumPassingPercent { get; init; }
    public int MinimumAttendancePercent { get; init; }
}

public sealed class DistanceLearningProfile
{
    public string State { get; init; } = string.Empty;
    public string DeliveryMethod { get; init; } = CourseDeliveryMethods.DistanceEducation;
    public string[] AllowedDeliveryMethods { get; init; } = [];
    public string[] AllowedProviderTypes { get; init; } = [];
    public DistanceLearningRequirements Requirements { get; init; } = new();
    public string? Notes { get; init; }
}

public static class DistanceLearningProfileCatalog
{
    private static readonly IReadOnlyDictionary<string, DistanceLearningProfile> Profiles =
        new Dictionary<string, DistanceLearningProfile>(StringComparer.OrdinalIgnoreCase)
        {
            ["Florida"] = new()
            {
                State = "Florida",
                DeliveryMethod = CourseDeliveryMethods.DistanceEducation,
                AllowedDeliveryMethods =
                [
                    CourseDeliveryMethods.DistanceEducation,
                    CourseDeliveryMethods.SynchronousDistanceLearning,
                    CourseDeliveryMethods.Blended
                ],
                AllowedProviderTypes = ["Approved education provider", "Proprietary school", "Distance provider"],
                Requirements = new DistanceLearningRequirements
                {
                    AllowsDistanceEducation = true,
                    AllowsBlendedDelivery = true,
                    SupportsIndependentStudy = true,
                    RequiresProviderDisclosure = true,
                    RequiresInstructorCameraVisibilityForSynchronousLearning = false,
                    RequiresAttendanceTrackingForSynchronousCourses = false,
                    MinimumPassingPercent = 75,
                    MinimumAttendancePercent = 80
                },
                Notes = "Florida permits distance education and blended formats when the provider maintains appropriate disclosures and the course meets its required instructional and completion standards."
            },
            ["North Carolina"] = new()
            {
                State = "North Carolina",
                DeliveryMethod = CourseDeliveryMethods.DistanceEducation,
                AllowedDeliveryMethods =
                [
                    CourseDeliveryMethods.DistanceEducation,
                    CourseDeliveryMethods.SynchronousDistanceLearning,
                    CourseDeliveryMethods.Blended,
                    CourseDeliveryMethods.InPerson
                ],
                AllowedProviderTypes = ["Approved education provider", "Public education provider", "Private education provider"],
                Requirements = new DistanceLearningRequirements
                {
                    AllowsDistanceEducation = true,
                    AllowsBlendedDelivery = true,
                    SupportsIndependentStudy = true,
                    RequiresProviderDisclosure = true,
                    RequiresInstructorCameraVisibilityForSynchronousLearning = true,
                    RequiresAttendanceTrackingForSynchronousCourses = true,
                    MinimumPassingPercent = 75,
                    MinimumAttendancePercent = 80
                },
                Notes = "North Carolina requires stricter synchronous and attendance controls, including visible participation tracking for live distance learning."
            }
        };

    public static IReadOnlyCollection<DistanceLearningProfile> All => Profiles.Values.ToArray();

    public static DistanceLearningProfile Get(string state)
    {
        if (string.IsNullOrWhiteSpace(state))
        {
            throw new ArgumentException("State is required.", nameof(state));
        }

        if (Profiles.TryGetValue(state, out var profile))
        {
            return profile;
        }

        throw new ArgumentException($"No distance-learning profile exists for state '{state}'.", nameof(state));
    }

    public static bool TryGet(string state, out DistanceLearningProfile? profile)
    {
        return Profiles.TryGetValue(state, out profile);
    }

    public static string MapNcLicenseTypeToFlorida(string? northCarolinaLicenseType)
    {
        return string.Equals(northCarolinaLicenseType, "PB", StringComparison.OrdinalIgnoreCase)
            ? "Sales Associate"
            : string.Equals(northCarolinaLicenseType, "B", StringComparison.OrdinalIgnoreCase)
                ? "Broker"
                : ToFloridaLicenseCategory(northCarolinaLicenseType);
    }

    public static string ToFloridaLicenseCategory(string? floridaLicenseType)
    {
        return string.Equals(floridaLicenseType, "SL", StringComparison.OrdinalIgnoreCase)
            ? "Sales Associate"
            : string.Equals(floridaLicenseType, "BL", StringComparison.OrdinalIgnoreCase)
                ? "Broker Associate"
                : string.Equals(floridaLicenseType, "BK", StringComparison.OrdinalIgnoreCase)
                    ? "Broker"
                : string.Empty;
    }

    public static string ToDisplayLicenseCategory(string? state, string? licenseType)
    {
        if (string.Equals(state, "Florida", StringComparison.OrdinalIgnoreCase)
            || string.Equals(state, "FL", StringComparison.OrdinalIgnoreCase))
        {
            return ToFloridaLicenseCategory(licenseType);
        }

        if (string.Equals(state, "North Carolina", StringComparison.OrdinalIgnoreCase)
            || string.Equals(state, "NC", StringComparison.OrdinalIgnoreCase))
        {
            return string.Equals(licenseType, "PB", StringComparison.OrdinalIgnoreCase)
                ? "Provisional Broker"
                : string.Equals(licenseType, "B", StringComparison.OrdinalIgnoreCase)
                    ? "Broker"
                    : string.Empty;
        }

        return string.Empty;
    }
}