using System.Security.Claims;
using Identity.API.Common;
using Identity.API.DTOs;

namespace Identity.API.Services
{
    public interface IAuthService
    {
        Task<Result<AuthResponse>> RegisterAsync(RegisterRequest request);
        Task<Result<AuthResponse>> LoginAsync(LoginRequest request);
        Task<Result<AuthResponse>> RefreshTokenAsync(RefreshTokenRequest request);
        Task<Result<bool>> LogoutAsync(ClaimsPrincipal principal);
        Task<Result<UserResponse>> GetCurrentUserAsync(ClaimsPrincipal principal);
        Task<Result<UserResponse>> UpdateCurrentUserAsync(ClaimsPrincipal principal, UpdateCurrentUserRequest request);
        Task<Result<List<UserResponse>>> GetUsersAsync();
        Task<Result<UserResponse>> GetUserAsync(string email);
        Task<Result<UserResponse>> CreateUserAsync(AdminCreateUserRequest request);
        Task<Result<UserResponse>> UpdateUserAsync(Guid id, AdminUpdateUserRequest request);
        Task<Result<bool>> DeleteUserAsync(Guid id);
    }
}
