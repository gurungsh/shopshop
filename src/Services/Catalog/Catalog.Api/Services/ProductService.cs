using Catalog.Api.DTOs;
using Common.Core;

namespace Catalog.Api.Services
{
    public class ProductService : IProductService
    {
        Task<Result<ProductResponse>> IProductService.GetProductAsync(Guid id)
        {
            throw new NotImplementedException();
        }

        Task<Result<List<ProductResponse>>> IProductService.GetProductsAsync(ProductQuery query)
        {
            throw new NotImplementedException();
        }

        Task<Result<ProductResponse>> IProductService.CreateProductAsync(AdminCreateProductRequest request)
        {
            throw new NotImplementedException();
        }

        Task<Result<ProductResponse>> IProductService.UpdateProductAsync(Guid id, AdminUpdateProductRequest request)
        {
            throw new NotImplementedException();
        }

        Task<Result<bool>> IProductService.DeleteProductAsync(Guid id)
        {
            throw new NotImplementedException();
        }
    }
}
