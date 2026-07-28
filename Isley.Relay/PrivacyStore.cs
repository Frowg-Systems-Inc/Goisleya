using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Isley.Relay;

public sealed record PrivacyProfile(
    string SteamId,
    bool ShareWithSteamFriends,
    IReadOnlyList<string> ExplicitViewerSteamIds,
    DateTimeOffset UpdatedAt);

internal sealed class PrivacyStore
{
    private readonly ConcurrentDictionary<string, PrivacyProfile> _profiles =
        new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly string _stateFile;

    public PrivacyStore(IOptions<RelayOptions> options, ILogger<PrivacyStore> logger)
    {
        var stateDirectory = Path.GetFullPath(options.Value.StatePath);
        _stateFile = Path.Combine(stateDirectory, "privacy.json");
        try
        {
            if (!File.Exists(_stateFile))
            {
                return;
            }
            var values = JsonSerializer.Deserialize<PrivacyProfile[]>(
                File.ReadAllText(_stateFile),
                IsleyJson.Options) ?? [];
            foreach (var value in values.Where(value =>
                         Isley.Telemetry.TelemetryValidation.IsSteamId(value.SteamId)))
            {
                _profiles[value.SteamId] = Normalize(value);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            logger.LogWarning(ex, "Isley privacy state could not be loaded; relay starts fail-closed.");
        }
    }

    internal PrivacyProfile Get(string steamId) =>
        _profiles.TryGetValue(steamId, out var profile)
            ? profile
            : new PrivacyProfile(steamId, false, [], DateTimeOffset.MinValue);

    internal Task<PrivacyProfile> UpdateAsync(
        string steamId,
        bool shareWithSteamFriends,
        CancellationToken cancellationToken) =>
        MutateAsync(steamId, profile => profile with
        {
            ShareWithSteamFriends = shareWithSteamFriends,
            UpdatedAt = DateTimeOffset.UtcNow
        }, cancellationToken);

    internal Task<PrivacyProfile> GrantAsync(
        string steamId,
        string viewerSteamId,
        CancellationToken cancellationToken) =>
        MutateAsync(steamId, profile => profile with
        {
            ExplicitViewerSteamIds = profile.ExplicitViewerSteamIds
                .Append(viewerSteamId)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .Take(128)
                .ToArray(),
            UpdatedAt = DateTimeOffset.UtcNow
        }, cancellationToken);

    internal Task<PrivacyProfile> RevokeAsync(
        string steamId,
        string viewerSteamId,
        CancellationToken cancellationToken) =>
        MutateAsync(steamId, profile => profile with
        {
            ExplicitViewerSteamIds = profile.ExplicitViewerSteamIds
                .Where(value => !string.Equals(value, viewerSteamId, StringComparison.Ordinal))
                .ToArray(),
            UpdatedAt = DateTimeOffset.UtcNow
        }, cancellationToken);

    private async Task<PrivacyProfile> MutateAsync(
        string steamId,
        Func<PrivacyProfile, PrivacyProfile> mutate,
        CancellationToken cancellationToken)
    {
        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            var updated = Normalize(mutate(Get(steamId)));
            _profiles[steamId] = updated;
            await SaveAsync(cancellationToken);
            return updated;
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_stateFile)!;
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $".privacy-{Guid.NewGuid():N}.tmp");
        try
        {
            var content = JsonSerializer.Serialize(
                _profiles.Values.OrderBy(value => value.SteamId),
                IsleyJson.Options);
            await File.WriteAllTextAsync(temporary, content, cancellationToken);
            File.Move(temporary, _stateFile, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private static PrivacyProfile Normalize(PrivacyProfile value) =>
        value with
        {
            ExplicitViewerSteamIds = value.ExplicitViewerSteamIds
                .Where(Isley.Telemetry.TelemetryValidation.IsSteamId)
                .Where(viewer => !string.Equals(viewer, value.SteamId, StringComparison.Ordinal))
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .Take(128)
                .ToArray()
        };
}
