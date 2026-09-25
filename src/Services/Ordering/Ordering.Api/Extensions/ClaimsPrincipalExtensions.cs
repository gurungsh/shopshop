using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Common.Core;

namespace Ordering.Api.Extensions
{
    public sealed record CallerContext(Guid UserId, string Role);

    public static class ClaimsPrincipalExtensions
    {
        public static CallerContext ToCallerContext(this ClaimsPrincipal principal)
        {
            var userIdClaim = principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
                ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (!Guid.TryParse(userIdClaim, out var userId))
            {
                throw new InvalidOperationException("Authenticated principal is missing a valid user id claim.");
            }

            var role = principal.IsInRole(Roles.Admin) ? Roles.Admin : Roles.Customer;

            return new CallerContext(userId, role);
        }
    }
}
