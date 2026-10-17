using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Identity.Api.Options;
using Identity.Api.Security;
using Microsoft.IdentityModel.Tokens;
using OptionsFactory = Microsoft.Extensions.Options.Options;

namespace Identity.Api.UnitTests;

public class JwtKeyProviderTests
{
    private readonly JwtKeyProvider _jwtKeyProvider;

    public JwtKeyProviderTests()
    {
        using var rsa = RSA.Create(2048);

        var options = OptionsFactory.Create(new JwtOptions
        {
            PrivateKeyPem = rsa.ExportPkcs8PrivateKeyPem(),
            KeyId = "test-key"
        });

        _jwtKeyProvider = new JwtKeyProvider(options);
    }

    #region GetJwks Tests

    [Fact]
    public void GetJwks_WithValidKey_ShouldExposePublicKeyFields()
    {
        // Act
        var jwks = _jwtKeyProvider.GetJwks();

        // Assert            
        var key = Assert.Single(jwks.Keys);
        Assert.Equal("RSA", key.Kty);
        Assert.Equal("RS256", key.Alg);
        Assert.Equal("test-key", key.Kid);
        Assert.False(string.IsNullOrEmpty(key.N));
        Assert.False(string.IsNullOrEmpty(key.E));
    }

    [Fact]
    public void GetJwks_WithValidKey_ShouldVerifyTokenSignedByProvider()
    {
        // Arrange
        var handler = new JwtSecurityTokenHandler();
        var token = handler.WriteToken(handler.CreateToken(new SecurityTokenDescriptor
        {
            Subject = new System.Security.Claims.ClaimsIdentity([new Claim("sub", "user-1")]),
            Expires = DateTime.UtcNow.AddMinutes(5),
            SigningCredentials = new SigningCredentials(_jwtKeyProvider.SigningKey, SecurityAlgorithms.RsaSha256)
        }));

        var jwk = _jwtKeyProvider.GetJwks().Keys[0];
        var publicKey = new RsaSecurityKey(new RSAParameters
        {
            Modulus = Base64UrlEncoder.DecodeBytes(jwk.N),
            Exponent = Base64UrlEncoder.DecodeBytes(jwk.E)
        });

        publicKey.KeyId = jwk.Kid;

        // Act
        handler.ValidateToken(token, new TokenValidationParameters
        {
            IssuerSigningKey = publicKey,
            ValidateIssuer = false,
            ValidateAudience = false
        }, out var validatedToken);

        // Assert
        Assert.IsType<JwtSecurityToken>(validatedToken);
    }

    #endregion

    #region Constructor Tests
    [Fact]
    public void Constructor_WithInvalidPem_ShouldThrow()
    {
        // Arrange
        var options = OptionsFactory.Create(new JwtOptions { PrivateKeyPem = "not a pem", KeyId = "x" });

        // Act and Assert
        Assert.ThrowsAny<Exception>(() => new JwtKeyProvider(options));
    }

    #endregion
}
