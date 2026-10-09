using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BuildingBlocks.Core;
using Catalog.Api.Caching;
using Catalog.Api.DTOs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Catalog.Api.IntegrationTests;

[Collection(CatalogApiCollection.Name)]
public class CatalogApiTests(CatalogApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    private static HttpRequestMessage Admin(HttpMethod method, string url, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add(TestAuthHandler.RoleHeader, Roles.Admin);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }
        return request;
    }

    private async Task<CategoryResponse> CreateCategoryAsync(string? name = null)
    {
        var response = await _client.SendAsync(Admin(
            HttpMethod.Post, "/api/admin/categories",
            new AdminCreateCategoryRequest(name ?? $"Category {Guid.NewGuid():N}", "Test")));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CategoryResponse>())!;
    }

    private async Task<ProductResponse> CreateProductAsync(Guid categoryId, string? name = null, decimal price = 10m)
    {
        var response = await _client.SendAsync(Admin(
            HttpMethod.Post, "/api/admin/products",
            new AdminCreateProductRequest(
                categoryId, name ?? $"Product {Guid.NewGuid():N}", "Desc", $"SKU-{Guid.NewGuid():N}", price)));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ProductResponse>())!;
    }

    #region Health Tests

    [Fact]
    public async Task Health_ShouldReturnOk()
    {
        // Act
        var response = await _client.GetAsync("/health");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    #endregion

    #region Authorization Tests

    [Fact]
    public async Task CreateCategory_WithoutToken_ShouldReturnUnauthorized()
    {
        // Act
        var response = await _client.PostAsJsonAsync(
            "/api/admin/categories", new AdminCreateCategoryRequest("Nope"));

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CreateCategory_AsCustomer_ShouldReturnForbidden()
    {
        // Arrange
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/admin/categories")
        {
            Content = JsonContent.Create(new AdminCreateCategoryRequest("Nope"))
        };
        request.Headers.Add(TestAuthHandler.RoleHeader, Roles.Customer);

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    #endregion

    #region Database Tests

    [Fact]
    public async Task CreateProduct_AsAdmin_ShouldPersistRowInPostgres()
    {
        // Arrange
        var category = await CreateCategoryAsync();

        // Act
        var product = await CreateProductAsync(category.Id, price: 19.99m);

        // Assert
        await using var db = factory.CreateDbContext();
        var row = await db.Products.AsNoTracking().SingleAsync(p => p.Id == product.Id);
        Assert.Equal(19.99m, row.Price);
        Assert.Equal(category.Id, row.CategoryId);
    }

    [Fact]
    public async Task UpdateProduct_WithExistingSku_ShouldReturnConflict()
    {
        // Arrange
        var category = await CreateCategoryAsync();
        var first = await CreateProductAsync(category.Id);
        var second = await CreateProductAsync(category.Id);

        // Act
        var response = await _client.SendAsync(Admin(
            HttpMethod.Put, $"/api/admin/products/{second.Id}",
            new AdminUpdateProductRequest(Sku: first.Sku)));

        // Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task SearchProducts_ByCategoryWithPaging_ShouldReturnPageAndTotal()
    {
        // Arrange
        var category = await CreateCategoryAsync();
        for (var i = 0; i < 3; i++)
        {
            await CreateProductAsync(category.Id);
        }

        // Act
        var response = await _client.PostAsJsonAsync(
            "/api/products/search",
            new ProductQuery(CategoryIds: [category.Id], Page: 1, PageSize: 2));

        // Assert
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, body.GetProperty("items").GetArrayLength());
        Assert.Equal(3, body.GetProperty("totalCount").GetInt32());
    }

    #endregion

    #region Cache Tests

    [Fact]
    public async Task GetProduct_ShouldPopulateRedisAndSurviveRowChangeUntilInvalidated()
    {
        // Arrange
        var category = await CreateCategoryAsync();
        var product = await CreateProductAsync(category.Id, name: "Original");
        await _client.GetAsync($"/api/products/{product.Id}"); // fills the cache

        await using (var db = factory.CreateDbContext())
        {
            await db.Products.Where(p => p.Id == product.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.Name, "Changed behind the cache"));
        }

        // Act
        var cached = await _client.GetFromJsonAsync<ProductResponse>($"/api/products/{product.Id}");

        // Assert
        Assert.Equal("Original", cached!.Name);
        var cache = factory.Services.GetRequiredService<Catalog.Api.Cacheing.ICatalogCache>();
        Assert.NotNull(await cache.GetAsync<ProductResponse>(CatalogCacheKeys.Product(product.Id)));
    }

    [Fact]
    public async Task UpdateProduct_AsAdmin_ShouldInvalidateCachedProduct()
    {
        // Arrange
        var category = await CreateCategoryAsync();
        var product = await CreateProductAsync(category.Id, name: "Before");
        await _client.GetAsync($"/api/products/{product.Id}"); // fills the cache

        // Act
        var update = await _client.SendAsync(Admin(
            HttpMethod.Put, $"/api/admin/products/{product.Id}",
            new AdminUpdateProductRequest(Name: "After")));
        update.EnsureSuccessStatusCode();
        var fresh = await _client.GetFromJsonAsync<ProductResponse>($"/api/products/{product.Id}");

        // Assert
        Assert.Equal("After", fresh!.Name);
    }

    [Fact]
    public async Task SearchProducts_AfterAdminCreate_ShouldIncludeNewProduct()
    {
        // Arrange
        var category = await CreateCategoryAsync();
        var query = new ProductQuery(CategoryIds: [category.Id]);
        var before = await (await _client.PostAsJsonAsync("/api/products/search", query))
            .Content.ReadFromJsonAsync<JsonElement>(); // fills the search cache

        // Act
        await CreateProductAsync(category.Id);
        var after = await (await _client.PostAsJsonAsync("/api/products/search", query))
            .Content.ReadFromJsonAsync<JsonElement>();

        // Assert
        Assert.Equal(0, before.GetProperty("totalCount").GetInt32());
        Assert.Equal(1, after.GetProperty("totalCount").GetInt32());
    }

    #endregion
}
