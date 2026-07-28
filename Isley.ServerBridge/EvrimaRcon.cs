using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using Isley.Telemetry;
using Microsoft.Extensions.Options;

namespace Isley.ServerBridge;

internal sealed class EvrimaRconClient(
    IOptions<RconOptions> options,
    ILogger<EvrimaRconClient> logger) : IAsyncDisposable
{
    private readonly RconOptions _options = options.Value;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private TcpClient? _client;
    private NetworkStream? _stream;

    internal async Task<string> GetPlayerDataAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_stream is null || _client?.Connected != true)
            {
                await ConnectAndAuthenticateAsync(cancellationToken);
            }
            try
            {
                await _stream!.WriteAsync(
                    new byte[] { 0x02, 0x77, 0x00 },
                    cancellationToken);
                return await ReadResponseAsync(
                    _options.ResponseTimeoutMilliseconds,
                    cancellationToken);
            }
            catch
            {
                Disconnect();
                throw;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task ConnectAndAuthenticateAsync(CancellationToken cancellationToken)
    {
        Disconnect();
        var address = await ResolveAddressAsync(cancellationToken);
        var client = new TcpClient(address.AddressFamily);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_options.ConnectTimeoutMilliseconds);
        await client.ConnectAsync(address, _options.Port, timeout.Token);
        var stream = client.GetStream();

        var password = Encoding.UTF8.GetBytes(_options.Password);
        var authentication = new byte[password.Length + 2];
        authentication[0] = 0x01;
        password.CopyTo(authentication, 1);
        authentication[^1] = 0x00;
        await stream.WriteAsync(authentication, timeout.Token);

        _client = client;
        _stream = stream;
        var response = await ReadResponseAsync(
            _options.ResponseTimeoutMilliseconds,
            cancellationToken);
        if (!response.Contains("Password Accepted", StringComparison.Ordinal))
        {
            Disconnect();
            throw new UnauthorizedAccessException("The Isle RCON rejected the configured password.");
        }
    }

    private async Task<IPAddress> ResolveAddressAsync(CancellationToken cancellationToken)
    {
        var addresses = await Dns.GetHostAddressesAsync(_options.Host, cancellationToken);
        var selected = _options.AllowUnsafeRemoteRcon
            ? addresses.FirstOrDefault()
            : addresses.FirstOrDefault(IsPrivateOrLoopback);
        if (selected is null)
        {
            throw new InvalidOperationException(
                "RCON resolved to a public address. Run the bridge beside the server or use a private VPN; "
                + "set AllowUnsafeRemoteRcon only after securing that network path.");
        }
        return selected;
    }

    private async Task<string> ReadResponseAsync(
        int firstByteTimeoutMilliseconds,
        CancellationToken cancellationToken)
    {
        if (_stream is null)
        {
            throw new InvalidOperationException("RCON is not connected.");
        }

        using var payload = new MemoryStream();
        var buffer = new byte[16 * 1024];
        while (payload.Length < 1024 * 1024)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(payload.Length == 0
                ? firstByteTimeoutMilliseconds
                : _options.ReadIdleMilliseconds);
            try
            {
                var read = await _stream.ReadAsync(buffer, timeout.Token);
                if (read == 0)
                {
                    break;
                }
                await payload.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }
            catch (OperationCanceledException) when (
                !cancellationToken.IsCancellationRequested
                && payload.Length > 0)
            {
                break;
            }
        }
        if (payload.Length == 0)
        {
            throw new IOException("The Isle RCON did not answer before the timeout.");
        }
        if (payload.Length >= 1024 * 1024)
        {
            throw new InvalidDataException("The Isle RCON response exceeded the bridge limit.");
        }
        return Encoding.UTF8.GetString(payload.ToArray()).TrimEnd('\0');
    }

    private static bool IsPrivateOrLoopback(IPAddress address)
    {
        if (IPAddress.IsLoopback(address)
            || address.IsIPv6LinkLocal
            || address.IsIPv6SiteLocal)
        {
            return true;
        }
        if (address.AddressFamily != AddressFamily.InterNetwork)
        {
            return false;
        }
        var bytes = address.GetAddressBytes();
        return bytes[0] == 10
               || bytes[0] == 127
               || bytes[0] == 169 && bytes[1] == 254
               || bytes[0] == 172 && bytes[1] is >= 16 and <= 31
               || bytes[0] == 192 && bytes[1] == 168;
    }

    private void Disconnect()
    {
        try
        {
            _stream?.Dispose();
            _client?.Dispose();
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "RCON cleanup failed.");
        }
        _stream = null;
        _client = null;
    }

    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync();
        try
        {
            Disconnect();
        }
        finally
        {
            _gate.Release();
            _gate.Dispose();
        }
    }
}

