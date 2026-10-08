namespace Identity.Api.Options;

public record JwtOptions
{
    public const string SectionName = "JwtSettings";

    public string PrivateKeyPem { get; set; } = string.Empty;
    public string KeyId { get; set; } = string.Empty;
    public string Issuer { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    public int ExpirationInMinutes { get; set; } = 60;
}
