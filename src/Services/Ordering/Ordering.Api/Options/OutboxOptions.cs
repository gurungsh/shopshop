namespace Ordering.Api.Options;

public record OutboxOptions
{
    public const string SectionName = "Outbox";

    public TimeSpan PollingInterval { get; init; } = TimeSpan.FromSeconds(2);
    public int BatchSize { get; init; } = 20;
}
