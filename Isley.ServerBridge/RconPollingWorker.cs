using Isley.Telemetry;
using Microsoft.Extensions.Options;
using System.Net.Sockets;

namespace Isley.ServerBridge;

internal sealed class RconPollingWorker(
    EvrimaRconClient rcon,
    RconPlayerDataParser parser,
    FrameFactory frames,
    BridgeFrameQueue queue,
    BridgeRuntimeStatus status,
    IOptions<BridgeOptions> bridgeOptions,
    IOptions<RconOptions> rconOptions,
    ILogger<RconPollingWorker> logger)
    : BackgroundService
{
    private static readonly TelemetryCapabilities RconCapabilities = new()
    {
        Position = true,
        AuthoritativeDirection = false,
        Health = true,
        Growth = true,
        Stamina = true,
        Food = true,
        Water = true,
        Conditions = false,
        AiAnimals = false
    };

    private readonly BridgeOptions _bridge = bridgeOptions.Value;
    private readonly RconOptions _options = rconOptions.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_bridge.RconEnabled)
        {
            return;
        }
        if (!_options.Configured || !_bridge.RelayConfigured)
        {
            status.SourceError("RCON or relay configuration is incomplete.");
            return;
        }

        var failureDelay = TimeSpan.FromMilliseconds(_options.PollIntervalMilliseconds);
        while (!stoppingToken.IsCancellationRequested)
        {
            var cycleStarted = DateTimeOffset.UtcNow;
            try
            {
                var raw = await rcon.GetPlayerDataAsync(stoppingToken);
                var sampledAt = DateTimeOffset.UtcNow;
                var entities = parser.Parse(raw, sampledAt);
                var frame = frames.Create(sampledAt, "evrima-rcon", RconCapabilities, entities);
                var errors = TelemetryValidation.Validate(frame, sampledAt);
                if (errors.Count > 0)
                {
                    throw new InvalidDataException(string.Join(" ", errors));
                }
                status.Sampled(frame);
                await queue.EnqueueAsync(frame, stoppingToken);
                failureDelay = TimeSpan.FromMilliseconds(_options.PollIntervalMilliseconds);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex) when (
                ex is IOException
                or SocketException
                or UnauthorizedAccessException
                or InvalidOperationException
                or InvalidDataException
                or FormatException)
            {
                status.SourceError("RCON unavailable; reconnecting with backoff.");
                logger.LogWarning("Isley RCON sample failed: {Message}", ex.Message);
                await Task.Delay(failureDelay, stoppingToken);
                failureDelay = TimeSpan.FromMilliseconds(
                    Math.Min(15_000, failureDelay.TotalMilliseconds * 2));
                continue;
            }

            var elapsed = DateTimeOffset.UtcNow - cycleStarted;
            var remaining = TimeSpan.FromMilliseconds(_options.PollIntervalMilliseconds) - elapsed;
            if (remaining > TimeSpan.Zero)
            {
                await Task.Delay(remaining, stoppingToken);
            }
        }
    }
}
