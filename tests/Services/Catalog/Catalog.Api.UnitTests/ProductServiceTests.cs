using BuildingBlocks.Core;
using Catalog.Api.Cacheing;
using Catalog.Api.Caching;
using Catalog.Api.DTOs;
using Catalog.Api.Options;
using Catalog.Api.Services;
using Catalog.Infrastructure.Data;
using Catalog.Infrastructure.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace Catalog.Api.UnitTests;

public class ProductServiceTests
{
    private readonly ProductService _productService;
    private readonly CatalogDbContext _dbContext;
    private readonly Mock<ICatalogCache> _cache;
    private readonly IOptions<CacheOptions> _cacheOptions;
    private readonly Mock<ILogger<ProductService>> _mockLogger;

    public ProductServiceTests()
    {
        var options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseInMemoryDatabase($"test-db-{Guid.NewGuid()}")
            .Options;

        _dbContext = new CatalogDbContext(options);
        _cache = new Mock<ICatalogCache>();
        _cacheOptions = new OptionsWrapper<CacheOptions>(new CacheOptions());
        _mockLogger = new Mock<ILogger<ProductService>>();

        _productService = new ProductService(_dbContext, _cache.Object, _cacheOptions, _mockLogger.Object);
    }

    private static Product CreateTestProduct(
        Guid categoryId,
        string name = "Running Shoe",
        string description = "Comfortable running shoe",
        string sku = "SKU-001",
        decimal price = 49.99m,
        bool isActive = true)
    {
        return new Product
        {
            CategoryId = categoryId,
            Name = name,
            Description = description,
            Sku = sku,
            Price = price,
            IsActive = isActive
        };
    }

    #region GetProductsAsync Tests

    [Fact]
    public async Task GetProductsAsync_WithNoFilters_ShouldReturnAllProducts()
    {
        // Arrange
        var categoryId = Guid.NewGuid();
        _dbContext.Products.AddRange(
            CreateTestProduct(categoryId, name: "Running Shoe", sku: "SKU-001"),
            CreateTestProduct(categoryId, name: "Hiking Boot", sku: "SKU-002"));
        await _dbContext.SaveChangesAsync();

        var query = new ProductQuery();

        // Act
        var result = await _productService.GetProductsAsync(query);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal(2, result.Value.Items.Count);
    }

    [Fact]
    public async Task GetProductsAsync_WithMatchingIdsFilter_ShouldReturnOnlySpecifiedProducts()
    {
        // Arrange
        var categoryId = Guid.NewGuid();
        var product1 = CreateTestProduct(categoryId, name: "Running Shoe", sku: "SKU-001");
        var product2 = CreateTestProduct(categoryId, name: "Hiking Boot", sku: "SKU-002");
        var product3 = CreateTestProduct(categoryId, name: "Sandal", sku: "SKU-003");
        _dbContext.Products.AddRange(product1, product2, product3);
        await _dbContext.SaveChangesAsync();

        var query = new ProductQuery(Ids: [product1.Id, product3.Id]);

        // Act
        var result = await _productService.GetProductsAsync(query);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal(2, result.Value.Items.Count);
        Assert.Contains(result.Value.Items, p => p.Id == product1.Id);
        Assert.Contains(result.Value.Items, p => p.Id == product3.Id);
    }

    [Fact]
    public async Task GetProductsAsync_WithMatchingCategoryIdsFilter_ShouldReturnProductsInThoseCategories()
    {
        // Arrange
        var categoryId1 = Guid.NewGuid();
        var categoryId2 = Guid.NewGuid();
        var categoryId3 = Guid.NewGuid();
        _dbContext.Products.AddRange(
            CreateTestProduct(categoryId1, sku: "SKU-001"),
            CreateTestProduct(categoryId2, sku: "SKU-002"),
            CreateTestProduct(categoryId3, sku: "SKU-003"));
        await _dbContext.SaveChangesAsync();

        var query = new ProductQuery(CategoryIds: [categoryId1, categoryId2]);

        // Act
        var result = await _productService.GetProductsAsync(query);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal(2, result.Value.Items.Count);
    }

