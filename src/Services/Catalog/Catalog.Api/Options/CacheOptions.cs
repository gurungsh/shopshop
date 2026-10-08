namespace Catalog.Api.Options;

public record CacheOptions
{
    public const string SectionName = "Cache";

    public int EntityTtlMinutes { get; init; } = 10;
    public int SearchTtlMinutes { get; init; } = 2;
}
