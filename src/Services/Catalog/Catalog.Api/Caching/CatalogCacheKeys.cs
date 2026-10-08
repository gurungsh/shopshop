using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Catalog.Api.Caching;

public static class CatalogCacheKeys
{
    public const string Version = "catalog:version";

    public static string Product(Guid id) => $"catalog:product:{id}";

    public static string Category(Guid id) => $"catalog:category:{id}";

    public static string ProductSearch(string version, object query) =>
        $"catalog:products:search:{version}:{Hash(query)}";

    public static string CategorySearch(string version, object query) =>
        $"catalog:categories:search:{version}:{Hash(query)}";

    private static string Hash(object query)
    {
        var json = JsonSerializer.Serialize(query);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    }
}