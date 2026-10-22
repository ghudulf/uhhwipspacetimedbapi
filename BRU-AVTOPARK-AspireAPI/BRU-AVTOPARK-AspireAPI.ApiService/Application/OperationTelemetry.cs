using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace BRU_AVTOPARK_AspireAPI.ApiService.Application;

public static class OperationTelemetry
{
    public const string Name = "BRU.Application";
    public static readonly ActivitySource Traces = new(Name);
    private static readonly Meter Meter = new(Name);
    public static readonly Counter<long> Completed = Meter.CreateCounter<long>("bru.commands.completed");
    public static readonly Histogram<double> Duration = Meter.CreateHistogram<double>("bru.commands.duration", "ms");
    public static readonly UpDownCounter<long> Connections = Meter.CreateUpDownCounter<long>("bru.realtime.connections");
    public static readonly UpDownCounter<long> Queued = Meter.CreateUpDownCounter<long>("bru.realtime.pending");
}
