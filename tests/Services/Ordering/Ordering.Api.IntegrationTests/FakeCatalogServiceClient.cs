using System.Collections.Concurrent;
using BuildingBlocks.Core;
using Ordering.Api.Services;

namespace Ordering.Api.IntegrationTests;

/// <summary>
/// Stands in for Catalog so the Ordering tests don't need a running Catalog service.
/// Products that were never added are simply missing from the result, like in the real client.
/// </summary>
public sealed class FakeCatalogServiceClient : ICatalogServiceClient
{
    private readonly ConcurrentDictionary<Guid, ProductDetails> _products = new();

    public ProductDetails AddProduct(string name, decimal price, bool isActive = true)
    {
        var product = new ProductDetails(
            Guid.NewGuid(), Guid.NewGuid(), name, "Test product", $"SKU-{Guid.NewGuid():N}",
            price, isActive, DateTime.UtcNow, DateTime.UtcNow);

        _products[product.Id] = product;
        return product;
    }

    public Task<Result<IReadOnlyDictionary<Guid, ProductDetails>>> GetProductsAsync(
        IEnumerable<Guid> productIds, CancellationToken cancellationToken = default)
    {
        IReadOnlyDictionary<Guid, ProductDetails> found = productIds
            .Distinct()
            .Where(_products.ContainsKey)
            .ToDictionary(id => id, id => _products[id]);

        return Task.FromResult(Result<IReadOnlyDictionary<Guid, ProductDetails>>.Success(found));
    }
}
