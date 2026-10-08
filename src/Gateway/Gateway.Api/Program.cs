using System.Threading.RateLimiting;
using Gateway.Api.Middleware;
using Microsoft.AspNetCore.Diagnostics;
using Serilog;

// Logger setup
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    Log.Information("Starting Gateway.Api application.");

    var builder = WebApplication.CreateBuilder(args);

    // Serilog
    builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services));

    // CORS
    var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
        ?? throw new InvalidOperationException("Cors:AllowedOrigins is not configured.");

    if (allowedOrigins.Length == 0)
    {
        throw new InvalidOperationException("Cors:AllowedOrigins must contain at least one origin.");
    }

    builder.Services.AddCors(options =>
    {
        options.AddDefaultPolicy(policy => policy
        .WithOrigins(allowedOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod()
        .WithExposedHeaders(CorrelationIdMiddleware.HeaderName));
    });

    // Rate limiting
    builder.Services.AddRateLimiter(options =>
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 100,
                Window = TimeSpan.FromMinutes(1)
            }));
    });

    // Reverse proxy
    builder.Services.AddReverseProxy()
        .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

    var app = builder.Build();

    app.UseMiddleware<CorrelationIdMiddleware>();

    // Serilog
    app.UseSerilogRequestLogging();

    // Global exception handling
    app.UseExceptionHandler(errorApp =>
    {
        errorApp.Run(async context =>
        {
            var exceptionFeature = context.Features.Get<IExceptionHandlerFeature>();
            Log.Error(exceptionFeature?.Error, "Unhandled exception occured.");

            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(new { error = "An unexpected error occured." });
        });
    });

    if (app.Environment.IsDevelopment())
    {
        Log.Information("Running in Development environment.");

        // Combined Swagger UI: each entry loads a service's own document through the proxy
        app.UseSwaggerUI(options =>
        {
            options.SwaggerEndpoint("/docs/identity/swagger.json", "Identity");
            options.SwaggerEndpoint("/docs/catalog/swagger.json", "Catalog");
            options.SwaggerEndpoint("/docs/ordering/swagger.json", "Ordering");
        });
    }

    // Security middleware - HTTPS redirect
    app.UseHttpsRedirection();
    app.UseCors();
    app.UseRateLimiter();

    // Endpoint routing
    app.MapGet("/health", () => Results.Ok(new { Status = "Healthy", Service = "Gateway.Api" }));
    app.MapReverseProxy();

    Log.Information("Application pipeline configured. Running application.");

    app.Run();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "Application terminated unexpectedly.");
}
finally
{
    Log.Information("Shutting down Gateway.Api.");
    Log.CloseAndFlush();
}