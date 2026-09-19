namespace Catalog.Api.DTOs
{
    public sealed record CategoryQuery(
        string? Name,
        string? Description);
    public sealed record CategoryResponse(
        Guid Id,
        string Name,
        string Description,
        bool IsActive,
        DateTime CreatedAtUtc,
        DateTime UpdatedAtUtc);
    public sealed record AdminCreateCategoryRequest(
        string Name,
        string? Description = null);
    public sealed record AdminUpdateCategoryRequest(
        string? Name = null,
        string? Description = null,
        bool? IsActive = null);
}