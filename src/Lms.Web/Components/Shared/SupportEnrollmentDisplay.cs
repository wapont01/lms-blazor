using Lms.Domain.Entities;

namespace Lms.Web.Components.Shared;

public sealed record SupportEnrollmentCourseDisplay(string Title, string SourceLabel, string SourceCssClass);

public static class SupportEnrollmentDisplay
{
	public static List<SupportEnrollmentCourseDisplay> BuildCourseDisplays(IEnumerable<Enrollment> enrollments, Guid? brokerUserId = null)
	{
		return enrollments
			.Where(enrollment => enrollment.Course is not null && !string.IsNullOrWhiteSpace(enrollment.Course.Title))
			.Select(enrollment => new SupportEnrollmentCourseDisplay(
				enrollment.Course!.Title,
				GetSourceLabel(enrollment, brokerUserId),
				GetSourceCssClass(enrollment, brokerUserId)))
			.GroupBy(item => new { item.Title, item.SourceLabel, item.SourceCssClass })
			.Select(group => group.First())
			.OrderBy(item => item.Title)
			.ToList();
	}

	private static string GetSourceLabel(Enrollment enrollment, Guid? brokerUserId)
	{
		if (string.Equals(enrollment.EnrollmentSource, "LearnerPurchase", StringComparison.Ordinal))
		{
			return "Learner enrolled";
		}

		if (string.Equals(enrollment.EnrollmentSource, "BrokerSponsored", StringComparison.Ordinal))
		{
			if (!brokerUserId.HasValue || enrollment.SponsoredByBrokerUserId == brokerUserId)
			{
				return "Broker enrolled";
			}

			return "Broker enrolled (other broker)";
		}

		return "Enrollment";
	}

	private static string GetSourceCssClass(Enrollment enrollment, Guid? brokerUserId)
	{
		if (string.Equals(enrollment.EnrollmentSource, "LearnerPurchase", StringComparison.Ordinal))
		{
			return "source-badge source-badge-learner";
		}

		if (string.Equals(enrollment.EnrollmentSource, "BrokerSponsored", StringComparison.Ordinal))
		{
			if (!brokerUserId.HasValue || enrollment.SponsoredByBrokerUserId == brokerUserId)
			{
				return "source-badge source-badge-broker";
			}

			return "source-badge source-badge-neutral";
		}

		return "source-badge source-badge-neutral";
	}
}