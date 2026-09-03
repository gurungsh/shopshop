using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
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
        private readonly IdentityDbContext _db;
        private readonly IPasswordHasher<ApplicationUser> _hasher;
        private readonly JwtSettings _jwtSettings;

        public AuthService(IdentityDbContext db, IPasswordHasher<ApplicationUser> hasher, IOptions<JwtSettings> jwtOptions)
        {
            _db = db;
            _hasher = hasher;
            _jwtSettings = jwtOptions.Value;
        }

        public async Task<Result<AuthResponse>> RegisterAsync(RegisterRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
            {
                return Result<AuthResponse>.Failure("Email and password are required.", ResultErrorType.BadRequest);
            }

            var existingUser = await _db.Users.AnyAsync(u => u.Email.ToLower() == request.Email.ToLower());
            if (existingUser)
            {
                return Result<AuthResponse>.Failure("User with this email already exists.", ResultErrorType.Conflict);
            }

            var assignedRole = string.Equals(request.Role, "Admin", StringComparison.OrdinalIgnoreCase) ? "Admin" : "Customer";

            var user = new ApplicationUser
            {
                Email = request.Email,
                Role = assignedRole
            };

            user.PasswordHash = _hasher.HashPassword(user, request.Password);

            _db.Users.Add(user);
            await _db.SaveChangesAsync();

            var token = GenerateJwtToken(user);
            var response = new AuthResponse(user.Id, user.Email, user.Role, token);

            return Result<AuthResponse>.Success(response);
        }

        public async Task<Result<AuthResponse>> LoginAsync(LoginRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
            {
                return Result<AuthResponse>.Failure("Email and password are required.", ResultErrorType.BadRequest);
            }

            var user = await _db.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == request.Email.ToLower());
            if (user is null)
            {
                return Result<AuthResponse>.Failure("Invalid credentials.", ResultErrorType.Unauthorized);
            }

            var verificationResult = _hasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
            if (verificationResult == PasswordVerificationResult.Failed)
            {
                return Result<AuthResponse>.Failure("Invalid credentials.", ResultErrorType.Unauthorized);
            }

            if (verificationResult == PasswordVerificationResult.SuccessRehashNeeded)
            {
                user.PasswordHash = _hasher.HashPassword(user, request.Password);
                await _db.SaveChangesAsync();
            }

            var token = GenerateJwtToken(user);
            var response = new AuthResponse(user.Id, user.Email, user.Role, token);

            return Result<AuthResponse>.Success(response);
        }

        private string GenerateJwtToken(ApplicationUser user)
        {
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
            return tokenHandler.WriteToken(token);
        }
    }
}
