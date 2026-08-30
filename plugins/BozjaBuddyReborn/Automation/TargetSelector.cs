using System.Collections.Generic;
using System.Numerics;
using BozjaBuddyReborn.Game;
using BozjaBuddyReborn.Multibox;
using BozjaBuddyReborn.Relic;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game.InstanceContent;

namespace BozjaBuddyReborn.Automation;

/// <summary>
/// Chooses what to do next: a Critical Engagement if one is open, otherwise a skirmish FATE.
///
/// THE MULTIBOX CONSTRAINT SHAPES THIS WHOLE CLASS. When several boxes are running, every one
/// of them must independently arrive at the SAME answer, or they scatter. So in deterministic
/// mode the selection deliberately ignores distance - distance differs per character, and a
/// distance-ranked pick is exactly how two boxes end up at two different engagements. Ties
/// break on the engagement id, which every client agrees on.
///
/// That determinism also serves as the fallback when the pipe is down: boxes still converge on
/// the same objective without any coordination at all, because they are running the same rule
/// over the same game state.
/// </summary>
public sealed class TargetSelector(CeCatalog catalog, Configuration config, RegionResolver regions)
{
    private readonly CeCatalog _catalog = catalog;
    private readonly Configuration _config = config;
    private readonly RegionResolver _regions = regions;

    /// <summary>
    /// The material being farmed, or null. Resolved once per selection pass so the region and
    /// activity filters below share one answer.
    /// </summary>
    private DropSource? FarmTarget =>
        _config.FarmMaterialItemId == 0 ? null : ZoneDrops.For(_config.FarmMaterialItemId);

    /// <summary>Why an objective was rejected by the region/activity filter, for the UI.</summary>
    public string? FarmFilterNote { get; private set; }

    /// <summary>
    /// The region work is restricted to, and the activity if the restriction also pins one.
    ///
    /// A farm material implies BOTH (and in Zadnor the activity genuinely matters, since
    /// skirmishes and Critical Engagements in the same plateau drop different items), so it
    /// takes precedence over the manual zone picker. With no material selected, the picker
    /// stands on its own.
    /// </summary>
    public (FieldRegionId Region, DropActivity? Activity) Restriction
    {
        get
        {
            if (FarmTarget is { } target)
                return (target.Region, target.Activity);

            var manual = (FieldRegionId)_config.PreferredRegion;
            return (manual, null);
        }
    }

    /// <summary>
    /// The territory the farm target lives in, or 0 when nothing pins one.
    ///
    /// <see cref="Restriction"/> deliberately drops this, which is fine for the region filter but
    /// wrong for anything that acts on the restriction geographically: a Zadnor Z3 material
    /// resolved against the Bozjan Southern Front produces a real-looking Z3 that is a different
    /// place entirely. Callers that move the character need to know the zone the ordinal belongs
    /// to before they trust it.
    /// </summary>
    public uint RestrictedTerritory => FarmTarget is { } target ? target.Territory : 0u;

    /// <summary>
    /// Is this objective still allowed by the current region/activity/territory restriction?
    ///
    /// Exposed because "stickiness" must not outrank the filter. The controller holds a
    /// committed objective without re-running selection, and that branch used to test only
    /// whether the objective was still LIVE - so changing the farm material, or starting a box
    /// while one was already committed elsewhere, never took effect, and on a multibox host the
    /// out-of-region objective was re-broadcast to the whole group every tick.
    /// </summary>
    public bool StillPermitted(ObjectiveKind kind, uint id, Vector3 position)
    {
        if (kind == ObjectiveKind.None)
            return false;

        var territory = Svc.ClientState.TerritoryType;
        if (RestrictedTerritory != 0 && RestrictedTerritory != territory)
            return false;

        var activity = kind == ObjectiveKind.Fate
            ? DropActivity.Skirmish
            : DropActivity.CriticalEngagement;

        return PassesFarmFilter(kind, id, position, activity);
    }

    /// <summary>A chosen objective plus the reason, for the UI.</summary>
    public readonly record struct Choice(SharedObjective Objective, string Reason)
    {
        public static readonly Choice None = new(SharedObjective.None, "nothing available");
    }

