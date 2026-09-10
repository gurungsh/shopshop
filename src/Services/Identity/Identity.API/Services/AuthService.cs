using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Identity.API.Constants;
using Identity.API.DTOs;
using Identity.API.Services.Common;
using Identity.Infrastructure.Data;
using Identity.Infrastructure.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Identity.API.Services
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

            var normalizedEmail = request.Email.Trim().ToLowerInvariant();

            var existingUser = await _dbContext.Users.AnyAsync(u => u.Email.ToLower() == request.Email.ToLower());
            if (existingUser)
            {
                _logger.LogWarning("User with this email already exists. {Email}", request.Email);
                return Result<AuthResponse>.Failure("User with this email already exists.", ResultErrorType.Conflict);
            }

            string assignedRole = Roles.Customer;

            if (!_dbContext.Users.Any())
            {
                // First user is an admin by default
                assignedRole = Roles.Admin;
            }
            else if (string.Equals(request.Role, Roles.Admin, StringComparison.OrdinalIgnoreCase))
            {
                assignedRole = Roles.Admin;
            }

            var user = new ApplicationUser
            {
                Email = normalizedEmail,
                Role = assignedRole
            };

            user.PasswordHash = _hasher.HashPassword(user, request.Password);

            _dbContext.Users.Add(user);
            await _dbContext.SaveChangesAsync();

            _logger.LogInformation("User registered successfully - Email: {Email}, Role: {Role}", normalizedEmail, assignedRole);

            var token = GenerateJwtToken(user);
            var response = new AuthResponse(user.Id, user.Email, user.Role, token);

            return Result<AuthResponse>.Success(response);
        }

        public async Task<Result<AuthResponse>> LoginAsync(LoginRequest request)
        {
            _logger.LogInformation("Login attempt for email: {Email}", request.Email);

            var normalizedEmail = request.Email.Trim().ToLowerInvariant();

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
    }
}
