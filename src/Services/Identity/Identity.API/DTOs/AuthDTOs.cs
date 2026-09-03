namespace Identity.API.DTOs;

public record RegisterRequest(string Email, string Password, string? Role = "Customer");
public record LoginRequest(string Email, string Password);
public record AuthResponse(Guid Id, string Email, string Role, string Token);