namespace Catalog.Api.IntegrationTests;

/// <summary>Shares one factory (and its containers) across all test classes.</summary>
[CollectionDefinition(Name)]
public sealed class CatalogApiCollection : ICollectionFixture<CatalogApiFactory>
{
    public const string Name = "CatalogApi";
}
