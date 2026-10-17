using System.Buffers.Text;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using BuildingBlocks.Core;
using Identity.Api.DTOs;
using Identity.Api.Options;
using Identity.Api.Security;
using Identity.Infrastructure.Data;
using Identity.Infrastructure.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Identity.Api.Services;

public class AuthService : IAuthService
{
    private readonly IdentityDbContext _dbContext;
    private readonly IPasswordHasher<User> _hasher;
    private readonly JwtOptions _jwtSettings;
    private readonly IJwtKeyProvider _jwtKeyProvider;
    private readonly ILogger<AuthService> _logger;

    public AuthService(
        IdentityDbContext dbcontext,
        IPasswordHasher<User> hasher,
        IOptions<JwtOptions> jwtOptions,
        IJwtKeyProvider jwtKeyProvider,
        ILogger<AuthService> logger)
    {
        _dbContext = dbcontext;
        _hasher = hasher;
        _jwtSettings = jwtOptions.Value;
        _jwtKeyProvider = jwtKeyProvider;
        _logger = logger;
    }

    public async Task<Result<AuthResponse>> RegisterAsync(RegisterRequest request)
    {
        _logger.LogInformation("Register attempt for email: {Email}", request.Email);

        var normalizedEmail = NormalizeEmail(request.Email);

        var existingUser = await _dbContext.Users.AnyAsync(u => string.Equals(u.Email, normalizedEmail));
        if (existingUser)
        {
            _logger.LogWarning("User with this email already exists. {Email}", request.Email);
            return Result<AuthResponse>.Failure("User with this email already exists.", ResultErrorType.Conflict);
        }

        var user = new User
        {
            Email = normalizedEmail,
            Role = Roles.Customer
        };

        user.PasswordHash = _hasher.HashPassword(user, request.Password);

        _dbContext.Users.Add(user);
        try
        {
            await _dbContext.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex))
        {
            return Result<AuthResponse>.Failure("User with this email already exists.", ResultErrorType.Conflict);
        }

        _logger.LogInformation("User registered successfully - Email: {Email}, Role: {Role}", user.Email, user.Role);