internal sealed partial class RconPlayerDataParser(
    IOptions<RconOptions> options,
    MotionHeadingEstimator headings)
{
    private readonly RconOptions _options = options.Value;

    internal IReadOnlyList<TelemetryEntity> Parse(
        string response,
        DateTimeOffset sampledAt)
    {
        var marker = response.IndexOf("PlayerData", StringComparison.Ordinal);
        var content = marker >= 0
            ? response[(marker + "PlayerData".Length)..]
            : response;
        var players = new List<TelemetryEntity>();
        foreach (var rawLine in content.Split('\n'))
        {
            var line = rawLine.Trim().TrimEnd('\0');
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }
            var match = PlayerLineRegex().Match(line);
            if (!match.Success)
            {
                continue;
            }

            var playerId = match.Groups["id"].Value;
            var x = ParseDouble(match, "x");
            var y = ParseDouble(match, "y");
            var z = ParseDouble(match, "z");
            var direction = headings.Update(playerId, x, y, sampledAt);
            players.Add(new TelemetryEntity
            {
                EntityId = $"player-{playerId}",
                SteamId = TelemetryValidation.IsSteamId(playerId) ? playerId : null,
                DisplayName = TelemetryValidation.CleanLabel(
                    match.Groups["name"].Value,
                    "Player",
                    32),
                Kind = TelemetryEntityKind.Player,
                SpeciesId = NormalizeSpecies(match.Groups["class"].Value),
                X = x,
                Y = y,
                Z = z,
                Yaw = direction.Yaw,
                DirectionQuality = direction.Yaw is null
                    ? TelemetryDirectionQuality.Missing
                    : TelemetryDirectionQuality.MotionInferred,
                GrowthPercent = ParsePercent(match, "growth"),
                HealthPercent = ParsePercent(match, "health"),
                StaminaPercent = ParsePercent(match, "stamina"),
                FoodPercent = ParsePercent(match, "food"),
                WaterPercent = ParsePercent(match, "water"),
                ShareScope = _options.ShareScope
            });
        }
        headings.Prune(players.Select(player => player.EntityId["player-".Length..]), sampledAt);
        return players;
    }

    private static double ParseDouble(Match match, string group) =>
        double.Parse(match.Groups[group].Value, NumberStyles.Float, CultureInfo.InvariantCulture);

    private static double ParsePercent(Match match, string group) =>
        Math.Clamp(ParseDouble(match, group) * 100, 0, 100);

    private static string NormalizeSpecies(string value)
    {
        var normalized = value.Trim();
        if (normalized.StartsWith("BP_", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[3..];
        }
        if (normalized.EndsWith("_C", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[..^2];
        }
        return TelemetryValidation.CleanLabel(normalized, "unknown", 32).ToLowerInvariant();
    }

    [GeneratedRegex(
        @"^Name:\s*(?<name>.*),\s*PlayerID:\s*(?<id>\d+),\s*Location:\s*X=(?<x>[-+0-9.eE]+)\s*Y=(?<y>[-+0-9.eE]+)\s*Z=(?<z>[-+0-9.eE]+),\s*Class:\s*(?<class>[^,]+),\s*Growth:\s*(?<growth>[-+0-9.eE]+),\s*Health:\s*(?<health>[-+0-9.eE]+),\s*Stamina:\s*(?<stamina>[-+0-9.eE]+),\s*Hunger:\s*(?<food>[-+0-9.eE]+),\s*Thirst:\s*(?<water>[-+0-9.eE]+)\s*$",
        RegexOptions.CultureInvariant)]
    private static partial Regex PlayerLineRegex();
}

internal sealed record HeadingEstimate(double? Yaw);

internal sealed class MotionHeadingEstimator
{
    private readonly Dictionary<string, MotionSample> _samples = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    internal HeadingEstimate Update(
        string playerId,
        double x,
        double y,
        DateTimeOffset sampledAt)
    {
        lock (_gate)
        {
            double? yaw = null;
            if (_samples.TryGetValue(playerId, out var previous))
            {
                var deltaX = x - previous.X;
                var deltaY = y - previous.Y;
                var distance = Math.Sqrt(deltaX * deltaX + deltaY * deltaY);
                yaw = distance >= 5
                    ? NormalizeDegrees(Math.Atan2(deltaX, deltaY) * 180 / Math.PI)
                    : previous.Yaw;
            }
            _samples[playerId] = new MotionSample(x, y, yaw, sampledAt);
            return new HeadingEstimate(yaw);
        }
    }

    internal void Prune(IEnumerable<string> activeIds, DateTimeOffset now)
    {
        lock (_gate)
        {
            var active = activeIds.ToHashSet(StringComparer.Ordinal);
            foreach (var id in _samples
                         .Where(item => !active.Contains(item.Key)
                                        || now - item.Value.SampledAt > TimeSpan.FromMinutes(2))
                         .Select(item => item.Key)
                         .ToArray())
            {
                _samples.Remove(id);
            }
        }
    }

    private static double NormalizeDegrees(double value)
    {
        var normalized = value % 360;
        return normalized < 0 ? normalized + 360 : normalized;
    }

    private sealed record MotionSample(
        double X,
        double Y,
        double? Yaw,
        DateTimeOffset SampledAt);
}
