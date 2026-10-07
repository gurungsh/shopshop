namespace BuildingBlocks.Contracts.Orders
{
    public sealed record OrderPlacedEvent(
        Guid OrderId,
        Guid CustomerId,
        decimal TotalAmount,
        IReadOnlyList<OrderPlacedItem> Items,
        DateTime OccurredAtUtc,
        string? PaymentMethodId = null);
    public sealed record OrderPlacedItem(
        Guid ProductId,
        string ProductName,
        int Quantity,
        decimal UnitPrice);
    public sealed record OrderCancelledEvent(
        Guid OrderId,
        Guid CustomerId,
        string CancelledBy,
        DateTime OccurredAtUtc);
    public sealed record OrderPaymentRetriedEvent(
        Guid OrderId,
        Guid CustomerId,
        decimal TotalAmount,
        int Attempt,
        string PaymentMethodId,
        DateTime OccurredAtUtc);
    public sealed record OrderConfirmedEvent(
        Guid OrderId,
        Guid CustomerId,
        decimal TotalAmount,
        DateTime OccurredAtUtc);
    public sealed record OrderPaymentFailedEvent(
        Guid OrderId,
        Guid CustomerId,
        string Reason,
        int RetriesRemaining,
        DateTime OccurredAtUtc);
}