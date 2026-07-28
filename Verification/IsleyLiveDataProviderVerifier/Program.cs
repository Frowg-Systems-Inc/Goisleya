using System.Text.Json;
using Isley;

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

var now = new DateTimeOffset(2026, 7, 23, 22, 0, 5, TimeSpan.Zero);
var valid = """
{
  "updatedAt": "2026-07-23T22:00:00Z",
  "self": {"id":"self","label":"You","x":12500,"y":-8400,"z":310,"yaw":45},
  "players": [
    {"id":"friend-1","label":"Packmate","x":12850,"y":-8120,"friend":true},
    {"id":"animal-1","label":"Animal","x":13000,"y":-7900}
  ],
  "vitals": {
    "speciesId":"triceratops","growthPercent":82,
    "healthCurrent":94,"healthMaximum":100,
    "foodCurrent":71,"foodMaximum":100,
    "waterCurrent":66,"waterMaximum":100
  }
}
""";

var snapshot = IsleyLiveDataProvider.Parse(valid, now);
Check(snapshot.Self is { Self: true, X: 12500, Y: -8400 }, "self position");
Check(snapshot.Players.Count == 2
      && snapshot.Players[0].Friend
      && !snapshot.Players[1].Friend,
    "friend and other-animal roles");
Check(snapshot.Vitals is { SpeciesId: "triceratops", GrowthPercent: 82 },
    "bounded vitals");
Check(now - snapshot.UpdatedAt <= IsleyLiveDataProvider.FreshnessLimit,
    "freshness boundary");

using var payload = JsonDocument.Parse(snapshot.ToMapJson());
Check(payload.RootElement.GetProperty("self").GetProperty("yaw").GetDouble() == 45,
    "facing direction bridge");
Check(payload.RootElement.GetProperty("players").GetArrayLength() == 2,
    "map roster bridge");

foreach (var invalid in new[]
{
    """{"self":{"x":0,"y":0}}""",
    """{"updatedAt":"2026-07-23T22:00:00Z","players":[{"x":1000001,"y":0}]}""",
    """{"updatedAt":"2026-07-23T22:00:00Z","vitals":{"growthPercent":101,"healthCurrent":1,"healthMaximum":1,"foodCurrent":1,"foodMaximum":1,"waterCurrent":1,"waterMaximum":1}}"""
})
{
    try
    {
        IsleyLiveDataProvider.Parse(invalid, now);
        throw new InvalidOperationException("invalid provider payload accepted");
    }
    catch (InvalidDataException)
    {
    }
}

Check(IsleyLiveDataProvider.MaximumPlayers == 512
      && IsleyLiveDataProvider.MaximumBytes == 256 * 1024
      && IsleyLiveDataProvider.FreshnessLimit == TimeSpan.FromSeconds(10),
    "provider bounds");

Console.WriteLine(
    "Isley live-data provider verification passed (self direction, friends, animals, vitals, freshness, bounds, and map bridge).");
