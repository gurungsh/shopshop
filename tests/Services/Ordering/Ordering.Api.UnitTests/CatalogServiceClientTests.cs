using System.Net;
using System.Text.Json;
using BuildingBlocks.Core;
using Microsoft.Extensions.Logging;
using Moq;
using Ordering.Api.Services;

namespace Ordering.Api.UnitTests;

public class CatalogServiceClientTests
{
    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return await respond(request);
        }
    }

    private static CatalogServiceClient CreateClient(StubHandler handler)
        => new(
            new HttpClient(handler) { BaseAddress = new Uri("https://catalog.test/") },
            new Mock<ILogger<CatalogServiceClient>>().Object);

    #region GetProductsAsync Tests

    [Fact]
    public async Task GetProductsAsync_WithPagedResponse_ShouldReturnProductsById()
    {
        // Arrange
        var productId = Guid.NewGuid();
        var body = JsonSerializer.Serialize(new
        {
            items = new[]
            {
                new
                {
                    id = productId,
                    categoryId = Guid.NewGuid(),
                    name = "Running Shoe",
                    description = "Fast",
                    sku = "SKU-001",
                    price = 59.99m,
                    isActive = true,
                    createdAtUtc = DateTime.UtcNow,
                    updatedAtUtc = DateTime.UtcNow
                }
            },
            page = 1,
            pageSize = 1,
            totalCount = 1,
            totalPages = 1
        });
        var handler = new StubHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
        }));
        var client = CreateClient(handler);

        // Act
        var result = await client.GetProductsAsync([productId]);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("Running Shoe", result.Value![productId].Name);
    }

    [Fact]
    public async Task GetProductsAsync_WithManyIds_ShouldRequestPageSizeEqualToDistinctIdCount()
    {
        // Arrange
        var ids = Enumerable.Range(0, 30).Select(_ => Guid.NewGuid()).ToList();
        var handler = new StubHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"items":[],"page":1,"pageSize":30,"totalCount":0,"totalPages":0}""", System.Text.Encoding.UTF8, "application/json")
        }));
        var client = CreateClient(handler);

        // Act
        await client.GetProductsAsync(ids.Concat(ids));

        // Assert
        using var json = JsonDocument.Parse(handler.RequestBody!);
        Assert.Equal(1, json.RootElement.GetProperty("page").GetInt32());
        Assert.Equal(30, json.RootElement.GetProperty("pageSize").GetInt32());
    }

    [Fact]
    public async Task GetProductsAsync_WithServerError_ShouldReturnServiceUnavailable()
    {
        // Arrange
        var handler = new StubHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)));
        var client = CreateClient(handler);

        // Act
        var result = await client.GetProductsAsync([Guid.NewGuid()]);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(ResultErrorType.ServiceUnavailable, result.ErrorType);
    }

    #endregion
}
