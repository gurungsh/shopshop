using Catalog.Api.DTOs;
using Catalog.Infrastructure.Data;
using Catalog.Infrastructure.Models;
using Common.Core;
using Microsoft.EntityFrameworkCore;

namespace Catalog.Api.Services
{
    public class CategoryService : ICategoryService
    {
        private readonly CatalogDbContext _dbContext;
        private readonly ILogger<CategoryService> _logger;

        public CategoryService(
            CatalogDbContext dbContext,
            ILogger<CategoryService> logger)
        {
            _dbContext = dbContext;
            _logger = logger;
        }

        public async Task<Result<List<CategoryResponse>>> GetCategoriesAsync(CategoryQuery query)
        {
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

            var result = await categories
                .Select(c => new CategoryResponse(
                    c.Id,
                    c.Name,
                    c.Description,
                    c.IsActive,
                    c.CreatedAtUtc,
                    c.UpdatedAtUtc))
                .ToListAsync();

            return Result<List<CategoryResponse>>.Success(result);
        }

        public async Task<Result<CategoryResponse>> GetCategoryAsync(Guid id)
        {
            var category = await _dbContext.Categories
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == id);

            if (category is null)
            {
                _logger.LogWarning("Category not found. Category Id:{CategoryId}", id);
                return Result<CategoryResponse>.Failure("Category not found.", ResultErrorType.NotFound);
            }

            return Result<CategoryResponse>.Success(new CategoryResponse(
                category.Id,
                category.Name,
                category.Description,
                category.IsActive,
                category.CreatedAtUtc,
                category.UpdatedAtUtc));
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

            _logger.LogInformation("Category deleted. Category Id:{CategoryId}", id);

            return Result<bool>.Success(true);
        }
    }
}
