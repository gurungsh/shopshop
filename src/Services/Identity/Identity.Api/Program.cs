using FluentValidation;
using Identity.Api.Endpoints;
using Identity.Api.Options;
using Identity.Api.Security;
using Identity.Api.Services;
using Identity.Infrastructure.Data;
using Identity.Infrastructure.Models;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using Serilog;

// Logger setup
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    Log.Information("Starting Identity.Api application.");

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

    if (string.IsNullOrWhiteSpace(jwtSettings.PrivateKeyPem) ||
        string.IsNullOrWhiteSpace(jwtSettings.KeyId))
    {
        throw new InvalidOperationException("JWT PrivateKeyPem or KeyId is missing.");
    }

    builder.Services.Configure<JwtOptions>(
        builder.Configuration.GetSection(JwtOptions.SectionName));

    // Infrastructure and database services
    var connectionString = builder.Configuration.GetConnectionString("AuthDb")
        ?? throw new InvalidOperationException("Connection string 'AuthDb' is missing.");

    builder.Services.AddDbContext<IdentityDbContext>(options =>
        options.UseNpgsql(connectionString));

    // Authentication and authorization
    builder.Services.AddSingleton<IJwtKeyProvider, JwtKeyProvider>();

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
        });

    builder.Services
        .AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
        .Configure<IJwtKeyProvider, IOptions<JwtOptions>>((options, keyProvider, jwtOptions) =>
        {
            var jwt = jwtOptions.Value;

            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = keyProvider.SigningKey,
                ValidateIssuer = true,
                ValidIssuer = jwt.Issuer,
                ValidateAudience = true,
                ValidAudience = jwt.Audience,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromSeconds(30)
            };
        });

    builder.Services.AddAuthorization();

    // Application business services
    builder.Services.AddScoped<IPasswordHasher<User>, PasswordHasher<User>>();
    builder.Services.AddScoped<IAuthService, AuthService>();
    builder.Services.AddValidatorsFromAssemblyContaining<Program>();

    // API documentation and diagnostics
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(options =>
    {
        options.SwaggerDoc("v1", new OpenApiInfo
        {
            Title = "Identity API",
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

    // Fail fast: build the key provider so an invalid PEM stops startup
    app.Services.GetRequiredService<IJwtKeyProvider>();

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
            options.SwaggerEndpoint("/swagger/v1/swagger.json", "Identity API v1");
        });

        using var scope = app.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        await dbContext.Database.MigrateAsync();
    }

    // Security middleware - HTTPS redirect
    app.UseHttpsRedirection();

    // Security middleware - Authentication and Authorization
    app.UseAuthentication();
    app.UseAuthorization();

    // Endpoint routing
    app.MapGet("/health", () => Results.Ok(new { Status = "Healthy", Service = "Identity.Api" })).ExcludeFromDescription();
    app.MapAdminUserEndpoints();
    app.MapAuthEndpoints();
    app.MapUserEndpoints();
    app.MapJwksEndpoints();

    Log.Information("Application pipeline configured. Running application.");

    app.Run();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "Application terminated unexpectedly.");
}
finally
{
    Log.Information("Shutting down Identity.Api.");
    Log.CloseAndFlush();
}