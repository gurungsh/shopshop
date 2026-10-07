using BuildingBlocks.Messaging;
using Microsoft.AspNetCore.Diagnostics;
using Notification.Api.Messaging;
using Notification.Api.Services;
using Serilog;

// Logger setup
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    Log.Information("Starting Notification.Api application.");

    var builder = WebApplication.CreateBuilder(args);

    // Serilog
    builder.Host.UseSerilog((context, services, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services));

    // Application business services
    builder.Services.AddScoped<INotificationService, NotificationService>();

    // Messaging: order status events are consumed from the orders exchange
    builder.Services.AddRabbitMq(builder.Configuration);
    builder.Services.AddHostedService<OrderEventsConsumer>();

    var app = builder.Build();

    // Request logging early in the pipeline
    app.UseSerilogRequestLogging();

    // Global exception handling
    app.UseExceptionHandler(errorApp =>
    {
        errorApp.Run(async context =>
        {
            var exceptionFeature = context.Features.Get<IExceptionHandlerFeature>();
            Log.Error(exceptionFeature?.Error, "Unhandled exception occurred.");

            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(new { error = "An unexpected error occurred." });
        });
    });

    if (app.Environment.IsDevelopment())
    {
        Log.Information("Running in Development environment.");
    }

    // Security middleware - HTTPS redirect
    app.UseHttpsRedirection();

    // Endpoint routing
    app.MapGet("/health", () => Results.Ok(new { Status = "Healthy", Service = "Notification.Api" }));

    Log.Information("Application pipeline configured. Running application.");

    app.Run();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "Application terminated unexpectedly.");
}
finally
{
    Log.Information("Shutting down Notification.Api.");
    Log.CloseAndFlush();
}
