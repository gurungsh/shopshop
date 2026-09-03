namespace Identity.API.Services.Common
{
    public record JwtSettings
    {
        public const string SectionName = "JwtSettings";

        public string Secret { get; init; } = string.Empty;
        public string Issuer { get; set; } = string.Empty;
        public string Audience { get; set; } = string.Empty;
        public int ExpirationInMinutes { get; set; } = 60;
    }
}