    [Theory]
    [InlineData("Shoe", 1)]
    [InlineData("Nonexistent", 0)]
    public async Task GetProductsAsync_WithNameFilter_ShouldReturnProductsContainingText(string name, int expectedCount)
    {
        // Arrange
        var categoryId = Guid.NewGuid();
        _dbContext.Products.AddRange(
            CreateTestProduct(categoryId, name: "Running Shoe", sku: "SKU-001"),
            CreateTestProduct(categoryId, name: "Hiking Boot", sku: "SKU-002"));
        await _dbContext.SaveChangesAsync();

        var query = new ProductQuery(Name: name);

        // Act
        var result = await _productService.GetProductsAsync(query);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal(expectedCount, result.Value.Items.Count);
        Assert.All(result.Value.Items, p => Assert.Contains(name, p.Name));
    }

    [Fact]
    public async Task GetProductsAsync_WithDescriptionFilter_ShouldReturnProductsContainingText()
    {
        // Arrange
        var categoryId = Guid.NewGuid();
        _dbContext.Products.AddRange(
            CreateTestProduct(categoryId, description: "Comfortable and lightweight", sku: "SKU-001"),
            CreateTestProduct(categoryId, description: "Heavy duty", sku: "SKU-002"));
        await _dbContext.SaveChangesAsync();

        var query = new ProductQuery(Description: "lightweight");

        // Act
        var result = await _productService.GetProductsAsync(query);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Single(result.Value.Items);
    }

    [Fact]
    public async Task GetProductsAsync_WithMatchingSkuFilter_ShouldReturnExactMatch()
    {
        // Arrange
        var categoryId = Guid.NewGuid();
        _dbContext.Products.AddRange(
            CreateTestProduct(categoryId, sku: "SKU-001"),
            CreateTestProduct(categoryId, sku: "SKU-002"));
        await _dbContext.SaveChangesAsync();

        var query = new ProductQuery(Sku: "SKU-001");

        // Act
        var result = await _productService.GetProductsAsync(query);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Single(result.Value.Items);
        Assert.Equal("SKU-001", result.Value.Items[0].Sku);
    }

    [Theory]
    [InlineData(50, null, "SKU-002,SKU-003")]
    [InlineData(null, 50, "SKU-001,SKU-002")]
    [InlineData(20, 80, "SKU-002")]
    public async Task GetProductsAsync_WithPriceFilters_ShouldReturnProductsWithinBounds(
        int? minPrice, int? maxPrice, string expectedSkus)
    {
        // Arrange
        var categoryId = Guid.NewGuid();
        _dbContext.Products.AddRange(
            CreateTestProduct(categoryId, sku: "SKU-001", price: 10m),
            CreateTestProduct(categoryId, sku: "SKU-002", price: 50m),
            CreateTestProduct(categoryId, sku: "SKU-003", price: 100m));
        await _dbContext.SaveChangesAsync();

        var query = new ProductQuery(MinPrice: minPrice, MaxPrice: maxPrice);

        // Act
        var result = await _productService.GetProductsAsync(query);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal(
            expectedSkus.Split(',').Order(),
            result.Value.Items.Select(p => p.Sku).Order());
    }

    [Theory]
    [InlineData(2, 2)]
    [InlineData(10, 0)]
    public async Task GetProductsAsync_WithPageAndPageSize_ShouldReturnRequestedPageAndTotals(int page, int expectedItemCount)
    {
        // Arrange
        var categoryId = Guid.NewGuid();
        _dbContext.Products.AddRange(Enumerable.Range(1, 5).Select(i =>
            CreateTestProduct(categoryId, name: $"Product {i}", sku: $"SKU-{i:000}")));
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _productService.GetProductsAsync(new ProductQuery(Page: page, PageSize: 2));

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(expectedItemCount, result.Value!.Items.Count);
        Assert.Equal(page, result.Value.Page);
        Assert.Equal(2, result.Value.PageSize);
        Assert.Equal(5, result.Value.TotalCount);
        Assert.Equal(3, result.Value.TotalPages);
    }