    /// <summary>
    /// Pick the objective to work on.
    /// </summary>
    /// <param name="engagements">Live snapshot from <see cref="CriticalEngagements.Read"/>.</param>
    /// <param name="deterministic">
    /// True when several clients must agree (multibox). Suppresses the distance tiebreak.
    /// </param>
    public Choice Select(IReadOnlyList<CeSnapshot> engagements, bool deterministic)
    {
        FarmFilterNote = null;

        var farm = FarmTarget;
        var territory = Svc.ClientState.TerritoryType;

        // A farm target that lives in the OTHER field zone is the single most useful thing to
        // say out loud - it is the difference between a productive run and hours in the wrong
        // half of Bozja.
        if (farm is { } target && target.Territory != territory)
        {
            FarmFilterNote =
                $"{BozjaZones.Name(target.Territory)} is where that material drops - you are in " +
                $"{BozjaZones.Name(territory)}.";
            return Choice.None;
        }

        // Skirmishes and Critical Engagements in the same Zadnor plateau drop DIFFERENT items,
        // so the activity filter is checked before the objective type is even considered.
        var (_, requiredActivity) = Restriction;
        var wantCe = _config.DoCriticalEngagements && requiredActivity != DropActivity.Skirmish;
        var wantFate = _config.DoFates && requiredActivity != DropActivity.CriticalEngagement;

        if (wantCe)
        {
            var ce = SelectEngagement(engagements, deterministic);
            if (ce.Objective.IsSet)
                return ce;
        }

        if (wantFate)
        {
            var fate = SelectFate(deterministic);
            if (fate.Objective.IsSet)
                return fate;
        }

        var (restrictedRegion, _) = Restriction;
        if (restrictedRegion != FieldRegionId.Unknown && FarmFilterNote == null)
            FarmFilterNote = $"Nothing available in {FieldRegions.Label(territory, restrictedRegion)} right now.";

        return Choice.None;
    }

    /// <summary>
    /// Does an objective satisfy the current region (and activity) restriction? True when
    /// nothing is restricted.
    ///
    /// THIS USED TO RESTRICT NOTHING AT ALL, and that is the whole of "the runner goes to zones
    /// outside the one I selected". The old form asked whether the objective's region had been
    /// LEARNED, and accepted it outright when it had not:
    ///
    ///     var learned = _regions.IsLearned(...);
    ///     if (!learned &amp;&amp; !_config.SkipUnknownRegions)
    ///         return true;
    ///
    /// SkipUnknownRegions ships off and the learned table ships empty, so on any fresh profile
    /// every objective in the zone passed - the restriction was a no-op until every engagement
    /// had been individually visited, and the only way to visit one was for the filter to have
    /// already passed it. Worse, the early return sat ABOVE the Resolve call, so the positional
    /// estimate that three separate comments describe as the pre-learning fallback was never
    /// consulted on the one path it was written for.
    ///
    /// Now the estimate does the job it was built for: resolve first (learned answer if there is
    /// one, position otherwise) and only fall back to a policy decision when even the estimate
    /// abstains. SkipUnknownRegions keeps its documented meaning - what to do when the region is
    /// genuinely unknown - and stops being the difference between filtering and not filtering.
    ///
    /// It also matters for multiboxing: IsLearned reads per-box persisted state, so two boxes
    /// with different learned tables computed different eligibility sets from identical game
    /// state, which is one of the ways a group scatters.
    /// </summary>
    private bool PassesFarmFilter(ObjectiveKind kind, uint id, Vector3 position, DropActivity activity)
    {
        var (requiredRegion, requiredActivity) = Restriction;

        if (requiredRegion == FieldRegionId.Unknown)
            return true;

        if (requiredActivity is { } wanted && wanted != DropActivity.Any && wanted != activity)
            return false;

        var territory = Svc.ClientState.TerritoryType;
        var region = _regions.Resolve(territory, kind, id, position);

        // Neither learned nor confidently placeable. Visiting it is still how it gets learned,
        // so the default is to allow it; SkipUnknownRegions is for users who would rather waste
        // no clears at all.
        if (region == FieldRegionId.Unknown)
            return !_config.SkipUnknownRegions;

        return region == requiredRegion;
    }

    private Choice SelectEngagement(IReadOnlyList<CeSnapshot> engagements, bool deterministic)
    {
        CeSnapshot? best = null;
        var bestRank = int.MaxValue;
        var bestDistance = float.MaxValue;

        foreach (var ce in engagements)
        {
            if (!IsEligible(ce))
                continue;

            var rank = PriorityRank(ce.EventId);
            var distance = ce.HasPosition ? Movement.DistanceToPlayer(ce.Position) : float.MaxValue;

            if (best == null || Better(rank, ce.EventId, distance, bestRank, best.Value.EventId, bestDistance, deterministic))
            {
                best = ce;
                bestRank = rank;
                bestDistance = distance;
            }
        }

        if (best is not { } chosen)
            return Choice.None;

        return new Choice(
            new SharedObjective(ObjectiveKind.CriticalEngagement, chosen.EventId, chosen.Position, Svc.ClientState.TerritoryType),
            $"CE \"{chosen.Name}\" registering, {chosen.SecondsLeft}s left");
    }

