using Isley.Telemetry;
using Microsoft.Extensions.Options;

namespace Isley.Relay;

/// <summary>
/// Process-local operational counters for the loopback-gated /metrics
/// endpoint. Counts are intentionally aggregate only: no bridge server IDs,
/// no viewer Steam IDs, no entity data.
/// </summary>
internal sealed class RelayMetrics(IOptions<RelayOptions> options)
{
    private readonly DateTimeOffset _startedAt = DateTimeOffset.UtcNow;
    private readonly RelayOptions _options = options.Value;
    private long _framesRelayed;
    private long _rateLimitRejections;
    private long _viewerConnections;

    internal void FrameRelayed() => Interlocked.Increment(ref _framesRelayed);

    internal void RateLimitRejected() => Interlocked.Increment(ref _rateLimitRejections);

    internal void ViewerConnected() => Interlocked.Increment(ref _viewerConnections);

    internal object Snapshot(int activeBridges, int activeViewers)
    {
        var now = DateTimeOffset.UtcNow;
        return new
        {
            service = "Isley Relay",
            protocolVersion = TelemetryProtocol.Version,
            viewerStreamVersion = TelemetryProtocol.ViewerStreamVersion,
            visibility = _options.MetricsPubliclyVisible ? "network" : "loopback-only",
            startedAt = _startedAt,
            uptimeSeconds = Math.Max(0, (long)(now - _startedAt).TotalSeconds),
            framesRelayed = Interlocked.Read(ref _framesRelayed),
            rateLimitRejections = Interlocked.Read(ref _rateLimitRejections),
            viewerConnectionsTotal = Interlocked.Read(ref _viewerConnections),
            activeBridges,
            activeViewers
        };
    }
}
