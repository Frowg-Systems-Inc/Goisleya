using System.Collections.Concurrent;
using System.Text.Json;
using Isley.Telemetry;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace Isley.Relay;

internal sealed record FriendDecision(bool Visible, bool Friend, string Reason);

internal interface IFriendResolver
{
    Task<FriendDecision> EvaluateAsync(
        string targetSteamId,
        string viewerSteamId,
        TelemetryShareScope sourceScope,
        IReadOnlyList<string> sourceGrants,
        CancellationToken cancellationToken);
}

internal sealed class SteamFriendResolver(
    HttpClient httpClient,
    IOptions<SteamOptions> options,
    PrivacyStore privacy,
    ILogger<SteamFriendResolver> logger) : IFriendResolver
{
    private readonly SteamOptions _options = options.Value;
    private readonly ConcurrentDictionary<string, FriendCacheEntry> _cache =
        new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Task<HashSet<string>?>> _inflight =
        new(StringComparer.Ordinal);

    public async Task<FriendDecision> EvaluateAsync(
        string targetSteamId,
        string viewerSteamId,
        TelemetryShareScope sourceScope,
        IReadOnlyList<string> sourceGrants,
        CancellationToken cancellationToken)
    {
        if (string.Equals(targetSteamId, viewerSteamId, StringComparison.Ordinal))
        {
            return new FriendDecision(true, false, "self");
        }
        if (sourceGrants.Contains(viewerSteamId, StringComparer.Ordinal))
        {
            return new FriendDecision(true, true, "bridge-grant");
        }

        var targetPrivacy = privacy.Get(targetSteamId);
        if (targetPrivacy.ExplicitViewerSteamIds.Contains(viewerSteamId, StringComparer.Ordinal))
        {
            return new FriendDecision(true, true, "explicit-grant");
        }
        if (sourceScope == TelemetryShareScope.Server)
        {
            return new FriendDecision(true, false, "server-policy");
        }
        // Bridge Friends only marks entities as friend-eligible. The player's
        // ShareWithSteamFriends opt-in is still required before Steam friend
        // matching can reveal them.
        if (sourceScope != TelemetryShareScope.Friends
            || !targetPrivacy.ShareWithSteamFriends)
        {
            return new FriendDecision(false, false, "not-shared");
        }
        if (string.IsNullOrWhiteSpace(_options.WebApiKey))
        {
            return new FriendDecision(false, false, "steam-friends-unavailable");
        }

        // Steam friendships are symmetric. Resolve the signed-in viewer once,
        // then reuse that cached list for every target in a high-frequency frame.
        var friends = await GetFriendsAsync(viewerSteamId, cancellationToken);
        return friends?.Contains(targetSteamId) == true
            ? new FriendDecision(true, true, "steam-friend")
            : new FriendDecision(false, false, "not-a-visible-steam-friend");
    }

    private Task<HashSet<string>?> GetFriendsAsync(
        string steamId,
        CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue(steamId, out var cached)
            && cached.ExpiresAt > DateTimeOffset.UtcNow)
        {
            return Task.FromResult(cached.Friends);
        }

        // Own the loader lifetime so one cancelled waiter cannot abort a shared fetch.
        return _inflight.GetOrAdd(steamId, _ => LoadFriendsAsync(steamId));
    }

    private async Task<HashSet<string>?> LoadFriendsAsync(string steamId)
    {
        try
        {
            if (_cache.TryGetValue(steamId, out var cached)
                && cached.ExpiresAt > DateTimeOffset.UtcNow)
            {
                return cached.Friends;
            }

            var endpoint = QueryHelpers.AddQueryString(
                "https://api.steampowered.com/ISteamUser/GetFriendList/v1/",
                new Dictionary<string, string?>
                {
                    ["key"] = _options.WebApiKey,
                    ["steamid"] = steamId,
                    ["relationship"] = "friend"
                });
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            using var response = await httpClient.GetAsync(endpoint, timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                _cache[steamId] = new FriendCacheEntry(
                    null,
                    DateTimeOffset.UtcNow.AddSeconds(30));
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var document = await JsonDocument.ParseAsync(
                stream,
                cancellationToken: timeout.Token);
            var friends = new HashSet<string>(StringComparer.Ordinal);
            if (document.RootElement.TryGetProperty("friendslist", out var friendList)
                && friendList.TryGetProperty("friends", out var values)
                && values.ValueKind == JsonValueKind.Array)
            {
                foreach (var value in values.EnumerateArray())
                {
                    if (value.TryGetProperty("steamid", out var id)
                        && TelemetryValidation.IsSteamId(id.GetString()))
                    {
                        friends.Add(id.GetString()!);
                    }
                }
            }
            _cache[steamId] = new FriendCacheEntry(
                friends,
                DateTimeOffset.UtcNow.AddSeconds(_options.FriendCacheSeconds));
            return friends;
        }
        catch (Exception ex) when (
            ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogDebug(ex, "Steam friend lookup failed for an Isley privacy decision.");
            _cache[steamId] = new FriendCacheEntry(
                null,
                DateTimeOffset.UtcNow.AddSeconds(30));
            return null;
        }
        finally
        {
            _inflight.TryRemove(steamId, out _);
        }
    }

    private sealed record FriendCacheEntry(
        HashSet<string>? Friends,
        DateTimeOffset ExpiresAt);
}
