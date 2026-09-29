namespace BuildingBlocks.Messaging
{
    public interface IMessagePublisher
    {
        Task PublishAsync(
            string exchange,
            string routingKey,
            string messageId,
            ReadOnlyMemory<byte> body,
            CancellationToken cancellationToken = default);
    }
}
