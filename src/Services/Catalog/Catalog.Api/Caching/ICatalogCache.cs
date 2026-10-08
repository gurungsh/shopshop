namespace Catalog.Api.Cacheing
{

    public interface ICatalogCache
    {
        Task<T?> GetAsync<T>(string key);
        Task SetAsync<T>(string key, T value, TimeSpan ttl);
        Task RemoveAsync(string key);
        Task<string?> GetVersionAsync();
        Task BumpVersionAsync();
    }
}
