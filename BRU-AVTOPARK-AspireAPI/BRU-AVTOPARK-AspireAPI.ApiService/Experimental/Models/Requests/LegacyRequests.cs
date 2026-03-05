using System.ComponentModel.DataAnnotations;

namespace BRU_AVTOPARK.Models.Requests;

/// <summary>
/// Legacy login request for Avalonia UI client - uses "Login" instead of "Username".
/// </summary>
public sealed record LegacyLoginRequest
{
    [Required]
    public string Login { get; init; } = string.Empty;

    [Required]
    public string Password { get; init; } = string.Empty;
}

/// <summary>
/// Legacy registration request for Avalonia UI client - uses "Login" instead of "Username".
/// </summary>
public sealed record LegacyRegisterRequest
{
    [Required]
    public string Login { get; init; } = string.Empty;

    [Required]
    public string Password { get; init; } = string.Empty;

    public int Role { get; init; } = 0;
    public string? Email { get; init; }
    public string? PhoneNumber { get; init; }
}
