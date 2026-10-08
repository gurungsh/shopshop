using System.Security.Cryptography;
using Identity.Api.DTOs;
using Identity.Api.Options;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Identity.Api.Security
{
    public class JwtKeyProvider : IJwtKeyProvider, IDisposable
    {
        private readonly RSA _rsa;
        private readonly string _keyId;

        public JwtKeyProvider(IOptions<JwtOptions> jwtOptions)
        {
            var settings = jwtOptions.Value;

            _rsa = RSA.Create();
            _rsa.ImportFromPem(settings.PrivateKeyPem);
            _keyId = settings.KeyId;

            SigningKey = new RsaSecurityKey(_rsa) { KeyId = _keyId };
        }

        public SecurityKey SigningKey { get; }

        public JwksResponse GetJwks()
        {
            var parameters = _rsa.ExportParameters(false);

            var key = new JwkResponse(
                Kty: "RSA",
                Use: "sig",
                Alg: SecurityAlgorithms.RsaSha256,
                Kid: _keyId,
                N: Base64UrlEncoder.Encode(parameters.Modulus),
                E: Base64UrlEncoder.Encode(parameters.Exponent));

            return new JwksResponse([key]);
        }

        public void Dispose() => _rsa.Dispose();
    }
}
