using System.Threading.Channels;
using Isley.Telemetry;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Isley.ServerBridge;

internal sealed class BridgeFrameQueue
{
    private readonly Channel<TelemetryFrame> _frames =
        Channel.CreateBounded<TelemetryFrame>(new BoundedChannelOptions(2)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });

    internal ValueTask EnqueueAsync(
        TelemetryFrame frame,
        CancellationToken cancellationToken) =>
        _frames.Writer.WriteAsync(frame, cancellationToken);

    internal IAsyncEnumerable<TelemetryFrame> ReadAllAsync(
        CancellationToken cancellationToken) =>
        _frames.Reader.ReadAllAsync(cancellationToken);

    internal TelemetryFrame TakeNewest(TelemetryFrame seed)
    {
        var newest = seed;
        while (_frames.Reader.TryRead(out var next))
        {
            newest = next;
        }
        return newest;
    }

    internal bool TryDequeueNewest(out TelemetryFrame frame)
    {
        if (!_frames.Reader.TryRead(out frame!))
        {
            frame = null!;
            return false;
        }
        frame = TakeNewest(frame);
        return true;
    }
}

internal sealed class FrameFactory(IOptions<BridgeOptions> options)
{
    private readonly BridgeOptions _options = options.Value;
    private readonly string _sessionId =
        Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16))
            .ToLowerInvariant();
    private long _sequence;

    internal TelemetryFrame Create(
        DateTimeOffset sampledAt,
        string source,
        TelemetryCapabilities capabilities,
        IReadOnlyList<TelemetryEntity> entities)
    {
        // Server-wide player awareness is an explicit operator choice. Never let a
        // plugin/RCON player ShareScope.Server value silently escalate a
        // privacy-filtered bridge into all-player awareness. AI animals may still
        // use Server scope so authorized wildlife can appear without revealing
        // every Steam player.
        var sharedEntities = _options.ServerWideAwareness
            ? entities.Select(entity => entity with
            {
                ShareScope = TelemetryShareScope.Server
            }).ToArray()
            : entities.Select(entity =>
                entity is
                {
                    ShareScope: TelemetryShareScope.Server,
                    Kind: not TelemetryEntityKind.AiAnimal
                }
                    ? entity with { ShareScope = TelemetryShareScope.Self }
                    : entity).ToArray();
        return new()
        {
            ServerId = _options.ServerId,
            ServerName = _options.ServerName,
            BridgeSessionId = _sessionId,
            Sequence = Interlocked.Increment(ref _sequence),
            SampledAt = sampledAt,
            Source = Isley.Telemetry.TelemetryValidation.CleanLabel(source, "unknown", 48),
            VisibilityPolicy = _options.ServerWideAwareness
                ? TelemetryVisibilityPolicy.ServerWide
                : TelemetryVisibilityPolicy.PrivacyFiltered,
            Capabilities = capabilities,
            Entities = sharedEntities
        };
    }
}

internal sealed class BridgeRuntimeStatus
{
    private readonly object _gate = new();
    private DateTimeOffset? _lastSampledAt;
    private DateTimeOffset? _lastPublishedAt;
    private long _lastSequence;
    private int _lastEntityCount;
    private string _sourceState = "waiting";
    private string _relayState = "waiting";
    private string _detail = "Waiting for configuration.";

    internal void Sampled(TelemetryFrame frame)
    {
        lock (_gate)
        {
            _lastSampledAt = frame.SampledAt;
            _lastSequence = frame.Sequence;
            _lastEntityCount = frame.Entities.Count;
            _sourceState = "live";
            _detail = string.Empty;
        }
    }

    internal void SourceError(string detail)
    {
        lock (_gate)
        {
            _sourceState = "error";
            _detail = detail;
        }
    }

    internal void Published(TelemetryFrame frame)
    {
        lock (_gate)
        {
            _lastPublishedAt = DateTimeOffset.UtcNow;
            _lastSequence = frame.Sequence;
            _relayState = "live";
        }
    }

    internal void RelayError(string detail)
    {
        lock (_gate)
        {
            _relayState = "error";
            _detail = detail;
        }
    }

    internal object Snapshot()
    {
        lock (_gate)
        {
            return new
            {
                source = _sourceState,
                relay = _relayState,
                detail = _detail,
                lastSampledAt = _lastSampledAt,
                lastPublishedAt = _lastPublishedAt,
                lastSequence = _lastSequence,
                lastEntityCount = _lastEntityCount
            };
        }
    }

    internal bool Ready(BridgeOptions bridge, RconOptions rcon)
    {
        lock (_gate)
        {
            var sourceConfigured = bridge.PluginEnabled
                                   || bridge.RconEnabled && rcon.Configured;
            return bridge.RelayConfigured
                   && sourceConfigured
                   && _relayState == "live";
        }
    }
}

internal sealed class BridgeReadinessHealthCheck(
    IOptions<BridgeOptions> bridge,
    IOptions<RconOptions> rcon,
    BridgeRuntimeStatus status)
    : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(status.Ready(bridge.Value, rcon.Value)
            ? HealthCheckResult.Healthy("Isley bridge is configured.")
            : HealthCheckResult.Degraded(
                "Configure the relay and at least one authorized telemetry source."));
}
