using System.ComponentModel.DataAnnotations;

namespace Lms.Web.Components.Shared.Admin;

public sealed class AdminCreateUserModel
{
    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required]
    public string DisplayName { get; set; } = string.Empty;

    [Required]
    [MinLength(10)]
    public string Password { get; set; } = string.Empty;

    [Required]
    public string Role { get; set; } = "Learner";
}
