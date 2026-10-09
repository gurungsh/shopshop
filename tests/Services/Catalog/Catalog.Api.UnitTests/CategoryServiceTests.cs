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

public class CategoryServiceTests
{
    private readonly CategoryService _categoryService;
    private readonly CatalogDbContext _dbContext;
    private readonly Mock<ICatalogCache> _cache;
    private readonly IOptions<CacheOptions> _cacheOptions;
    private readonly Mock<ILogger<CategoryService>> _mockLogger;

    public CategoryServiceTests()
    {
        var options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseInMemoryDatabase($"test-db-{Guid.NewGuid()}")
            .Options;

        _dbContext = new CatalogDbContext(options);
        _cache = new Mock<ICatalogCache>();
        _cacheOptions = new OptionsWrapper<CacheOptions>(new CacheOptions());
        _mockLogger = new Mock<ILogger<CategoryService>>();

        _categoryService = new CategoryService(_dbContext, _cache.Object, _cacheOptions, _mockLogger.Object);
    }

    #region GetCategoriesAsync Tests

    [Fact]
    public async Task GetCategoriesAsync_WithNoFilters_ShouldReturnAllCategories()
    {
        // Arrange
        _dbContext.Categories.AddRange(
            new Category { Name = "Electronics", Description = "Gadgets" },
            new Category { Name = "Books", Description = "Reading material" });
        await _dbContext.SaveChangesAsync();

        var query = new CategoryQuery();

        // Act
        var result = await _categoryService.GetCategoriesAsync(query);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal(2, result.Value.Items.Count);
    }

    [Theory]
    [InlineData("Electronics", 1)]
    [InlineData("Nonexistent", 0)]
    public async Task GetCategoriesAsync_WithNameFilter_ShouldReturnMatchingCategories(string name, int expectedCount)
    {
        // Arrange
        _dbContext.Categories.AddRange(
            new Category { Name = "Electronics", Description = "Gadgets" },
            new Category { Name = "Books", Description = "Reading material" });
        await _dbContext.SaveChangesAsync();

        var query = new CategoryQuery(Name: name);

        // Act
        var result = await _categoryService.GetCategoriesAsync(query);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal(expectedCount, result.Value.Items.Count);
        Assert.All(result.Value.Items, c => Assert.Equal(name, c.Name));
    }

    [Fact]
    public async Task GetCategoriesAsync_WithDescriptionFilter_ShouldReturnCategoriesContainingText()
    {
        // Arrange
        _dbContext.Categories.AddRange(
            new Category { Name = "Electronics", Description = "Cool gadgets and devices" },
            new Category { Name = "Books", Description = "Reading material" });
        await _dbContext.SaveChangesAsync();

        var query = new CategoryQuery(Description: "gadgets");

        // Act
        var result = await _categoryService.GetCategoriesAsync(query);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Single(result.Value.Items);
        Assert.Equal("Electronics", result.Value.Items[0].Name);
    }

    [Fact]
    public async Task GetCategoriesAsync_WithNameAndDescriptionFilters_ShouldReturnCategoriesMatchingBoth()
    {
        // Arrange
        _dbContext.Categories.AddRange(
            new Category { Name = "Electronics", Description = "Cool gadgets" },
            new Category { Name = "Electronics", Description = "Something else" },
            new Category { Name = "Books", Description = "Cool gadgets" });
        await _dbContext.SaveChangesAsync();

        var query = new CategoryQuery(Name: "Electronics", Description: "gadgets");

        // Act
        var result = await _categoryService.GetCategoriesAsync(query);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Single(result.Value.Items);
    }

    [Fact]
    public async Task GetCategoriesAsync_WithMatchingIdsFilter_ShouldReturnOnlySpecifiedCategories()
    {
        // Arrange
        var category1 = new Category { Name = "Electronics", Description = "Gadgets" };
        var category2 = new Category { Name = "Books", Description = "Reading material" };
        var category3 = new Category { Name = "Clothing", Description = "Apparel" };
        _dbContext.Categories.AddRange(category1, category2, category3);
        await _dbContext.SaveChangesAsync();

        var query = new CategoryQuery(Ids: [category1.Id, category3.Id]);

        // Act
        var result = await _categoryService.GetCategoriesAsync(query);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal(2, result.Value.Items.Count);
        Assert.Contains(result.Value.Items, c => c.Id == category1.Id);
        Assert.Contains(result.Value.Items, c => c.Id == category3.Id);
    }

