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
            },
            ["South Carolina"] = new()
            {
                State = "South Carolina",
                DeliveryMethod = CourseDeliveryMethods.DistanceEducation,
                AllowedDeliveryMethods =
                [
                    CourseDeliveryMethods.DistanceEducation,
                    CourseDeliveryMethods.SynchronousDistanceLearning,
                    CourseDeliveryMethods.Blended,
                    CourseDeliveryMethods.InPerson
                ],
                AllowedProviderTypes = ["Commission-approved education provider"],
                Requirements = new DistanceLearningRequirements
                {
                    AllowsDistanceEducation = true,
                    AllowsBlendedDelivery = true,
                    SupportsIndependentStudy = true,
                    RequiresProviderDisclosure = true,
                    RequiresInstructorCameraVisibilityForSynchronousLearning = true,
                    RequiresAttendanceTrackingForSynchronousCourses = true,
                    MinimumPassingPercent = 70,
                    MinimumAttendancePercent = 80
                },
                Notes = "South Carolina requires approved distance-learning providers and ARELLO or IDECC certification, active participation, strict attendance, and a 70 percent proctored prelicensing final where applicable."
            },
            ["Georgia"] = new()
            {
                State = "Georgia",
                DeliveryMethod = CourseDeliveryMethods.DistanceEducation,
                AllowedDeliveryMethods =
                [
                    CourseDeliveryMethods.DistanceEducation,
                    CourseDeliveryMethods.SynchronousDistanceLearning,
                    CourseDeliveryMethods.Blended,
                    CourseDeliveryMethods.InPerson
                ],
                AllowedProviderTypes = ["GREC-approved real estate school", "Approved education provider"],
                Requirements = new DistanceLearningRequirements
                {
                    AllowsDistanceEducation = true,
                    AllowsBlendedDelivery = true,
                    SupportsIndependentStudy = true,
                    RequiresProviderDisclosure = true,
                    RequiresInstructorCameraVisibilityForSynchronousLearning = true,
                    RequiresAttendanceTrackingForSynchronousCourses = true,
                    MinimumPassingPercent = 70,
                    MinimumAttendancePercent = 100
                },
                Notes = "Georgia requires an approved school, full attendance for in-class credit, required exercises or examinations, and documented course completion. Computer-based courses must satisfy GREC notice and completion requirements."
            },
            ["Arkansas"] = CreateProfile("Arkansas", "AREC-approved education provider", minimumAttendancePercent: 0, minimumPassingPercent: 0, notes: "Arkansas course approval sources identify classroom and distance delivery options; course-specific examination and attendance rules must be applied from the approved course record."),
            ["Connecticut"] = CreateProfile("Connecticut", "DCP-registered real estate school", minimumAttendancePercent: 0, minimumPassingPercent: 0, notes: "Connecticut requires school and course registration; asynchronous CE courses require ARELLO certification, and live-virtual delivery has separate submission requirements."),
            ["Hawaii"] = CreateProfile("Hawaii", "Commission-registered CE provider", minimumAttendancePercent: 0, minimumPassingPercent: 0, notes: "Hawaii permits virtual education and requires Commission-approved providers and courses; completion reporting and license-status verification remain provider responsibilities."),
            ["Illinois"] = CreateProfile("Illinois", "IDFPR-approved real estate education provider", minimumAttendancePercent: 0, minimumPassingPercent: 0, notes: "Illinois supports classroom, interactive webinar, online distance learning, and home-study formats subject to course-specific approval and proctor rules."),
            ["New York"] = CreateProfile("New York", "Department of State-approved school", minimumAttendancePercent: 0, minimumPassingPercent: 0, notes: "New York course approval distinguishes classroom, asynchronous distance learning, and live distance education; distance-learning examinations require New York examination locations."),
            ["Texas"] = CreateProfile("Texas", "TREC-approved education provider", minimumAttendancePercent: 0, minimumPassingPercent: 0, notes: "Texas distance courses require identity authentication, clock-hour verification, inactivity controls, interactive elements where required, and proctored closed-book final examinations for applicable courses."),
            ["Virginia"] = CreateProfile("Virginia", "Virginia Real Estate Board-approved school", minimumAttendancePercent: 0, minimumPassingPercent: 0, notes: "Virginia permits classroom and distance learning for documented real-estate education courses; appraisal-board delivery requirements are tracked separately."),
            ["West Virginia"] = CreateProfile("West Virginia", "West Virginia Real Estate Commission-approved provider", minimumAttendancePercent: 90, minimumPassingPercent: 70, notes: "West Virginia requires approved providers and courses, 90 percent attendance for prelicense completion, and a 70 percent subject-area threshold; asynchronous courses require ARELLO certification where applicable.")
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

    private static DistanceLearningProfile CreateProfile(
        string state,
        string providerType,
        int minimumAttendancePercent,
        int minimumPassingPercent,
        string notes)
    {
        return new DistanceLearningProfile
        {
            State = state,
            DeliveryMethod = CourseDeliveryMethods.DistanceEducation,
            AllowedDeliveryMethods =
            [
                CourseDeliveryMethods.DistanceEducation,
                CourseDeliveryMethods.SynchronousDistanceLearning,
                CourseDeliveryMethods.Blended,
                CourseDeliveryMethods.InPerson
            ],
            AllowedProviderTypes = [providerType],
            Requirements = new DistanceLearningRequirements
            {
                AllowsDistanceEducation = true,
                AllowsBlendedDelivery = true,
                SupportsIndependentStudy = true,
                RequiresProviderDisclosure = true,
                RequiresInstructorCameraVisibilityForSynchronousLearning = false,
                RequiresAttendanceTrackingForSynchronousCourses = false,
                MinimumPassingPercent = minimumPassingPercent,
                MinimumAttendancePercent = minimumAttendancePercent
            },
            Notes = notes
        };
    }
}