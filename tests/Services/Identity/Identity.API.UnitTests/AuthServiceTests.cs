using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Identity.API.Common;
using Identity.API.Constants;
using Identity.API.DTOs;
using Identity.API.Services;
using Identity.Infrastructure.Data;
using Identity.Infrastructure.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace Identity.API.UnitTests
{
    public class AuthServiceTests
    {
        private readonly AuthService _authService;
        private readonly IdentityDbContext _dbContext;
        private readonly Mock<IPasswordHasher<ApplicationUser>> _mockHasher;
        private readonly JwtSettings _jwtSettings;
        private readonly Mock<ILogger<AuthService>> _mockLogger;

        public AuthServiceTests()
        {
            var options = new DbContextOptionsBuilder<IdentityDbContext>()
                .UseInMemoryDatabase($"test-db-{Guid.NewGuid()}")
                .Options;

            _dbContext = new IdentityDbContext(options);
            _mockHasher = new Mock<IPasswordHasher<ApplicationUser>>();
            _mockLogger = new Mock<ILogger<AuthService>>();

            _jwtSettings = new JwtSettings
            {
                Secret = "ThisIsAVeryLongSecretKeyForJwtTokenSigningPurposesOnly1234567890",
                ExpirationInMinutes = 60,
                Issuer = "ShopShop",
                Audience = "ShopShopClient"
            };

            var jwtOptionsWrapper = Options.Create(_jwtSettings);
            _authService = new AuthService(_dbContext, _mockHasher.Object, jwtOptionsWrapper, _mockLogger.Object);
        }

        #region RegisterAsync Tests

        [Fact]
        public async Task RegisterAsync_WithValidRequest_ShouldCreateUser()
        {
            // Arrange
            var request = new RegisterRequest("test@example.com", "Test@123");
            var hashedPassword = "hashed_password_123";

            _mockHasher
                .Setup(h => h.HashPassword(It.IsAny<ApplicationUser>(), request.Password))
                .Returns(hashedPassword);

            // Act
            var result = await _authService.RegisterAsync(request);

            // Assert
            Assert.True(result.IsSuccess);
            Assert.NotNull(result.Value);
            Assert.Equal("test@example.com", result.Value.Email);
            Assert.NotEmpty(result.Value.Token);
            Assert.Equal(3, result.Value.Token.Split('.').Length);

            var createdUser = await _dbContext.Users.FirstOrDefaultAsync(u => u.Email == "test@example.com");
            Assert.NotNull(createdUser);
            Assert.Equal(hashedPassword, createdUser.PasswordHash);

            var handler = new JwtSecurityTokenHandler();
            var token = handler.ReadJwtToken(result.Value.Token);

            Assert.NotNull(token);
            Assert.Equal("ShopShop", token.Issuer);
            Assert.Equal("ShopShopClient", token.Audiences.First());
            Assert.Contains(token.Claims, c => c.Type == JwtRegisteredClaimNames.Email || c.Type == ClaimTypes.Email);
            Assert.Contains(token.Claims, c => c.Type == "role" || c.Type == ClaimTypes.Role);
        }

        [Fact]
        public async Task RegisterAsync_WithDuplicateEmail_ShouldReturnConflictError()
        {
            // Arrange
            _dbContext.Users.Add(new ApplicationUser
            {
                Email = "duplicate@example.com",
                PasswordHash = "existing_hash",
                Role = Roles.Customer
            });
            await _dbContext.SaveChangesAsync();

            var request = new RegisterRequest("duplicate@example.com", "Test@123");

            // Act
            var result = await _authService.RegisterAsync(request);

            // Assert
            Assert.False(result.IsSuccess);
            Assert.Equal("User with this email already exists.", result.Error);
            Assert.Equal(ResultErrorType.Conflict, result.ErrorType);
        }

        [Fact]
        public async Task RegisterAsync_WithCaseInsensitiveEmail_ShouldPreventDuplicate()
        {
            // Arrange
            _dbContext.Users.Add(new ApplicationUser
            {
                Email = "test@example.com",
                PasswordHash = "existing_hash",
                Role = Roles.Customer
            });
            await _dbContext.SaveChangesAsync();

            var request = new RegisterRequest("Test@Example.COM", "Test@123");

            // Act
            var result = await _authService.RegisterAsync(request);

            // Assert
            Assert.False(result.IsSuccess);
            Assert.Equal("User with this email already exists.", result.Error);
            Assert.Equal(ResultErrorType.Conflict, result.ErrorType);
        }
        #endregion

        #region LoginAsync Tests

        [Fact]
        public async Task LoginAsync_WithValidCredentials_ShouldReturnAuthResponse()
        {
            // Arrange
            var password = "Test@123";
            var user = new ApplicationUser
            {
                Email = "test@example.com",
                PasswordHash = "hashed_password",
                Role = Roles.Customer
            };
            _dbContext.Users.Add(user);
            await _dbContext.SaveChangesAsync();

            var request = new LoginRequest("test@example.com", password);

            _mockHasher
                .Setup(h => h.VerifyHashedPassword(It.IsAny<ApplicationUser>(), user.PasswordHash, password))
                .Returns(PasswordVerificationResult.Success);

            // Act
            var result = await _authService.LoginAsync(request);

            // Assert
            Assert.True(result.IsSuccess);
            Assert.NotNull(result.Value);
            Assert.Equal(user.Email, result.Value.Email);
            Assert.Equal(user.Role, result.Value.Role);
            Assert.NotEmpty(result.Value.Token);
        }

        [Fact]
        public async Task LoginAsync_WithInvalidEmail_ShouldReturnUnauthorizedError()
        {
            // Arrange
            var request = new LoginRequest("nonexistent@example.com", "Test@123");

            // Act
            var result = await _authService.LoginAsync(request);

            // Assert
            Assert.False(result.IsSuccess);
            Assert.Equal("Invalid credentials.", result.Error);
            Assert.Equal(ResultErrorType.Unauthorized, result.ErrorType);
        }

        [Fact]
        public async Task LoginAsync_WithInvalidPassword_ShouldReturnUnauthorizedError()
        {
            // Arrange
            var user = new ApplicationUser
            {
                Email = "test@example.com",
                PasswordHash = "hashed_password",
                Role = Roles.Customer
            };
            _dbContext.Users.Add(user);
            await _dbContext.SaveChangesAsync();

            var request = new LoginRequest("test@example.com", "WrongPassword");

            _mockHasher
                .Setup(h => h.VerifyHashedPassword(It.IsAny<ApplicationUser>(), user.PasswordHash, "WrongPassword"))
                .Returns(PasswordVerificationResult.Failed);

            // Act
            var result = await _authService.LoginAsync(request);

            // Assert
            Assert.False(result.IsSuccess);
            Assert.Equal("Invalid credentials.", result.Error);
            Assert.Equal(ResultErrorType.Unauthorized, result.ErrorType);
        }

        [Fact]
        public async Task LoginAsync_WithSuccessRehashNeeded_ShouldUpdatePassword()
        {
            // Arrange
            var password = "Test@123";
            var user = new ApplicationUser
            {
                Email = "test@example.com",
                PasswordHash = "old_hashed_password",
                Role = Roles.Customer
            };
            _dbContext.Users.Add(user);
            await _dbContext.SaveChangesAsync();

            var request = new LoginRequest("test@example.com", password);
            var newHashedPassword = "new_hashed_password";

            _mockHasher
                .Setup(h => h.VerifyHashedPassword(It.IsAny<ApplicationUser>(), user.PasswordHash, password))
                .Returns(PasswordVerificationResult.SuccessRehashNeeded);

            _mockHasher
                .Setup(h => h.HashPassword(It.IsAny<ApplicationUser>(), password))
                .Returns(newHashedPassword);

            // Act
            var result = await _authService.LoginAsync(request);

            // Assert
            Assert.True(result.IsSuccess);

            var updatedUser = await _dbContext.Users.FirstOrDefaultAsync(u => u.Email == "test@example.com");
            Assert.NotNull(updatedUser);
            Assert.Equal(newHashedPassword, updatedUser.PasswordHash);
        }

        [Fact]
        public async Task LoginAsync_WithCaseInsensitiveEmail_ShouldWork()
        {
            // Arrange
            var password = "Test@123";
            var user = new ApplicationUser
            {
                Email = "test@example.com",
                PasswordHash = "hashed_password",
                Role = Roles.Customer
            };
            _dbContext.Users.Add(user);
            await _dbContext.SaveChangesAsync();

            var request = new LoginRequest("Test@Example.COM", password);

            _mockHasher
                .Setup(h => h.VerifyHashedPassword(It.IsAny<ApplicationUser>(), user.PasswordHash, password))
                .Returns(PasswordVerificationResult.Success);

            // Act
            var result = await _authService.LoginAsync(request);

            // Assert
            Assert.True(result.IsSuccess);
        }

        #endregion

        #region GetCurrentUserAsync Tests

        [Fact]
        public async Task GetCurrentUserAsync_WithValidPrincipal_ShouldReturnUser()
        {
            // Arrange
            var user = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                Email = "test@example.com",
                PasswordHash = "hashed_password",
                Role = Roles.Customer
            };
            _dbContext.Users.Add(user);
            await _dbContext.SaveChangesAsync();

            var claims = new List<Claim>
            {
                new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString())
            };
            var identity = new ClaimsIdentity(claims);
            var principal = new ClaimsPrincipal(identity);

            // Act
            var result = await _authService.GetCurrentUserAsync(principal);

            // Assert
            Assert.True(result.IsSuccess);
            Assert.NotNull(result.Value);
            Assert.Equal(user.Id, result.Value.Id);
            Assert.Equal(user.Email, result.Value.Email);
            Assert.Equal(user.Role, result.Value.Role);
        }

        [Fact]
        public async Task GetCurrentUserAsync_WithInvalidPrincipal_ShouldReturnUnauthorizedError()
        {
            // Arrange
            var principal = new ClaimsPrincipal(new ClaimsIdentity());

            // Act
            var result = await _authService.GetCurrentUserAsync(principal);

            // Assert
            Assert.False(result.IsSuccess);
            Assert.Equal("Invalid user identity.", result.Error);
            Assert.Equal(ResultErrorType.Unauthorized, result.ErrorType);
        }

        [Fact]
        public async Task GetCurrentUserAsync_WithNonexistentUser_ShouldReturnNotFoundError()
        {
            // Arrange
            var nonexistentUserId = Guid.NewGuid();
            var claims = new List<Claim>
            {
                new Claim(JwtRegisteredClaimNames.Sub, nonexistentUserId.ToString())
            };
            var identity = new ClaimsIdentity(claims);
            var principal = new ClaimsPrincipal(identity);

            // Act
            var result = await _authService.GetCurrentUserAsync(principal);

            // Assert
            Assert.False(result.IsSuccess);
            Assert.Equal("User not found.", result.Error);
            Assert.Equal(ResultErrorType.NotFound, result.ErrorType);
        }

        [Fact]
        public async Task GetCurrentUserAsync_WithNameIdentifierClaim_ShouldWork()
        {
            // Arrange
            var user = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                Email = "test@example.com",
                PasswordHash = "hashed_password",
                Role = Roles.Customer
            };
            _dbContext.Users.Add(user);
            await _dbContext.SaveChangesAsync();

            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString())
            };
            var identity = new ClaimsIdentity(claims);
            var principal = new ClaimsPrincipal(identity);

            // Act
            var result = await _authService.GetCurrentUserAsync(principal);

            // Assert
            Assert.True(result.IsSuccess);
            Assert.NotNull(result.Value);
        }

        #endregion

        #region UpdateCurrentUserAsync Tests

        [Fact]
        public async Task UpdateCurrentUserAsync_WithValidData_ShouldUpdateUser()
        {
            // Arrange
            var user = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                Email = "test@example.com",
                PasswordHash = "hashed_password",
                Role = Roles.Customer
            };
            _dbContext.Users.Add(user);
            await _dbContext.SaveChangesAsync();

            var request = new UpdateCurrentUserRequest("newemail@example.com", null);

            var claims = new List<Claim>
            {
                new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString())
            };
            var identity = new ClaimsIdentity(claims);
            var principal = new ClaimsPrincipal(identity);

            // Act
            var result = await _authService.UpdateCurrentUserAsync(principal, request);

            // Assert
            Assert.True(result.IsSuccess);
            Assert.NotNull(result.Value);
            Assert.Equal("newemail@example.com", result.Value.Email);

            var updatedUser = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == user.Id);
            Assert.NotNull(updatedUser);
            Assert.Equal("newemail@example.com", updatedUser.Email);
        }

        [Fact]
        public async Task UpdateCurrentUserAsync_WithPassword_ShouldUpdatePassword()
        {
            // Arrange
            var user = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                Email = "test@example.com",
                PasswordHash = "old_hashed_password",
                Role = Roles.Customer
            };
            _dbContext.Users.Add(user);
            await _dbContext.SaveChangesAsync();

            var newPassword = "NewPassword@123";
            var newHashedPassword = "new_hashed_password";
            var request = new UpdateCurrentUserRequest("test@example.com", newPassword);

            _mockHasher
                .Setup(h => h.HashPassword(It.IsAny<ApplicationUser>(), newPassword))
                .Returns(newHashedPassword);

            var claims = new List<Claim>
            {
                new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString())
            };
            var identity = new ClaimsIdentity(claims);
            var principal = new ClaimsPrincipal(identity);

            // Act
            var result = await _authService.UpdateCurrentUserAsync(principal, request);

            // Assert
            Assert.True(result.IsSuccess);

            var updatedUser = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == user.Id);
            Assert.NotNull(updatedUser);
            Assert.Equal(newHashedPassword, updatedUser.PasswordHash);
        }

        [Fact]
        public async Task UpdateCurrentUserAsync_WithDuplicateEmail_ShouldReturnConflictError()
        {
            // Arrange
            var user1 = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                Email = "user1@example.com",
                PasswordHash = "hashed_password",
                Role = Roles.Customer
            };
            var user2 = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                Email = "user2@example.com",
                PasswordHash = "hashed_password",
                Role = Roles.Customer
            };
            _dbContext.Users.AddRange(user1, user2);
            await _dbContext.SaveChangesAsync();

            var request = new UpdateCurrentUserRequest("user2@example.com", null);

            var claims = new List<Claim>
            {
                new Claim(JwtRegisteredClaimNames.Sub, user1.Id.ToString())
            };
            var identity = new ClaimsIdentity(claims);
            var principal = new ClaimsPrincipal(identity);

            // Act
            var result = await _authService.UpdateCurrentUserAsync(principal, request);

            // Assert
            Assert.False(result.IsSuccess);
            Assert.Equal("User with this email already exists.", result.Error);
            Assert.Equal(ResultErrorType.Conflict, result.ErrorType);
        }

        [Fact]
        public async Task UpdateCurrentUserAsync_WithInvalidPrincipal_ShouldReturnUnauthorizedError()
        {
            // Arrange
            var request = new UpdateCurrentUserRequest("newemail@example.com", null);
            var principal = new ClaimsPrincipal(new ClaimsIdentity());

            // Act
            var result = await _authService.UpdateCurrentUserAsync(principal, request);

            // Assert
            Assert.False(result.IsSuccess);
            Assert.Equal("Invalid user identity.", result.Error);
            Assert.Equal(ResultErrorType.Unauthorized, result.ErrorType);
        }

        [Fact]
        public async Task UpdateCurrentUserAsync_WithNonexistentUser_ShouldReturnNotFoundError()
        {
            // Arrange
            var nonexistentUserId = Guid.NewGuid();
            var request = new UpdateCurrentUserRequest("newemail@example.com", null);

            var claims = new List<Claim>
            {
                new Claim(JwtRegisteredClaimNames.Sub, nonexistentUserId.ToString())
            };
            var identity = new ClaimsIdentity(claims);
            var principal = new ClaimsPrincipal(identity);

            // Act
            var result = await _authService.UpdateCurrentUserAsync(principal, request);

            // Assert
            Assert.False(result.IsSuccess);
            Assert.Equal("User not found.", result.Error);
            Assert.Equal(ResultErrorType.NotFound, result.ErrorType);
        }

        #endregion

        #region GetUsersAsync Tests

        [Fact]
        public async Task GetUsersAsync_ShouldReturnAllUsers()
        {
            // Arrange
            var users = new[]
            {
                new ApplicationUser { Email = "user1@example.com", PasswordHash = "hash1", Role = Roles.Admin },
                new ApplicationUser { Email = "user2@example.com", PasswordHash = "hash2", Role = Roles.Customer },
                new ApplicationUser { Email = "user3@example.com", PasswordHash = "hash3", Role = Roles.Customer }
            };
            _dbContext.Users.AddRange(users);
            await _dbContext.SaveChangesAsync();

            // Act
            var result = await _authService.GetUsersAsync();

            // Assert
            Assert.True(result.IsSuccess);
            Assert.NotNull(result.Value);
            Assert.Equal(3, result.Value.Count);
            Assert.Contains(result.Value, u => u.Email == "user1@example.com");
            Assert.Contains(result.Value, u => u.Email == "user2@example.com");
            Assert.Contains(result.Value, u => u.Email == "user3@example.com");
        }

        [Fact]
        public async Task GetUsersAsync_WithNoUsers_ShouldReturnEmptyList()
        {
            // Act
            var result = await _authService.GetUsersAsync();

            // Assert
            Assert.True(result.IsSuccess);
            Assert.NotNull(result.Value);
            Assert.Empty(result.Value);
        }

        #endregion

        #region GetUserAsync Tests

        [Fact]
        public async Task GetUserAsync_WithValidEmail_ShouldReturnUser()
        {
            // Arrange
            var user = new ApplicationUser
            {
                Email = "test@example.com",
                PasswordHash = "hashed_password",
                Role = Roles.Customer
            };
            _dbContext.Users.Add(user);
            await _dbContext.SaveChangesAsync();

            // Act
            var result = await _authService.GetUserAsync("test@example.com");

            // Assert
            Assert.True(result.IsSuccess);
            Assert.NotNull(result.Value);
            Assert.Equal("test@example.com", result.Value.Email);
            Assert.Equal(Roles.Customer, result.Value.Role);
        }

        [Fact]
        public async Task GetUserAsync_WithInvalidEmail_ShouldReturnNotFoundError()
        {
            // Act
            var result = await _authService.GetUserAsync("nonexistent@example.com");

            // Assert
            Assert.False(result.IsSuccess);
            Assert.Equal("User not found.", result.Error);
            Assert.Equal(ResultErrorType.NotFound, result.ErrorType);
        }

        [Fact]
        public async Task GetUserAsync_WithCaseInsensitiveEmail_ShouldFindUser()
        {
            // Arrange
            var user = new ApplicationUser
            {
                Email = "test@example.com",
                PasswordHash = "hashed_password",
                Role = Roles.Customer
            };
            _dbContext.Users.Add(user);
            await _dbContext.SaveChangesAsync();

            // Act
            var result = await _authService.GetUserAsync("Test@Example.COM");

            // Assert
            Assert.True(result.IsSuccess);
            Assert.NotNull(result.Value);
        }

        #endregion

        #region CreateUserAsync Tests

        [Fact]
        public async Task CreateUserAsync_WithValidRequest_ShouldCreateUser()
        {
            // Arrange
            var request = new AdminCreateUserRequest("newuser@example.com", "Password@123", Roles.Customer);
            var hashedPassword = "hashed_password";

            _mockHasher
                .Setup(h => h.HashPassword(It.IsAny<ApplicationUser>(), request.Password))
                .Returns(hashedPassword);

            // Act
            var result = await _authService.CreateUserAsync(request);

            // Assert
            Assert.True(result.IsSuccess);
            Assert.NotNull(result.Value);
            Assert.Equal("newuser@example.com", result.Value.Email);
            Assert.Equal(Roles.Customer, result.Value.Role);

            var createdUser = await _dbContext.Users.FirstOrDefaultAsync(u => u.Email == "newuser@example.com");
            Assert.NotNull(createdUser);
            Assert.Equal(hashedPassword, createdUser.PasswordHash);
        }

        [Fact]
        public async Task CreateUserAsync_WithInvalidRole_ShouldReturnBadRequestError()
        {
            // Arrange
            var request = new AdminCreateUserRequest("newuser@example.com", "Password@123", "InvalidRole");

            // Act
            var result = await _authService.CreateUserAsync(request);

            // Assert
            Assert.False(result.IsSuccess);
            Assert.Equal("Role must be Admin or Customer.", result.Error);
            Assert.Equal(ResultErrorType.BadRequest, result.ErrorType);
        }

        [Fact]
        public async Task CreateUserAsync_WithDuplicateEmail_ShouldReturnConflictError()
        {
            // Arrange
            _dbContext.Users.Add(new ApplicationUser
            {
                Email = "duplicate@example.com",
                PasswordHash = "existing_hash",
                Role = Roles.Customer
            });
            await _dbContext.SaveChangesAsync();

            var request = new AdminCreateUserRequest("duplicate@example.com", "Password@123", Roles.Customer);

            // Act
            var result = await _authService.CreateUserAsync(request);

            // Assert
            Assert.False(result.IsSuccess);
            Assert.Equal("User with this email already exists.", result.Error);
            Assert.Equal(ResultErrorType.Conflict, result.ErrorType);
        }

        [Fact]
        public async Task CreateUserAsync_WithAdminRole_ShouldCreateAdminUser()
        {
            // Arrange
            var request = new AdminCreateUserRequest("admin@example.com", "Password@123", Roles.Admin);

            _mockHasher
                .Setup(h => h.HashPassword(It.IsAny<ApplicationUser>(), It.IsAny<string>()))
                .Returns("hashed_password");

            // Act
            var result = await _authService.CreateUserAsync(request);

            // Assert
            Assert.True(result.IsSuccess);
            Assert.Equal(Roles.Admin, result.Value!.Role);
        }

        #endregion

        #region UpdateUserAsync Tests

        [Fact]
        public async Task UpdateUserAsync_WithValidRequest_ShouldUpdateUser()
        {
            // Arrange
            var user = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                Email = "test@example.com",
                PasswordHash = "hashed_password",
                Role = Roles.Customer
            };
            _dbContext.Users.Add(user);
            await _dbContext.SaveChangesAsync();

            var request = new AdminUpdateUserRequest("newemail@example.com", Roles.Admin);

            // Act
            var result = await _authService.UpdateUserAsync(user.Id, request);

            // Assert
            Assert.True(result.IsSuccess);
            Assert.NotNull(result.Value);
            Assert.Equal("newemail@example.com", result.Value.Email);
            Assert.Equal(Roles.Admin, result.Value.Role);

            var updatedUser = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == user.Id);
            Assert.NotNull(updatedUser);
            Assert.Equal("newemail@example.com", updatedUser.Email);
            Assert.Equal(Roles.Admin, updatedUser.Role);
        }

        [Fact]
        public async Task UpdateUserAsync_WithNonexistentUser_ShouldReturnNotFoundError()
        {
            // Arrange
            var nonexistentUserId = Guid.NewGuid();
            var request = new AdminUpdateUserRequest("newemail@example.com", Roles.Customer);

            // Act
            var result = await _authService.UpdateUserAsync(nonexistentUserId, request);

            // Assert
            Assert.False(result.IsSuccess);
            Assert.Equal("User not found.", result.Error);
            Assert.Equal(ResultErrorType.NotFound, result.ErrorType);
        }

        [Fact]
        public async Task UpdateUserAsync_WithDuplicateEmail_ShouldReturnConflictError()
        {
            // Arrange
            var user1 = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                Email = "user1@example.com",
                PasswordHash = "hashed_password",
                Role = Roles.Customer
            };
            var user2 = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                Email = "user2@example.com",
                PasswordHash = "hashed_password",
                Role = Roles.Customer
            };
            _dbContext.Users.AddRange(user1, user2);
            await _dbContext.SaveChangesAsync();

            var request = new AdminUpdateUserRequest("user2@example.com", Roles.Customer);

            // Act
            var result = await _authService.UpdateUserAsync(user1.Id, request);

            // Assert
            Assert.False(result.IsSuccess);
            Assert.Equal("User with this email already exists.", result.Error);
            Assert.Equal(ResultErrorType.Conflict, result.ErrorType);
        }

        #endregion

        #region DeleteUserAsync Tests

        [Fact]
        public async Task DeleteUserAsync_WithValidUser_ShouldDeleteUser()
        {
            // Arrange
            var user = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                Email = "test@example.com",
                PasswordHash = "hashed_password",
                Role = Roles.Customer
            };
            _dbContext.Users.Add(user);
            await _dbContext.SaveChangesAsync();

            // Act
            var result = await _authService.DeleteUserAsync(user.Id);

            // Assert
            Assert.True(result.IsSuccess);
            Assert.True(result.Value);

            var deletedUser = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == user.Id);
            Assert.Null(deletedUser);
        }

        [Fact]
        public async Task DeleteUserAsync_WithNonexistentUser_ShouldReturnNotFoundError()
        {
            // Arrange
            var nonexistentUserId = Guid.NewGuid();

            // Act
            var result = await _authService.DeleteUserAsync(nonexistentUserId);

            // Assert
            Assert.False(result.IsSuccess);
            Assert.Equal("User not found.", result.Error);
            Assert.Equal(ResultErrorType.NotFound, result.ErrorType);
        }

        #endregion
    }
}