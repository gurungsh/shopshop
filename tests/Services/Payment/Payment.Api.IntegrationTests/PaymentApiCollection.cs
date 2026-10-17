namespace Payment.Api.IntegrationTests;

/// <summary>Shares one factory (and its containers) across all test classes.</summary>
[CollectionDefinition(Name)]
public sealed class PaymentApiCollection : ICollectionFixture<PaymentApiFactory>
{
    public const string Name = "PaymentApi";
}
