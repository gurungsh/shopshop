namespace BuildingBlocks.Contracts.Payments
{
    public sealed record PaymentSucceededEvent(
        Guid OrderId,
        Guid PaymentId,
        int Attempt,
        decimal Amount,
        string? ProviderReference,
        DateTime OccurredAtUtc);
    public sealed record PaymentFailedEvent(
        Guid OrderId,
        Guid PaymentId,
        int Attempt,
        decimal Amount,
        string Reason,
        DateTime OccurredAtUtc);
}
