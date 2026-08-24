namespace Lms.Application.Services;

public static class AssessmentCompletionTimestampResolver
{
    public static DateTime GetCompletionTimestampUtc(
        DateTime? persistedSubmissionUtc = null,
        DateTime? queryTimestampUtc = null,
        DateTime? fallbackUtc = null)
    {
        if (TryNormalizeUtc(persistedSubmissionUtc, out var persistedUtc))
        {
            return persistedUtc;
        }

        if (TryNormalizeUtc(queryTimestampUtc, out var queryUtc))
        {
            return queryUtc;
        }

        if (TryNormalizeUtc(fallbackUtc, out var fallbackUtcValue))
        {
            return fallbackUtcValue;
        }

        return DateTime.UtcNow;
    }

    private static bool TryNormalizeUtc(DateTime? candidate, out DateTime normalizedUtc)
    {
        normalizedUtc = default;

        if (!candidate.HasValue)
        {
            return false;
        }

        var value = candidate.Value;

        normalizedUtc = value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };

        return true;
    }
}
