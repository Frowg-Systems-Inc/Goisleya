using System.Text.RegularExpressions;

namespace Isley;

internal readonly record struct VoiceRelayConfig(string Url, string Username, string Credential);

internal static partial class VoiceRelayLogic
{
    internal const int MaxUrlLength = 180;
    internal const int MaxUsernameLength = 64;
    internal const int MaxCredentialLength = 128;

    [GeneratedRegex(@"^(?<scheme>turns?):(?<host>\[[0-9a-fA-F:]+\]|[a-zA-Z0-9](?:[a-zA-Z0-9.-]*[a-zA-Z0-9])?)(?::(?<port>[0-9]{1,5}))?(?:\?transport=(?<transport>udp|tcp))?$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex TurnUrlPattern();

    internal static bool TryCreate(
        string? url,
        string? username,
        string? credential,
        out VoiceRelayConfig config,
        out string error)
    {
        config = default;
        error = string.Empty;
        var candidateUrl = url?.Trim() ?? string.Empty;
        if (candidateUrl.Length is < 8 or > MaxUrlLength || ContainsControl(candidateUrl))
        {
            error = "TURN URL IS INVALID";
            return false;
        }

        var match = TurnUrlPattern().Match(candidateUrl);
        if (!match.Success)
        {
            error = "USE TURN: OR TURNS: HOST[:PORT]";
            return false;
        }

        if (match.Groups["port"].Success
            && (!int.TryParse(match.Groups["port"].Value, out var port) || port is < 1 or > 65535))
        {
            error = "TURN PORT IS INVALID";
            return false;
        }

        var candidateUsername = username?.Trim() ?? string.Empty;
        if (candidateUsername.Length is < 1 or > MaxUsernameLength || ContainsControl(candidateUsername))
        {
            error = "TURN USERNAME IS INVALID";
            return false;
        }

        var candidateCredential = credential ?? string.Empty;
        if (candidateCredential.Length is < 1 or > MaxCredentialLength || ContainsControl(candidateCredential))
        {
            error = "TURN CREDENTIAL IS INVALID";
            return false;
        }

        var normalizedUrl = match.Groups["scheme"].Value.ToLowerInvariant()
                            + ":" + match.Groups["host"].Value.ToLowerInvariant()
                            + (match.Groups["port"].Success ? ":" + match.Groups["port"].Value : string.Empty)
                            + (match.Groups["transport"].Success
                                ? "?transport=" + match.Groups["transport"].Value.ToLowerInvariant()
                                : string.Empty);
        config = new VoiceRelayConfig(normalizedUrl, candidateUsername, candidateCredential);
        return true;
    }

    private static bool ContainsControl(string value) => value.Any(char.IsControl);
}
