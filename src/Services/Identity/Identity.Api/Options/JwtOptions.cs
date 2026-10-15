using System.ComponentModel.DataAnnotations;

namespace Identity.Api.Options;

public record JwtOptions
{
    public const string SectionName = "JwtSettings";

    [Required]
    public string PrivateKeyPem { get; init; } = string.Empty;

    [Required]
    public string KeyId { get; init; } = string.Empty;

    [Required]
    public string Issuer { get; init; } = string.Empty;

    [Required]
    public string Audience { get; init; } = string.Empty;

    [Range(1, 1440)]
    public int ExpirationInMinutes { get; init; } = 60;
}
