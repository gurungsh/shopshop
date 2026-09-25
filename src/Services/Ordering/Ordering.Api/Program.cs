using System.Text;
using System.Text.Json.Serialization;
using FluentValidation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using Ordering.Api.Endpoints;
using Ordering.Api.Options;
using Ordering.Api.Services;
using Ordering.Infrastructure.Data;
using Serilog;

// Logger setup
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    Log.Information("Starting Ordering.Api application.");

    var builder = WebApplication.CreateBuilder(args);

    // Serilog
    builder.Host.UseSerilog((context, services, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services));

    // Configuration and options
    var jwtSettings = builder.Configuration
        .GetSection(JwtOptions.SectionName)
        .Get<JwtOptions>()
        ?? throw new InvalidOperationException("JwtSettings configuration is missing.");

    if (string.IsNullOrWhiteSpace(jwtSettings.Secret) || jwtSettings.Secret.Length < 32)
    {
        throw new InvalidOperationException("JWT Secret is missing or invalid.");
    }

    builder.Services.Configure<JwtOptions>(
        builder.Configuration.GetSection(JwtOptions.SectionName));

    var catalogServiceSettings = builder.Configuration
        .GetSection(CatalogServiceOptions.SectionName)
        .Get<CatalogServiceOptions>()
        ?? throw new InvalidOperationException("CatalogService configuration is missing.");

    if (!Uri.TryCreate(catalogServiceSettings.BaseUrl, UriKind.Absolute, out var catalogBaseUri))
    {
        throw new InvalidOperationException("CatalogService BaseUrl is missing or invalid.");
    }

    // Infrastructure and database services
    var connectionString = builder.Configuration.GetConnectionString("OrderDb")
        ?? throw new InvalidOperationException("Connection string 'OrderDb' is missing.");

    builder.Services.AddDbContext<OrderingDbContext>(options =>
        options.UseNpgsql(connectionString));

    // Authentication and authorization
    builder.Services
        .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            options.Events = new JwtBearerEvents
            {
                OnAuthenticationFailed = context =>
                {
                    Log.Warning(context.Exception, "JWT authentication failed.");
                    return Task.CompletedTask;
                }
            };

            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.Secret)),
                ValidateIssuer = true,
                ValidIssuer = jwtSettings.Issuer,
                ValidateAudience = true,
                ValidAudience = jwtSettings.Audience,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromSeconds(30)
            };
        });

    builder.Services.AddAuthorization();

    // Application business services
    builder.Services.AddScoped<IOrderingService, OrderingService>();
    builder.Services.AddValidatorsFromAssemblyContaining<Program>();

    // Service-to-service communication
    builder.Services
        .AddHttpClient<ICatalogServiceClient, CatalogServiceClient>(client =>
            client.BaseAddress = catalogBaseUri)
        .AddStandardResilienceHandler();

    // Serialize enums (e.g. OrderStatus) as strings
    builder.Services.ConfigureHttpJsonOptions(options =>
        options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

    // API documentation and diagnostics
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(options =>
    {
        options.SwaggerDoc("v1", new OpenApiInfo
        {
            Title = "Ordering API",
            Version = "v1"
        });

        var securityScheme = new OpenApiSecurityScheme
        {
            Name = "Authorization",
            Type = SecuritySchemeType.Http,
            Scheme = "Bearer",
            BearerFormat = "JWT",
            In = ParameterLocation.Header,
            Description = "Enter your JWT token directly. Swagger will automatically format it as 'Bearer <token>'."
        };

        options.AddSecurityDefinition("Bearer", securityScheme);

        options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference("Bearer", document)] = []
        });
    });

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
        app.UseSwagger();
        app.UseSwaggerUI(options =>
        {
            options.SwaggerEndpoint("/swagger/v1/swagger.json", "Ordering API v1");
        });

        using var scope = app.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OrderingDbContext>();
        await dbContext.Database.MigrateAsync();
    }

    // Security middleware - HTTPS redirect
    app.UseHttpsRedirection();

    // Security middleware - Authentication and Authorization
    app.UseAuthentication();
    app.UseAuthorization();

    // Endpoint routing
    app.MapGet("/health", () => Results.Ok(new { Status = "Healthy", Service = "Ordering.Api" })).WithTags("Health");
    app.MapOrderEndpoints();
    app.MapAdminOrderEndpoints();

    Log.Information("Application pipeline configured. Running application.");

    app.Run();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "Application terminated unexpectedly.");
}
finally
{
    Log.Information("Shutting down Ordering.Api.");
    Log.CloseAndFlush();
}
