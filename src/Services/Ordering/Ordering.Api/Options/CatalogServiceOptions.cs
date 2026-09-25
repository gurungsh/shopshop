namespace Ordering.Api.Options
{
    public record CatalogServiceOptions
    {
        public const string SectionName = "CatalogService";

        public string BaseUrl { get; init; } = string.Empty;
    }
}
