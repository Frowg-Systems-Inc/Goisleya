using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Isley;

internal enum VoiceServerCheckState
{
    Unchecked,
    Checking,
    Ready,
    Incompatible,
    Unavailable
}

internal readonly record struct VoiceServerReadinessSnapshot(
    int ProtocolVersion,
    int MaxPeersPerRoom,
    int MaxMessageBytes,
    int MaxRooms,
    int MaxTotalPeers,
    int ActiveRooms,
    int ActivePeers,
    DateTimeOffset CheckedAt);

internal readonly record struct VoiceServerCheckPresentation(
    string Label,
    string Detail,
    int Severity,
    bool CanConnect);

internal static class VoiceServerReadinessClient
{
    internal const int ProtocolVersion = 2;
    internal const int MaximumPayloadBytes = 32 * 1024;
    private static readonly HttpClient Client = CreateClient();

    internal static bool TryCreateReadinessUri(string? voiceServerUrl, out Uri readinessUri)
    {
        readinessUri = null!;
        if (!VoiceInviteLogic.TryNormalizeServerUrl(
                voiceServerUrl,
                out var normalizedServer,
                out _)
            || !Uri.TryCreate(normalizedServer, UriKind.Absolute, out var serverUri))
        {
            return false;
        }

        var path = serverUri.AbsolutePath;
        var finalSlash = path.LastIndexOf('/');
        var basePath = finalSlash >= 0 ? path[..(finalSlash + 1)] : "/";
        var builder = new UriBuilder(serverUri)
        {
            Scheme = string.Equals(serverUri.Scheme, "wss", StringComparison.OrdinalIgnoreCase)
                ? Uri.UriSchemeHttps
                : Uri.UriSchemeHttp,
            Path = $"{basePath}ready",
            Query = string.Empty,
            Fragment = string.Empty
        };
        readinessUri = builder.Uri;
        return readinessUri.AbsoluteUri.Length <= VoiceInviteLogic.MaximumServerUrlCharacters;
    }

