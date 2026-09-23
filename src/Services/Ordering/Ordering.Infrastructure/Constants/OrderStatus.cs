namespace Ordering.Infrastructure.Constants
{
    public enum OrderStatus
    {
        Pending,
        Confirmed,
        Processing,
        Shipped,
        Delivered,
        Cancelled,
        Refunded,
        Failed
    }
}
