using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Isley.Telemetry;
using Microsoft.Extensions.Options;

namespace Isley.ServerBridge;

internal enum RelayPublishOutcome
{
    Published,
    Superseded
}

internal sealed class RelayPublisher(
    HttpClient httpClient,
    IOptions<BridgeOptions> options)
{
    private readonly BridgeOptions _options = options.Value;

    internal async Task<RelayPublishOutcome> PublishAsync(
        TelemetryFrame frame,
        CancellationToken cancellationToken)
    {
        if (!_options.RelayConfigured)
        {
            throw new InvalidOperationException("The Isley relay is not configured.");
        }

        var body = JsonSerializer.SerializeToUtf8Bytes(
            frame,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        if (body.Length > TelemetryProtocol.MaximumFrameBytes)
        {
            throw new InvalidDataException("The telemetry frame is too large for the relay.");
        }

        var endpoint = new Uri(new Uri(_options.RelayUrl.TrimEnd('/') + "/"), "api/v1/ingest");
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new ByteArrayContent(body)
        };
        request.Content.Headers.ContentType =
            new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        Sign(request, body);
        using var response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            var error = await ReadConflictErrorAsync(response, cancellationToken);
            if (string.Equals(error, "sequence_not_newer", StringComparison.Ordinal))
            {
                // The relay already has a newer frame from this bridge session.
                return RelayPublishOutcome.Superseded;
            }

            throw new InvalidOperationException(
                $"Relay rejected the frame ({error}).");
        }
        response.EnsureSuccessStatusCode();
        return RelayPublishOutcome.Published;
    }

    private static async Task<string> ReadConflictErrorAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(
                stream,
                cancellationToken: cancellationToken);
            if (document.RootElement.TryGetProperty("error", out var error)
                && error.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(error.GetString()))
            {
                return error.GetString()!;
            }
        }
        catch (JsonException)
        {
        }
        catch (InvalidOperationException)
        {
        }

        return "conflict";
    }

    private void Sign(HttpRequestMessage request, ReadOnlySpan<byte> body)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            .ToString(CultureInfo.InvariantCulture);
        var nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        var bodyHash = Convert.ToHexString(SHA256.HashData(body)).ToLowerInvariant();
        var canonical = $"{_options.ServerId}\n{timestamp}\n{nonce}\n{bodyHash}";
        var signature = HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(_options.RelaySecret),
            Encoding.UTF8.GetBytes(canonical));

        request.Headers.Add("X-Isley-Server", _options.ServerId);
        request.Headers.Add("X-Isley-Timestamp", timestamp);
        request.Headers.Add("X-Isley-Nonce", nonce);
        request.Headers.Add(
            "X-Isley-Signature",
            Convert.ToHexString(signature).ToLowerInvariant());
    }
}

internal sealed class RelayPublishWorker(
    BridgeFrameQueue queue,
    RelayPublisher publisher,
    BridgeRuntimeStatus status,
    IOptions<BridgeOptions> options,
    ILogger<RelayPublishWorker> logger)
    : BackgroundService
{
    private readonly BridgeOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var frame in queue.ReadAllAsync(stoppingToken))
        {
            if (!_options.RelayConfigured)
            {
                status.RelayError("Relay configuration is incomplete.");
                continue;
            }

            var candidate = queue.TakeNewest(frame);
            var delay = TimeSpan.FromMilliseconds(100);
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var outcome = await publisher.PublishAsync(candidate, stoppingToken);
                    if (outcome == RelayPublishOutcome.Published)
                    {
                        status.Published(candidate);
                    }
                    break;
                }
                catch (InvalidOperationException ex)
                {
                    status.RelayError(ex.Message);
                    logger.LogWarning(
                        "Isley relay rejected frame {Sequence}: {Message}",
                        candidate.Sequence,
                        ex.Message);
                    break;
                }
                catch (Exception ex) when (
                    ex is HttpRequestException
                    or TaskCanceledException
                    or InvalidDataException)
                {
                    status.RelayError("Relay unavailable; retrying the newest frame.");
                    logger.LogWarning(
                        "Isley relay publish failed for frame {Sequence}: {Message}",
                        candidate.Sequence,
                        ex.Message);
                    if (queue.TryDequeueNewest(out var newer))
                    {
                        candidate = newer;
                        delay = TimeSpan.FromMilliseconds(100);
                        continue;
                    }

                    await Task.Delay(delay, stoppingToken);
                    delay = TimeSpan.FromMilliseconds(Math.Min(5000, delay.TotalMilliseconds * 2));
                }
            }
        }
    }
}
