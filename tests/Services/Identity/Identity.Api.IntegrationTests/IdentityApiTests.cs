using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BuildingBlocks.Core;
using Identity.Api.DTOs;
using Identity.Infrastructure.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Identity.Api.IntegrationTests;

[Collection(IdentityApiCollection.Name)]
public class IdentityApiTests(IdentityApiFactory factory)
{
    private const string Password = "P@ssw0rd!123";

    private readonly HttpClient _client = factory.CreateClient();

    private static string NewEmail() => $"user-{Guid.NewGuid():N}@example.com";

    private async Task<AuthResponse> RegisterAsync(string? email = null)
    {
        var response = await _client.PostAsJsonAsync(
            "/api/auth/register", new RegisterRequest(email ?? NewEmail(), Password));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuthResponse>())!;
    }

    private async Task<string> CreateAdminTokenAsync()
    {
        var email = NewEmail();
        await using (var db = factory.CreateDbContext())
        {
            db.Users.Add(new User
            {
                Email = email,
                Role = Roles.Admin,
                PasswordHash = new PasswordHasher<User>().HashPassword(new User(), Password)
            });
            await db.SaveChangesAsync();
        }

        var login = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, Password));
        login.EnsureSuccessStatusCode();
        return (await login.Content.ReadFromJsonAsync<AuthResponse>())!.Token;
    }

    private static HttpRequestMessage Authorized(HttpMethod method, string url, string token, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }
        return request;
    }

    #region Register Tests

    [Fact]
    public async Task Register_NewUser_ShouldStoreHashedPasswordInPostgres()
    {
        // Arrange
        var email = NewEmail();

        // Act
        var auth = await RegisterAsync(email);

        // Assert
        Assert.Equal(Roles.Customer, auth.Role);
        await using var db = factory.CreateDbContext();
        var user = await db.Users.AsNoTracking().SingleAsync(u => u.Id == auth.Id);
        Assert.Equal(email, user.Email);
        Assert.NotEqual(Password, user.PasswordHash);
    }

    [Fact]
    public async Task Register_DuplicateEmailWithDifferentCase_ShouldReturnConflict()
    {
        // Arrange
        var email = NewEmail();
        await RegisterAsync(email);

        // Act
        var response = await _client.PostAsJsonAsync(
            "/api/auth/register", new RegisterRequest(email.ToUpperInvariant(), Password));

        // Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Register_WeakPassword_ShouldReturnBadRequest()
    {
        // Act
        var response = await _client.PostAsJsonAsync(
            "/api/auth/register", new RegisterRequest(NewEmail(), "weak"));

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    #endregion

    #region Login Tests

    [Fact]
    public async Task Login_WithWrongPassword_ShouldReturnUnauthorized()
    {
        // Arrange
        var auth = await RegisterAsync();

        // Act
        var response = await _client.PostAsJsonAsync(
            "/api/auth/login", new LoginRequest(auth.Email, "Wr0ng!Passw0rd"));

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_ThenGetCurrentUser_ShouldAcceptIssuedToken()
    {
        // Arrange
        var auth = await RegisterAsync();
        var login = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest(auth.Email, Password));
        var token = (await login.Content.ReadFromJsonAsync<AuthResponse>())!.Token;

        // Act
        var response = await _client.SendAsync(Authorized(HttpMethod.Get, "/api/users/me", token));

        // Assert
        response.EnsureSuccessStatusCode();
        var me = await response.Content.ReadFromJsonAsync<UserResponse>();
        Assert.Equal(auth.Id, me!.Id);
    }

    [Fact]
    public async Task GetCurrentUser_WithoutToken_ShouldReturnUnauthorized()
    {
        // Act
        var response = await _client.GetAsync("/api/users/me");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    #endregion

    #region Admin Tests

    [Fact]
    public async Task SearchUsers_AsCustomer_ShouldReturnForbidden()
    {
        // Arrange
        var auth = await RegisterAsync();

        // Act
        var response = await _client.SendAsync(
            Authorized(HttpMethod.Post, "/api/admin/users/search", auth.Token, new UserQuery()));

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task SearchUsers_AsAdminWithPaging_ShouldReturnPageAndTotal()
    {
        // Arrange
        var adminToken = await CreateAdminTokenAsync();
        for (var i = 0; i < 3; i++)
        {
            await RegisterAsync();
        }

        // Act
        var response = await _client.SendAsync(Authorized(
            HttpMethod.Post, "/api/admin/users/search", adminToken,
            new UserQuery(Role: Roles.Customer, PageSize: 2)));

        // Assert
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, body.GetProperty("items").GetArrayLength());
        Assert.True(body.GetProperty("totalCount").GetInt32() >= 3);
    }

    [Fact]
    public async Task SearchUsers_ByEmail_ShouldReturnExactMatch()
    {
        // Arrange
        var adminToken = await CreateAdminTokenAsync();
        var auth = await RegisterAsync();

        // Act
        var response = await _client.SendAsync(Authorized(
            HttpMethod.Post, "/api/admin/users/search", adminToken,
            new UserQuery(Email: auth.Email.ToUpperInvariant())));

        // Assert
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, body.GetProperty("totalCount").GetInt32());
        Assert.Equal(auth.Id, body.GetProperty("items")[0].GetProperty("id").GetGuid());
    }

    #endregion

    #region Jwks Tests

    [Fact]
    public async Task Jwks_ShouldPublishPublicKeyOnly()
    {
        // Act
        var json = await _client.GetStringAsync("/.well-known/jwks.json");

        // Assert
        var key = JsonDocument.Parse(json).RootElement.GetProperty("keys")[0];
        Assert.Equal("test-key", key.GetProperty("kid").GetString());
        Assert.True(key.TryGetProperty("n", out _));
        Assert.False(key.TryGetProperty("d", out _));
        Assert.False(key.TryGetProperty("p", out _));
    }

    [Fact]
    public async Task OpenIdConfiguration_ShouldPointToIssuerAndJwks()
    {
        // Act
        var json = await _client.GetStringAsync("/.well-known/openid-configuration");

        // Assert
        var doc = JsonDocument.Parse(json).RootElement;
        Assert.Equal(IdentityApiFactory.Issuer, doc.GetProperty("issuer").GetString());
        Assert.EndsWith("/.well-known/jwks.json", doc.GetProperty("jwks_uri").GetString());
    }

    #endregion
}
