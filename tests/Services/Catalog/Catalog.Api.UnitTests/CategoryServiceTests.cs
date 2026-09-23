using Catalog.Api.DTOs;
using Catalog.Api.Services;
using Catalog.Infrastructure.Data;
using Catalog.Infrastructure.Models;
using Common.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;

namespace Catalog.Api.UnitTests
{
    public class CategoryServiceTests
    {
        private readonly CategoryService _categoryService;
        private readonly CatalogDbContext _dbContext;
        private readonly Mock<ILogger<CategoryService>> _mockLogger;

        public CategoryServiceTests()
        {
            var options = new DbContextOptionsBuilder<CatalogDbContext>()
                .UseInMemoryDatabase($"test-db-{Guid.NewGuid()}")
                .Options;

            _dbContext = new CatalogDbContext(options);
            _mockLogger = new Mock<ILogger<CategoryService>>();

            _categoryService = new CategoryService(_dbContext, _mockLogger.Object);
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
            Assert.Equal(2, result.Value.Count);
        }

        [Fact]
        public async Task GetCategoriesAsync_WithMatchingNameFilter_ShouldReturnFilteredCategories()
        {
            // Arrange
            _dbContext.Categories.AddRange(
                new Category { Name = "Electronics", Description = "Gadgets" },
                new Category { Name = "Books", Description = "Reading material" });
            await _dbContext.SaveChangesAsync();

            var query = new CategoryQuery(Name: "Electronics");

            // Act
            var result = await _categoryService.GetCategoriesAsync(query);

            // Assert
            Assert.True(result.IsSuccess);
            Assert.NotNull(result.Value);
            Assert.Single(result.Value);
            Assert.Equal("Electronics", result.Value[0].Name);
        }

        [Fact]
        public async Task GetCategoriesAsync_WithNonMatchingNameFilter_ShouldReturnEmptyList()
        {
            // Arrange
            _dbContext.Categories.Add(new Category { Name = "Electronics", Description = "Gadgets" });
            await _dbContext.SaveChangesAsync();

            var query = new CategoryQuery(Name: "Nonexistent");

            // Act
            var result = await _categoryService.GetCategoriesAsync(query);

            // Assert
            Assert.True(result.IsSuccess);
            Assert.NotNull(result.Value);
            Assert.Empty(result.Value);
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
            Assert.Single(result.Value);
            Assert.Equal("Electronics", result.Value[0].Name);
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
            Assert.Single(result.Value);
        }

        #endregion

        #region GetCategoryAsync Tests

        [Fact]
        public async Task GetCategoryAsync_WithExistingId_ShouldReturnCategory()
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
        }

        [Fact]
        public async Task GetCategoryAsync_WithNonexistentId_ShouldReturnNotFoundError()
        {
            // Arrange
            var nonexistentId = Guid.NewGuid();

            // Act
            var result = await _categoryService.GetCategoryAsync(nonexistentId);

            // Assert
            Assert.False(result.IsSuccess);
            Assert.Equal("Category not found.", result.Error);
            Assert.Equal(ResultErrorType.NotFound, result.ErrorType);
        }

        #endregion

        #region CreateCategoryAsync Tests

        [Fact]
        public async Task CreateCategoryAsync_WithValidRequest_ShouldCreateCategory()
        {
            // Arrange
            var request = new AdminCreateCategoryRequest("Electronics", "Gadgets and devices");

            // Act
            var result = await _categoryService.CreateCategoryAsync(request);

            // Assert
            Assert.True(result.IsSuccess);
            Assert.NotNull(result.Value);
            Assert.Equal("Electronics", result.Value.Name);
            Assert.Equal("Gadgets and devices", result.Value.Description);
            Assert.True(result.Value.IsActive);
            Assert.NotEqual(Guid.Empty, result.Value.Id);

            var createdCategory = await _dbContext.Categories.FirstOrDefaultAsync(c => c.Name == "Electronics");
            Assert.NotNull(createdCategory);
            Assert.Equal("Gadgets and devices", createdCategory.Description);
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

        [Fact]
        public async Task CreateCategoryAsync_WithValidRequest_ShouldDefaultIsActiveToTrue()
        {
            // Arrange
            var request = new AdminCreateCategoryRequest("Electronics", "Gadgets");

            // Act
            var result = await _categoryService.CreateCategoryAsync(request);

            // Assert
            Assert.True(result.IsSuccess);
            Assert.NotNull(result.Value);
            Assert.True(result.Value.IsActive);
        }

        [Fact]
        public async Task CreateCategoryAsync_WithValidRequest_ShouldSetCreatedAndUpdatedTimestamps()
        {
            // Arrange
            var request = new AdminCreateCategoryRequest("Electronics", "Gadgets");
            var beforeCreate = DateTime.UtcNow;

            // Act
            var result = await _categoryService.CreateCategoryAsync(request);
            var afterCreate = DateTime.UtcNow;

            // Assert
            Assert.True(result.IsSuccess);
            Assert.NotNull(result.Value);
            Assert.InRange(result.Value.CreatedAtUtc, beforeCreate, afterCreate);
            Assert.InRange(result.Value.UpdatedAtUtc, beforeCreate, afterCreate);
        }

        #endregion

        #region UpdateCategoryAsync Tests

        [Fact]
        public async Task UpdateCategoryAsync_WithNameOnly_ShouldUpdateNameAndKeepOtherFields()
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

            var request = new AdminUpdateCategoryRequest(Name: "Consumer Electronics");

            // Act
            var result = await _categoryService.UpdateCategoryAsync(category.Id, request);

            // Assert
            Assert.True(result.IsSuccess);
            Assert.NotNull(result.Value);
            Assert.Equal("Consumer Electronics", result.Value.Name);
            Assert.Equal("Gadgets", result.Value.Description);
            Assert.True(result.Value.IsActive);
        }

