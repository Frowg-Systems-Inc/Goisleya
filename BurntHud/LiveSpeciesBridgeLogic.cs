namespace Isley;

internal enum LiveSpeciesBridgeState
{
    Unavailable,
    ReadyToStart,
    Drifted,
    Matched
}

internal readonly record struct LiveSpeciesBridgeSnapshot(
    bool LiveFresh,
    bool LifeRunActive,
    int SavedSpeciesIndex,
    string SourceSpeciesId);

internal readonly record struct LiveSpeciesBridgeView(
    LiveSpeciesBridgeState State,
    bool Available,
    bool CanAdopt,
    int SavedSpeciesIndex,
    int LiveSpeciesIndex,
    int EffectiveSpeciesIndex,
    string LiveSpeciesId,
    string LiveSpeciesName,
    string StateLabel,
    string ActionLabel,
    string Detail);

internal static class LiveSpeciesBridgeLogic
{
    internal static LiveSpeciesBridgeView Analyze(LiveSpeciesBridgeSnapshot snapshot)
    {
        var savedIndex = DietCoachLogic.NormalizeSpeciesIndex(snapshot.SavedSpeciesIndex);
        var liveIndex = SpeciesIndex(snapshot.SourceSpeciesId);
        if (!snapshot.LiveFresh || liveIndex == 0)
        {
            return new LiveSpeciesBridgeView(
                LiveSpeciesBridgeState.Unavailable,
                false,
                false,
                savedIndex,
                liveIndex,
                savedIndex,
                string.Empty,
                string.Empty,
                "MANUAL SPECIES",
                "LIVE WAITING",
                "A fresh recognized Live Map current dinosaur is required.");
        }

        var species = DietCoachLogic.Species[liveIndex - 1];
        var state = !snapshot.LifeRunActive
            ? LiveSpeciesBridgeState.ReadyToStart
            : savedIndex == liveIndex
                ? LiveSpeciesBridgeState.Matched
                : LiveSpeciesBridgeState.Drifted;

        return new LiveSpeciesBridgeView(
            state,
            true,
            state is LiveSpeciesBridgeState.ReadyToStart or LiveSpeciesBridgeState.Drifted,
            savedIndex,
            liveIndex,
            liveIndex,
            species.Id,
            species.Name,
            state switch
            {
                LiveSpeciesBridgeState.ReadyToStart => "LIVE SPECIES · NEW RUN",
                LiveSpeciesBridgeState.Matched => "LIVE SPECIES · RUN MATCHED",
                _ => $"LIVE {species.Name.ToUpperInvariant()} · RUN DIFFERS"
            },
            state == LiveSpeciesBridgeState.Matched
                ? "LIVE MATCHED"
                : $"USE {species.Name.ToUpperInvariant()}",
            state switch
            {
                LiveSpeciesBridgeState.ReadyToStart =>
                    "Starting from the fresh snapshot can save this species to the new Life Run.",
                LiveSpeciesBridgeState.Matched =>
                    "The saved Life Run species matches the fresh current dinosaur.",
                _ =>
                    "Live species guides current advice; use it only when this is the same Life Run."
            });
    }

    internal static int SpeciesIndex(string? sourceSpeciesId)
    {
        var normalized = NormalizeIdentifier(sourceSpeciesId);
        if (string.IsNullOrEmpty(normalized)) return 0;
        var index = Array.FindIndex(
            DietCoachLogic.Species,
            species => string.Equals(species.Id, normalized, StringComparison.OrdinalIgnoreCase));
        return index < 0 ? 0 : index + 1;
    }

    internal static string DisplayName(string? sourceSpeciesId)
    {
        var index = SpeciesIndex(sourceSpeciesId);
        return index == 0 ? string.Empty : DietCoachLogic.Species[index - 1].Name;
    }

    private static string NormalizeIdentifier(string? value)
    {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        return normalized.Length is > 0 and <= 32
               && normalized.All(character => character is >= 'a' and <= 'z')
            ? normalized
            : string.Empty;
    }
}
