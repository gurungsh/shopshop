using Serilog;
using Serilog.Sinks.OpenTelemetry;

namespace BuildingBlocks.Web.Observability;

public static class SerilogOtlpExtensions
{
    private const string DefaultEndpoint = "http://localhost:4317";

    public static LoggerConfiguration WriteToOtlp(
        this LoggerConfiguration configuration, string serviceName)
    {
        return configuration.WriteTo.OpenTelemetry(options =>
        {
            options.Endpoint = Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT")
                 ?? DefaultEndpoint;
            options.Protocol = OtlpProtocol.Grpc;
            options.ResourceAttributes = new Dictionary<string, object>
            {
                ["service.name"] = serviceName
            };
        });
    }
}
