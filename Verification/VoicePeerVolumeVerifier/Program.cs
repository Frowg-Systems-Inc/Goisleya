using System.Security.Cryptography;
using System.Text;
using Isley;

static void Check(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

static string ExpectedPeerKey(string normalizedLowerName)
{
    var hash = SHA256.HashData(Encoding.UTF8.GetBytes(
        $"isley-voice-peer-volume-v1\n{normalizedLowerName}"));
    return Convert.ToHexString(hash)[..16].ToLowerInvariant();
}

static VoicePeerVolumeEntry Entry(string key, int volume, long updatedAt) =>
    new() { PeerKey = key, VolumePercent = volume, UpdatedAtUnixMs = updatedAt };

var now = new DateTimeOffset(2026, 7, 28, 12, 0, 0, TimeSpan.Zero);

Check(VoicePeerVolumeLogic.MaximumRememberedPeers == 64, "the remembered-peer roster stays bounded");
Check(VoicePeerVolumeLogic.PeerKeyLength == 16, "peer keys stay 16 lowercase hex characters");

Check(VoicePeerVolumeLogic.TryComputePeerKey("Rex  Runner!", out var rexKey)
      && rexKey == ExpectedPeerKey("rex runner"),
    "peer keys pin the isley-voice-peer-volume-v1 hash domain over the normalized name");
Check(VoicePeerVolumeLogic.TryComputePeerKey("REX RUNNER", out var rexUpper)
      && rexUpper == rexKey,
    "peer keys ignore display-name case");
Check(VoicePeerVolumeLogic.TryComputePeerKey("rex   \t runner", out var rexSpaced)
      && rexSpaced == rexKey,
    "peer keys collapse whitespace");
Check(!VoicePeerVolumeLogic.TryComputePeerKey(null, out _)
      && !VoicePeerVolumeLogic.TryComputePeerKey("   ", out _)
      && !VoicePeerVolumeLogic.TryComputePeerKey("!!!", out _),
    "names without any safe characters produce no key");
var longName = new string('a', 40);
Check(VoicePeerVolumeLogic.TryComputePeerKey(longName, out var longKey)
      && longKey == ExpectedPeerKey(new string('a', 32)),
    "names are truncated to 32 characters before hashing");
Check(VoicePeerVolumeLogic.IsValidPeerKey(rexKey), "computed keys validate");
Check(!VoicePeerVolumeLogic.IsValidPeerKey(rexKey.ToUpperInvariant()), "uppercase keys are rejected");
Check(!VoicePeerVolumeLogic.IsValidPeerKey(rexKey[..15]), "short keys are rejected");
Check(!VoicePeerVolumeLogic.IsValidPeerKey(null), "null keys are rejected");

Check(VoicePeerVolumeLogic.NormalizeVolumePercent(-5) == 0, "volume clamps at zero");
Check(VoicePeerVolumeLogic.NormalizeVolumePercent(250) == 100, "volume clamps at one hundred");
Check(VoicePeerVolumeLogic.NormalizeVolumePercent(55) == 55, "in-range volume is preserved");

Check(VoicePeerVolumeLogic.NormalizeEntries(null, now).Count == 0, "null history normalizes to empty");
var keyA = new string('a', 16);
var keyB = new string('b', 16);
var entries = VoicePeerVolumeLogic.NormalizeEntries(new[]
{
    Entry(keyA, 140, now.AddHours(-2).ToUnixTimeMilliseconds()),
    Entry("INVALID", 50, now.ToUnixTimeMilliseconds()),
    Entry(keyA, 30, now.AddHours(-1).ToUnixTimeMilliseconds()),
    Entry(keyB, 80, now.AddYears(-10).ToUnixTimeMilliseconds()),
    Entry(null!, 10, now.ToUnixTimeMilliseconds())
}, now);
Check(entries.Count == 2, "invalid keys, duplicates, and null entries are dropped");
Check(entries[0].PeerKey == keyA && entries[0].VolumePercent == 30,
    "the most recent duplicate wins");
Check(entries[1].PeerKey == keyB && entries[1].UpdatedAtUnixMs == now.ToUnixTimeMilliseconds(),
    "out-of-range timestamps are re-stamped to now");
Check(VoicePeerVolumeLogic.NormalizeEntries(new[] { Entry(keyB, 140, now.ToUnixTimeMilliseconds()) }, now)[0]
        .VolumePercent == 100,
    "out-of-range volumes are clamped during normalization");

Check(VoicePeerVolumeLogic.TryFindVolume(entries, keyA, out var found) && found == 30,
    "remembered volumes are found");
Check(!VoicePeerVolumeLogic.TryFindVolume(entries, new string('c', 16), out var missing)
      && missing == 100,
    "unknown peers fall back to full volume");
Check(!VoicePeerVolumeLogic.TryFindVolume(entries, "nope", out _), "invalid lookup keys fail");

var upserted = VoicePeerVolumeLogic.Upsert(entries, keyB, 45, now.AddMinutes(1));
Check(upserted.Count == 2
      && upserted[0].PeerKey == keyB
      && upserted[0].VolumePercent == 45,
    "upsert moves the touched peer to the front without duplicating it");
var invalidUpsert = VoicePeerVolumeLogic.Upsert(entries, "nope", 45, now.AddMinutes(1));
Check(invalidUpsert.Count == 2 && invalidUpsert.All(entry => entry.PeerKey != "nope"),
    "upserting an invalid key retains the normalized roster only");
var full = Enumerable.Range(0, 64)
    .Select(index => Entry(index.ToString("x2").PadLeft(16, '0'), 50, index))
    .ToList();
var pruned = VoicePeerVolumeLogic.Upsert(full, keyA, 90, now);
Check(pruned.Count == 64 && pruned[0].PeerKey == keyA,
    "a full roster prunes the least-recently-used peer");

Console.WriteLine(
    "Voice peer volume verification passed (pinned key-derivation domain, normalization, duplicate and timestamp handling, LRU pruning, and lookup honesty).");
