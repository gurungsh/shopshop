using BuildingBlocks.Core;

namespace Catalog.Api.DTOs
{
    public sealed record CategoryQuery(
        Guid[]? Ids = null,
        string? Name = null,
        string? Description = null,
        int Page = PagingDefaults.DefaultPage,
        int PageSize = PagingDefaults.DefaultPageSize);
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