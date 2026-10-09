namespace Identity.Api.IntegrationTests;

/// <summary>Shares one factory (and its container) across all test classes.</summary>
[CollectionDefinition(Name)]
public sealed class IdentityApiCollection : ICollectionFixture<IdentityApiFactory>
{
    public const string Name = "IdentityApi";
}
