namespace Catalog.Api.DTOs
{
    public sealed record ProductQuery(
        Guid[]? Ids = null,
        Guid[]? CategoryIds = null,
        string? Name = null,
        string? Description = null,
        string? Sku = null,
        decimal? MinPrice = null,
        decimal? MaxPrice = null
        );
    public sealed record ProductResponse(
        Guid Id,
        Guid CategoryId,
        string Name,
        string Description,
        string Sku,
        decimal Price,
        bool IsActive,
        DateTime CreatedAtUtc,
        DateTime UpdatedAtUtc);
    public sealed record AdminCreateProductRequest(
        Guid CategoryId,
        string Name,
        string Description,
        string Sku,
        decimal Price);
    public sealed record AdminUpdateProductRequest(
        Guid? CategoryId = null,
        string? Name = null,
        string? Description = null,
        string? Sku = null,
        decimal? Price = null,
        bool? IsActive = null);
}