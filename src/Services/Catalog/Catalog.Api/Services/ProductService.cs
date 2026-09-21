using Catalog.Api.DTOs;
using Catalog.Infrastructure.Data;
using Catalog.Infrastructure.Models;
using Common.Core;
using Microsoft.EntityFrameworkCore;

namespace Catalog.Api.Services
{
    public class ProductService : IProductService
    {
        private readonly CatalogDbContext _dbContext;
        private readonly ILogger<ProductService> _logger;

        public ProductService(
            CatalogDbContext dbContext,
            ILogger<ProductService> logger)
        {
            _dbContext = dbContext;
            _logger = logger;
        }

        public async Task<Result<List<ProductResponse>>> GetProductsAsync(ProductQuery query)
        {
            var products = _dbContext.Products.AsNoTracking();

            if (query.Ids?.Length > 0)
            {
                products = products.Where(p => query.Ids.Contains(p.Id));
            }

            if (query.CategoryIds?.Length > 0)
            {
                products = products.Where(p => query.CategoryIds.Contains(p.CategoryId));
            }

            if (!string.IsNullOrWhiteSpace(query.Name))
            {
                products = products.Where(p => p.Name.Contains(query.Name));
            }

            if (!string.IsNullOrWhiteSpace(query.Description))
            {
                products = products.Where(p => p.Description.Contains(query.Description));
            }

            if (!string.IsNullOrWhiteSpace(query.Sku))
            {
                products = products.Where(p => p.Sku == query.Sku);
            }

            if (query.MinPrice.HasValue)
            {
                products = products.Where(p => p.Price >= query.MinPrice);
            }

            if (query.MaxPrice.HasValue)
            {
                products = products.Where(p => p.Price <= query.MaxPrice);
            }

            var result = await products
                .Select(p => new ProductResponse(
                    p.Id,
                    p.CategoryId,
                    p.Name,
                    p.Description,
                    p.Sku,
                    p.Price,
                    p.IsActive,
                    p.CreatedAtUtc,
                    p.UpdatedAtUtc))
                .ToListAsync();

            return Result<List<ProductResponse>>.Success(result);
        }

        public async Task<Result<ProductResponse>> GetProductAsync(Guid id)
        {
            var product = await _dbContext.Products
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == id);

            if (product is null)
            {
                _logger.LogWarning("Product not found. Product Id: {ProductId}", id);
                return Result<ProductResponse>.Failure("Product not found.", ResultErrorType.NotFound);
            }

            return Result<ProductResponse>.Success(new ProductResponse(
                    product.Id,
                    product.CategoryId,
                    product.Name,
                    product.Description,
                    product.Sku,
                    product.Price,
                    product.IsActive,
                    product.CreatedAtUtc,
                    product.UpdatedAtUtc));
        }

        public async Task<Result<ProductResponse>> CreateProductAsync(AdminCreateProductRequest request)
        {
            var product = new Product
            {
                CategoryId = request.CategoryId,
                Name = request.Name,
                Description = request.Description,
                Sku = request.Sku,
                Price = request.Price,
                IsActive = true
            };

            _dbContext.Add(product);
            await _dbContext.SaveChangesAsync();

            _logger.LogInformation("Product created. Product Id:{ProductId}, Name:{Name}", product.Id, product.Name);

            return Result<ProductResponse>.Success(new ProductResponse(
                product.Id,
                product.CategoryId,
                product.Name,
                product.Description,
                product.Sku,
                product.Price,
                product.IsActive,
                product.CreatedAtUtc,
                product.UpdatedAtUtc));
        }

        public async Task<Result<ProductResponse>> UpdateProductAsync(Guid id, AdminUpdateProductRequest request)
        {
            var product = await _dbContext.Products
                .FirstOrDefaultAsync(p => p.Id == id);

            if (product is null)
            {
                _logger.LogWarning("Product not found. Product Id:{ProductId}", id);
                return Result<ProductResponse>.Failure("Product not found.", ResultErrorType.NotFound);
            }

            if (request.CategoryId.HasValue)
            {
                var categoryExists = await _dbContext.Categories
                    .AnyAsync(c => c.Id == request.CategoryId);
                if (!categoryExists)
                {
                    return Result<ProductResponse>.Failure("Category does not exist.", ResultErrorType.BadRequest);
                }

                product.CategoryId = request.CategoryId.Value;
            }

            if (!string.IsNullOrWhiteSpace(request.Name))
            {
                product.Name = request.Name;
            }

            if (!string.IsNullOrWhiteSpace(request.Description))
            {
                product.Description = request.Description;
            }

            if (!string.IsNullOrWhiteSpace(request.Sku))
            {
                var skuExists = await _dbContext.Products
                    .AnyAsync(p => p.Sku == request.Sku);
                if (skuExists)
                {
                    return Result<ProductResponse>.Failure("A Product with this SKU already exists.", ResultErrorType.Conflict);
                }

                product.Sku = request.Sku;
            }

            if (request.Price.HasValue)
            {
                product.Price = request.Price.Value;
            }

            if (request.IsActive.HasValue)
            {
                product.IsActive = request.IsActive.Value;
            }

            product.UpdatedAtUtc = DateTime.UtcNow;

            await _dbContext.SaveChangesAsync();

            _logger.LogInformation("Product updated. Category Id:{ProductId}", product.Id);

            return Result<ProductResponse>.Success(new ProductResponse(
                product.Id,
                product.CategoryId,
                product.Name,
                product.Description,
                product.Sku,
                product.Price,
                product.IsActive,
                product.CreatedAtUtc,
                product.UpdatedAtUtc
                ));
        }

        public async Task<Result<bool>> DeleteProductAsync(Guid id)
        {
            var product = await _dbContext.Products
                .FirstOrDefaultAsync(p => p.Id == id);

            if (product is null)
            {
                _logger.LogWarning("Product not found. Product Id:{ProductId}", id);
                return Result<bool>.Failure("Product not found.", ResultErrorType.NotFound);
            }

            _dbContext.Products.Remove(product);
            await _dbContext.SaveChangesAsync();

            _logger.LogInformation("Product deleted. Product Id:{ProductId}", id);

            return Result<bool>.Success(true);
        }
    }
}
