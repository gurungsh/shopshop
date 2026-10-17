using BuildingBlocks.Web;
using BuildingBlocks.Web.Observability;
using Catalog.Api.Cacheing;
using Catalog.Api.Caching;
using Catalog.Api.Endpoints;
using Catalog.Api.Options;
using Catalog.Api.Services;
using Catalog.Infrastructure.Data;
using FluentValidation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using Serilog;
using StackExchange.Redis;

// Logger setup
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    Log.Information("Starting Catalog.Api application.");

    var builder = WebApplication.CreateBuilder(args);

    // Serilog
    builder.Host.UseSerilog((context, services, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .WriteToOtlp("Catalog.Api"));

    // OpenTelemetry
    builder.Services.AddShopShopTelemetry("Catalog.Api");

    // Configuration and options
    var jwtSettings = builder.Configuration
        .GetSection(JwtOptions.SectionName)
        .Get<JwtOptions>()
        ?? throw new InvalidOperationException("JwtSettings configuration is missing.");

    if (!Uri.TryCreate(jwtSettings.MetadataAddress, UriKind.Absolute, out _))
    {
        throw new InvalidOperationException("JwtSettings MetadataAddress is missing or invalid.");
    }

    if (string.IsNullOrWhiteSpace(jwtSettings.Issuer) || string.IsNullOrWhiteSpace(jwtSettings.Audience))
    {
        throw new InvalidOperationException("JwtSettings Issuer and Audience are required.");
    }

    builder.Services.Configure<JwtOptions>(
        builder.Configuration.GetSection(JwtOptions.SectionName));

    // Infrastructure and database services
    var connectionString = builder.Configuration.GetConnectionString("CatalogDb")
        ?? throw new InvalidOperationException("Connection string 'CatalogDb' is missing.");

    builder.Services.AddDbContext<CatalogDbContext>(options =>
        options.UseNpgsql(connectionString));

    var redisConnectionString = builder.Configuration.GetConnectionString("Redis")
        ?? throw new InvalidOperationException("Connection string 'Redis' is missing.");

    builder.Services.AddOptions<CacheOptions>()
        .BindConfiguration(CacheOptions.SectionName)
        .ValidateDataAnnotations()
        .ValidateOnStart();

    builder.Services.AddStackExchangeRedisCache(options =>
    {
        var redisOptions = ConfigurationOptions.Parse(redisConnectionString);
        redisOptions.ConnectTimeout = Math.Min(redisOptions.ConnectTimeout, 1000);
        redisOptions.AsyncTimeout = Math.Min(redisOptions.AsyncTimeout, 500);
        options.ConfigurationOptions = redisOptions;
    });

    builder.Services.AddSingleton<ICatalogCache, CatalogCache>();

    // Authentcation and authorization
    builder.Services
        .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            options.MetadataAddress = jwtSettings.MetadataAddress;
            options.RequireHttpsMetadata = jwtSettings.RequireHttpsMetadata;
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
                ValidateIssuer = true,
                ValidIssuer = jwtSettings.Issuer,
                ValidateAudience = true,
                ValidAudience = jwtSettings.Audience,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromSeconds(30)
            };
        });

    builder.Services.AddAuthorization();

    // Application business servicse
    builder.Services.AddScoped<ICategoryService, CategoryService>();
    builder.Services.AddScoped<IProductService, ProductService>();
    builder.Services.AddValidatorsFromAssemblyContaining<Program>();

    // API documentation and diagnostics
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(options =>
    {
        options.SupportNonNullableReferenceTypes();
        options.NonNullableReferenceTypesAsRequired();

        options.SwaggerDoc("v1", new OpenApiInfo
        {
            Title = "Catalog API",
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

    // Dependency checks for /health
    builder.Services.AddHealthChecks()
        .AddDbContextCheck<CatalogDbContext>("postgres")
        .AddCheck<RedisHealthCheck>("redis");

    var app = builder.Build();

    // One-shot mode for containers: apply migrations and exit (docker compose runs this before the app starts)
    if (args.Contains("--migrate"))
    {
        using var migrationScope = app.Services.CreateScope();
        await migrationScope.ServiceProvider.GetRequiredService<CatalogDbContext>().Database.MigrateAsync();
        Log.Information("Database migrated.");
        return;
    }

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

    // OpenAPI document and UI, on in Development and wherever OpenApi:Enabled is set
    if (app.Configuration.GetValue<bool>("OpenApi:Enabled"))
    {
        app.UseSwagger();
        app.UseSwaggerUI(options =>
        {
            options.SwaggerEndpoint("/swagger/v1/swagger.json", "Catalog API v1");
        });
    }

    // Development tooling
    if (app.Environment.IsDevelopment())
    {
        Log.Information("Running in Development environment.");

        using var scope = app.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        await dbContext.Database.MigrateAsync();
    }

    // Security middleware - HTTPS redirect
    app.UseHttpsRedirection();

    // Security middleware - Authentication and Authorization
    app.UseAuthentication();
    app.UseAuthorization();

    // Endpoint routing 
    app.MapHealthEndpoints("Catalog.Api");
    app.MapCategoryEndpoints();
    app.MapAdminCategoryEndpoints();
    app.MapProductEndpoints();
    app.MapAdminProductEndpoints();

    Log.Information("Application pipeline configured. Running application.");

    app.Run();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "Application terminated unexpectedly.");
}
finally
{
    Log.Information("Shutting down Catalog.Api");
    Log.CloseAndFlush();
}

// Makes Program visible to WebApplicationFactory<Program> in integration tests.
public partial class Program;
