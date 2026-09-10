using FluentValidation;
using Identity.API.Endpoints;
using Identity.API.Services;
using Identity.API.Services.Common;
using Identity.Infrastructure.Data;
using Identity.Infrastructure.Models;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Configure Serilog
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .CreateLogger();

builder.Host.UseSerilog();

try
{
    Log.Information("Starting Identity.API application.");

    // Register DbContext with Npgsql
    var connectionString = builder.Configuration.GetConnectionString("AuthDb");
    builder.Services.AddDbContext<IdentityDbContext>(options =>
        options.UseNpgsql(connectionString));

    builder.Services.Configure<JwtSettings>(
        builder.Configuration.GetSection(JwtSettings.SectionName));

    builder.Services.AddScoped<IPasswordHasher<ApplicationUser>, PasswordHasher<ApplicationUser>>();
    builder.Services.AddScoped<IAuthService, AuthService>();

    builder.Services.AddOpenApi();

    builder.Services.AddValidatorsFromAssemblyContaining<Program>();

    builder.Services.AddHttpLogging(options =>
    {
        options.LoggingFields = HttpLoggingFields.RequestPath
        | HttpLoggingFields.RequestMethod
        | HttpLoggingFields.RequestQuery
        | HttpLoggingFields.ResponseStatusCode
        | HttpLoggingFields.Duration;
    });

    var app = builder.Build();

    app.UseHttpLogging();

    Log.Information("Building application pipeline.");

    if (app.Environment.IsDevelopment())
    {
        Log.Information("Running in Development environment.");
        app.MapOpenApi();
        app.UseSwaggerUI(options =>
        {
            options.SwaggerEndpoint("/openapi/v1.json", "Identity API v1");
        });
    }

    app.MapGet("/health", () => Results.Ok(new { Status = "Healthy", Service = "Identity.API" }));
    app.MapAuthEndpoints();

    Log.Information("Application started successfully.");
    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly.");
}
finally
{
    Log.CloseAndFlush();
}