using Identity.API.Endpoints;
using Identity.API.Services;
using Identity.API.Services.Common;
using Identity.Infrastructure.Data;
using Identity.Infrastructure.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Register DbContext with Npgsql
var connectionSstring = builder.Configuration.GetConnectionString("AuthDb");
builder.Services.AddDbContext<IdentityDbContext>(options =>
    options.UseNpgsql(connectionSstring));

builder.Services.Configure<JwtSettings>(
    builder.Configuration.GetSection(JwtSettings.SectionName));

builder.Services.AddScoped<IPasswordHasher<ApplicationUser>, PasswordHasher<ApplicationUser>>();
builder.Services.AddScoped<IAuthService, AuthService>();

builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/openapi/v1.json", "Identity API v1");
    });
}

app.MapGet("/health", () => Results.Ok(new { Status = "Healthy", Service = "Identity.API" }));

app.MapAuthEndpoints();

app.Run();