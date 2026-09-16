using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Identity.Api.Common;
using Identity.Api.Constants;
using Identity.Api.DTOs;
using Identity.Infrastructure.Data;
using Identity.Infrastructure.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Identity.Api.Services
{
    public class AuthService : IAuthService
    {
        private readonly IdentityDbContext _dbContext;
        private readonly IPasswordHasher<ApplicationUser> _hasher;
        private readonly JwtSettings _jwtSettings;
        private readonly ILogger<AuthService> _logger;

        public AuthService(
            IdentityDbContext db,
            IPasswordHasher<ApplicationUser> hasher,
            IOptions<JwtSettings> jwtOptions,
            ILogger<AuthService> logger)
        {
            _dbContext = db;
            _hasher = hasher;
            _jwtSettings = jwtOptions.Value;
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

            var user = new ApplicationUser
            {
                Email = normalizedEmail,
                Role = Roles.Customer
            };

            user.PasswordHash = _hasher.HashPassword(user, request.Password);

            _dbContext.Users.Add(user);
            await _dbContext.SaveChangesAsync();

            _logger.LogInformation("User registered successfully - Email: {Email}, Role: {Role}", user.Email, user.Role);

            var token = GenerateJwtToken(user);
            var response = new AuthResponse(user.Id, user.Email, user.Role, token);

            return Result<AuthResponse>.Success(response);
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

            var token = GenerateJwtToken(user);

            return Result<AuthResponse>.Success(new AuthResponse(user.Id, user.Email, user.Role, token));
        }

        public async Task<Result<AuthResponse>> RefreshTokenAsync(RefreshTokenRequest request)
        {
            throw new NotImplementedException();
        }

        public async Task<Result<bool>> LogoutAsync(ClaimsPrincipal principal)
        {
            throw new NotImplementedException();
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

                await _dbContext.SaveChangesAsync();

                _logger.LogInformation(
                    "User updated successfully - UserId: {UserId}",
                    user.Id);

                return Result<UserResponse>.Success(
                    new UserResponse(user.Id, user.Email, user.Role));
            }
        }

        public async Task<Result<List<UserResponse>>> GetUsersAsync()
        {
            var users = await _dbContext.Users
                .AsNoTracking()
                .Select(u => new UserResponse(u.Id, u.Email, u.Role))
                .ToListAsync();

            return Result<List<UserResponse>>.Success(users);
        }

        public async Task<Result<UserResponse>> GetUserAsync(string email)
        {
            var normalizedEmail = NormalizeEmail(email);

            var user = await _dbContext.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(u => string.Equals(u.Email, normalizedEmail));

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

            var existingUser = await _dbContext.Users.AnyAsync(u => string.Equals(u.Email.ToLowerInvariant(), normalizedEmail));
            if (existingUser)
            {
                _logger.LogWarning("User with this email already exists. {Email}", request.Email);
                return Result<UserResponse>.Failure("User with this email already exists.", ResultErrorType.Conflict);
            }

            var user = new ApplicationUser
            {
                Email = normalizedEmail,
                Role = request.Role
            };

            user.PasswordHash = _hasher.HashPassword(user, request.Password);

            _dbContext.Users.Add(user);
            await _dbContext.SaveChangesAsync();

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

            await _dbContext.SaveChangesAsync();

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

        private string GenerateJwtToken(ApplicationUser user)
        {
            _logger.LogDebug("Generating JWT token for user: {Email}", user.Email);

            var key = Encoding.UTF8.GetBytes(_jwtSettings.Secret);

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
                    new SymmetricSecurityKey(key),
                    SecurityAlgorithms.HmacSha256Signature)
            };

            var tokenHandler = new JwtSecurityTokenHandler();
            var token = tokenHandler.CreateToken(tokenDescriptor);

            _logger.LogDebug("JWT token generated successfully for user: {Email}", user.Email);

            return tokenHandler.WriteToken(token);
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
}