        return Result<AuthResponse>.Success(await IssueTokensAsync(user));
    }

    public async Task<Result<AuthResponse>> LoginAsync(LoginRequest request)
    {
        _logger.LogInformation("Login attempt for email: {Email}", request.Email);

        var normalizedEmail = NormalizeEmail(request.Email);

        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Email == normalizedEmail);
        if (user is null)
        {
            _logger.LogWarning("Login failed: User not found - {Email}", normalizedEmail);
            return Result<AuthResponse>.Failure("Invalid credentials.", ResultErrorType.Unauthorized);
        }

        var verificationResult = _hasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
        if (verificationResult == PasswordVerificationResult.Failed)
        {
            _logger.LogWarning("Login failed: Invalid password - {Email}", normalizedEmail);
            return Result<AuthResponse>.Failure("Invalid credentials.", ResultErrorType.Unauthorized);
        }

        if (verificationResult == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = _hasher.HashPassword(user, request.Password);
            await _dbContext.SaveChangesAsync();
        }

        _logger.LogInformation("User logged in successfully - Email: {Email}, Role: {Role}", normalizedEmail, user.Role);

        return Result<AuthResponse>.Success(await IssueTokensAsync(user));
    }

    public async Task<Result<AuthResponse>> RefreshTokenAsync(RefreshTokenRequest request)
    {
        var tokenHash = HashRefreshToken(request.RefreshToken);

        var refreshToken = await _dbContext.RefreshTokens
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash);

        if (refreshToken is null)
        {
            _logger.LogWarning("Refresh failed: Unknown refresh token.");
            return Result<AuthResponse>.Failure("Invalid refresh token.", ResultErrorType.Unauthorized);
        }

        if (refreshToken.RevokedAtUtc is not null)
        {
            // A revoked token coming back means it may have been stolen, so end every session of the user.
            _logger.LogWarning("Refresh failed: Revoked refresh token reused - UserId: {UserId}", refreshToken.UserId);
            await RevokeRefreshTokensAsync(refreshToken.UserId);
            return Result<AuthResponse>.Failure("Invalid refresh token.", ResultErrorType.Unauthorized);
        }

        if (refreshToken.ExpiresAtUtc <= DateTime.UtcNow)
        {
            _logger.LogWarning("Refresh failed: Expired refresh token - UserId: {UserId}", refreshToken.UserId);
            return Result<AuthResponse>.Failure("Invalid refresh token.", ResultErrorType.Unauthorized);
        }

        // Rotate: the old token is revoked in the same save that stores the new one.
        refreshToken.RevokedAtUtc = DateTime.UtcNow;

        try
        {
            var response = await IssueTokensAsync(refreshToken.User);
            _logger.LogInformation("Tokens refreshed - UserId: {UserId}", refreshToken.UserId);
            return Result<AuthResponse>.Success(response);
        }
        catch (DbUpdateConcurrencyException)
        {
            _logger.LogWarning("Refresh failed: Refresh token used concurrently - UserId: {UserId}", refreshToken.UserId);
            return Result<AuthResponse>.Failure("Invalid refresh token.", ResultErrorType.Unauthorized);
        }
    }

    public async Task<Result<bool>> LogoutAsync(ClaimsPrincipal principal)
    {
        var userId = GetUserId(principal);

        if (userId is null)
        {
            return Result<bool>.Failure("Invalid user identity.", ResultErrorType.Unauthorized);
        }

        await RevokeRefreshTokensAsync(userId.Value);

        _logger.LogInformation("User logged out - UserId: {UserId}", userId.Value);

        return Result<bool>.Success(true);
    }

    public async Task<Result<UserResponse>> GetCurrentUserAsync(ClaimsPrincipal principal)
    {
        var userId = GetUserId(principal);

        if (userId is null)
        {
            return Result<UserResponse>.Failure("Invalid user identity.", ResultErrorType.Unauthorized);
        }

        var user = await _dbContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId.Value);

        if (user is null)
        {
            return Result<UserResponse>.Failure("User not found.", ResultErrorType.NotFound);
        }

        return Result<UserResponse>.Success(
            new UserResponse(user.Id, user.Email, user.Role));
    }

    public async Task<Result<UserResponse>> UpdateCurrentUserAsync(ClaimsPrincipal principal, UpdateCurrentUserRequest request)
    {
        {
            var userId = GetUserId(principal);

            if (userId is null)
            {
                return Result<UserResponse>.Failure("Invalid user identity.", ResultErrorType.Unauthorized);
            }

            var user = await _dbContext.Users
                .FirstOrDefaultAsync(u => u.Id == userId.Value);

            if (user is null)
            {
                return Result<UserResponse>.Failure("User not found.", ResultErrorType.NotFound);
            }

            var normalizedEmail = NormalizeEmail(request.Email);

            var emailExists = await _dbContext.Users
                .AnyAsync(u =>
                    u.Id != user.Id &&
                    u.Email == normalizedEmail);

            if (emailExists)
            {
                return Result<UserResponse>.Failure("User with this email already exists.", ResultErrorType.Conflict);
            }

            user.Email = normalizedEmail;

            if (!string.IsNullOrWhiteSpace(request.Password))
            {
                user.PasswordHash = _hasher.HashPassword(user, request.Password);
            }

            try
            {
                await _dbContext.SaveChangesAsync();
            }
            catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex))
            {
                return Result<UserResponse>.Failure("User with this email already exists.", ResultErrorType.Conflict);
            }

            _logger.LogInformation(
                "User updated successfully - UserId: {UserId}",
                user.Id);

            return Result<UserResponse>.Success(
                new UserResponse(user.Id, user.Email, user.Role));
        }
    }

    public async Task<Result<PagedResponse<UserResponse>>> GetUsersAsync(UserQuery query)
    {
        var users = _dbContext.Users.AsNoTracking();

        if (!string.IsNullOrEmpty(query.Email))
        {
            var normalizedEmail = NormalizeEmail(query.Email);

            users = users.Where(u => string.Equals(u.Email, normalizedEmail));
        }

        if (!string.IsNullOrEmpty(query.Role))
        {
            users = users.Where(u => string.Equals(u.Role, query.Role));
        }

        var totalCount = await users.CountAsync();

        var items = await users
            .OrderBy(u => u.CreatedAtUtc)
            .ThenBy(u => u.Id)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(u => new UserResponse(u.Id, u.Email, u.Role))
            .ToListAsync();

        return Result<PagedResponse<UserResponse>>.Success(
            new PagedResponse<UserResponse>(items, query.Page, query.PageSize, totalCount));
    }

    public async Task<Result<UserResponse>> GetUserAsync(Guid id)
    {
        var user = await _dbContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => string.Equals(u.Id, id));

        if (user is null)
        {
            return Result<UserResponse>.Failure("User not found.", ResultErrorType.NotFound);
        }

        return Result<UserResponse>.Success(new UserResponse(user.Id, user.Email, user.Role));
    }

    public async Task<Result<UserResponse>> CreateUserAsync(AdminCreateUserRequest request)
    {
        _logger.LogInformation("Admin user-creation attempt for email: {Email}, Role: {Role}", request.Email, request.Role);

        if (request.Role != Roles.Admin && request.Role != Roles.Customer)
        {
            _logger.LogWarning("Invalid role specified during user creation: {Role}", request.Role);
            return Result<UserResponse>.Failure("Role must be Admin or Customer.", ResultErrorType.BadRequest);
        }

        var normalizedEmail = request.Email.Trim().ToLowerInvariant();

        var existingUser = await _dbContext.Users.AnyAsync(u => string.Equals(u.Email, normalizedEmail));
        if (existingUser)
        {
            _logger.LogWarning("User with this email already exists. {Email}", request.Email);
            return Result<UserResponse>.Failure("User with this email already exists.", ResultErrorType.Conflict);
        }

        var user = new User
        {
            Email = normalizedEmail,
            Role = request.Role
        };

        user.PasswordHash = _hasher.HashPassword(user, request.Password);

        _dbContext.Users.Add(user);
        try
        {
            await _dbContext.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex))
        {
            return Result<UserResponse>.Failure("User with this email already exists.", ResultErrorType.Conflict);
        }

        _logger.LogInformation("User created successfully by admin - Email: {Email}, Role: {Role}", user.Email, user.Role);

        var response = new UserResponse(user.Id, user.Email, user.Role);

        return Result<UserResponse>.Success(response);
    }

    public async Task<Result<UserResponse>> UpdateUserAsync(Guid id, AdminUpdateUserRequest request)
    {
        var user = await _dbContext.Users
            .FirstOrDefaultAsync(u => u.Id == id);

        if (user is null)
        {
            return Result<UserResponse>.Failure("User not found.", ResultErrorType.NotFound);
        }

        var normalizedEmail = NormalizeEmail(request.Email);

        var emailExists = await _dbContext.Users
            .AnyAsync(u => u.Id != user.Id && string.Equals(u.Email, normalizedEmail));

        if (emailExists)
        {
            return Result<UserResponse>.Failure("User with this email already exists.", ResultErrorType.Conflict);
        }

        user.Email = normalizedEmail;
        user.Role = request.Role;

        try
        {
            await _dbContext.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex))
        {
            return Result<UserResponse>.Failure("User with this email already exists.", ResultErrorType.Conflict);
        }

        _logger.LogInformation("User updated. User Id:{UserId}, Email:{Email}, Role:{Role}", user.Id, user.Email, user.Role);

        return Result<UserResponse>.Success(new UserResponse(user.Id, user.Email, user.Role));
    }

    public async Task<Result<bool>> DeleteUserAsync(Guid id)
    {
        var user = await _dbContext.Users
            .FirstOrDefaultAsync(u => u.Id == id);

        if (user is null)
        {
            return Result<bool>.Failure("User not found.", ResultErrorType.NotFound);
        }

        _dbContext.Users.Remove(user);

        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("User deleted. User Id:{UserId}, Email:{Email}", user.Id, user.Email);

        return Result<bool>.Success(true);
    }

    private string GenerateJwtToken(User user)
    {
        _logger.LogDebug("Generating JWT token for user: {Email}", user.Email);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim(ClaimTypes.Role, user.Role),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddMinutes(_jwtSettings.ExpirationInMinutes),
            Issuer = _jwtSettings.Issuer,
            Audience = _jwtSettings.Audience,
            SigningCredentials = new SigningCredentials(
                _jwtKeyProvider.SigningKey,
                SecurityAlgorithms.RsaSha256)
        };

        var tokenHandler = new JwtSecurityTokenHandler();
        var token = tokenHandler.CreateToken(tokenDescriptor);

        _logger.LogDebug("JWT token generated successfully for user: {Email}", user.Email);

        return tokenHandler.WriteToken(token);
    }

    private async Task<AuthResponse> IssueTokensAsync(User user)
    {
        var refreshToken = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

        _dbContext.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            TokenHash = HashRefreshToken(refreshToken),
            ExpiresAtUtc = DateTime.UtcNow.AddDays(_jwtSettings.RefreshTokenExpirationInDays)
        });

        await _dbContext.SaveChangesAsync();

        return new AuthResponse(user.Id, user.Email, user.Role, GenerateJwtToken(user), refreshToken);
    }

    private async Task RevokeRefreshTokensAsync(Guid userId)
    {
        var activeTokens = await _dbContext.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAtUtc == null)
            .ToListAsync();

        var now = DateTime.UtcNow;
        foreach (var token in activeTokens)
        {
            token.RevokedAtUtc = now;
        }

        await _dbContext.SaveChangesAsync();
    }

    private static string HashRefreshToken(string refreshToken)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken)));
    }

    private static string NormalizeEmail(string email)
    {
        return email.Trim().ToLowerInvariant();
    }

    private static Guid? GetUserId(ClaimsPrincipal principal)
    {
        var userIdClaim = principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
            ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        return Guid.TryParse(userIdClaim, out var userId) ? userId : null;
    }
}