    [Fact]
    public async Task GetProductsAsync_WithDifferentPages_ShouldNotReturnOverlappingItems()
    {
        // Arrange
        var categoryId = Guid.NewGuid();
        _dbContext.Products.AddRange(Enumerable.Range(1, 5).Select(i =>
            CreateTestProduct(categoryId, name: $"Product {i}", sku: $"SKU-{i:000}")));
        await _dbContext.SaveChangesAsync();

        // Act
        var first = await _productService.GetProductsAsync(new ProductQuery(Page: 1, PageSize: 3));
        var second = await _productService.GetProductsAsync(new ProductQuery(Page: 2, PageSize: 3));

        // Assert
        var ids = first.Value!.Items.Select(i => i.Id).Concat(second.Value!.Items.Select(i => i.Id)).ToList();
        Assert.Equal(5, ids.Count);
        Assert.Equal(5, ids.Distinct().Count());
    }

    #endregion

    #region GetProductAsync Tests

    [Fact]
    public async Task GetProductAsync_WithExistingId_ShouldReturnProductAndStoreItInCache()
    {
        // Arrange
        var categoryId = Guid.NewGuid();
        var product = CreateTestProduct(categoryId, sku: "SKU-001");
        _dbContext.Products.Add(product);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _productService.GetProductAsync(product.Id);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal(product.Id, result.Value.Id);
        Assert.Equal(product.Name, result.Value.Name);
        Assert.Equal(product.Sku, result.Value.Sku);

        _cache.Verify(c => c.SetAsync(
            CatalogCacheKeys.Product(product.Id),
            It.IsAny<ProductResponse>(),
            It.IsAny<TimeSpan>()), Times.Once);
    }

    [Fact]
    public async Task GetProductAsync_WithNonexistentId_ShouldReturnNotFoundErrorAndCacheNothing()
    {
        // Arrange
        var nonexistentId = Guid.NewGuid();

        // Act
        var result = await _productService.GetProductAsync(nonexistentId);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal("Product not found.", result.Error);
        Assert.Equal(ResultErrorType.NotFound, result.ErrorType);

        _cache.Verify(c => c.SetAsync(
            It.IsAny<string>(),
            It.IsAny<ProductResponse>(),
            It.IsAny<TimeSpan>()), Times.Never);
    }

    #endregion

    #region CreateProductAsync Tests

    [Fact]
    public async Task CreateProductAsync_WithValidRequest_ShouldCreateActiveProductAndBumpCatalogVersion()
    {
        // Arrange
        var categoryId = Guid.NewGuid();
        var request = new AdminCreateProductRequest(
            categoryId, "Running Shoe", "Comfortable running shoe", "SKU-001", 49.99m);
        var beforeCreate = DateTime.UtcNow;

        // Act
        var result = await _productService.CreateProductAsync(request);
        var afterCreate = DateTime.UtcNow;

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal("Running Shoe", result.Value.Name);
        Assert.Equal("Comfortable running shoe", result.Value.Description);
        Assert.Equal("SKU-001", result.Value.Sku);
        Assert.Equal(49.99m, result.Value.Price);
        Assert.Equal(categoryId, result.Value.CategoryId);
        Assert.NotEqual(Guid.Empty, result.Value.Id);
        Assert.True(result.Value.IsActive);
        Assert.InRange(result.Value.CreatedAtUtc, beforeCreate, afterCreate);
        Assert.InRange(result.Value.UpdatedAtUtc, beforeCreate, afterCreate);

        var createdProduct = await _dbContext.Products.FirstOrDefaultAsync(p => p.Sku == "SKU-001");
        Assert.NotNull(createdProduct);
        Assert.Equal("Running Shoe", createdProduct.Name);

        _cache.Verify(c => c.BumpVersionAsync(), Times.Once);
    }

