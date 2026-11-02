namespace Lms.Web.Services;

public static class RegulatoryStatusDisplay
{
    public static string Format(string state, string value)
    {
        if (!string.Equals(state, "Florida", StringComparison.OrdinalIgnoreCase))
            return value;
        var parts = value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 2 && parts[0].Equals("Current", StringComparison.OrdinalIgnoreCase))
        {
            if (parts[1].Equals("Active", StringComparison.OrdinalIgnoreCase)) return "Active";
            if (parts[1].Equals("Inactive", StringComparison.OrdinalIgnoreCase)) return "Inactive";
        }
        return value.Trim().Equals("Current", StringComparison.OrdinalIgnoreCase)
            ? "Current (activity unspecified)" : value;
    }
}
