using System.Text.Json.Serialization;

namespace Identity.Api.DTOs
{
    public sealed record JwkResponse(string Kty, string Use, string Alg, string Kid, string N, string E);
    public sealed record JwksResponse(IReadOnlyList<JwkResponse> Keys);
    public sealed record OpenIdConfigurationResponse(
        string Issuer,
        [property: JsonPropertyName("jwks_uri")] string jwksUri);
}
