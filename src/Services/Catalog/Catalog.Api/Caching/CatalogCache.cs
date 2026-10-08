using System.Text.Json;
using Catalog.Api.Cacheing;
using Microsoft.Extensions.Caching.Distributed;

namespace Catalog.Api.Caching
{
    public class CatalogCache : ICatalogCache
    {
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

        private readonly IDistributedCache _cache;
        private readonly ILogger<CatalogCache> _logger;

        public CatalogCache(IDistributedCache cache, ILogger<CatalogCache> logger)
        {
            _cache = cache;
            _logger = logger;
        }

        public async Task<T?> GetAsync<T>(string key)
        {
            try
            {
                var json = await _cache.GetStringAsync(key);
                return json is null ? default : JsonSerializer.Deserialize<T>(json, JsonOptions);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Cache read failed. Key:{CacheKey}", key);
                return default;
            }
        }

        public async Task SetAsync<T>(string key, T value, TimeSpan ttl)
        {
            try
            {
                var options = new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = ttl };
                await _cache.SetStringAsync(key, JsonSerializer.Serialize(value, JsonOptions), options);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Cache write failed. Key:{CacheKey}", key);
            }
        }

        public async Task RemoveAsync(string key)
        {
            try
            {
                await _cache.RemoveAsync(key);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Cache remove failed. Key:{CacheKey}", key);
            }
        }

        public async Task<string?> GetVersionAsync()
        {
            try
            {
                var version = await _cache.GetStringAsync(CatalogCacheKeys.Version);
                if (version is null)
                {
                    version = Guid.NewGuid().ToString("N");
                    await _cache.SetStringAsync(CatalogCacheKeys.Version, version);
                }

                return version;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Cache version read failed.");
                return null;
            }
        }

        public async Task BumpVersionAsync()
        {
            try
            {
                await _cache.SetStringAsync(CatalogCacheKeys.Version, Guid.NewGuid().ToString("N"));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Cache version bump failed.");
            }
        }
    }
}