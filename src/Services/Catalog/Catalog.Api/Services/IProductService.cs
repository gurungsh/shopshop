using Catalog.Api.DTOs;
using Common.Core;

namespace Catalog.Api.Services
{
    public interface IProductService
    {
        Task<Result<List<ProductResponse>>> GetProductsAsync(ProductQuery query);
        Task<Result<ProductResponse>> GetProductAsync(Guid id);
        Task<Result<ProductResponse>> CreateProductAsync(AdminCreateProductRequest request);
        Task<Result<ProductResponse>> UpdateProductAsync(Guid id, AdminUpdateProductRequest request);
        Task<Result<bool>> DeleteProductAsync(Guid id);
    }
}
