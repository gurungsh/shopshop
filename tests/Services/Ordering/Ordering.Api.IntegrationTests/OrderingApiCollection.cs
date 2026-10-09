namespace Ordering.Api.IntegrationTests;

/// <summary>Shares one factory (and its containers) across all test classes.</summary>
[CollectionDefinition(Name)]
public sealed class OrderingApiCollection : ICollectionFixture<OrderingApiFactory>
{
    public const string Name = "OrderingApi";
}
