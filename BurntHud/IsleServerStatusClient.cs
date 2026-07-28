using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Isley;

internal sealed record IsleServerStatus(
    bool Online,
    int Players,
    int Capacity,
    string Map,
    string Version,
    string ConnectAddress,
    string DisplayName,
    DateTimeOffset? SourceUpdatedAt,
    DateTimeOffset RetrievedAt)
{
    public double Occupancy => Capacity <= 0 ? 0 : Math.Clamp((double)Players / Capacity, 0, 1);
}

internal static class IsleServerStatusClient
{
    internal const string PublicStatusSourcePage = "https://gamemonitoring.net/the-isle/servers";

    private static readonly HttpClient Client = CreateClient();

    internal static async Task<IsleServerStatus> FetchPublicAsync(
        string connectAddress,
        string fallbackName,
        CancellationToken cancellationToken)
    {
        if (!CommunityServerWatchLogic.TryNormalizeAddress(connectAddress, out var normalizedAddress))
        {
            throw new InvalidDataException("The public server address was invalid.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, BuildStatusEndpoint(normalizedAddress));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        using var response = await Client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        return ParsePublic(json, DateTimeOffset.Now, normalizedAddress, fallbackName);
    }

    internal static string BuildStatusEndpoint(string connectAddress)
    {
        if (!CommunityServerWatchLogic.TryNormalizeAddress(connectAddress, out var normalizedAddress))
        {
            throw new InvalidDataException("The public server address was invalid.");
        }
        return "https://api.gamemonitoring.net/servers?limit=5&game=376210&connect="
               + Uri.EscapeDataString(normalizedAddress);
    }

    internal static IsleServerStatus ParsePublic(
        string json,
        DateTimeOffset retrievedAt,
        string connectAddress,
        string fallbackName)
    {
        if (!CommunityServerWatchLogic.TryNormalizeAddress(connectAddress, out var normalizedAddress))
        {
            throw new InvalidDataException("The public server address was invalid.");
        }
        var envelope = JsonSerializer.Deserialize<ServerListEnvelope>(json)
                       ?? throw new InvalidDataException("The public server response was empty.");
        var server = envelope.Response?.Items?.FirstOrDefault(item =>
            string.Equals(item.Connect, normalizedAddress, StringComparison.OrdinalIgnoreCase)
            && item.Game == 376210)
                     ?? throw new InvalidDataException("The requested Isle server was not in the public response.");

        if (server.NumPlayers < 0 || server.MaxPlayers <= 0 || server.NumPlayers > server.MaxPlayers)
        {
            throw new InvalidDataException("The public server response contained an invalid player count.");
        }

        DateTimeOffset? sourceUpdatedAt = null;
        if (server.LastUpdate > 0)
        {
            try
            {
                sourceUpdatedAt = DateTimeOffset.FromUnixTimeSeconds(server.LastUpdate).ToLocalTime();
            }
            catch (ArgumentOutOfRangeException)
            {
                // The retrieved timestamp remains available when a provider timestamp is malformed.
            }
        }

        return new IsleServerStatus(
            server.Status,
            server.NumPlayers,
            server.MaxPlayers,
            Clean(server.Map, "Unknown map"),
            Clean(server.Version, "Version unavailable"),
            normalizedAddress,
            Clean(server.Name, Clean(fallbackName, "Any Isle server")),
            sourceUpdatedAt,
            retrievedAt);
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(8)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Isley/1.0");
        return client;
    }

    private static string Clean(string? value, string fallback)
    {
        var normalized = string.IsNullOrWhiteSpace(value)
            ? fallback
            : string.Join(" ", value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return normalized.Length <= 100 ? normalized : normalized[..100];
    }

    private sealed class ServerListEnvelope
    {
        [JsonPropertyName("response")]
        public ServerListResponse? Response { get; set; }
    }

    private sealed class ServerListResponse
    {
        [JsonPropertyName("items")]
        public List<ServerItem>? Items { get; set; }
    }

    private sealed class ServerItem
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("status")]
        public bool Status { get; set; }

        [JsonPropertyName("numplayers")]
        public int NumPlayers { get; set; }

        [JsonPropertyName("maxplayers")]
        public int MaxPlayers { get; set; }

        [JsonPropertyName("map")]
        public string? Map { get; set; }

        [JsonPropertyName("version")]
        public string? Version { get; set; }

        [JsonPropertyName("connect")]
        public string? Connect { get; set; }

        [JsonPropertyName("game")]
        public int Game { get; set; }

        [JsonPropertyName("last_update")]
        public long LastUpdate { get; set; }
    }
}
