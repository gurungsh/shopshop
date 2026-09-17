namespace Identity.Api.DTOs;

public sealed record RegisterRequest(string Email, string Password);
public sealed record UpdateUserRequest(string Email, string Password);
public sealed record RefreshTokenRequest(string RefreshToken);
public sealed record UpdateCurrentUserRequest(string Email, string Password);
public sealed record AdminCreateUserRequest(string Email, string Password, string Role);
public sealed record AdminUpdateUserRequest(string Email, string Role);
public sealed record LoginRequest(string Email, string Password);
public sealed record AuthResponse(Guid Id, string Email, string Role, string Token);
public sealed record UserResponse(Guid Id, string Email, string Role);
public sealed record UserQuery(string? Email = null, string? Role = null);