using Isley;

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

Check(ServerSessionLogic.Profiles.Length == 3, "profile count");
Check(ServerSessionLogic.Profiles.Select(profile => profile.Id).Distinct().Count() == 3, "unique profile ids");
Check(ServerSessionLogic.HasLiveMapServices(ServerSessionLogic.LiveMapId), "Live Map services");
Check(!ServerSessionLogic.HasLiveMapServices(ServerSessionLogic.OfficialId), "official live boundary");
Check(!ServerSessionLogic.HasLiveMapServices(ServerSessionLogic.CommunityId), "community live boundary");
Check(ServerSessionLogic.Profiles.All(profile => profile.UniversalToolsAvailable),
    "universal tools must be available in every server profile");
Check(ServerSessionLogic.Profiles.All(profile => !profile.RequiresServerName),
    "no server profile may require a server name");
Check(ServerSessionLogic.Profiles.All(profile => !profile.RequiresServerAddress),
    "no server profile may require a host, port, password, or public listing");
Check(ServerSessionLogic.HasUniversalTools(ServerSessionLogic.LiveMapId)
      && ServerSessionLogic.HasUniversalTools(ServerSessionLogic.OfficialId)
      && ServerSessionLogic.HasUniversalTools(ServerSessionLogic.CommunityId),
    "all three selectors must resolve to universal companion tools");
Check(ServerSessionLogic.NormalizeProfileId("invalid") == ServerSessionLogic.LiveMapId, "invalid fallback");
Check(ServerSessionLogic.NextProfileId(ServerSessionLogic.LiveMapId) == ServerSessionLogic.OfficialId,
    "Live Map to official cycle");
Check(ServerSessionLogic.NextProfileId(ServerSessionLogic.OfficialId) == ServerSessionLogic.CommunityId,
    "official to community cycle");
Check(ServerSessionLogic.NextProfileId(ServerSessionLogic.CommunityId) == ServerSessionLogic.LiveMapId,
    "community to Live Map cycle");
Check(ServerSessionLogic.Find(ServerSessionLogic.LiveMapId).SuggestedGrowthMultiplierIndex == -1,
    "Live Map requires the server's advertised growth rate");
var liveMapProfile = ServerSessionLogic.Find(ServerSessionLogic.LiveMapId);
Check(liveMapProfile.DisplayName == "Live Map"
      && liveMapProfile.HeaderLabel == "LIVE MAP"
      && liveMapProfile.SelectorLabel == "LIVE MAP",
    "the integrated profile must be branded as Live Map");
Check(liveMapProfile.Description.Contains("bundled map", StringComparison.OrdinalIgnoreCase)
      && liveMapProfile.CompatibilityStatus.Contains("NO SERVER-OPERATOR DEPENDENCY", StringComparison.Ordinal),
    "Live Map must clearly identify its independent bundled-map boundary");
Check(ServerSessionLogic.LiveMapId == "live-map",
    "the profile id must be independent and descriptive");
Check(ServerSessionLogic.NonAffiliationDisclosure.Contains("never uses the game server", StringComparison.OrdinalIgnoreCase)
      && ServerSessionLogic.NonAffiliationDisclosure.Contains("attributed public Gateway map feed", StringComparison.OrdinalIgnoreCase)
      && ServerSessionLogic.NonAffiliationDisclosure.Contains("no server-operator account", StringComparison.OrdinalIgnoreCase),
    "the independence disclosure must remain explicit");
Check(ServerSessionLogic.Find(ServerSessionLogic.OfficialId).SuggestedGrowthMultiplierIndex == 0,
    "official growth suggestion");
Check(ServerSessionLogic.Find(ServerSessionLogic.CommunityId).SuggestedGrowthMultiplierIndex == -1,
    "community manual growth boundary");
Check(ServerSessionLogic.Find(ServerSessionLogic.OfficialId).ModeLabel == "UNIVERSAL"
      && ServerSessionLogic.Find(ServerSessionLogic.CommunityId).ModeLabel == "UNIVERSAL",
    "non-integrated servers must identify as universal rather than unsupported");
Check(ServerSessionLogic.Find(ServerSessionLogic.CommunityId).SelectorLabel == "ANY SERVER"
      && ServerSessionLogic.Find(ServerSessionLogic.CommunityId).CompatibilityStatus.Contains(
          "PRIVATE / UNLISTED SUPPORTED",
          StringComparison.Ordinal),
    "Any Server selector must explicitly cover private and unlisted sessions");
Check(ServerSessionLogic.NormalizeCustomServerName("  My\u0001   Server  ") == "My Server",
    "custom name normalization");
Check(ServerSessionLogic.NormalizeCustomServerName("Community server") == "Any Isle server",
    "legacy default name migration");
Check(ServerSessionLogic.NormalizeCustomServerName(string.Empty) == "Any Isle server",
    "optional empty server name");
Check(ServerSessionLogic.NormalizeCustomServerName("My | Server") == "My Server",
    "brief separator normalization");
Check(ServerSessionLogic.NormalizeCustomServerName(new string('x', 40)).Length == 28,
    "custom name bound");
Check(ServerSessionLogic.DisplayName(ServerSessionLogic.CommunityId, "Raptor Realm") == "Raptor Realm",
    "custom display name");
Check(ServerSessionLogic.HeaderLabel(ServerSessionLogic.CommunityId, "A Very Long Community Server") .Length <= 14,
    "header label bound");

Console.WriteLine(
    "Server session verification passed (Live Map, Official, and Any Server modes; " +
    "all-server tools; optional name/address; live-service boundaries; growth; naming; and cycling).");
