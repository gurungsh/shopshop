using System.ComponentModel.DataAnnotations;

namespace Catalog.Api.Options;

public record CacheOptions
{
    public const string SectionName = "Cache";

    [Range(1, 1440)]
    public int EntityTtlMinutes { get; init; } = 10;

    [Range(1, 1440)]
    public int SearchTtlMinutes { get; init; } = 2;
}
