using Common.Core;

namespace Ordering.Api.Services
{
    public sealed record ProductDetails(
        Guid Id,
        Guid CategoryId,
        string Name,
        string Description,
        string Sku,
        decimal Price,
        bool IsActive,
        DateTime CreatedAtUtc,
        DateTime UpdatedAtUtc);

    public interface ICatalogServiceClient
    {
        Task<Result<IReadOnlyDictionary<Guid, ProductDetails>>> GetProductsAsync(IEnumerable<Guid> productIds, CancellationToken cancellationToken = default);
    }
}
