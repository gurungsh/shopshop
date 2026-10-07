using BuildingBlocks.Messaging;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Payment.Api.Messaging;
using Payment.Api.Options;
using Payment.Api.Services;
using Payment.Infrastructure.Data;
using Serilog;
using Stripe;

// Logger setup
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    Log.Information("Starting Payment.Api application.");

    var builder = WebApplication.CreateBuilder(args);

    // Serilog
    builder.Host.UseSerilog((context, services, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services));

    // Configuration and options
    var stripeSettings = builder.Configuration
        .GetSection(StripeOptions.SectionName)
        .Get<StripeOptions>()
        ?? throw new InvalidOperationException("Stripe configuration is missing.");

    if (string.IsNullOrWhiteSpace(stripeSettings.SecretKey))
    {
        throw new InvalidOperationException("Stripe SecretKey is missing.");
    }

    // Infrastructure and database services
    var connectionString = builder.Configuration.GetConnectionString("PaymentDb")
        ?? throw new InvalidOperationException("Connection string 'PaymentDb' is missing.");

    builder.Services.AddDbContext<PaymentDbContext>(options =>
        options.UseNpgsql(connectionString));

    // Payment provider
    builder.Services.AddSingleton<IStripeClient>(new StripeClient(stripeSettings.SecretKey));
    builder.Services.AddSingleton(sp => new PaymentIntentService(sp.GetRequiredService<IStripeClient>()));

    // Application business services
    builder.Services.AddScoped<IPaymentService, StripePaymentService>();
    builder.Services.AddScoped<IPaymentProcessingService, PaymentProcessingService>();

    // Messaging: payment requests are consumed from the orders exchange, results go through the outbox to the payments exchange
    builder.Services.AddRabbitMq(builder.Configuration);
    builder.Services.Configure<OutboxOptions>(
        builder.Configuration.GetSection(OutboxOptions.SectionName));
    builder.Services.AddHostedService<OutboxPublisher>();
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

    // Development tooling
    if (app.Environment.IsDevelopment())
    {
        Log.Information("Running in Development environment.");

        using var scope = app.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();
        await dbContext.Database.MigrateAsync();
    }

    // Security middleware - HTTPS redirect
    app.UseHttpsRedirection();

    // Endpoint routing: Payment has no public API, it only reacts to events
    app.MapGet("/health", () => Results.Ok(new { Status = "Healthy", Service = "Payment.Api" }));

    Log.Information("Application pipeline configured. Running application.");

    app.Run();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "Application terminated unexpectedly.");
}
finally
{
    Log.Information("Shutting down Payment.Api.");
    Log.CloseAndFlush();
}
