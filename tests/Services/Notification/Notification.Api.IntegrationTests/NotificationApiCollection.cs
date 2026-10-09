namespace Notification.Api.IntegrationTests;

/// <summary>Shares one factory (and its container) across all test classes.</summary>
[CollectionDefinition(Name)]
public sealed class NotificationApiCollection : ICollectionFixture<NotificationApiFactory>
{
    public const string Name = "NotificationApi";
}