    [Fact]
    public async Task GetCategoriesAsync_WithIdsAndNameFilters_ShouldReturnCategoriesMatchingBoth()
    {
        // Arrange
        var category1 = new Category { Name = "Electronics", Description = "Gadgets" };
        var category2 = new Category { Name = "Books", Description = "Reading material" };
        var category3 = new Category { Name = "Electronics", Description = "Other gadgets" };
        _dbContext.Categories.AddRange(category1, category2, category3);
        await _dbContext.SaveChangesAsync();

        var query = new CategoryQuery(Ids: [category1.Id, category2.Id], Name: "Electronics");

        // Act
        var result = await _categoryService.GetCategoriesAsync(query);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Single(result.Value.Items);
        Assert.Equal(category1.Id, result.Value.Items[0].Id);
    }

    [Theory]
    [InlineData(2, 2)]
    [InlineData(10, 0)]
    public async Task GetCategoriesAsync_WithPageAndPageSize_ShouldReturnRequestedPageAndTotals(int page, int expectedItemCount)
    {
        // Arrange
        _dbContext.Categories.AddRange(Enumerable.Range(1, 5).Select(i =>
            new Category { Name = $"Category {i}", Description = "Description" }));
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _categoryService.GetCategoriesAsync(new CategoryQuery(Page: page, PageSize: 2));

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(expectedItemCount, result.Value!.Items.Count);
        Assert.Equal(page, result.Value.Page);
        Assert.Equal(2, result.Value.PageSize);
        Assert.Equal(5, result.Value.TotalCount);
        Assert.Equal(3, result.Value.TotalPages);
    }

    [Fact]
    public async Task GetCategoriesAsync_WithDifferentPages_ShouldNotReturnOverlappingItems()
    {
        // Arrange
        _dbContext.Categories.AddRange(Enumerable.Range(1, 5).Select(i =>
            new Category { Name = $"Category {i}", Description = "Description" }));
        await _dbContext.SaveChangesAsync();

        // Act
        var first = await _categoryService.GetCategoriesAsync(new CategoryQuery(Page: 1, PageSize: 3));
        var second = await _categoryService.GetCategoriesAsync(new CategoryQuery(Page: 2, PageSize: 3));

        // Assert
        var ids = first.Value!.Items.Select(i => i.Id).Concat(second.Value!.Items.Select(i => i.Id)).ToList();
        Assert.Equal(5, ids.Count);
        Assert.Equal(5, ids.Distinct().Count());
    }

    #endregion

    #region GetCategoryAsync Tests

    [Fact]
    public async Task GetCategoryAsync_WithExistingId_ShouldReturnCategoryAndStoreItInCache()
    {
        // Arrange
        var category = new Category { Name = "Electronics", Description = "Gadgets" };
        _dbContext.Categories.Add(category);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _categoryService.GetCategoryAsync(category.Id);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal(category.Id, result.Value.Id);
        Assert.Equal("Electronics", result.Value.Name);
        Assert.Equal("Gadgets", result.Value.Description);

        _cache.Verify(c => c.SetAsync(
            CatalogCacheKeys.Category(category.Id),
            It.IsAny<CategoryResponse>(),
            It.IsAny<TimeSpan>()), Times.Once);
    }

    [Fact]
    public async Task GetCategoryAsync_WithNonexistentId_ShouldReturnNotFoundErrorAndCacheNothing()
    {
        // Arrange
        var nonexistentId = Guid.NewGuid();

        // Act
        var result = await _categoryService.GetCategoryAsync(nonexistentId);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal("Category not found.", result.Error);
        Assert.Equal(ResultErrorType.NotFound, result.ErrorType);

        _cache.Verify(c => c.SetAsync(
            It.IsAny<string>(),
            It.IsAny<CategoryResponse>(),
            It.IsAny<TimeSpan>()), Times.Never);
    }

