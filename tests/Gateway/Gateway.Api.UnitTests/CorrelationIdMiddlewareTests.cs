using Gateway.Api.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace Gateway.Api.UnitTests;

public class CorrelationIdMiddlewareTests
{
    private const string HeaderName = CorrelationIdMiddleware.HeaderName;

    private static async Task<HttpContext> InvokeAsync(HttpContext context, RequestDelegate? next = null)
    {
        var responseFeature = new CallbackResponseFeature();
        context.Features.Set<IHttpResponseFeature>(responseFeature);

        var middleware = new CorrelationIdMiddleware(next ?? (_ => Task.CompletedTask));
        await middleware.InvokeAsync(context);
        await responseFeature.StartAsync();
        return context;
    }

    #region InvokeAsync Tests

    [Fact]
    public async Task InvokeAsync_RequestHasCorrelationId_ShouldKeepIt()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Request.Headers[HeaderName] = "abc-123";

        // Act
        await InvokeAsync(context);

        // Assert
        Assert.Equal("abc-123", context.Request.Headers[HeaderName].ToString());
        Assert.Equal("abc-123", context.Response.Headers[HeaderName].ToString());
    }

    [Fact]
    public async Task InvokeAsync_RequestHasNoCorrelationId_ShouldGenerateOne()
    {
        // Arrange
        var context = new DefaultHttpContext();

        // Act
        await InvokeAsync(context);

        // Assert
        var requestId = context.Request.Headers[HeaderName].ToString();
        Assert.True(Guid.TryParse(requestId, out _));
        Assert.Equal(requestId, context.Response.Headers[HeaderName].ToString());
    }

    [Fact]
    public async Task InvokeAsync_ShouldCallNextMiddleware()
    {
        // Arrange
        var context = new DefaultHttpContext();
        var nextCalled = false;

        // Act
        await InvokeAsync(context, _ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        // Assert
        Assert.True(nextCalled);
    }

    #endregion

    private sealed class CallbackResponseFeature : HttpResponseFeature
    {
        private readonly List<(Func<object, Task> Callback, object State)> _callbacks = [];

        public override void OnStarting(Func<object, Task> callback, object state)
            => _callbacks.Add((callback, state));

        public async Task StartAsync()
        {
            foreach (var (callback, state) in _callbacks)
            {
                await callback(state);
            }
        }
    }
}