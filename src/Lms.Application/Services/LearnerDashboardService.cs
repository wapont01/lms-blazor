using Lms.Domain.Entities;

namespace Lms.Application.Services;

public interface ILearnerDashboardService
{
    Task<LearnerDashboardSelection> ResolveLearnerContextAsync(Guid viewerUserId, bool isAdminViewer, Guid? requestedLearnerId);
    Task<LearnerDashboardResult> GetDashboardAsync(Guid viewerUserId, bool isAdminViewer, Guid? requestedLearnerId);
}

public sealed record LearnerDashboardSelection(
    LearnerDashboardContext Context,
    List<LearnerDashboardLearnerOption> LearnerOptions);

public sealed record LearnerDashboardResult(
    LearnerDashboardContext Context,
    List<LearnerDashboardLearnerOption> LearnerOptions,
    List<LearnerDashboardCourseCard> Courses);

public sealed record LearnerDashboardContext(
    bool IsAdminViewer,
    Guid ViewerUserId,
    Guid? LearnerUserId,
    string DisplayName,
    string Email,
    int EnrolledCourseCount,
    int CompletedCourseCount);

public sealed record LearnerDashboardLearnerOption(Guid UserId, string DisplayName, string Email);

public sealed record LearnerDashboardCourseCard(
    Guid CourseId,
    string Title,
    string Description,
    decimal DurationHours,
    decimal ProgressPercent,
    string Status,
    Guid? CertificateId);

public sealed class LearnerDashboardService : ILearnerDashboardService
{
    private readonly IEnrollmentService _enrollmentService;
    private readonly IAssessmentService _assessmentService;
    private readonly IUserAccountService _userAccountService;

    public LearnerDashboardService(
        IEnrollmentService enrollmentService,
        IAssessmentService assessmentService,
        IUserAccountService userAccountService)
    {
        _enrollmentService = enrollmentService;
        _assessmentService = assessmentService;
        _userAccountService = userAccountService;
    }

    public async Task<LearnerDashboardResult> GetDashboardAsync(Guid viewerUserId, bool isAdminViewer, Guid? requestedLearnerId)
    {
        var resolution = await ResolveLearnerSelectionAsync(viewerUserId, isAdminViewer, requestedLearnerId);

        if (resolution.Learner is null)
        {
            return new LearnerDashboardResult(resolution.Context, resolution.LearnerOptions, []);
        }

        return await BuildDashboardAsync(resolution.Viewer, resolution.Learner, resolution.Context.IsAdminViewer, resolution.LearnerOptions);
    }

    public async Task<LearnerDashboardSelection> ResolveLearnerContextAsync(Guid viewerUserId, bool isAdminViewer, Guid? requestedLearnerId)
    {
        var resolution = await ResolveLearnerSelectionAsync(viewerUserId, isAdminViewer, requestedLearnerId);
        return new LearnerDashboardSelection(resolution.Context, resolution.LearnerOptions);
    }

    private async Task<LearnerDashboardResolution> ResolveLearnerSelectionAsync(Guid viewerUserId, bool isAdminViewer, Guid? requestedLearnerId)
    {
        var viewer = await _userAccountService.GetByIdAsync(viewerUserId)
            ?? throw new InvalidOperationException("Viewer account was not found.");

        if (!isAdminViewer)
        {
            return new LearnerDashboardResolution(
                viewer,
                viewer,
                new LearnerDashboardContext(false, viewer.Id, viewer.Id, viewer.DisplayName, viewer.Email, 0, 0),
                []);
        }

        var learnerUsers = await GetActiveLearnersAsync();
        var learnerOptions = learnerUsers
            .Select(learner => new LearnerDashboardLearnerOption(learner.Id, learner.DisplayName, learner.Email))
            .ToList();

        UserAccount? selectedLearner = null;
        if (requestedLearnerId.HasValue)
        {
            selectedLearner = learnerUsers.FirstOrDefault(learner => learner.Id == requestedLearnerId.Value);
        }

        selectedLearner ??= await ResolveDefaultLearnerAsync(learnerUsers);

        if (selectedLearner is null)
        {
            return new LearnerDashboardResolution(
                viewer,
                null,
                new LearnerDashboardContext(true, viewer.Id, null, string.Empty, string.Empty, 0, 0),
                learnerOptions);
        }

        return new LearnerDashboardResolution(
            viewer,
            selectedLearner,
            new LearnerDashboardContext(true, viewer.Id, selectedLearner.Id, selectedLearner.DisplayName, selectedLearner.Email, 0, 0),
            learnerOptions);
    }

