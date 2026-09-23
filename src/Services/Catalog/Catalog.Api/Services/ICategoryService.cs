using Catalog.Api.DTOs;
using Common.Core;

namespace Catalog.Api.Services
{
    public interface ICategoryService
    {
        Task<Result<List<CategoryResponse>>> GetCategoriesAsync(CategoryQuery query);
        Task<Result<CategoryResponse>> GetCategoryAsync(Guid id);
        Task<Result<CategoryResponse>> CreateCategoryAsync(AdminCreateCategoryRequest request);
        Task<Result<CategoryResponse>> UpdateCategoryAsync(Guid id, AdminUpdateCategoryRequest request);
        Task<Result<bool>> DeleteCategoryAsync(Guid id);
    }
}
