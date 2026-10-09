using System.Diagnostics;

namespace BuildingBlocks.Messaging;

public static class OutboxTracing
{
    public const string SourceName = "BuildingBlocks.Messaging";

    private static readonly ActivitySource Source = new ActivitySource(SourceName);

    public static string? CaptureTraceParent() => Activity.Current?.Id;

    public static Activity? StartPublishActivity(string? traceParent)
    {
        ActivityContext parent = default;

        if (traceParent is not null)
        {
            ActivityContext.TryParse(traceParent, null, out parent);
        }

        return Source.StartActivity("outbox publish", ActivityKind.Internal, parent);
    }
}
