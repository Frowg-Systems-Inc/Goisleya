using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Isley.Telemetry;

namespace Isley;

internal sealed record IsleyRelayJoin(Uri RelayOrigin, string ServerId)
{
    internal string DisplayText => $"{RelayOrigin.Host} · {ServerId}";
}

internal static partial class IsleyRelayJoinLogic
{
    internal static bool TryParse(string? input, out IsleyRelayJoin join)
    {
        join = null!;
        var candidate = (input ?? string.Empty).Trim();
        if (candidate.Length is < 8 or > 1024)
        {
            return false;
        }

        if (candidate.Contains('|', StringComparison.Ordinal))
        {
            var parts = candidate.Split('|', 2, StringSplitOptions.TrimEntries);
            return TryCreate(parts[0], parts[1], out join);
        }
        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri))
        {
            return false;
        }
        if (string.Equals(uri.Scheme, "isley", StringComparison.OrdinalIgnoreCase))
        {
            var query = ParseQuery(uri.Query);
            return query.TryGetValue("relay", out var relay)
                   && query.TryGetValue("server", out var server)
                   && TryCreate(relay, server, out join);
        }

        var values = ParseQuery(uri.Query);
        var serverId = values.GetValueOrDefault("server");
        if (string.IsNullOrWhiteSpace(serverId))
        {
            var segments = uri.AbsolutePath
                .Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 2
                && string.Equals(segments[0], "join", StringComparison.OrdinalIgnoreCase))
            {
                serverId = segments[1];
            }
        }
        var origin = uri.GetLeftPart(UriPartial.Authority);
        return TryCreate(origin, serverId, out join);
    }

    private static bool TryCreate(string relay, string? serverId, out IsleyRelayJoin join)
    {
        join = null!;
        if (!Uri.TryCreate(relay.TrimEnd('/') + "/", UriKind.Absolute, out var relayUri)
            || !ServerIdRegex().IsMatch(serverId ?? string.Empty)
            || !IsTrustedRelayUri(relayUri))
        {
            return false;
        }
        join = new IsleyRelayJoin(relayUri, serverId!);
        return true;
    }

    private static bool IsTrustedRelayUri(Uri uri)
    {
        if (uri.Scheme == Uri.UriSchemeHttps)
        {
            return string.IsNullOrEmpty(uri.UserInfo);
        }
        return uri.Scheme == Uri.UriSchemeHttp
               && (string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase)
                   || IPAddress.TryParse(uri.Host, out var address)
                   && IPAddress.IsLoopback(address));
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in query.TrimStart('?').Split(
                     '&',
                     StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            result[Uri.UnescapeDataString(parts[0])] =
                parts.Length == 2 ? Uri.UnescapeDataString(parts[1]) : string.Empty;
        }
        return result;
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{1,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex ServerIdRegex();
}

internal sealed record IsleyDeviceAuthorization(
    string DeviceCode,
    string UserCode,
    Uri VerificationUri,
    int ExpiresIn,
    int Interval);

internal sealed record IsleyRelayCredential(string AccessToken, string SteamId);

internal sealed record IsleyRelayPrivacy(
    bool ShareWithSteamFriends,
    IReadOnlyList<string> ExplicitViewerSteamIds);

internal sealed record IsleyRelayConnectionState(
    string State,
    string Detail,
    DateTimeOffset ChangedAt);

internal sealed class IsleyRelayClient : IAsyncDisposable
{
    private static readonly byte[] ViewerHelloPayload = Encoding.UTF8.GetBytes(
        $"{{\"type\":\"hello\",\"maxStreamVersion\":{TelemetryProtocol.ViewerStreamVersion}}}");

    private readonly HttpClient _httpClient = CreateHttpClient();
    private readonly object _streamInfoGate = new();
    private CancellationTokenSource? _streamCancellation;
    private Task? _streamTask;
    private int _activeStreamVersion;
    private bool _deltaEncodingActive;

    internal event EventHandler<ViewerTelemetrySnapshot>? SnapshotReceived;
    internal event EventHandler<IsleyRelayConnectionState>? StateChanged;

    /// <summary>
    /// Viewer stream version the relay negotiated on the active connection:
    /// 0 while no hello answer arrived (legacy version-1 relays), otherwise
    /// the relay's answer clamped to what this build understands.
    /// </summary>
    internal int ActiveStreamVersion
    {
        get { lock (_streamInfoGate) { return _activeStreamVersion; } }
    }