    #endregion

    #region CreateCategoryAsync Tests

    [Fact]
    public async Task CreateCategoryAsync_WithValidRequest_ShouldCreateActiveCategoryAndBumpCatalogVersion()
    {
        // Arrange
        var request = new AdminCreateCategoryRequest("Electronics", "Gadgets and devices");
        var beforeCreate = DateTime.UtcNow;

        // Act
        var result = await _categoryService.CreateCategoryAsync(request);
        var afterCreate = DateTime.UtcNow;

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal("Electronics", result.Value.Name);
        Assert.Equal("Gadgets and devices", result.Value.Description);
        Assert.True(result.Value.IsActive);
        Assert.NotEqual(Guid.Empty, result.Value.Id);
        Assert.InRange(result.Value.CreatedAtUtc, beforeCreate, afterCreate);
        Assert.InRange(result.Value.UpdatedAtUtc, beforeCreate, afterCreate);

        var createdCategory = await _dbContext.Categories.FirstOrDefaultAsync(c => c.Name == "Electronics");
        Assert.NotNull(createdCategory);
        Assert.Equal("Gadgets and devices", createdCategory.Description);

        _cache.Verify(c => c.BumpVersionAsync(), Times.Once);
    }

    [Fact]
    public async Task CreateCategoryAsync_WithNullDescription_ShouldDefaultToEmptyString()
    {
        // Arrange
        var request = new AdminCreateCategoryRequest("Electronics", null);

        // Act
        var result = await _categoryService.CreateCategoryAsync(request);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal(string.Empty, result.Value.Description);
    }

    #endregion

    #region UpdateCategoryAsync Tests

    [Theory]
    [InlineData("Consumer Electronics", null, null, "Consumer Electronics", "Gadgets", true)]
    [InlineData(null, "Updated description", null, "Electronics", "Updated description", true)]
    [InlineData(null, "", null, "Electronics", "", true)]
    [InlineData(null, null, false, "Electronics", "Gadgets", false)]
    [InlineData("   ", null, null, "Electronics", "Gadgets", true)]
    [InlineData(null, null, null, "Electronics", "Gadgets", true)]
    [InlineData("Consumer Electronics", "Updated description", false, "Consumer Electronics", "Updated description", false)]
    public async Task UpdateCategoryAsync_WithPartialRequest_ShouldUpdateOnlyProvidedFieldsAndInvalidateCache(
        string? name, string? description, bool? isActive,
        string expectedName, string expectedDescription, bool expectedIsActive)
    {
        // Arrange
        var category = new Category
        {
            Name = "Electronics",
            Description = "Gadgets",
            IsActive = true
        };
        _dbContext.Categories.Add(category);
        await _dbContext.SaveChangesAsync();

        var request = new AdminUpdateCategoryRequest(name, description, isActive);

        // Act
        var result = await _categoryService.UpdateCategoryAsync(category.Id, request);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal(expectedName, result.Value.Name);
        Assert.Equal(expectedDescription, result.Value.Description);
        Assert.Equal(expectedIsActive, result.Value.IsActive);

        _cache.Verify(c => c.RemoveAsync(CatalogCacheKeys.Category(category.Id)), Times.Once);
        _cache.Verify(c => c.BumpVersionAsync(), Times.Once);
    }

    [Fact]
    public async Task UpdateCategoryAsync_WithValidRequest_ShouldUpdateTimestamp()
    {
        // Arrange
        var originalUpdatedAt = DateTime.UtcNow.AddDays(-1);
        var category = new Category
        {
            Name = "Electronics",
            Description = "Gadgets",
            UpdatedAtUtc = originalUpdatedAt
        };
        _dbContext.Categories.Add(category);
        await _dbContext.SaveChangesAsync();

        var request = new AdminUpdateCategoryRequest(Name: "Consumer Electronics");

        // Act
        var result = await _categoryService.UpdateCategoryAsync(category.Id, request);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.True(result.Value.UpdatedAtUtc > originalUpdatedAt);
    }

