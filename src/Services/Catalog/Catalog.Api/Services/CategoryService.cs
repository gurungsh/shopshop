using BuildingBlocks.Core;
using Catalog.Api.Cacheing;
using Catalog.Api.Caching;
using Catalog.Api.DTOs;
using Catalog.Api.Options;
using Catalog.Infrastructure.Data;
using Catalog.Infrastructure.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Catalog.Api.Services
{
    public class CategoryService : ICategoryService
    {
        private readonly CatalogDbContext _dbContext;
        private readonly ICatalogCache _cache;
        private readonly CacheOptions _cacheOptions;
        private readonly ILogger<CategoryService> _logger;

        public CategoryService(
            CatalogDbContext dbContext,
            ICatalogCache cache,
            IOptions<CacheOptions> cacheOptions,
            ILogger<CategoryService> logger)
        {
            _dbContext = dbContext;
            _cache = cache;
            _cacheOptions = cacheOptions.Value;
            _logger = logger;
        }

        public async Task<Result<PagedResponse<CategoryResponse>>> GetCategoriesAsync(CategoryQuery query)
        {
            var version = await _cache.GetVersionAsync();
            string? cacheKey = null;

            if (version is not null)
            {
                cacheKey = CatalogCacheKeys.CategorySearch(version, query);
                var cached = await _cache.GetAsync<PagedResponse<CategoryResponse>>(cacheKey);
                if (cached is not null)
                {
                    return Result<PagedResponse<CategoryResponse>>.Success(cached);
                }
            }

            var categories = _dbContext.Categories.AsNoTracking();

            if (query.Ids?.Length > 0)
            {
                categories = categories.Where(c => query.Ids.Contains(c.Id));
            }

            if (!string.IsNullOrWhiteSpace(query.Name))
            {
                categories = categories.Where(c => string.Equals(c.Name, query.Name));
            }

            if (!string.IsNullOrWhiteSpace(query.Description))
            {
                categories = categories.Where(c => c.Description.Contains(query.Description));
            }

            var totalCount = await categories.CountAsync();

            var items = await categories
                .OrderBy(c => c.Name)
                .ThenBy(c => c.Id)
                .Skip((query.Page - 1) * query.PageSize)
                .Take(query.PageSize)
                .Select(c => new CategoryResponse(
                    c.Id,
                    c.Name,
                    c.Description,
                    c.IsActive,
                    c.CreatedAtUtc,
                    c.UpdatedAtUtc))
                .ToListAsync();

            var response = new PagedResponse<CategoryResponse>(items, query.Page, query.PageSize, totalCount);

            if (cacheKey is not null)
            {
                await _cache.SetAsync(cacheKey, response, TimeSpan.FromMinutes(_cacheOptions.SearchTtlMinutes));
            }

            return Result<PagedResponse<CategoryResponse>>.Success(response);
        }

        public async Task<Result<CategoryResponse>> GetCategoryAsync(Guid id)
        {
            var cacheKey = CatalogCacheKeys.Category(id);
            var cached = await _cache.GetAsync<CategoryResponse>(cacheKey);
            if (cached is not null)
            {
                return Result<CategoryResponse>.Success(cached);
            }

            var category = await _dbContext.Categories
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == id);

            if (category is null)
            {
                _logger.LogWarning("Category not found. Category Id:{CategoryId}", id);
                return Result<CategoryResponse>.Failure("Category not found.", ResultErrorType.NotFound);
            }

            var response = new CategoryResponse(
                category.Id,
                category.Name,
                category.Description,
                category.IsActive,
                category.CreatedAtUtc,
                category.UpdatedAtUtc);

            await _cache.SetAsync(cacheKey, response, TimeSpan.FromMinutes(_cacheOptions.EntityTtlMinutes));

            return Result<CategoryResponse>.Success(response);
        }

        public async Task<Result<CategoryResponse>> CreateCategoryAsync(AdminCreateCategoryRequest request)
        {
            var category = new Category
            {
                Name = request.Name,
                Description = request.Description ?? string.Empty
            };

            _dbContext.Categories.Add(category);
            await _dbContext.SaveChangesAsync();
            await _cache.BumpVersionAsync();

            _logger.LogInformation("Category created. Category Id:{CategoryId}, Name:{Name}", category.Id, category.Name);

            return Result<CategoryResponse>.Success(new CategoryResponse(
                category.Id,
                category.Name,
                category.Description,
                category.IsActive,
                category.CreatedAtUtc,
                category.UpdatedAtUtc));
        }

        public async Task<Result<CategoryResponse>> UpdateCategoryAsync(Guid id, AdminUpdateCategoryRequest request)
        {
            var category = await _dbContext.Categories
                .FirstOrDefaultAsync(c => c.Id == id);

            if (category is null)
            {
                _logger.LogWarning("Category not found. Category Id:{CategoryId}", id);
                return Result<CategoryResponse>.Failure("Category not found.", ResultErrorType.NotFound);
            }

            if (!string.IsNullOrWhiteSpace(request.Name))
            {
                category.Name = request.Name;
            }

            if (request.Description is not null)
            {
                category.Description = request.Description;
            }

            if (request.IsActive.HasValue)
            {
                category.IsActive = request.IsActive.Value;
            }

            category.UpdatedAtUtc = DateTime.UtcNow;

            await _dbContext.SaveChangesAsync();
            await _cache.RemoveAsync(CatalogCacheKeys.Category(category.Id));
            await _cache.BumpVersionAsync();

            _logger.LogInformation("Category updated. Category Id:{CategoryId}", category.Id);

            return Result<CategoryResponse>.Success(new CategoryResponse(
                category.Id,
                category.Name,
                category.Description,
                category.IsActive,
                category.CreatedAtUtc,
                category.UpdatedAtUtc));
        }

        public async Task<Result<bool>> DeleteCategoryAsync(Guid id)
        {
            var category = await _dbContext.Categories
                .FirstOrDefaultAsync(c => c.Id == id);

            if (category is null)
            {
                _logger.LogWarning("Category not found. Category Id:{CategoryId}", id);
                return Result<bool>.Failure("Category not found.", ResultErrorType.NotFound);
            }

            _dbContext.Categories.Remove(category);
            await _dbContext.SaveChangesAsync();
            await _cache.RemoveAsync(CatalogCacheKeys.Category(id));
            await _cache.BumpVersionAsync();

            _logger.LogInformation("Category deleted. Category Id:{CategoryId}", id);

            return Result<bool>.Success(true);
        }
    }
}