    internal static async Task<VoiceServerReadinessSnapshot> FetchAsync(
        string voiceServerUrl,
        CancellationToken cancellationToken)
    {
        if (!TryCreateReadinessUri(voiceServerUrl, out var readinessUri))
        {
            throw new InvalidDataException("The voice readiness endpoint is invalid.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, readinessUri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        using var response = await Client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is > MaximumPayloadBytes)
        {
            throw new InvalidDataException("The voice readiness response exceeded the size limit.");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var block = new byte[4096];
        while (true)
        {
            var read = await stream.ReadAsync(block.AsMemory(0, block.Length), cancellationToken);
            if (read == 0)
            {
                break;
            }
            if (buffer.Length + read > MaximumPayloadBytes)
            {
                throw new InvalidDataException("The voice readiness response exceeded the size limit.");
            }
            buffer.Write(block, 0, read);
        }

        var json = Encoding.UTF8.GetString(
            buffer.GetBuffer(),
            0,
            checked((int)buffer.Length));
        return Parse(json, DateTimeOffset.UtcNow);
    }

    internal static VoiceServerReadinessSnapshot Parse(
        string json,
        DateTimeOffset checkedAt)
    {
        if (string.IsNullOrWhiteSpace(json)
            || Encoding.UTF8.GetByteCount(json) > MaximumPayloadBytes)
        {
            throw new InvalidDataException("The voice readiness response was empty or oversized.");
        }

        var value = JsonSerializer.Deserialize<VoiceReadinessPayload>(json)
                    ?? throw new InvalidDataException("The voice readiness response was empty.");
        if (!string.Equals(value.Service, "Isley Voice Signaling", StringComparison.Ordinal)
            || !string.Equals(value.Status, "ready", StringComparison.Ordinal)
            || value.ProtocolVersion != ProtocolVersion
            || value.MediaRelay
            || value.RoomIdsExposed
            || value.PositionDataReceived
            || !value.SignalingPayloadsEncrypted
            || value.DisplayNamesReceived
            || value.WebRtcCandidateDetailsReceived
            || value.MaxPeersPerRoom is < 2 or > 32
            || value.MaxMessageBytes is < 4096 or > 262_144
            || value.MaxRooms is < 1 or > 10_000
            || value.MaxTotalPeers < value.MaxPeersPerRoom
            || value.MaxTotalPeers > 100_000
            || value.ActiveRooms is < 0
            || value.ActiveRooms > value.MaxRooms
            || value.ActivePeers is < 0
            || value.ActivePeers > value.MaxTotalPeers)
        {
            throw new InvalidDataException("The endpoint is not a compatible Isley Voice service.");
        }

        return new VoiceServerReadinessSnapshot(
            value.ProtocolVersion,
            value.MaxPeersPerRoom,
            value.MaxMessageBytes,
            value.MaxRooms,
            value.MaxTotalPeers,
            value.ActiveRooms,
            value.ActivePeers,
            checkedAt);
    }

    internal static VoiceServerCheckPresentation Present(
        VoiceServerCheckState state,
        VoiceServerReadinessSnapshot? snapshot = null)
    {
        return state switch
        {
            VoiceServerCheckState.Checking => new(
                "CHECKING SERVER",
                "Microphone remains off while Isley verifies the signaling service",
                0,
                false),
            VoiceServerCheckState.Ready when snapshot is { } ready => new(
                "ISLEY VOICE V2 READY",
                $"{ready.ActiveRooms} active room{(ready.ActiveRooms == 1 ? string.Empty : "s")} · " +
                $"{ready.ActivePeers} peer{(ready.ActivePeers == 1 ? string.Empty : "s")} · " +
                $"{ready.MaxPeersPerRoom} per room",
                0,
                true),
            VoiceServerCheckState.Incompatible => new(
                "INCOMPATIBLE SERVER",
                "Not an Isley Voice v2 readiness response · microphone kept off",
                2,
                false),
            VoiceServerCheckState.Unavailable => new(
                "SERVER UNAVAILABLE",
                "No valid readiness response · microphone kept off",
                1,
                false),
            _ => new(
                "SERVER NOT CHECKED",
                "Check runs automatically before microphone permission",
                0,
                false)
        };
    }

    private static HttpClient CreateClient()
    {
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = false
        };
        var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(5)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Isley-Voice/1");
        return client;
    }

    private sealed class VoiceReadinessPayload
    {
        [JsonPropertyName("service")]
        public string? Service { get; set; }

        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [JsonPropertyName("protocolVersion")]
        public int ProtocolVersion { get; set; }

        [JsonPropertyName("mediaRelay")]
        public bool MediaRelay { get; set; }

        [JsonPropertyName("roomIdsExposed")]
        public bool RoomIdsExposed { get; set; }

        [JsonPropertyName("positionDataReceived")]
        public bool PositionDataReceived { get; set; }

        [JsonPropertyName("signalingPayloadsEncrypted")]
        public bool SignalingPayloadsEncrypted { get; set; }

        [JsonPropertyName("displayNamesReceived")]
        public bool DisplayNamesReceived { get; set; }

        [JsonPropertyName("webRtcCandidateDetailsReceived")]
        public bool WebRtcCandidateDetailsReceived { get; set; }

        [JsonPropertyName("maxPeersPerRoom")]
        public int MaxPeersPerRoom { get; set; }

        [JsonPropertyName("maxMessageBytes")]
        public int MaxMessageBytes { get; set; }

        [JsonPropertyName("maxRooms")]
        public int MaxRooms { get; set; }

        [JsonPropertyName("maxTotalPeers")]
        public int MaxTotalPeers { get; set; }

        [JsonPropertyName("activeRooms")]
        public int ActiveRooms { get; set; }

        [JsonPropertyName("activePeers")]
        public int ActivePeers { get; set; }
    }
}
