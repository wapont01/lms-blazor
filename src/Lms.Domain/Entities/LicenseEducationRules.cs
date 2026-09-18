namespace Lms.Domain.Entities;

public static class LicenseEducationRules
{
    public const int PostlicensingPeriodMonths = 18;
    public const int NorthCarolinaCeDueMonth = 6;
    public const int NorthCarolinaCeDueDay = 10;

    public static bool IsNorthCarolina(string? state)
    {
        return string.Equals(state, "North Carolina", StringComparison.OrdinalIgnoreCase)
            || string.Equals(state, "NC", StringComparison.OrdinalIgnoreCase);
    }

    public static DateTime GetNorthCarolinaCeDueDate(DateTime asOfUtc)
    {
        var dueDate = new DateTime(asOfUtc.Year, NorthCarolinaCeDueMonth, NorthCarolinaCeDueDay, 23, 59, 59, DateTimeKind.Utc);
        return asOfUtc <= dueDate ? dueDate : dueDate.AddYears(1);
    }

    public static LicenseEducationSnapshot Calculate(
        DateTime? initialLicensureDate,
        DateTime? postlicensingCompletedAt,
        DateTime? continuingEducationDueDate,
        DateTime? licenseRenewalDueDate,
        DateTime asOfUtc)
    {
        if (!initialLicensureDate.HasValue)
        {
            return new LicenseEducationSnapshot(
                "Unknown",
                null,
                null,
                continuingEducationDueDate ?? licenseRenewalDueDate,
                null,
                false);
        }

        var initialDate = initialLicensureDate.Value;
        var expectedPostlicensingDate = initialDate.AddMonths(PostlicensingPeriodMonths);
        var elapsedDays = Math.Max(0, (int)Math.Floor((asOfUtc - initialDate).TotalDays));
        var isPostlicensing = !postlicensingCompletedAt.HasValue;

        return new LicenseEducationSnapshot(
            isPostlicensing ? "Post-license" : "Continuing education",
            initialDate,
            expectedPostlicensingDate,
            continuingEducationDueDate ?? licenseRenewalDueDate,
            elapsedDays,
            isPostlicensing && asOfUtc > expectedPostlicensingDate);
    }

    public static LicenseEducationSnapshot CalculateForState(
        string? state,
        DateTime? initialLicensureDate,
        DateTime? postlicensingCompletedAt,
        DateTime? continuingEducationDueDate,
        DateTime? licenseRenewalDueDate,
        DateTime asOfUtc)
    {
        var snapshot = Calculate(
            initialLicensureDate,
            postlicensingCompletedAt,
            continuingEducationDueDate,
            licenseRenewalDueDate,
            asOfUtc);

        if (!IsNorthCarolina(state) || snapshot.ExpectedContinuingEducationDate.HasValue)
        {
            return snapshot;
        }

        return snapshot with { ExpectedContinuingEducationDate = GetNorthCarolinaCeDueDate(asOfUtc) };
    }
}

public sealed record LicenseEducationSnapshot(
    string Phase,
    DateTime? InitialLicensureDate,
    DateTime? ExpectedPostlicensingDate,
    DateTime? ExpectedContinuingEducationDate,
    int? ElapsedDays,
    bool IsPostlicensingOverdue);