        [Fact]
        public async Task UpdateCategoryAsync_WithDescriptionOnly_ShouldUpdateDescriptionAndKeepOtherFields()
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

            var request = new AdminUpdateCategoryRequest(Description: "Updated description");

            // Act
            var result = await _categoryService.UpdateCategoryAsync(category.Id, request);

            // Assert
            Assert.True(result.IsSuccess);
            Assert.NotNull(result.Value);
            Assert.Equal("Electronics", result.Value.Name);
            Assert.Equal("Updated description", result.Value.Description);
            Assert.True(result.Value.IsActive);
        }

        [Fact]
        public async Task UpdateCategoryAsync_WithEmptyStringDescription_ShouldClearDescription()
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

            var request = new AdminUpdateCategoryRequest(Description: "");

            // Act
            var result = await _categoryService.UpdateCategoryAsync(category.Id, request);

            // Assert
            Assert.True(result.IsSuccess);
            Assert.NotNull(result.Value);
            Assert.Equal(string.Empty, result.Value.Description);
        }

        [Fact]
        public async Task UpdateCategoryAsync_WithIsActiveOnly_ShouldUpdateIsActiveAndKeepOtherFields()
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

            var request = new AdminUpdateCategoryRequest(IsActive: false);

            // Act
            var result = await _categoryService.UpdateCategoryAsync(category.Id, request);

            // Assert
            Assert.True(result.IsSuccess);
            Assert.NotNull(result.Value);
            Assert.Equal("Electronics", result.Value.Name);
            Assert.Equal("Gadgets", result.Value.Description);
            Assert.False(result.Value.IsActive);
        }

        [Fact]
        public async Task UpdateCategoryAsync_WithBlankName_ShouldIgnoreNameAndKeepExisting()
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

            var request = new AdminUpdateCategoryRequest(Name: "   ");

            // Act
            var result = await _categoryService.UpdateCategoryAsync(category.Id, request);

            // Assert
            Assert.True(result.IsSuccess);
            Assert.NotNull(result.Value);
            Assert.Equal("Electronics", result.Value.Name);
        }

        [Fact]
        public async Task UpdateCategoryAsync_WithAllFieldsProvided_ShouldUpdateAllFields()
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

            var request = new AdminUpdateCategoryRequest("Consumer Electronics", "Updated description", false);

            // Act
            var result = await _categoryService.UpdateCategoryAsync(category.Id, request);

            // Assert
            Assert.True(result.IsSuccess);
            Assert.NotNull(result.Value);
            Assert.Equal("Consumer Electronics", result.Value.Name);
            Assert.Equal("Updated description", result.Value.Description);
            Assert.False(result.Value.IsActive);
        }

        [Fact]
        public async Task UpdateCategoryAsync_WithNoFieldsProvided_ShouldLeaveCategoryUnchanged()
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

            var request = new AdminUpdateCategoryRequest();

            // Act
            var result = await _categoryService.UpdateCategoryAsync(category.Id, request);

            // Assert
            Assert.True(result.IsSuccess);
            Assert.NotNull(result.Value);
            Assert.Equal("Electronics", result.Value.Name);
            Assert.Equal("Gadgets", result.Value.Description);
            Assert.True(result.Value.IsActive);
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
        public async Task UpdateCategoryAsync_WithNonexistentId_ShouldReturnNotFoundError()
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
        }

        #endregion

        #region DeleteCategoryAsync Tests

        [Fact]
        public async Task DeleteCategoryAsync_WithExistingId_ShouldDeleteCategory()
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
    }
}