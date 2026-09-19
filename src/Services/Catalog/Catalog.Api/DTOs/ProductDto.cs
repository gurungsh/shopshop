namespace Catalog.Api.DTOs
{
    public sealed record ProductQuery(
        Guid? CategoryId,
        string? Name,
        string? Description,
        string? Sku,
        decimal? MinPrice,
        decimal? MaxPrice
        );
    public sealed record ProductResponse(
        Guid Id,
        Guid CategoryId,
        string Name,
        string Description,
        string Sku,
        decimal Price,
        bool IsActive);
    public sealed record AdminCreateProductRequest(
        Guid CategoryId,
        string Name,
        string Description,
        string Sku,
        decimal Price);
    public sealed record AdminUpdateProductRequest(
        Guid CategoryId,
        string Name,
        string Description,
        string Sku,
        decimal Price,
        bool IsActive);
}