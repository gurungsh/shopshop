using Castle.Core.Logging;
using Identity.API.Constants;
using Identity.API.DTOs;
using Identity.API.Services;
using Identity.API.Services.Common;
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

            _jwtSettings = new JwtSettings
            {
                Secret = "ThisIsAVeryLongSecretKeyForJwtTokenSigningPurposesOnly1234567890",
                ExpirationInMinutes = 60,
                Issuer = "ShopShop",
                Audience = "ShopShopClient"
            };
            var jwtOptionsWrapper = Options.Create(_jwtSettings);

            _mockLogger = new Mock<ILogger<AuthService>>();

            _authService = new AuthService(_dbContext, _mockHasher.Object, jwtOptionsWrapper, _mockLogger.Object);
        }

        #region Register Tests

        [Fact]
        public async Task RegisterAsync_WithValidRequest_FirstUserShouldBeAdmin()
        {
            // Arrange
            var request = new RegisterRequest("test@example.com", "Test@123", Roles.Customer);

            var hashedPassword = "hashed_password_123";
            _mockHasher
                .Setup(h => h.HashPassword(It.IsAny<ApplicationUser>(), request.Password))
                .Returns(hashedPassword);

            // Act 
            var result = await _authService.RegisterAsync(request);

            // Assert
            Assert.True(result.IsSuccess);
            Assert.NotNull(result.Value);
            Assert.Equal(request.Email, result.Value.Email);
            Assert.Equal(Roles.Admin, result.Value.Role); // First user is always an admin

            Assert.NotEmpty(result.Value.Token);
            var tokenParts = result.Value.Token.Split('.');
            Assert.Equal(3, tokenParts.Length);

            var createdUser = await _dbContext.Users.FirstOrDefaultAsync(u => u.Email == request.Email);
            Assert.NotNull(createdUser);
            Assert.Equal(hashedPassword, createdUser.PasswordHash);
        }

        [Fact]
        public async Task RegisterAsync_WithValidRequest_SecondUserShouldBeAssignedRole()
        {
            // Arrange
            var adminUser = new ApplicationUser
            {
                Email = "admin@example.com",
                PasswordHash = "hashed_password_admin_123",
                Role = Roles.Admin
            };
            _dbContext.Users.Add(adminUser);
            await _dbContext.SaveChangesAsync();

            var request = new RegisterRequest("test@example.com", "Test@123", Roles.Customer);

            var hashedPassword = "hashed_password_123";
            _mockHasher
                .Setup(h => h.HashPassword(It.IsAny<ApplicationUser>(), request.Password))
                .Returns(hashedPassword);

            // Act 
            var result = await _authService.RegisterAsync(request);

            // Assert
            Assert.True(result.IsSuccess);
            Assert.NotNull(result.Value);
            Assert.Equal(request.Email, result.Value.Email);
            Assert.Equal(Roles.Customer, result.Value.Role);

            Assert.NotEmpty(result.Value.Token);
            var tokenParts = result.Value.Token.Split('.');
            Assert.Equal(3, tokenParts.Length);

            var createdUser = await _dbContext.Users.FirstOrDefaultAsync(u => u.Email == request.Email);
            Assert.NotNull(createdUser);
            Assert.Equal(hashedPassword, createdUser.PasswordHash);
        }

        [Fact]
        public async Task RegisterAsync_WithAdminRole_ShouldAssignAdminRole()
        {
            // Arrange 
            var request = new RegisterRequest("admin@example.com", "Test@123", Roles.Admin);

            _mockHasher
                .Setup(h => h.HashPassword(It.IsAny<ApplicationUser>(), It.IsAny<string>()))
                .Returns("hashed_password_123");

            // Act
            var result = await _authService.RegisterAsync(request);

            // Assert
            Assert.True(result.IsSuccess);
            Assert.NotNull(result.Value);
            Assert.Equal(Roles.Admin, result.Value.Role);

            var createdUser = await _dbContext.Users.FirstOrDefaultAsync(u => u.Email == request.Email);
            Assert.NotNull(createdUser);
            Assert.Equal(Roles.Admin, createdUser.Role);
        }

        [Fact]
        public async Task RegisterAsync_WithDuplicateEmail_ShouldFail()
        {
            // Arrange
            var email = "duplicate@example.com";

            _dbContext.Users.Add(new ApplicationUser
            {
                Email = email,
                PasswordHash = "existing_hash",
                Role = Roles.Customer
            });

            await _dbContext.SaveChangesAsync();

            var request = new RegisterRequest(
                email,
                "Test@1234",
                Roles.Customer);

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
            var existingUser = new ApplicationUser
            {
                Email = "test@example.com",
                PasswordHash = "existing_hash",
                Role = Roles.Customer
            };

            _dbContext.Users.Add(existingUser);
            await _dbContext.SaveChangesAsync();

            var request = new RegisterRequest("TEST@EXAMPLE.COM", "Test@1234", Roles.Customer);

            _mockHasher
                .Setup(h => h.HashPassword(It.IsAny<ApplicationUser>(), It.IsAny<string>()))
                .Returns("hashed_password");

            // Act
            var result = await _authService.RegisterAsync(request);

            // Assert
            Assert.False(result.IsSuccess);
            Assert.Equal(ResultErrorType.Conflict, result.ErrorType);
        }

        [Fact]
        public async Task RegisterAsync_WithWhitespaceAndUppercaseEmail_ShouldNormalizeEmail()
        {
            // Arrange
            var request = new RegisterRequest("  TEST@EXAMPLE.COM  ", "Test@1234", Roles.Customer);

            _mockHasher
                .Setup(h => h.HashPassword(It.IsAny<ApplicationUser>(), It.IsAny<string>()))
                .Returns("hashed_password");

            // Act
            var result = await _authService.RegisterAsync(request);

            // Assert
            Assert.True(result.IsSuccess);
            Assert.Equal("test@example.com", result.Value!.Email);

            var createdUser = await _dbContext.Users.FirstOrDefaultAsync(u => u.Email == "test@example.com");
            Assert.NotNull(createdUser);
        }

        #endregion

        #region Login Tests

        [Fact]
        public async Task LoginAsync_WithValidRequest_ShouldSucceed()
        {
            // Arrange
            var email = "test@example.com";
            var password = "Test@123!";

            var user = new ApplicationUser
            {
                Email = email,
                PasswordHash = "hashed_password_123",
                Role = Roles.Customer
            };

            _dbContext.Users.Add(user);
            await _dbContext.SaveChangesAsync();

            var request = new LoginRequest(email, password);

            _mockHasher
                .Setup(h => h.VerifyHashedPassword(It.IsAny<ApplicationUser>(), user.PasswordHash, password))
                .Returns(PasswordVerificationResult.Success);

            // Act
            var result = await _authService.LoginAsync(request);

            // Assert
            Assert.True(result.IsSuccess);
            Assert.NotNull(result.Value);
            Assert.Equal(email, result.Value.Email);
            Assert.Equal(Roles.Customer, result.Value.Role);

            Assert.NotEmpty(result.Value.Token);
            var tokenParts = result.Value.Token.Split('.');
            Assert.Equal(3, tokenParts.Length);
        }

        [Fact]
        public async Task LoginAsync_WithNonExistentUser_ShouldFail()
        {
            // Arrange
            var request = new LoginRequest("nonexistent@example.com", "Test@1234");

            // Act
            var result = await _authService.LoginAsync(request);

            // Assert
            Assert.False(result.IsSuccess);
            Assert.Contains("Invalid credentials", result.Error ?? "");
            Assert.Equal(ResultErrorType.Unauthorized, result.ErrorType);
        }

        [Fact]
        public async Task LoginAsync_WithIncorrectPassword_ShouldFail()
        {
            // Arrange
            var user = new ApplicationUser
            {
                Email = "user@example.com",
                PasswordHash = "hashed_password",
                Role = Roles.Customer
            };

            _dbContext.Users.Add(user);
            await _dbContext.SaveChangesAsync();

            var request = new LoginRequest("user@example.com", "WrongPassword");

            _mockHasher
                .Setup(h => h.VerifyHashedPassword(It.IsAny<ApplicationUser>(), "hashed_password", "WrongPassword"))
                .Returns(PasswordVerificationResult.Failed);

            // Act
            var result = await _authService.LoginAsync(request);

            // Assert
            Assert.False(result.IsSuccess);
            Assert.Contains("Invalid credentials", result.Error ?? "");
            Assert.Equal(ResultErrorType.Unauthorized, result.ErrorType);
        }

        [Fact]
        public async Task LoginAsync_WithCaseInsensitiveEmail_ShouldSucceed()
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

            var request = new LoginRequest("TEST@EXAMPLE.COM", "Test@1234");

            _mockHasher
                .Setup(h => h.VerifyHashedPassword(It.IsAny<ApplicationUser>(), "hashed_password", "Test@1234"))
                .Returns(PasswordVerificationResult.Success);

            // Act
            var result = await _authService.LoginAsync(request);

            // Assert
            Assert.True(result.IsSuccess);
        }

        [Fact]
        public async Task LoginAsync_WithAdminUser_ShouldSucceed()
        {
            // Arrange
            var email = "admin@example.com";
            var password = "Test@1234";
            var user = new ApplicationUser
            {
                Email = email,
                PasswordHash = "hashed_password",
                Role = Roles.Admin
            };

            _dbContext.Users.Add(user);
            await _dbContext.SaveChangesAsync();

            var request = new LoginRequest(email, password);

            _mockHasher
                .Setup(h => h.VerifyHashedPassword(It.IsAny<ApplicationUser>(), "hashed_password", password))
                .Returns(PasswordVerificationResult.Success);

            // Act
            var result = await _authService.LoginAsync(request);

            // Assert
            Assert.True(result.IsSuccess);
            Assert.Equal(Roles.Admin, result.Value!.Role);
        }

        [Fact]
        public async Task LoginAsync_WhenRehashNeeded_ShouldUpdatePassword()
        {
            // Arrange
            var email = "rehash@example.com";
            var password = "Test@1234";
            var oldHash = "old_hashed_password";
            var newHash = "new_hashed_password";

            var user = new ApplicationUser
            {
                Email = email,
                PasswordHash = oldHash,
                Role = Roles.Customer
            };

            _dbContext.Users.Add(user);
            await _dbContext.SaveChangesAsync();

            var request = new LoginRequest(email, password);

            _mockHasher
                .Setup(h => h.VerifyHashedPassword(It.IsAny<ApplicationUser>(), oldHash, password))
                .Returns(PasswordVerificationResult.SuccessRehashNeeded);

            _mockHasher
                .Setup(h => h.HashPassword(It.IsAny<ApplicationUser>(), password))
                .Returns(newHash);

            // Act
            var result = await _authService.LoginAsync(request);

            // Assert
            Assert.True(result.IsSuccess);

            var updatedUser = await _dbContext.Users.FirstOrDefaultAsync(u => u.Email == email);
            Assert.Equal(newHash, updatedUser!.PasswordHash);
        }
        #endregion
    }
}