    [Fact]
    public async Task UpdateCategoryAsync_WithNonexistentId_ShouldReturnNotFoundErrorAndNotInvalidateCache()
    {
        // Arrange
        var nonexistentId = Guid.NewGuid();
        var request = new AdminUpdateCategoryRequest(Name: "Consumer Electronics");

        // Act
        var result = await _categoryService.UpdateCategoryAsync(nonexistentId, request);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal("Category not found.", result.Error);
        Assert.Equal(ResultErrorType.NotFound, result.ErrorType);

        _cache.Verify(c => c.RemoveAsync(It.IsAny<string>()), Times.Never);
        _cache.Verify(c => c.BumpVersionAsync(), Times.Never);
    }

    #endregion

    #region DeleteCategoryAsync Tests

    [Fact]
    public async Task DeleteCategoryAsync_WithExistingId_ShouldDeleteCategoryAndInvalidateCache()
    {
        // Arrange
        var category = new Category { Name = "Electronics", Description = "Gadgets" };
        _dbContext.Categories.Add(category);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _categoryService.DeleteCategoryAsync(category.Id);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.True(result.Value);

        var deletedCategory = await _dbContext.Categories.FirstOrDefaultAsync(c => c.Id == category.Id);
        Assert.Null(deletedCategory);

        _cache.Verify(c => c.RemoveAsync(CatalogCacheKeys.Category(category.Id)), Times.Once);
        _cache.Verify(c => c.BumpVersionAsync(), Times.Once);
    }

    [Fact]
    public async Task DeleteCategoryAsync_WithNonexistentId_ShouldReturnNotFoundError()
    {
        // Arrange
        var nonexistentId = Guid.NewGuid();

        // Act
        var result = await _categoryService.DeleteCategoryAsync(nonexistentId);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal("Category not found.", result.Error);
        Assert.Equal(ResultErrorType.NotFound, result.ErrorType);
    }

    #endregion

    #region Caching Tests

    [Fact]
    public async Task GetCategoryAsync_WhenCached_ShouldReturnCachedValue()
    {
        // Arrange
        var id = Guid.NewGuid();
        var cached = new CategoryResponse(id, "Cached", "Desc", true, DateTime.UtcNow, DateTime.UtcNow);
        _cache.Setup(c => c.GetAsync<CategoryResponse>(CatalogCacheKeys.Category(id))).ReturnsAsync(cached);

        // Act
        var result = await _categoryService.GetCategoryAsync(id);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("Cached", result.Value!.Name);
    }

    [Fact]
    public async Task GetCategoriesAsync_WhenCached_ShouldReturnCachedPage()
    {
        // Arrange
        var query = new CategoryQuery();
        var cached = new PagedResponse<CategoryResponse>(
            new List<CategoryResponse>
            {
                new(Guid.NewGuid(), "Cached", "Desc", true, DateTime.UtcNow, DateTime.UtcNow)
            },
            query.Page, query.PageSize, 1);
        _cache.Setup(c => c.GetVersionAsync()).ReturnsAsync("v1");
        _cache.Setup(c => c.GetAsync<PagedResponse<CategoryResponse>>(CatalogCacheKeys.CategorySearch("v1", query)))
            .ReturnsAsync(cached);

        // Act
        var result = await _categoryService.GetCategoriesAsync(query);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("Cached", result.Value!.Items.Single().Name);
    }

    [Fact]
    public async Task GetCategoriesAsync_WhenCacheUnavailable_ShouldStillReturnFromDatabase()
    {
        // Arrange
        _dbContext.Categories.Add(new Category { Name = "Books", Description = "Reading material" });
        await _dbContext.SaveChangesAsync();
        _cache.Setup(c => c.GetVersionAsync()).ReturnsAsync((string?)null);

        // Act
        var result = await _categoryService.GetCategoriesAsync(new CategoryQuery());

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!.Items);
    }

    #endregion
}
