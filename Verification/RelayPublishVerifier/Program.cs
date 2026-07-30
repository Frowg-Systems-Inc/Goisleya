using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Isley.Relay;
using Isley.ServerBridge;
using Isley.Telemetry;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

static void Check(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

const string ServerId = "verify-bridge";
const string Secret = "verifier-relay-secret-0123456789abcdef";

BridgeOptions ConfiguredOptions() => new()
{
    ServerId = ServerId,
    ServerName = "Verify Bridge",
    RelayUrl = "https://relay.example/",
    RelaySecret = Secret
};

TelemetryFrame MakeFrame(long sequence) => new()
{
    ServerId = ServerId,
    ServerName = "Verify Bridge",
    BridgeSessionId = new string('a', 32),
    Sequence = sequence,
    SampledAt = DateTimeOffset.UtcNow,
    Source = "evrima-rcon"
};

// --- 1. Known-answer HMAC frame-signing vector. ------------------------------
// The canonical string and signature below are pinned constants computed
// independently from the contract in Isley.Relay/BridgeAuthentication.cs:
//   canonical = serverId '\n' timestamp '\n' nonce '\n' lowercase-sha256-hex(body)
//   signature = lowercase-hex(HMAC-SHA256(utf8(secret), utf8(canonical)))
{
    var body = Encoding.UTF8.GetBytes(
        "{\"protocolVersion\":1,\"serverId\":\"verify-bridge\","
        + "\"bridgeSessionId\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\",\"sequence\":7}");
    const string timestamp = "1700000000";
    const string nonce = "0123456789abcdef0123456789abcdef";
    var bodyHash = Convert.ToHexString(SHA256.HashData(body)).ToLowerInvariant();
    Check(bodyHash == "32cf89c3217073a223dfbfc1a837effe394f56107850d5a93bb9a441e1d4241c",
        "The pinned body hash drifted; the known-answer vector is no longer meaningful.");
    var canonical = $"{ServerId}\n{timestamp}\n{nonce}\n{bodyHash}";
    var signature = Convert.ToHexString(HMACSHA256.HashData(
        Encoding.UTF8.GetBytes(Secret),
        Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    Check(signature == "08485eb19debcb642b86bda4430433cc3792090b0a4fbdef273be5c157906ab8",
        "The bridge signing contract (known-answer vector) drifted from the relay's "
        + "verification contract.");
}

// --- 2. Live signing interop: the real RelayPublisher signs, the real --------
// BridgeSignatureVerifier (Isley.Relay) accepts; tamper/replay fail. ----------
{
    var handler = new StubHttpMessageHandler();
    handler.Respond(_ => new HttpResponseMessage(HttpStatusCode.Accepted));
    var publisher = new RelayPublisher(
        new HttpClient(handler),
        Options.Create(ConfiguredOptions()));

    var outcome = await publisher.PublishAsync(MakeFrame(7), CancellationToken.None);
    Check(outcome == RelayPublishOutcome.Published,
        "A 202 response must map to the Published outcome.");
    Check(handler.CallCount == 1 && handler.LastMethod == HttpMethod.Post
          && handler.LastRequestUri == "https://relay.example/api/v1/ingest"
          && handler.LastContentType == "application/json",
        "The publish must POST the JSON frame to {RelayUrl}/api/v1/ingest.");
    var body = handler.LastBodyBytes!;
    using (var parsed = JsonDocument.Parse(body))
    {
        Check(parsed.RootElement.GetProperty("serverId").GetString() == ServerId
              && parsed.RootElement.GetProperty("sequence").GetInt64() == 7,
            "The published body must be the web-default JSON serialization of the frame.");
    }

    var serverHeader = handler.LastServerHeader!;
    var timestampHeader = handler.LastTimestampHeader!;
    var nonceHeader = handler.LastNonceHeader!;
    var signatureHeader = handler.LastSignatureHeader!;
    Check(serverHeader == ServerId
          && long.TryParse(timestampHeader, out var signedAt)
          && Math.Abs(DateTimeOffset.UtcNow.ToUnixTimeSeconds() - signedAt) <= 5
          && Regex.IsMatch(nonceHeader, "^[a-f0-9]{32}$")
          && Regex.IsMatch(signatureHeader, "^[a-f0-9]{64}$"),
        "The X-Isley signing headers must match the relay's pinned header shapes.");

    // The signature must recompute from the captured headers and body, proving
    // the publisher signs exactly the canonical string the relay reconstructs.
    var recomputed = Convert.ToHexString(HMACSHA256.HashData(
        Encoding.UTF8.GetBytes(Secret),
        Encoding.UTF8.GetBytes(
            $"{serverHeader}\n{timestampHeader}\n{nonceHeader}\n"
            + Convert.ToHexString(SHA256.HashData(body)).ToLowerInvariant())))
        .ToLowerInvariant();
    Check(recomputed == signatureHeader,
        "The emitted signature must equal HMAC-SHA256 over the relay's canonical string.");

    // Cross-service interop: the real relay verifier must accept this request.
    BridgeSignatureVerifier NewRelayVerifier(BridgeReplayGuard guard) => new(
        Options.Create(new RelayOptions
        {
            Bridges = [new BridgeRegistration { ServerId = ServerId, Secret = Secret }]
        }),
        guard);
    HttpRequest RelayRequest()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Isley-Server"] = serverHeader;
        context.Request.Headers["X-Isley-Timestamp"] = timestampHeader;
        context.Request.Headers["X-Isley-Nonce"] = nonceHeader;
        context.Request.Headers["X-Isley-Signature"] = signatureHeader;
        return context.Request;
    }

    var replayGuard = new BridgeReplayGuard();
    var accepted = NewRelayVerifier(replayGuard).Verify(RelayRequest(), body);
    Check(accepted.Accepted && accepted.ServerId == ServerId,
        "The real relay signature verifier must accept the publisher's signed frame.");

    var replayed = NewRelayVerifier(replayGuard).Verify(RelayRequest(), body);
    Check(!replayed.Accepted && replayed.StatusCode == 409
          && replayed.Error == "replayed_signature",
        "Replaying the same signed frame must be rejected with 409 replayed_signature.");

    var tampered = (byte[])body.Clone();
    tampered[0] = (byte)(tampered[0] == '{' ? '[' : '{');
    var tamperedResult = NewRelayVerifier(new BridgeReplayGuard()).Verify(
        RelayRequest(), tampered);
    Check(!tamperedResult.Accepted && tamperedResult.StatusCode == 401
          && tamperedResult.Error == "invalid_signature",
        "A tampered body must fail signature verification with 401 invalid_signature.");

    var unknownServer = new BridgeSignatureVerifier(
        Options.Create(new RelayOptions
        {
            Bridges = [new BridgeRegistration { ServerId = "other-bridge", Secret = Secret }]
        }),
        new BridgeReplayGuard()).Verify(RelayRequest(), body);
    Check(!unknownServer.Accepted && unknownServer.Error == "unknown_bridge",
        "An unregistered bridge server id must be rejected as unknown_bridge.");

    // Nonces must be unique across publishes (replay protection at the source).
    await publisher.PublishAsync(MakeFrame(8), CancellationToken.None);
    var secondNonce = handler.LastNonceHeader!;
    Check(secondNonce != nonceHeader,
        "Each publish must mint a fresh nonce.");
}

// --- 3. Publish outcome matrix (409 sequencing, errors, guards). -------------
{
    var handler = new StubHttpMessageHandler();
    var publisher = new RelayPublisher(
        new HttpClient(handler),
        Options.Create(ConfiguredOptions()));

    handler.Respond(_ => new HttpResponseMessage(HttpStatusCode.Conflict)
    {
        Content = new StringContent(
            "{\"error\":\"sequence_not_newer\"}", Encoding.UTF8, "application/json")
    });
    Check(await publisher.PublishAsync(MakeFrame(3), CancellationToken.None)
          == RelayPublishOutcome.Superseded,
        "A 409 sequence_not_newer conflict must map to the Superseded outcome, not throw.");

    handler.Respond(_ => new HttpResponseMessage(HttpStatusCode.Conflict)
    {
        Content = new StringContent(
            "{\"error\":\"older_bridge_session\"}", Encoding.UTF8, "application/json")
    });
    var rejection = await AssertThrowsAsync<InvalidOperationException>(
        () => publisher.PublishAsync(MakeFrame(2), CancellationToken.None));
    Check(rejection.Message.Contains("older_bridge_session", StringComparison.Ordinal),
        "A 409 with any other error must surface that error as a rejection.");

    handler.Respond(_ => new HttpResponseMessage(HttpStatusCode.Conflict)
    {
        Content = new StringContent("not-json", Encoding.UTF8, "text/plain")
    });
    var opaqueConflict = await AssertThrowsAsync<InvalidOperationException>(
        () => publisher.PublishAsync(MakeFrame(2), CancellationToken.None));
    Check(opaqueConflict.Message.Contains("conflict", StringComparison.Ordinal),
        "An unparseable 409 body must fall back to the honest 'conflict' error.");

    handler.Respond(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
    await AssertThrowsAsync<HttpRequestException>(
        () => publisher.PublishAsync(MakeFrame(2), CancellationToken.None));

    // The frame-size guard fires before any HTTP attempt.
    var oversized = MakeFrame(1) with
    {
        Entities =
        [
            new TelemetryEntity
            {
                EntityId = "big",
                DisplayName = new string('x', TelemetryProtocol.MaximumFrameBytes)
            }
        ]
    };
    var callsBefore = handler.CallCount;
    await AssertThrowsAsync<InvalidDataException>(
        () => publisher.PublishAsync(oversized, CancellationToken.None));
    Check(handler.CallCount == callsBefore,
        "An oversized frame must be refused before any HTTP request is made.");

    var unconfigured = new RelayPublisher(
        new HttpClient(handler),
        Options.Create(new BridgeOptions { ServerId = ServerId }));
    var guard = await AssertThrowsAsync<InvalidOperationException>(
        () => unconfigured.PublishAsync(MakeFrame(1), CancellationToken.None));
    Check(guard.Message.Contains("not configured", StringComparison.Ordinal),
        "Publishing without relay configuration must throw the honest guard.");
}

// --- 4. Worker: retry coalescing drops stale frames, keeps the newest. -------
{
    var queue = new BridgeFrameQueue();
    var status = new BridgeRuntimeStatus();
    var handler = new StubHttpMessageHandler();
    var firstAttempt = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    handler.RespondAsync(async _ =>
    {
        if (handler.CallCount == 1)
        {
            firstAttempt.TrySetResult();
            await releaseFirst.Task;
            throw new HttpRequestException("relay down");
        }
        return new HttpResponseMessage(HttpStatusCode.Accepted);
    });

    var publisher = new RelayPublisher(
        new HttpClient(handler),
        Options.Create(ConfiguredOptions()));
    using var worker = new RelayPublishWorker(
        queue, publisher, status,
        Options.Create(ConfiguredOptions()),
        NullLogger<RelayPublishWorker>.Instance);
    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

    await queue.EnqueueAsync(MakeFrame(1), cts.Token);
    await worker.StartAsync(cts.Token);
    await firstAttempt.Task.WaitAsync(cts.Token);
    Check(status.LastSuccessfulPublishAt is null,
        "A failed publish must not touch LastSuccessfulPublishAt.");

    // Two fresher frames land while the first attempt is in flight; the retry
    // must coalesce to the newest and provably skip the stale middle frame.
    await queue.EnqueueAsync(MakeFrame(2), cts.Token);
    await queue.EnqueueAsync(MakeFrame(3), cts.Token);
    releaseFirst.TrySetResult();
    await WaitUntilAsync(
        () => status.LastSuccessfulPublishAt is not null, cts.Token);

    Check(handler.CallCount == 2,
        "The retry must publish once (failure then success), not once per stale frame.");
    using var published = JsonDocument.Parse(handler.Bodies[^1]);
    Check(published.RootElement.GetProperty("sequence").GetInt64() == 3,
        "The retry must publish the newest queued frame, dropping the stale ones.");
    Check(handler.Bodies.Count == 2
          && JsonDocument.Parse(handler.Bodies[0]).RootElement
                 .GetProperty("sequence").GetInt64() == 1,
        "Only the failed first attempt and the coalesced newest frame may be sent.");
    using (var snapshot = JsonDocument.Parse(JsonSerializer.Serialize(status.Snapshot())))
    {
        Check(snapshot.RootElement.GetProperty("relay").GetString() == "live"
              && snapshot.RootElement.GetProperty("lastSequence").GetInt64() == 3,
            "A successful publish must mark the relay live at the published sequence.");
    }
    await worker.StopAsync(CancellationToken.None);
}

// --- 5. Worker: persistent failure retries with backoff and never fakes a ----
// publish; Superseded and hard rejections leave LastSuccessfulPublishAt null. -
{
    var queue = new BridgeFrameQueue();
    var status = new BridgeRuntimeStatus();
    var handler = new StubHttpMessageHandler();
    handler.Respond(_ => throw new HttpRequestException("relay down"));
    using var worker = new RelayPublishWorker(
        queue,
        new RelayPublisher(new HttpClient(handler), Options.Create(ConfiguredOptions())),
        status,
        Options.Create(ConfiguredOptions()),
        NullLogger<RelayPublishWorker>.Instance);
    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
    await queue.EnqueueAsync(MakeFrame(1), cts.Token);
    await worker.StartAsync(cts.Token);

    await WaitUntilAsync(() => handler.CallCount >= 3, cts.Token);
    Check(status.LastSuccessfulPublishAt is null,
        "Repeated publish failures must leave LastSuccessfulPublishAt untouched.");
    using (var snapshot = JsonDocument.Parse(JsonSerializer.Serialize(status.Snapshot())))
    {
        Check(snapshot.RootElement.GetProperty("relay").GetString() == "error"
              && snapshot.RootElement.GetProperty("detail").GetString()!
                  .Contains("retrying the newest frame", StringComparison.Ordinal),
            "Persistent failure must surface the honest retrying error state.");
    }
    var attempts = handler.CallCount;
    await Task.Delay(250, cts.Token);
    Check(handler.CallCount - attempts <= 3,
        "Retries must back off (100→200→400 ms), not spin flat.");
    await worker.StopAsync(CancellationToken.None);
}

{
    // Superseded: the relay already holds a newer frame from this session.
    var queue = new BridgeFrameQueue();
    var status = new BridgeRuntimeStatus();
    var handler = new StubHttpMessageHandler();
    handler.Respond(_ => new HttpResponseMessage(HttpStatusCode.Conflict)
    {
        Content = new StringContent(
            "{\"error\":\"sequence_not_newer\"}", Encoding.UTF8, "application/json")
    });
    using var worker = new RelayPublishWorker(
        queue,
        new RelayPublisher(new HttpClient(handler), Options.Create(ConfiguredOptions())),
        status,
        Options.Create(ConfiguredOptions()),
        NullLogger<RelayPublishWorker>.Instance);
    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
    await queue.EnqueueAsync(MakeFrame(1), cts.Token);
    await worker.StartAsync(cts.Token);
    await WaitUntilAsync(() => handler.CallCount >= 1, cts.Token);
    await Task.Delay(300, cts.Token);
    Check(handler.CallCount == 1,
        "A Superseded frame must not be retried.");
    Check(status.LastSuccessfulPublishAt is null,
        "A Superseded frame is not a successful publish and must not set the timestamp.");
    await worker.StopAsync(CancellationToken.None);
}

{
    // Hard rejection (InvalidOperationException): surfaced, not retried.
    var queue = new BridgeFrameQueue();
    var status = new BridgeRuntimeStatus();
    var handler = new StubHttpMessageHandler();
    handler.Respond(_ => new HttpResponseMessage(HttpStatusCode.Conflict)
    {
        Content = new StringContent(
            "{\"error\":\"older_bridge_session\"}", Encoding.UTF8, "application/json")
    });
    using var worker = new RelayPublishWorker(
        queue,
        new RelayPublisher(new HttpClient(handler), Options.Create(ConfiguredOptions())),
        status,
        Options.Create(ConfiguredOptions()),
        NullLogger<RelayPublishWorker>.Instance);
    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
    await queue.EnqueueAsync(MakeFrame(1), cts.Token);
    await worker.StartAsync(cts.Token);
    await WaitUntilAsync(() => handler.CallCount >= 1, cts.Token);
    await Task.Delay(300, cts.Token);
    Check(handler.CallCount == 1,
        "A hard relay rejection must not be retried.");
    using var snapshot = JsonDocument.Parse(JsonSerializer.Serialize(status.Snapshot()));
    Check(snapshot.RootElement.GetProperty("relay").GetString() == "error"
          && snapshot.RootElement.GetProperty("detail").GetString()!
              .Contains("older_bridge_session", StringComparison.Ordinal)
          && status.LastSuccessfulPublishAt is null,
        "A hard rejection must surface the relay's error without faking a publish.");
    await worker.StopAsync(CancellationToken.None);
}

{
    // Unconfigured relay: the worker reports the honest guard, no HTTP at all.
    var queue = new BridgeFrameQueue();
    var status = new BridgeRuntimeStatus();
    var handler = new StubHttpMessageHandler();
    using var worker = new RelayPublishWorker(
        queue,
        new RelayPublisher(new HttpClient(handler), Options.Create(ConfiguredOptions())),
        status,
        Options.Create(new BridgeOptions { ServerId = ServerId }),
        NullLogger<RelayPublishWorker>.Instance);
    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
    await queue.EnqueueAsync(MakeFrame(1), cts.Token);
    await worker.StartAsync(cts.Token);
    await WaitUntilAsync(
        () => JsonSerializer.Serialize(status.Snapshot()).Contains("error"), cts.Token);
    await Task.Delay(200, cts.Token);
    Check(handler.CallCount == 0
          && status.LastSuccessfulPublishAt is null,
        "An unconfigured relay must never attempt an HTTP publish.");
    await worker.StopAsync(CancellationToken.None);
}

Console.WriteLine(
    "Relay publish verification passed: known-answer HMAC signing vector, live "
    + "publisher-to-relay signature interop with tamper/replay rejection, the "
    + "409 sequence_not_newer/conflict outcome matrix, oversized-frame and "
    + "configuration guards, retry coalescing that drops stale frames and keeps "
    + "the newest, and LastSuccessfulPublishAt updating only on real success.");

static async Task<TException> AssertThrowsAsync<TException>(Func<Task> action)
    where TException : Exception
{
    try
    {
        await action();
    }
    catch (TException exception)
    {
        return exception;
    }
    throw new InvalidOperationException(
        $"Expected {typeof(TException).Name} but nothing was thrown.");
}

static async Task WaitUntilAsync(Func<bool> condition, CancellationToken cancellationToken)
{
    while (!condition())
    {
        await Task.Delay(20, cancellationToken);
    }
}

sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private Func<HttpRequestMessage, Task<HttpResponseMessage>> _responder =
        _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));

    internal int CallCount { get; private set; }
    internal HttpMethod? LastMethod { get; private set; }
    internal string? LastRequestUri { get; private set; }
    internal string? LastContentType { get; private set; }
    internal string? LastServerHeader { get; private set; }
    internal string? LastTimestampHeader { get; private set; }
    internal string? LastNonceHeader { get; private set; }
    internal string? LastSignatureHeader { get; private set; }
    internal byte[]? LastBodyBytes { get; private set; }
    internal List<byte[]> Bodies { get; } = [];

    internal void Respond(Func<HttpRequestMessage, HttpResponseMessage> responder) =>
        _responder = request => Task.FromResult(responder(request));

    internal void RespondAsync(Func<HttpRequestMessage, Task<HttpResponseMessage>> responder) =>
        _responder = responder;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        CallCount++;
        LastMethod = request.Method;
        LastRequestUri = request.RequestUri?.AbsoluteUri;
        LastContentType = request.Content?.Headers.ContentType?.MediaType;
        LastServerHeader = request.Headers.TryGetValues("X-Isley-Server", out var serverValues)
            ? serverValues.Single()
            : null;
        LastTimestampHeader = request.Headers.TryGetValues("X-Isley-Timestamp", out var timestampValues)
            ? timestampValues.Single()
            : null;
        LastNonceHeader = request.Headers.TryGetValues("X-Isley-Nonce", out var nonceValues)
            ? nonceValues.Single()
            : null;
        LastSignatureHeader = request.Headers.TryGetValues("X-Isley-Signature", out var signatureValues)
            ? signatureValues.Single()
            : null;
        LastBodyBytes = request.Content is null
            ? null
            : await request.Content.ReadAsByteArrayAsync(cancellationToken);
        if (LastBodyBytes is not null)
        {
            Bodies.Add(LastBodyBytes);
        }
        return await _responder(request);
    }
}