    /// <summary>True only while a v2 stream with delta encoding is active.</summary>
    internal bool DeltaEncodingActive
    {
        get { lock (_streamInfoGate) { return _deltaEncodingActive; } }
    }

    private static HttpClient CreateHttpClient()
    {
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = false
        };
        return new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(10)
        };
    }

    private static Uri ReadTrustedVerificationUri(IsleyRelayJoin join, JsonElement root)
    {
        var raw = root.GetProperty("verificationUri").GetString()
                  ?? throw new InvalidDataException("The relay omitted the verification address.");
        if (!Uri.TryCreate(raw, UriKind.Absolute, out var verificationUri)
            || !string.Equals(
                verificationUri.GetLeftPart(UriPartial.Authority),
                join.RelayOrigin.GetLeftPart(UriPartial.Authority),
                StringComparison.OrdinalIgnoreCase)
            || !verificationUri.AbsolutePath.StartsWith(
                "/auth/steam/",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "The relay returned an untrusted Steam verification address.");
        }
        return verificationUri;
    }

    internal async Task<IsleyDeviceAuthorization> StartDeviceAuthorizationAsync(
        IsleyRelayJoin join,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri(join.RelayOrigin, "api/v1/auth/device"))
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        };
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;
        return new IsleyDeviceAuthorization(
            root.GetProperty("deviceCode").GetString()
            ?? throw new InvalidDataException("The relay omitted the device code."),
            root.GetProperty("userCode").GetString()
            ?? throw new InvalidDataException("The relay omitted the user code."),
            ReadTrustedVerificationUri(join, root),
            root.GetProperty("expiresIn").GetInt32(),
            root.GetProperty("interval").GetInt32());
    }

    internal async Task<IsleyRelayCredential> CompleteDeviceAuthorizationAsync(
        IsleyRelayJoin join,
        IsleyDeviceAuthorization authorization,
        CancellationToken cancellationToken)
    {
        var expiresAt = DateTimeOffset.UtcNow.AddSeconds(authorization.ExpiresIn);
        var interval = TimeSpan.FromSeconds(Math.Clamp(authorization.Interval, 1, 10));
        while (DateTimeOffset.UtcNow < expiresAt)
        {
            await Task.Delay(interval, cancellationToken);
            using var content = new StringContent(
                JsonSerializer.Serialize(new { deviceCode = authorization.DeviceCode }),
                Encoding.UTF8,
                "application/json");
            using var response = await _httpClient.PostAsync(
                new Uri(join.RelayOrigin, "api/v1/auth/device/token"),
                content,
                cancellationToken);
            if ((int)response.StatusCode == 428)
            {
                continue;
            }
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var root = document.RootElement;
            var token = root.GetProperty("accessToken").GetString();
            var steamId = root.GetProperty("steamId").GetString();
            if (string.IsNullOrWhiteSpace(token)
                || !TelemetryValidation.IsSteamId(steamId))
            {
                throw new InvalidDataException("The relay returned an invalid Steam credential.");
            }
            IsleyWindowsCredentialStore.Save(join.RelayOrigin, token);
            return new IsleyRelayCredential(token, steamId!);
        }
        throw new TimeoutException("Steam sign-in expired before it was completed.");
    }

    internal bool TryReadCredential(IsleyRelayJoin join, out string accessToken) =>
        IsleyWindowsCredentialStore.TryRead(join.RelayOrigin, out accessToken);

    internal void ForgetCredential(IsleyRelayJoin join) =>
        IsleyWindowsCredentialStore.Delete(join.RelayOrigin);

    internal async Task<IsleyRelayPrivacy> GetPrivacyAsync(
        IsleyRelayJoin join,
        string accessToken,
        CancellationToken cancellationToken = default)
    {
        using var response = await SendAuthorizedAsync(
            join,
            accessToken,
            HttpMethod.Get,
            "api/v1/me",
            null,
            cancellationToken);
        return await ReadPrivacyAsync(response, nested: true, cancellationToken);
    }

    internal async Task<IsleyRelayPrivacy> UpdatePrivacyAsync(
        IsleyRelayJoin join,
        string accessToken,
        bool shareWithSteamFriends,
        CancellationToken cancellationToken = default)
    {
        using var content = new StringContent(
            JsonSerializer.Serialize(new { shareWithSteamFriends }),
            Encoding.UTF8,
            "application/json");
        using var response = await SendAuthorizedAsync(
            join,
            accessToken,
            HttpMethod.Put,
            "api/v1/privacy",
            content,
            cancellationToken);
        return await ReadPrivacyAsync(response, nested: false, cancellationToken);
    }

    internal async Task<IsleyRelayPrivacy> SetViewerGrantAsync(
        IsleyRelayJoin join,
        string accessToken,
        string viewerSteamId,
        bool allowed,
        CancellationToken cancellationToken = default)
    {
        if (!TelemetryValidation.IsSteamId(viewerSteamId))
        {
            throw new ArgumentException("Enter a valid SteamID64.", nameof(viewerSteamId));
        }
        var method = allowed ? HttpMethod.Put : HttpMethod.Delete;
        using var response = await SendAuthorizedAsync(
            join,
            accessToken,
            method,
            $"api/v1/privacy/grants/{Uri.EscapeDataString(viewerSteamId)}",
            null,
            cancellationToken);
        return await ReadPrivacyAsync(response, nested: false, cancellationToken);
    }

    internal async Task ConnectAsync(
        IsleyRelayJoin join,
        string accessToken,
        bool streamV2OptIn = true,
        CancellationToken cancellationToken = default)
    {
        await StopAsync();
        _streamCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _streamTask = RunStreamAsync(join, accessToken, streamV2OptIn, _streamCancellation.Token);
    }

    internal async Task StopAsync()
    {
        var cancellation = _streamCancellation;
        var task = _streamTask;
        _streamCancellation = null;
        _streamTask = null;
        ResetStreamInfo();
        if (cancellation is null)
        {
            return;
        }
        cancellation.Cancel();
        try
        {
            if (task is not null)
            {
                await task;
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            cancellation.Dispose();
        }
        Report("disconnected", "Isley Relay disconnected.");
    }

    private async Task RunStreamAsync(
        IsleyRelayJoin join,
        string accessToken,
        bool streamV2OptIn,
        CancellationToken cancellationToken)
    {
        var reconnectDelay = TimeSpan.FromMilliseconds(250);
        while (!cancellationToken.IsCancellationRequested)
        {
            using var socket = new ClientWebSocket();
            socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(15);
            socket.Options.SetRequestHeader("Authorization", $"Bearer {accessToken}");
            try
            {
                ResetStreamInfo();
                Report("connecting", $"Connecting to {join.DisplayText}…");
                await socket.ConnectAsync(CreateWebSocketUri(join), cancellationToken);
                if (streamV2OptIn)
                {
                    // Viewer stream negotiation: one small control frame right
                    // after connect. Relays that predate stream v2 ignore it
                    // and keep sending version-1 full snapshots.
                    await socket.SendAsync(
                        ViewerHelloPayload,
                        WebSocketMessageType.Text,
                        true,
                        cancellationToken);
                }
                Report("live", $"Connected to {join.DisplayText}.");
                reconnectDelay = TimeSpan.FromMilliseconds(250);
                await ReceiveLoopAsync(socket, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (WebSocketException ex)
            {
                Report("reconnecting", $"Live connection interrupted: {ex.Message}");
            }
            catch (HttpRequestException ex)
            {
                Report("reconnecting", $"Relay unavailable: {ex.Message}");
            }
            catch (InvalidDataException ex)
            {
                Report("error", ex.Message);
            }

            await Task.Delay(reconnectDelay, cancellationToken);
            reconnectDelay = TimeSpan.FromMilliseconds(
                Math.Min(10_000, reconnectDelay.TotalMilliseconds * 2));
        }
    }

    private async Task ReceiveLoopAsync(
        ClientWebSocket socket,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[16 * 1024];
        var stream = new IsleyRelayStreamSession();
        while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
        {
            using var payload = new MemoryStream();
            WebSocketReceiveResult result;
            do
            {
                result = await socket.ReceiveAsync(buffer, cancellationToken);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    return;
                }
                if (result.MessageType != WebSocketMessageType.Text
                    || payload.Length + result.Count > TelemetryProtocol.MaximumFrameBytes)
                {
                    throw new InvalidDataException("The relay sent an invalid telemetry message.");
                }
                await payload.WriteAsync(buffer.AsMemory(0, result.Count), cancellationToken);
            } while (!result.EndOfMessage);

            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(payload.ToArray());
            }
            catch (JsonException)
            {
                // Malformed JSON can never be applied safely; reconnect so the
                // relay restarts the stream with a keyframe.
                throw new InvalidDataException("The relay sent malformed telemetry JSON.");
            }
            using (document)
            {
                var root = document.RootElement;
                var type = root.TryGetProperty("type", out var typeValue)
                    ? typeValue.GetString()
                    : null;
                if (stream.UpdateRequired)
                {
                    // A stream newer than this build is never parsed; the
                    // connection stays open as an honest waiting state.
                    continue;
                }
                if (type == "status")
                {
                    var state = root.TryGetProperty("state", out var stateValue)
                        ? stateValue.GetString() ?? "waiting"
                        : "waiting";
                    var detail = root.TryGetProperty("detail", out var detailValue)
                        ? detailValue.GetString() ?? "Waiting for live telemetry."
                        : "Waiting for live telemetry.";
                    Report(state, detail);
                    continue;
                }
                if (type == "hello")
                {
                    var verdict = stream.TryNegotiate(root, out var helloDetail);
                    if (verdict == IsleyRelayFrameVerdict.UpdateRequired)
                    {
                        PublishStreamInfo(stream);
                        Report("update-required", helloDetail);
                    }
                    else if (verdict == IsleyRelayFrameVerdict.ResyncRequired)
                    {
                        throw new InvalidDataException(
                            $"The relay stream negotiation failed ({helloDetail}); resynchronizing.");
                    }
                    else
                    {
                        PublishStreamInfo(stream);
                    }
                    continue;
                }
                if (type == "snapshot" && root.TryGetProperty("snapshot", out var snapshotValue))
                {
                    if (TryEnterUpdateRequired(stream, root))
                    {
                        continue;
                    }
                    var verdict = stream.TryApplySnapshot(snapshotValue, out var snapshot, out var detail);
                    if (verdict != IsleyRelayFrameVerdict.Applied || snapshot is null)
                    {
                        throw new InvalidDataException(
                            $"The relay snapshot could not be applied ({detail}).");
                    }
                    SnapshotReceived?.Invoke(this, snapshot);
                    continue;
                }
                if (type == "delta" && root.TryGetProperty("delta", out var deltaValue))
                {
                    if (TryEnterUpdateRequired(stream, root))
                    {
                        continue;
                    }
                    var verdict = stream.TryApplyDelta(deltaValue, out var materialized, out var detail);
                    if (verdict == IsleyRelayFrameVerdict.UpdateRequired)
                    {
                        PublishStreamInfo(stream);
                        Report("update-required", detail);
                        continue;
                    }
                    if (verdict != IsleyRelayFrameVerdict.Applied || materialized is null)
                    {
                        throw new InvalidDataException(
                            $"The relay delta stream lost sync ({detail}); resynchronizing.");
                    }
                    SnapshotReceived?.Invoke(this, materialized);
                    continue;
                }
            }
        }
    }

    private bool TryEnterUpdateRequired(IsleyRelayStreamSession stream, JsonElement envelope)
    {
        if (!IsleyRelayStreamLogic.IsUnsupportedStreamVersion(envelope, out var streamVersion))
        {
            return false;
        }
        stream.MarkUpdateRequired();
        PublishStreamInfo(stream);
        Report(
            "update-required",
            $"Relay viewer stream v{streamVersion} is newer than this Isley build (v{TelemetryProtocol.ViewerStreamVersion}) · update Isley to watch this server");
        return true;
    }

    private void PublishStreamInfo(IsleyRelayStreamSession stream)
    {
        lock (_streamInfoGate)
        {
            _activeStreamVersion = stream.NegotiatedStreamVersion;
            _deltaEncodingActive = stream.DeltaEncodingActive;
        }
    }

    private void ResetStreamInfo()
    {
        lock (_streamInfoGate)
        {
            _activeStreamVersion = 0;
            _deltaEncodingActive = false;
        }
    }

    private void Report(string state, string detail) =>
        StateChanged?.Invoke(this, new IsleyRelayConnectionState(
            state,
            detail,
            DateTimeOffset.UtcNow));

    private async Task<HttpResponseMessage> SendAuthorizedAsync(
        IsleyRelayJoin join,
        string accessToken,
        HttpMethod method,
        string relativePath,
        HttpContent? content,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            method,
            new Uri(join.RelayOrigin, relativePath))
        {
            Content = content
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var response = await _httpClient.SendAsync(request, cancellationToken);
        try
        {
            response.EnsureSuccessStatusCode();
            return response;
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }

    private static async Task<IsleyRelayPrivacy> ReadPrivacyAsync(
        HttpResponseMessage response,
        bool nested,
        CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = nested
            ? document.RootElement.GetProperty("privacy")
            : document.RootElement;
        var grants = root.TryGetProperty("explicitViewerSteamIds", out var values)
                     && values.ValueKind == JsonValueKind.Array
            ? values.EnumerateArray()
                .Select(value => value.GetString())
                .Where(TelemetryValidation.IsSteamId)
                .Select(value => value!)
                .ToArray()
            : [];
        return new IsleyRelayPrivacy(
            root.TryGetProperty("shareWithSteamFriends", out var sharing)
            && sharing.ValueKind == JsonValueKind.True,
            grants);
    }

    private static Uri CreateWebSocketUri(IsleyRelayJoin join)
    {
        var builder = new UriBuilder(
            new Uri(join.RelayOrigin, $"api/v1/live/{Uri.EscapeDataString(join.ServerId)}"))
        {
            Scheme = join.RelayOrigin.Scheme == Uri.UriSchemeHttps ? "wss" : "ws"
        };
        return builder.Uri;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _httpClient.Dispose();
    }
}

internal static class IsleyWindowsCredentialStore
{
    private const int CredentialTypeGeneric = 1;
    private const int CredentialPersistLocalMachine = 2;

    internal static void Save(Uri relayOrigin, string token)
    {
        var target = Target(relayOrigin);
        var bytes = Encoding.UTF8.GetBytes(token);
        var blob = Marshal.AllocCoTaskMem(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, blob, bytes.Length);
            var credential = new NativeCredential
            {
                Type = CredentialTypeGeneric,
                TargetName = target,
                CredentialBlobSize = (uint)bytes.Length,
                CredentialBlob = blob,
                Persist = CredentialPersistLocalMachine,
                UserName = "Isley Steam session"
            };
            if (!CredWrite(ref credential, 0))
            {
                throw new InvalidOperationException(
                    $"Windows could not protect the Isley session ({Marshal.GetLastWin32Error()}).");
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
            Marshal.FreeCoTaskMem(blob);
        }
    }

    internal static bool TryRead(Uri relayOrigin, out string token)
    {
        token = string.Empty;
        if (!CredRead(Target(relayOrigin), CredentialTypeGeneric, 0, out var pointer))
        {
            return false;
        }
        try
        {
            var credential = Marshal.PtrToStructure<NativeCredential>(pointer);
            if (credential.CredentialBlob == IntPtr.Zero
                || credential.CredentialBlobSize is 0 or > 16_384)
            {
                return false;
            }
            var bytes = new byte[credential.CredentialBlobSize];
            Marshal.Copy(credential.CredentialBlob, bytes, 0, bytes.Length);
            try
            {
                token = Encoding.UTF8.GetString(bytes);
                return !string.IsNullOrWhiteSpace(token);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(bytes);
            }
        }
        finally
        {
            CredFree(pointer);
        }
    }

    internal static void Delete(Uri relayOrigin) =>
        CredDelete(Target(relayOrigin), CredentialTypeGeneric, 0);

    private static string Target(Uri relayOrigin)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(relayOrigin.AbsoluteUri));
        return $"Isley/Relay/{Convert.ToHexString(hash.AsSpan(0, 16))}";
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeCredential
    {
        public uint Flags;
        public uint Type;
        public string TargetName;
        public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias;
        public string UserName;
    }

    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredWrite(ref NativeCredential credential, uint flags);

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredRead(
        string target,
        int type,
        int reservedFlag,
        out IntPtr credentialPtr);

    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredDelete(string target, int type, int flags);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern void CredFree(IntPtr credential);
}
