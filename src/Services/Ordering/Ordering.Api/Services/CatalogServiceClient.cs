using Common.Core;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace Ordering.Api.Services
{
    public class CatalogServiceClient : ICatalogServiceClient
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<CatalogServiceClient> _logger;

        public CatalogServiceClient(
            HttpClient httpClient,
            ILogger<CatalogServiceClient> logger)
        {
            _httpClient = httpClient;
            _logger = logger;
        }

        public async Task<Result<IReadOnlyDictionary<Guid, ProductDetails>>> GetProductsAsync(IEnumerable<Guid> productIds, CancellationToken cancellationToken = default)
        {
            var ids = productIds.Distinct().ToArray();

            try
            {
                // POST /api/products/search is Catalog's product search endpoint
                using var response = await _httpClient.PostAsJsonAsync("api/products/search",new { Ids = ids }, cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Catalog product lookup failed. Status Code:{StatusCode}", (int)response.StatusCode);
                    return Unavailable();
                }

                var products = await response.Content.ReadFromJsonAsync<List<ProductDetails>>(cancellationToken) ?? [];

                return Result<IReadOnlyDictionary<Guid, ProductDetails>>.Success(
                    products.ToDictionary(p => p.Id));
            }
            catch (Exception ex) when (
                ex is HttpRequestException or TimeoutRejectedException or BrokenCircuitException or TaskCanceledException
                && !cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "Catalog service is unavailable.");
                return Unavailable();
            }
        }

        private static Result<IReadOnlyDictionary<Guid, ProductDetails>> Unavailable()
            => Result<IReadOnlyDictionary<Guid, ProductDetails>>.Failure("Catalog service is unavailable.", ResultErrorType.ServiceUnavailable);
    }
}
