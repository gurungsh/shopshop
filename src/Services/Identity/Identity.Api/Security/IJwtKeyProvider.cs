using Identity.Api.DTOs;
using Microsoft.IdentityModel.Tokens;

namespace Identity.Api.Security
{
    public interface IJwtKeyProvider
    {
        SecurityKey SigningKey { get; }
        JwksResponse GetJwks();
    }
}