    #endregion

    #region UpdateProductAsync Tests

    [Theory]
    [InlineData("Trail Runner", null, null, null, null, "Trail Runner", "Comfortable running shoe", "SKU-001", 49.99, true)]
    [InlineData(null, "Updated description", null, null, null, "Running Shoe", "Updated description", "SKU-001", 49.99, true)]
    [InlineData(null, null, "SKU-999", null, null, "Running Shoe", "Comfortable running shoe", "SKU-999", 49.99, true)]
    [InlineData(null, null, null, 59.99, null, "Running Shoe", "Comfortable running shoe", "SKU-001", 59.99, true)]
    [InlineData(null, null, null, null, false, "Running Shoe", "Comfortable running shoe", "SKU-001", 49.99, false)]
    [InlineData(null, null, null, null, null, "Running Shoe", "Comfortable running shoe", "SKU-001", 49.99, true)]
    public async Task UpdateProductAsync_WithPartialRequest_ShouldUpdateOnlyProvidedFields(
        string? name, string? description, string? sku, double? price, bool? isActive,
        string expectedName, string expectedDescription, string expectedSku, double expectedPrice, bool expectedIsActive)
    {
        // Arrange
        var product = CreateTestProduct(Guid.NewGuid(), sku: "SKU-001", price: 49.99m, isActive: true);
        _dbContext.Products.Add(product);
        await _dbContext.SaveChangesAsync();

        var request = new AdminUpdateProductRequest(
            Name: name, Description: description, Sku: sku, Price: (decimal?)price, IsActive: isActive);

        // Act
        var result = await _productService.UpdateProductAsync(product.Id, request);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal(product.CategoryId, result.Value.CategoryId);
        Assert.Equal(expectedName, result.Value.Name);
        Assert.Equal(expectedDescription, result.Value.Description);
        Assert.Equal(expectedSku, result.Value.Sku);
        Assert.Equal((decimal)expectedPrice, result.Value.Price);
        Assert.Equal(expectedIsActive, result.Value.IsActive);
    }

    [Fact]
    public async Task UpdateProductAsync_WithAllFieldsProvided_ShouldUpdateAllFieldsAndTimestampAndInvalidateCache()
    {
        // Arrange
        var originalUpdatedAt = DateTime.UtcNow.AddDays(-1);
        var categoryId = Guid.NewGuid();
        var newCategoryId = Guid.NewGuid();
        _dbContext.Categories.Add(new Category { Id = newCategoryId, Name = "Footwear" });
        var product = CreateTestProduct(categoryId, sku: "SKU-001");
        product.UpdatedAtUtc = originalUpdatedAt;
        _dbContext.Products.Add(product);
        await _dbContext.SaveChangesAsync();

        var request = new AdminUpdateProductRequest(
            newCategoryId, "Trail Runner", "Updated description", "SKU-999", 79.99m, false);

        // Act
        var result = await _productService.UpdateProductAsync(product.Id, request);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal(newCategoryId, result.Value.CategoryId);
        Assert.Equal("Trail Runner", result.Value.Name);
        Assert.Equal("Updated description", result.Value.Description);
        Assert.Equal("SKU-999", result.Value.Sku);
        Assert.Equal(79.99m, result.Value.Price);
        Assert.False(result.Value.IsActive);
        Assert.True(result.Value.UpdatedAtUtc > originalUpdatedAt);

        _cache.Verify(c => c.RemoveAsync(CatalogCacheKeys.Product(product.Id)), Times.Once);
        _cache.Verify(c => c.BumpVersionAsync(), Times.Once);
    }

    [Fact]
    public async Task UpdateProductAsync_WithNonexistentCategoryId_ShouldReturnBadRequestError()
    {
        // Arrange
        var categoryId = Guid.NewGuid();
        var product = CreateTestProduct(categoryId, sku: "SKU-001");
        _dbContext.Products.Add(product);
        await _dbContext.SaveChangesAsync();

        var request = new AdminUpdateProductRequest(CategoryId: Guid.NewGuid());

        // Act
        var result = await _productService.UpdateProductAsync(product.Id, request);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal("Category does not exist.", result.Error);
        Assert.Equal(ResultErrorType.BadRequest, result.ErrorType);
    }

