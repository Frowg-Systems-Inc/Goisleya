using System.Globalization;
using System.Text.RegularExpressions;

namespace Isley;

internal readonly record struct VoiceSharedRoute(
    string Text,
    string Kind,
    int StopCount,
    double? PlannedDistance);

internal readonly record struct VoiceRouteOffer(
    string OfferId,
    string PeerId,
    string PeerName,
    VoiceSharedRoute Route,
    DateTimeOffset ReceivedAt);

internal static class VoiceRouteOfferLogic
{
    public const int MaximumRouteCharacters = 1600;
    public const int MaximumStopCount = 12;
    public static readonly TimeSpan OfferLifetime = TimeSpan.FromMinutes(2);

    private static readonly Regex OfferIdPattern = new(
        "^[a-f0-9]{24}$",
        RegexOptions.CultureInvariant);

    private static readonly Regex CoordinatePattern = new(
        @"^(?<x>-?(?:0|[1-9]\d*)(?:\.\d{1,4})?), (?<y>-?(?:0|[1-9]\d*)(?:\.\d{1,4})?)$",
        RegexOptions.CultureInvariant);

    private static readonly Regex DistancePattern = new(
        @"^(?<distance>\d{1,9}(?:\.\d{1,2})?) MU planned$",
        RegexOptions.CultureInvariant);

    private static readonly (string Prefix, string Kind)[] RoutePrefixes =
    [
        ("Isley road/trail course | ", "ROAD / TRAIL"),
        ("Isley breadcrumb return | ", "BREADCRUMB"),
        ("Isley route | ", "ROUTE")
    ];

    public static bool TryParseRoute(
        string? routeText,
        out VoiceSharedRoute route,
        out string error)
    {
        route = default;
        error = "INVALID VOICE ROUTE";
        if (string.IsNullOrEmpty(routeText)
            || routeText.Length > MaximumRouteCharacters
            || routeText.Any(char.IsControl)
            || !string.Equals(routeText, routeText.Trim(), StringComparison.Ordinal))
        {
            return false;
        }

        var prefix = RoutePrefixes.FirstOrDefault(candidate =>
            routeText.StartsWith(candidate.Prefix, StringComparison.Ordinal));
        if (string.IsNullOrEmpty(prefix.Prefix))
        {
            return false;
        }

        var routeBody = routeText[prefix.Prefix.Length..];
        double? plannedDistance = null;
        var suffixIndex = routeBody.LastIndexOf(" | ", StringComparison.Ordinal);
        if (suffixIndex >= 0)
        {
            var suffix = routeBody[(suffixIndex + 3)..];
            var distanceMatch = DistancePattern.Match(suffix);
            if (!distanceMatch.Success
                || !double.TryParse(
                    distanceMatch.Groups["distance"].Value,
                    NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture,
                    out var parsedDistance)
                || !double.IsFinite(parsedDistance)
                || parsedDistance <= 0
                || parsedDistance > 10_000_000)
            {
                return false;
            }

            plannedDistance = parsedDistance;
            routeBody = routeBody[..suffixIndex];
        }

        var stops = routeBody.Split(" > ", StringSplitOptions.None);
        if (stops.Length is < 2 or > MaximumStopCount)
        {
            return false;
        }

        foreach (var stop in stops)
        {
            var coordinateMatch = CoordinatePattern.Match(stop);
            if (!coordinateMatch.Success
                || !double.TryParse(
                    coordinateMatch.Groups["x"].Value,
                    NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture,
                    out var x)
                || !double.TryParse(
                    coordinateMatch.Groups["y"].Value,
                    NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture,
                    out var y)
                || !double.IsFinite(x)
                || !double.IsFinite(y)
                || Math.Abs(x) > 10_000_000
                || Math.Abs(y) > 10_000_000)
            {
                return false;
            }
        }

        route = new VoiceSharedRoute(routeText, prefix.Kind, stops.Length, plannedDistance);
        error = string.Empty;
        return true;
    }

    public static bool TryCreateIncoming(
        string? offerId,
        string? peerId,
        string? peerName,
        string? routeText,
        DateTimeOffset receivedAt,
        out VoiceRouteOffer offer,
        out string error)
    {
        offer = default;
        error = "INVALID VOICE ROUTE OFFER";
        var normalizedOfferId = (offerId ?? string.Empty).Trim().ToLowerInvariant();
        var normalizedPeerId = VoiceIntegrationLogic.NormalizePeerId(peerId);
        if (!OfferIdPattern.IsMatch(normalizedOfferId)
            || string.IsNullOrEmpty(normalizedPeerId)
            || !TryParseRoute(routeText, out var route, out _))
        {
            return false;
        }

        offer = new VoiceRouteOffer(
            normalizedOfferId,
            normalizedPeerId,
            VoiceIntegrationLogic.NormalizeParticipantName(peerName, 0),
            route,
            receivedAt);
        error = string.Empty;
        return true;
    }

    public static bool IsExpired(VoiceRouteOffer offer, DateTimeOffset now) =>
        now < offer.ReceivedAt
        || now - offer.ReceivedAt >= OfferLifetime;

    public static int RemainingSeconds(VoiceRouteOffer offer, DateTimeOffset now)
    {
        if (IsExpired(offer, now)) return 0;
        return Math.Clamp(
            (int)Math.Ceiling((OfferLifetime - (now - offer.ReceivedAt)).TotalSeconds),
            0,
            (int)OfferLifetime.TotalSeconds);
    }

    public static string Summary(VoiceRouteOffer offer, bool streamerMode)
    {
        var sender = streamerMode ? "PLAYER" : offer.PeerName.ToUpperInvariant();
        var distance = offer.Route.PlannedDistance is > 0
            ? $" · {offer.Route.PlannedDistance.Value:0.0} MU"
            : string.Empty;
        return $"{sender} · {offer.Route.Kind} · {offer.Route.StopCount} STOPS{distance}";
    }
}
