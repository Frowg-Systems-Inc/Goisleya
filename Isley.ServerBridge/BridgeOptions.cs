using System.Net;
using Isley.Telemetry;

namespace Isley.ServerBridge;

public sealed class BridgeOptions
{
    public string ServerId { get; init; } = string.Empty;
    public string ServerName { get; init; } = "My The Isle server";
    public string RelayUrl { get; init; } = string.Empty;
    public string RelaySecret { get; init; } = string.Empty;
    public string SourceMode { get; init; } = "Rcon";
    public bool PluginEnabled { get; init; }
    public string PluginKey { get; init; } = string.Empty;
    public bool AllowRemotePlugin { get; init; }
    public bool ServerWideAwareness { get; init; }

    internal bool RelayConfigured =>
        !string.IsNullOrWhiteSpace(ServerId)
        && !string.IsNullOrWhiteSpace(RelayUrl)
        && RelaySecret.Length >= 32;

    internal bool RconEnabled =>
        string.Equals(SourceMode, "Rcon", StringComparison.OrdinalIgnoreCase)
        || string.Equals(SourceMode, "Both", StringComparison.OrdinalIgnoreCase);

    internal bool PluginCapable =>
        PluginEnabled && PluginKey.Length >= 32;

    public static bool IsValid(BridgeOptions value)
    {
        var sourceValid = value.SourceMode is "Rcon" or "Plugin" or "Both"
                          || value.SourceMode.Equals("rcon", StringComparison.OrdinalIgnoreCase)
                          || value.SourceMode.Equals("plugin", StringComparison.OrdinalIgnoreCase)
                          || value.SourceMode.Equals("both", StringComparison.OrdinalIgnoreCase);
        if (!sourceValid
            || value.ServerName.Length is < 1 or > 80
            || value.PluginEnabled && value.PluginKey.Length < 32)
        {
            return false;
        }
        if (string.IsNullOrWhiteSpace(value.ServerId)
            && string.IsNullOrWhiteSpace(value.RelayUrl)
            && string.IsNullOrWhiteSpace(value.RelaySecret))
        {
            return true;
        }
        return value.RelaySecret.Length >= 32
               && Uri.TryCreate(value.RelayUrl, UriKind.Absolute, out var relay)
               && (relay.Scheme == Uri.UriSchemeHttps
                   || relay.Scheme == Uri.UriSchemeHttp && IsLoopback(relay.Host))
               && ValidServerId(value.ServerId);
    }

    private static bool ValidServerId(string serverId)
    {
        var errors = TelemetryValidation.Validate(new TelemetryFrame
        {
            ServerId = serverId,
            BridgeSessionId = new string('a', 32),
            Sequence = 1,
            SampledAt = DateTimeOffset.UtcNow
        }, DateTimeOffset.UtcNow);
        return errors.All(error => !error.StartsWith("ServerId", StringComparison.Ordinal));
    }

    private static bool IsLoopback(string host) =>
        string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
        || IPAddress.TryParse(host, out var address) && IPAddress.IsLoopback(address);
}

public sealed class RconOptions
{
    public string Host { get; init; } = "127.0.0.1";
    public int Port { get; init; } = 8888;
    public string Password { get; init; } = string.Empty;
    public int PollIntervalMilliseconds { get; init; } = 200;
    public int ConnectTimeoutMilliseconds { get; init; } = 2000;
    public int ResponseTimeoutMilliseconds { get; init; } = 2500;
    public int ReadIdleMilliseconds { get; init; } = 100;
    public bool AllowUnsafeRemoteRcon { get; init; }
    public string DefaultShareScope { get; init; } = "Self";

    internal TelemetryShareScope ShareScope =>
        Enum.TryParse<TelemetryShareScope>(DefaultShareScope, true, out var value)
            ? value
            : TelemetryShareScope.Self;

    internal bool Configured => !string.IsNullOrWhiteSpace(Password);

    public static bool IsValid(RconOptions value) =>
        value.Host.Length is > 0 and <= 253
        && value.Port is >= 1 and <= 65535
        && value.PollIntervalMilliseconds is >= 200 and <= 60_000
        && value.ConnectTimeoutMilliseconds is >= 250 and <= 30_000
        && value.ResponseTimeoutMilliseconds is >= 500 and <= 30_000
        && value.ReadIdleMilliseconds is >= 25 and <= 1000
        && value.Password.Length <= 256
        && Enum.TryParse<TelemetryShareScope>(value.DefaultShareScope, true, out _);
}