    private async Task<List<UserAccount>> GetActiveLearnersAsync()
    {
        var users = await _userAccountService.GetAllAsync();
        return users
            .Where(user => user.IsActive && string.Equals(user.Role, "Learner", StringComparison.OrdinalIgnoreCase))
            .OrderBy(user => user.DisplayName)
            .ThenBy(user => user.Email)
            .ToList();
    }

    private async Task<UserAccount?> ResolveDefaultLearnerAsync(List<UserAccount> learnerUsers)
    {
        foreach (var learner in learnerUsers)
        {
            var learnerEnrollments = await _enrollmentService.GetEnrollmentsForLearnerAsync(learner.Id);
            if (learnerEnrollments.Count > 0)
            {
                return learner;
            }
        }

        return learnerUsers.FirstOrDefault();
    }

    private async Task<LearnerDashboardResult> BuildDashboardAsync(
        UserAccount viewer,
        UserAccount learner,
        bool isAdminViewer,
        List<LearnerDashboardLearnerOption> learnerOptions)
    {
        var enrollments = await _enrollmentService.GetEnrollmentsForLearnerAsync(learner.Id);
        var certificates = await _enrollmentService.GetCertificatesForLearnerAsync(learner.Id);
        var certificateIdsByCourseId = certificates.ToDictionary(certificate => certificate.CourseId, certificate => certificate.CertificateId);
        var courses = new List<LearnerDashboardCourseCard>(enrollments.Count);

        foreach (var enrollment in enrollments)
        {
            var eligibility = await _assessmentService.GetEligibilityAsync(enrollment.CourseId, learner.Id);
            var progress = enrollment.ProgressPercent;
            var status = GetBaseStatus(enrollment);
            Guid? certificateId = null;

            if (eligibility.HasPassed)
            {
                progress = 100m;
                status = "Completed";
            }
            else if (eligibility.RequiresAssessment && progress >= 100m)
            {
                status = "Assessment Pending";
            }

            if (certificateIdsByCourseId.TryGetValue(enrollment.CourseId, out var resolvedCertificateId))
            {
                certificateId = resolvedCertificateId;
            }

            courses.Add(new LearnerDashboardCourseCard(
                enrollment.CourseId,
                enrollment.Course?.Title ?? string.Empty,
                enrollment.Course?.Description ?? string.Empty,
                enrollment.Course?.DurationHours ?? 0m,
                progress,
                status,
                certificateId));
        }

        var completedCourseCount = courses.Count(course => string.Equals(course.Status, "Completed", StringComparison.OrdinalIgnoreCase));
        var context = new LearnerDashboardContext(
            isAdminViewer,
            viewer.Id,
            learner.Id,
            learner.DisplayName,
            learner.Email,
            courses.Count,
            completedCourseCount);

        return new LearnerDashboardResult(context, learnerOptions, courses);
    }

    private static string GetBaseStatus(Enrollment enrollment)
    {
        if (enrollment.Completed)
        {
            return "Completed";
        }

        return enrollment.ProgressPercent <= 0m ? "Not Started" : "In Progress";
    }

    private sealed record LearnerDashboardResolution(
        UserAccount Viewer,
        UserAccount? Learner,
        LearnerDashboardContext Context,
        List<LearnerDashboardLearnerOption> LearnerOptions);
}