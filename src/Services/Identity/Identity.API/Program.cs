using Identity.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Register DbContext with Npgsql
var connectionSstring = builder.Configuration.GetConnectionString("AuthDb");
builder.Services.AddDbContext<IdentityDbContext>(options =>
    options.UseNpgsql(connectionSstring));

builder.Services.AddOpenApi();

var app = builder.Build();

if(app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapGet("/health", () => Results.Ok(new { Status = "Healthy", Service = "Identity.API" }));

app.Run();