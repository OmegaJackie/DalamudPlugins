using System.Globalization;
using System.Numerics;
using BozjaBuddyReborn.Game;
using BozjaBuddyReborn.Multibox;
using ECommons.DalamudServices;

namespace BozjaBuddyReborn.Automation;

/// <summary>
/// Answers "which third of the zone is that objective in?".
///
/// There is no shipped table mapping each Critical Engagement to its region, so this learns:
/// while the character is standing at an objective, the game's own TerritoryInfo says which
/// region that is, and the answer is recorded permanently. Until then a positional estimate
/// from the region label anchors keeps the first run useful.
///
/// The learned answer always wins over the estimate, so a bad estimate near a boundary is
/// self-correcting the first time that objective is actually visited.
/// </summary>
public sealed class RegionResolver(Configuration config)
{
    private readonly Configuration _config = config;

    private static string Key(uint territory, ObjectiveKind kind, uint id)
    {
        var k = kind == ObjectiveKind.Fate ? 'f' : 'c';
        return string.Create(CultureInfo.InvariantCulture, $"{territory}:{k}:{id}");
    }

    /// <summary>The region an objective sits in, learned if known, estimated otherwise.</summary>
    public FieldRegionId Resolve(uint territory, ObjectiveKind kind, uint id, Vector3 position)
    {
        if (_config.LearnedRegions.TryGetValue(Key(territory, kind, id), out var learned))
        {
            var region = (FieldRegionId)learned;
            if (region != FieldRegionId.Unknown)
                return region;
        }

        return FieldRegions.ClassifyByPosition(territory, position);
    }

    /// <summary>True when this objective's region has been observed rather than estimated.</summary>
    public bool IsLearned(uint territory, ObjectiveKind kind, uint id)
        => _config.LearnedRegions.ContainsKey(Key(territory, kind, id));

    /// <summary>
    /// Record the region an objective is in, from the game's live reading. Called when the
    /// character is physically at the objective, which is the only moment TerritoryInfo can be
    /// trusted to describe it rather than wherever we happen to be standing.
    /// </summary>
    /// <returns>True when this taught us something new.</returns>
    public bool Learn(uint territory, ObjectiveKind kind, uint id, FieldRegionId region)
    {
        if (region == FieldRegionId.Unknown || !BozjaZones.IsFieldZone(territory))
            return false;

        var key = Key(territory, kind, id);

        // FIRST SIGHTING WINS, and re-sightings are free.
        //
        // The old form only skipped when the value MATCHED, so a disagreeing reading overwrote
        // and saved. Callers run this every 200ms tick while standing at an objective, and
        // TerritoryInfo flips as the character moves across a region boundary - which a Critical
        // Engagement arena can straddle - so a fight near an edge wrote the whole configuration
        // to disk several times a second, synchronously, on the framework thread. That is a
        // visible hitch on its own; with several boxes of one install contending for the same
        // config it is a much worse one, since ConfigSaver blocks and retries.
        //
        // Refusing the overwrite also stops the value oscillating between two answers, and the
        // caller's proximity guard is what keeps the one value we do keep honest.
        if (_config.LearnedRegions.ContainsKey(key))
            return false;

        _config.LearnedRegions[key] = (byte)region;
        ConfigSaver.Save(_config);
        return true;
    }

    /// <summary>Drop everything learned, e.g. if a patch moves engagements around.</summary>
    public void Forget()
    {
        _config.LearnedRegions.Clear();
        ConfigSaver.Save(_config);
    }

    public int LearnedCount => _config.LearnedRegions.Count;
}
