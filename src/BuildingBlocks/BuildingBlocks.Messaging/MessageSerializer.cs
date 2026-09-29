using System.Text.Json;

namespace BuildingBlocks.Messaging
{
    public static class MessageSerializer
    {
        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        public static string Serialize<T>(T message) => JsonSerializer.Serialize(message, Options);
        public static T Deserialize<T>(ReadOnlyMemory<byte> body)
        {
            return JsonSerializer.Deserialize<T>(body.Span, Options)
                ?? throw new JsonException($"Message body could not be deserialized to {typeof(T).Name}.");
        }
    }
}