    /// <summary>
    /// Ordering: explicit priority first, then - only when we are not required to agree with
    /// other clients - proximity, and finally the engagement id, which is stable everywhere.
    /// </summary>
    private static bool Better(
        int rank, ushort id, float distance,
        int bestRank, ushort bestId, float bestDistance,
        bool deterministic)
    {
        if (rank != bestRank)
            return rank < bestRank;

        if (!deterministic && distance != bestDistance)
            return distance < bestDistance;

        return id < bestId;
    }

    private int PriorityRank(uint eventId)
    {
        var index = _config.PriorityEngagements.IndexOf(eventId);
        return index >= 0 ? index : int.MaxValue - 1;
    }

    private bool IsEligible(CeSnapshot ce)
    {
        // Only a registering engagement can be joined by walking in. One already in Warmup or
        // Battle that we are not part of is closed to us.
        if (ce.State != DynamicEventState.Register)
            return false;

        if (_config.BlockedEngagements.Contains(ce.EventId))
            return false;

        if (ce.IsDuel && !_config.EngageDuels)
            return false;

        if (_catalog.IsLargeScale(ce.EventId) && !_config.EngageLargeScale)
            return false;

        // The game refuses registration under 10 seconds; require enough margin to also travel.
        if (ce.SecondsLeft < (uint)_config.MinRegisterSecondsLeft)
            return false;

        // Without a position we cannot route to it.
        if (!ce.HasPosition)
            return false;

        // Region/activity gate for the current farm target.
        if (!PassesFarmFilter(ObjectiveKind.CriticalEngagement, ce.EventId, ce.Position,
                DropActivity.CriticalEngagement))
            return false;

        return true;
    }

    /// <summary>
    /// Pick a skirmish FATE. In field operations the skirmishes are ordinary FATEs (the
    /// Critical Engagements are the DynamicEvents), so the live FATE table is the right source
    /// and it only ever contains the current zone's events.
    /// </summary>
    private Choice SelectFate(bool deterministic)
    {
        uint bestId = 0;
        var bestName = string.Empty;
        var bestPosition = Vector3.Zero;
        var bestDistance = float.MaxValue;

        try
        {
            foreach (var fate in Svc.Fates)
            {
                if (fate == null)
                    continue;

                // Skip one that is effectively over - joining at 100% earns nothing.
                if (fate.Progress >= 100)
                    continue;

                if (_config.BlockedEngagements.Contains(fate.FateId))
                    continue;

                if (!PassesFarmFilter(ObjectiveKind.Fate, fate.FateId, fate.Position, DropActivity.Skirmish))
                    continue;

                var distance = Movement.DistanceToPlayer(fate.Position);

                var better = bestId == 0
                    || (!deterministic && distance < bestDistance)
                    || (deterministic && fate.FateId < bestId);

                if (!better)
                    continue;

                bestId = fate.FateId;
                bestName = fate.Name.TextValue;
                bestPosition = fate.Position;
                bestDistance = distance;
            }
        }
        catch
        {
            return Choice.None;
        }

        if (bestId == 0)
            return Choice.None;

        return new Choice(
            new SharedObjective(ObjectiveKind.Fate, bestId, bestPosition, Svc.ClientState.TerritoryType),
            $"skirmish \"{bestName}\"");
    }

    /// <summary>Look up a live FATE's current position, which drifts as the FATE progresses.</summary>
    public static Vector3? FatePosition(uint fateId)
    {
        try
        {
            foreach (var fate in Svc.Fates)
                if (fate != null && fate.FateId == fateId)
                    return fate.Position;
        }
        catch { /* table not ready */ }
        return null;
    }

    /// <summary>True when a FATE is still running and worth being at.</summary>
    public static bool FateIsActive(uint fateId)
    {
        try
        {
            foreach (var fate in Svc.Fates)
                if (fate != null && fate.FateId == fateId)
                    return fate.Progress < 100;
        }
        catch { /* table not ready */ }
        return false;
    }

    /// <summary>
    /// A live FATE's ring radius, used as the engage distance - being inside the ring is what
    /// level-syncs the character and makes the mobs count, so it is the right threshold to
    /// switch the rotation on.
    /// </summary>
    public static float FateRadius(uint fateId)
    {
        try
        {
            foreach (var fate in Svc.Fates)
                if (fate != null && fate.FateId == fateId)
                    return fate.Radius;
        }
        catch { /* table not ready */ }
        return 0f;
    }
}
