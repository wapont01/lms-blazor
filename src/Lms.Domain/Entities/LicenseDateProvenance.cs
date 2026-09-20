namespace Lms.Domain.Entities;

public static class LicenseDateProvenance
{
    public const string Unknown = "Unknown";
    public const string Derived = "Derived";
    public const string Authoritative = "Authoritative";

    public static bool IsValid(string? value) => value is Unknown or Derived or Authoritative;

    public static bool IsAuthoritative(string? value) => string.Equals(value, Authoritative, StringComparison.OrdinalIgnoreCase);
}
