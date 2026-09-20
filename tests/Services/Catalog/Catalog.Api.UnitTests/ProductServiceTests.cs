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
    public class ProductServiceTests
    {
        private readonly ProductService _productService;
        private readonly CatalogDbContext _dbContext;
        private readonly Mock<ILogger<ProductService>> _mockLogger;

        public ProductServiceTests()
        {
            var options = new DbContextOptionsBuilder<CatalogDbContext>()
                .UseInMemoryDatabase($"test-db-{Guid.NewGuid()}")
                .Options;

            _dbContext = new CatalogDbContext(options);
            _mockLogger = new Mock<ILogger<ProductService>>();

            _productService = new ProductService(_dbContext, _mockLogger.Object);
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
            Assert.Equal(2, result.Value.Count);
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
            Assert.Equal(2, result.Value.Count);
            Assert.Contains(result.Value, p => p.Id == product1.Id);
            Assert.Contains(result.Value, p => p.Id == product3.Id);
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
            Assert.Equal(2, result.Value.Count);
        }

        [Fact]
        public async Task GetProductsAsync_WithNameFilter_ShouldReturnProductsContainingText()
        {
            // Arrange
            var categoryId = Guid.NewGuid();
            _dbContext.Products.AddRange(
                CreateTestProduct(categoryId, name: "Running Shoe", sku: "SKU-001"),
                CreateTestProduct(categoryId, name: "Hiking Boot", sku: "SKU-002"));
            await _dbContext.SaveChangesAsync();

            var query = new ProductQuery(Name: "Shoe");

            // Act
            var result = await _productService.GetProductsAsync(query);

            // Assert
            Assert.True(result.IsSuccess);
            Assert.NotNull(result.Value);
            Assert.Single(result.Value);
            Assert.Equal("Running Shoe", result.Value[0].Name);
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
            Assert.Single(result.Value);
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
            Assert.Single(result.Value);
            Assert.Equal("SKU-001", result.Value[0].Sku);
        }

        [Fact]
        public async Task GetProductsAsync_WithMinPriceFilter_ShouldReturnProductsAtOrAboveMinPrice()
        {
            // Arrange
            var categoryId = Guid.NewGuid();
            _dbContext.Products.AddRange(
                CreateTestProduct(categoryId, sku: "SKU-001", price: 10m),
                CreateTestProduct(categoryId, sku: "SKU-002", price: 50m),
                CreateTestProduct(categoryId, sku: "SKU-003", price: 100m));
            await _dbContext.SaveChangesAsync();

            var query = new ProductQuery(MinPrice: 50m);

            // Act
            var result = await _productService.GetProductsAsync(query);

            // Assert
            Assert.True(result.IsSuccess);
            Assert.NotNull(result.Value);
            Assert.Equal(2, result.Value.Count);
            Assert.All(result.Value, p => Assert.True(p.Price >= 50m));
        }

        [Fact]
        public async Task GetProductsAsync_WithMaxPriceFilter_ShouldReturnProductsAtOrBelowMaxPrice()
        {
            // Arrange
            var categoryId = Guid.NewGuid();
            _dbContext.Products.AddRange(
                CreateTestProduct(categoryId, sku: "SKU-001", price: 10m),
                CreateTestProduct(categoryId, sku: "SKU-002", price: 50m),
                CreateTestProduct(categoryId, sku: "SKU-003", price: 100m));
            await _dbContext.SaveChangesAsync();

            var query = new ProductQuery(MaxPrice: 50m);

            // Act
            var result = await _productService.GetProductsAsync(query);

            // Assert
            Assert.True(result.IsSuccess);
            Assert.NotNull(result.Value);
            Assert.Equal(2, result.Value.Count);
            Assert.All(result.Value, p => Assert.True(p.Price <= 50m));
        }

        [Fact]
        public async Task GetProductsAsync_WithMinAndMaxPriceFilters_ShouldReturnProductsWithinRange()
        {
            // Arrange
            var categoryId = Guid.NewGuid();
            _dbContext.Products.AddRange(
                CreateTestProduct(categoryId, sku: "SKU-001", price: 10m),
                CreateTestProduct(categoryId, sku: "SKU-002", price: 50m),
                CreateTestProduct(categoryId, sku: "SKU-003", price: 100m));
            await _dbContext.SaveChangesAsync();

            var query = new ProductQuery(MinPrice: 20m, MaxPrice: 80m);

            // Act
            var result = await _productService.GetProductsAsync(query);

            // Assert
            Assert.True(result.IsSuccess);
            Assert.NotNull(result.Value);
            Assert.Single(result.Value);
            Assert.Equal("SKU-002", result.Value[0].Sku);
        }

        [Fact]
        public async Task GetProductsAsync_WithNonMatchingFilters_ShouldReturnEmptyList()
        {
            // Arrange
            var categoryId = Guid.NewGuid();
            _dbContext.Products.Add(CreateTestProduct(categoryId, sku: "SKU-001"));
            await _dbContext.SaveChangesAsync();

            var query = new ProductQuery(Name: "Nonexistent");

            // Act
            var result = await _productService.GetProductsAsync(query);

            // Assert
            Assert.True(result.IsSuccess);
            Assert.NotNull(result.Value);
            Assert.Empty(result.Value);
        }

        #endregion

        #region GetProductAsync Tests

        [Fact]
        public async Task GetProductAsync_WithExistingId_ShouldReturnProduct()
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
        }

        [Fact]
        public async Task GetProductAsync_WithNonexistentId_ShouldReturnNotFoundError()
        {
            // Arrange
            var nonexistentId = Guid.NewGuid();

            // Act
            var result = await _productService.GetProductAsync(nonexistentId);

            // Assert
            Assert.False(result.IsSuccess);
            Assert.Equal("Product not found.", result.Error);
            Assert.Equal(ResultErrorType.NotFound, result.ErrorType);
        }

        #endregion

        #region CreateProductAsync Tests

        [Fact]
        public async Task CreateProductAsync_WithValidRequest_ShouldCreateProduct()
        {
            // Arrange
            var categoryId = Guid.NewGuid();
            var request = new AdminCreateProductRequest(
                categoryId, "Running Shoe", "Comfortable running shoe", "SKU-001", 49.99m);

            // Act
            var result = await _productService.CreateProductAsync(request);

            // Assert
            Assert.True(result.IsSuccess);
            Assert.NotNull(result.Value);
            Assert.Equal("Running Shoe", result.Value.Name);
            Assert.Equal("Comfortable running shoe", result.Value.Description);
            Assert.Equal("SKU-001", result.Value.Sku);
            Assert.Equal(49.99m, result.Value.Price);
            Assert.Equal(categoryId, result.Value.CategoryId);
            Assert.NotEqual(Guid.Empty, result.Value.Id);

            var createdProduct = await _dbContext.Products.FirstOrDefaultAsync(p => p.Sku == "SKU-001");
            Assert.NotNull(createdProduct);
            Assert.Equal("Running Shoe", createdProduct.Name);
        }

        [Fact]
        public async Task CreateProductAsync_WithValidRequest_ShouldDefaultIsActiveToTrue()
        {
            // Arrange
            var request = new AdminCreateProductRequest(
                Guid.NewGuid(), "Running Shoe", "Comfortable running shoe", "SKU-001", 49.99m);

            // Act
            var result = await _productService.CreateProductAsync(request);

            // Assert
            Assert.True(result.IsSuccess);
            Assert.NotNull(result.Value);
            Assert.True(result.Value.IsActive);
        }

        [Fact]
        public async Task CreateProductAsync_WithValidRequest_ShouldSetCreatedAndUpdatedTimestamps()
        {
            // Arrange
            var request = new AdminCreateProductRequest(
                Guid.NewGuid(), "Running Shoe", "Comfortable running shoe", "SKU-001", 49.99m);
            var beforeCreate = DateTime.UtcNow;

            // Act
            var result = await _productService.CreateProductAsync(request);
            var afterCreate = DateTime.UtcNow;

            // Assert
            Assert.True(result.IsSuccess);
            Assert.NotNull(result.Value);
            Assert.InRange(result.Value.CreatedAtUtc, beforeCreate, afterCreate);
            Assert.InRange(result.Value.UpdatedAtUtc, beforeCreate, afterCreate);
        }

        #endregion

        #region UpdateProductAsync Tests

        [Fact]
        public async Task UpdateProductAsync_WithNameOnly_ShouldUpdateNameAndKeepOtherFields()
        {
            // Arrange
            var categoryId = Guid.NewGuid();
            var product = CreateTestProduct(categoryId, sku: "SKU-001");
            _dbContext.Products.Add(product);
            await _dbContext.SaveChangesAsync();

            var request = new AdminUpdateProductRequest(Name: "Trail Runner");

            // Act
            var result = await _productService.UpdateProductAsync(product.Id, request);

            // Assert
            Assert.True(result.IsSuccess);
            Assert.NotNull(result.Value);
            Assert.Equal("Trail Runner", result.Value.Name);
            Assert.Equal(product.Description, result.Value.Description);
            Assert.Equal(product.Sku, result.Value.Sku);
            Assert.Equal(product.Price, result.Value.Price);
        }

        [Fact]
        public async Task UpdateProductAsync_WithDescriptionOnly_ShouldUpdateDescriptionAndKeepOtherFields()
        {
            // Arrange
            var categoryId = Guid.NewGuid();
            var product = CreateTestProduct(categoryId, sku: "SKU-001");
            _dbContext.Products.Add(product);
            await _dbContext.SaveChangesAsync();

            var request = new AdminUpdateProductRequest(Description: "Updated description");

            // Act
            var result = await _productService.UpdateProductAsync(product.Id, request);

            // Assert
            Assert.True(result.IsSuccess);
            Assert.NotNull(result.Value);
            Assert.Equal(product.Name, result.Value.Name);
            Assert.Equal("Updated description", result.Value.Description);
        }

        [Fact]
        public async Task UpdateProductAsync_WithPriceOnly_ShouldUpdatePriceAndKeepOtherFields()
        {
            // Arrange
            var categoryId = Guid.NewGuid();
            var product = CreateTestProduct(categoryId, sku: "SKU-001", price: 49.99m);
            _dbContext.Products.Add(product);
            await _dbContext.SaveChangesAsync();

            var request = new AdminUpdateProductRequest(Price: 59.99m);

            // Act
            var result = await _productService.UpdateProductAsync(product.Id, request);

            // Assert
            Assert.True(result.IsSuccess);
            Assert.NotNull(result.Value);
            Assert.Equal(59.99m, result.Value.Price);
            Assert.Equal(product.Name, result.Value.Name);
        }

        [Fact]
        public async Task UpdateProductAsync_WithIsActiveOnly_ShouldUpdateIsActiveAndKeepOtherFields()
        {
            // Arrange
            var categoryId = Guid.NewGuid();
            var product = CreateTestProduct(categoryId, sku: "SKU-001", isActive: true);
            _dbContext.Products.Add(product);
            await _dbContext.SaveChangesAsync();

            var request = new AdminUpdateProductRequest(IsActive: false);

            // Act
            var result = await _productService.UpdateProductAsync(product.Id, request);

            // Assert
            Assert.True(result.IsSuccess);
            Assert.NotNull(result.Value);
            Assert.False(result.Value.IsActive);
            Assert.Equal(product.Name, result.Value.Name);
        }

        [Fact]
        public async Task UpdateProductAsync_WithValidCategoryId_ShouldUpdateCategoryId()
        {
            // Arrange
            var originalCategoryId = Guid.NewGuid();
            var newCategoryId = Guid.NewGuid();
            _dbContext.Categories.Add(new Category { Id = newCategoryId, Name = "Footwear" });
            var product = CreateTestProduct(originalCategoryId, sku: "SKU-001");
            _dbContext.Products.Add(product);
            await _dbContext.SaveChangesAsync();

            var request = new AdminUpdateProductRequest(CategoryId: newCategoryId);

            // Act
            var result = await _productService.UpdateProductAsync(product.Id, request);

            // Assert
            Assert.True(result.IsSuccess);
            Assert.NotNull(result.Value);
            Assert.Equal(newCategoryId, result.Value.CategoryId);
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
        public async Task UpdateProductAsync_WithUniqueSku_ShouldUpdateSku()
        {
            // Arrange
            var categoryId = Guid.NewGuid();
            var product = CreateTestProduct(categoryId, sku: "SKU-001");
            _dbContext.Products.Add(product);
            await _dbContext.SaveChangesAsync();

            var request = new AdminUpdateProductRequest(Sku: "SKU-999");

            // Act
            var result = await _productService.UpdateProductAsync(product.Id, request);

            // Assert
            Assert.True(result.IsSuccess);
            Assert.NotNull(result.Value);
            Assert.Equal("SKU-999", result.Value.Sku);
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
        public async Task UpdateProductAsync_WithAllFieldsProvided_ShouldUpdateAllFields()
        {
            // Arrange
            var categoryId = Guid.NewGuid();
            var newCategoryId = Guid.NewGuid();
            _dbContext.Categories.Add(new Category { Id = newCategoryId, Name = "Footwear" });
            var product = CreateTestProduct(categoryId, sku: "SKU-001");
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
        }

        [Fact]
        public async Task UpdateProductAsync_WithNoFieldsProvided_ShouldLeaveProductUnchanged()
        {
            // Arrange
            var categoryId = Guid.NewGuid();
            var product = CreateTestProduct(categoryId, sku: "SKU-001");
            _dbContext.Products.Add(product);
            await _dbContext.SaveChangesAsync();

            var request = new AdminUpdateProductRequest();

            // Act
            var result = await _productService.UpdateProductAsync(product.Id, request);

            // Assert
            Assert.True(result.IsSuccess);
            Assert.NotNull(result.Value);
            Assert.Equal(product.Name, result.Value.Name);
            Assert.Equal(product.Description, result.Value.Description);
            Assert.Equal(product.Sku, result.Value.Sku);
            Assert.Equal(product.Price, result.Value.Price);
            Assert.Equal(product.IsActive, result.Value.IsActive);
        }

        [Fact]
        public async Task UpdateProductAsync_WithValidRequest_ShouldUpdateTimestamp()
        {
            // Arrange
            var originalUpdatedAt = DateTime.UtcNow.AddDays(-1);
            var categoryId = Guid.NewGuid();
            var product = CreateTestProduct(categoryId, sku: "SKU-001");
            product.UpdatedAtUtc = originalUpdatedAt;
            _dbContext.Products.Add(product);
            await _dbContext.SaveChangesAsync();

            var request = new AdminUpdateProductRequest(Name: "Trail Runner");

            // Act
            var result = await _productService.UpdateProductAsync(product.Id, request);

            // Assert
            Assert.True(result.IsSuccess);
            Assert.NotNull(result.Value);
            Assert.True(result.Value.UpdatedAtUtc > originalUpdatedAt);
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
        public async Task DeleteProductAsync_WithExistingId_ShouldDeleteProduct()
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
    }
}