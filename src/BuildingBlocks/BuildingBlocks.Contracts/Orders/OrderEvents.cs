namespace BuildingBlocks.Contracts.Orders
{
    public sealed record OrderPlacedEvent(
        Guid OrderId,
        Guid CustomerId,
        decimal TotalAmount,
        IReadOnlyList<OrderPlacedItem> Items,
        DateTime OccurredAtUtc);
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
}