    [Fact]
    public async Task UpdateProductAsync_WithSkuTakenByAnotherProduct_ShouldReturnConflictError()
    {
        // Arrange
        var categoryId = Guid.NewGuid();
        var existingProduct = CreateTestProduct(categoryId, sku: "SKU-001");
        var productToUpdate = CreateTestProduct(categoryId, sku: "SKU-002");
        _dbContext.Products.AddRange(existingProduct, productToUpdate);
        await _dbContext.SaveChangesAsync();

        var request = new AdminUpdateProductRequest(Sku: "SKU-001");

        // Act
        var result = await _productService.UpdateProductAsync(productToUpdate.Id, request);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal("A Product with this SKU already exists.", result.Error);
        Assert.Equal(ResultErrorType.Conflict, result.ErrorType);
    }

    [Fact]
    public async Task UpdateProductAsync_WithNonexistentId_ShouldReturnNotFoundError()
    {
        // Arrange
        var nonexistentId = Guid.NewGuid();
        var request = new AdminUpdateProductRequest(Name: "Trail Runner");

        // Act
        var result = await _productService.UpdateProductAsync(nonexistentId, request);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal("Product not found.", result.Error);
        Assert.Equal(ResultErrorType.NotFound, result.ErrorType);
    }

    #endregion

    #region DeleteProductAsync Tests

    [Fact]
    public async Task DeleteProductAsync_WithExistingId_ShouldDeleteProductAndInvalidateCache()
    {
        // Arrange
        var categoryId = Guid.NewGuid();
        var product = CreateTestProduct(categoryId, sku: "SKU-001");
        _dbContext.Products.Add(product);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _productService.DeleteProductAsync(product.Id);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.True(result.Value);

        var deletedProduct = await _dbContext.Products.FirstOrDefaultAsync(p => p.Id == product.Id);
        Assert.Null(deletedProduct);

        _cache.Verify(c => c.RemoveAsync(CatalogCacheKeys.Product(product.Id)), Times.Once);
        _cache.Verify(c => c.BumpVersionAsync(), Times.Once);
    }

    [Fact]
    public async Task DeleteProductAsync_WithNonexistentId_ShouldReturnNotFoundError()
    {
        // Arrange
        var nonexistentId = Guid.NewGuid();

        // Act
        var result = await _productService.DeleteProductAsync(nonexistentId);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal("Product not found.", result.Error);
        Assert.Equal(ResultErrorType.NotFound, result.ErrorType);
    }

    #endregion

    #region Caching Tests

    [Fact]
    public async Task GetProductAsync_WhenCached_ShouldReturnCachedValueWithoutQueryingDatabase()
    {
        // Arrange
        var id = Guid.NewGuid();
        var cached = new ProductResponse(id, Guid.NewGuid(), "Cached", "Desc", "SKU-C", 9.99m, true, DateTime.UtcNow, DateTime.UtcNow);
        _cache.Setup(c => c.GetAsync<ProductResponse>(CatalogCacheKeys.Product(id))).ReturnsAsync(cached);

        // Act
        var result = await _productService.GetProductAsync(id);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("Cached", result.Value!.Name);
    }

    [Fact]
    public async Task GetProductsAsync_WhenCacheUnavailable_ShouldStillReturnFromDatabase()
    {
        // Arrange
        var category = new Category { Name = "Shoes", Description = "Footwear" };
        _dbContext.Categories.Add(category);
        _dbContext.Products.Add(CreateTestProduct(category.Id));
        await _dbContext.SaveChangesAsync();
        _cache.Setup(c => c.GetVersionAsync()).ReturnsAsync((string?)null);

        // Act
        var result = await _productService.GetProductsAsync(new ProductQuery());

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!.Items);
    }

    #endregion
}
