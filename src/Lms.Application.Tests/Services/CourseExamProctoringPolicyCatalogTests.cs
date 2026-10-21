using Lms.Application.Services;
using Lms.Domain.Entities;
using Xunit;

namespace Lms.Application.Tests.Services;

public class CourseExamProctoringPolicyCatalogTests
{
    [Fact]
    public void Resolve_NorthCarolinaDistancePrelicensing_RequiresIdentityAndClosedBookControls()
    {
        var policy = CourseExamProctoringPolicyCatalog.Resolve(new Course
        {
            State = "NC",
            ComplianceType = CourseComplianceTypes.Prelicensing,
            DeliveryMethod = CourseDeliveryMethods.DistanceEducation
        });

        Assert.Equal(CourseExamPolicyStatus.Verified, policy.Status);
        Assert.Equal(75, policy.MinimumPassingPercent);
        Assert.True(policy.ClosedBookRequired);
        Assert.True(policy.IdentityVerificationRequired);
        Assert.Contains("security controls", policy.Requirement, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("58H .0207", policy.Citation);
    }

    [Fact]
    public void Resolve_AlabamaDistancePostlicensing_UsesCommissionApprovalForRemoteProctoring()
    {
        var policy = CourseExamProctoringPolicyCatalog.Resolve(new Course
        {
            State = "Alabama",
            ComplianceType = CourseComplianceTypes.Postlicensing,
            DeliveryMethod = CourseDeliveryMethods.DistanceEducation
        });

        Assert.Equal(CourseExamPolicyStatus.Verified, policy.Status);
        Assert.Equal(70, policy.MinimumPassingPercent);
        Assert.True(policy.RemoteProctoringAllowed);
        Assert.True(policy.RemoteProctoringRequiresCommissionApproval);
    }

    [Fact]
    public void Resolve_GeorgiaContinuingEducation_ReportsConditionalProctoring()
    {
        var policy = CourseExamProctoringPolicyCatalog.Resolve(new Course
        {
            State = "Georgia",
            ComplianceType = CourseComplianceTypes.ContinuingEducation,
            DeliveryMethod = CourseDeliveryMethods.DistanceEducation
        });

        Assert.Equal(CourseExamPolicyStatus.Conditional, policy.Status);
        Assert.Contains("when a CE final examination is required", policy.Requirement, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Resolve_SouthCarolinaPrelicensing_ExposesVirtualWaiverEndDate()
    {
        var policy = CourseExamProctoringPolicyCatalog.Resolve(new Course
        {
            State = "South Carolina",
            ComplianceType = CourseComplianceTypes.Prelicensing,
            DeliveryMethod = CourseDeliveryMethods.DistanceEducation
        });

        Assert.Equal(CourseExamPolicyStatus.Verified, policy.Status);
        Assert.Equal(new DateOnly(2027, 6, 30), policy.EffectiveThrough);
        Assert.True(policy.RemoteProctoringAllowed);
        Assert.Equal(CourseExamPolicyEvidenceKind.CommissionNotice, policy.EvidenceKind);
    }

    [Fact]
    public void Resolve_IllinoisSelfPacedDistanceCourse_RequiresReviewBecauseDeliveryTypesDiffer()
    {
        var policy = CourseExamProctoringPolicyCatalog.Resolve(new Course
        {
            State = "Illinois",
            ComplianceType = CourseComplianceTypes.ContinuingEducation,
            DeliveryMethod = CourseDeliveryMethods.DistanceEducation
        });

        Assert.Equal(CourseExamPolicyStatus.NeedsReview, policy.Status);
        Assert.Contains("does not yet distinguish", policy.ReviewNote, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Resolve_UncatalogedJurisdiction_DoesNotAssumeNoProctoringRequirement()
    {
        var policy = CourseExamProctoringPolicyCatalog.Resolve(new Course
        {
            State = "Florida",
            ComplianceType = CourseComplianceTypes.ContinuingEducation,
            DeliveryMethod = CourseDeliveryMethods.DistanceEducation
        });

        Assert.Equal(CourseExamPolicyStatus.NeedsReview, policy.Status);
        Assert.Contains("No verified course-exam proctoring rule", policy.Requirement, StringComparison.OrdinalIgnoreCase);
        Assert.Null(policy.SourceDocument);
    }
}