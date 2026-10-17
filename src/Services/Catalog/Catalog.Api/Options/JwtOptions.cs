namespace Catalog.Api.Options;

public record JwtOptions
{
    public const string SectionName = "JwtSettings";

    public string MetadataAddress { get; init; } = string.Empty;
    public string Issuer { get; init; } = string.Empty;
    public string Audience { get; init; } = string.Empty;
    public bool RequireHttpsMetadata { get; init; } = true;
}
