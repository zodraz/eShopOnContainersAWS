using System.Diagnostics.Metrics;
using OpenTelemetry.Metrics;

namespace Ordering.SignalrHub.Metrics;

public static class RealtimeMapMetrics
{
    private static readonly Meter RealtimeMapMeter = new("RealtimeMap");

    public static MeterProviderBuilder AddRealtimeMapInstrumentation(this MeterProviderBuilder builder)
        => builder
            .AddMeter("RealtimeMap");


    public static readonly AdjustableGauge SignalRConnections = new(
        RealtimeMapMeter,
        "app_signalr_connections",
        description: "Number of SignalR connections");
}