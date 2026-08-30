using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.ClientState.Conditions;
using ECommons.DalamudServices;

namespace BozjaBuddyReborn.Automation;

/// <summary>One enemy's aggro footprint: a sight cone plus an all-round proximity ring.</summary>
public readonly record struct DangerZone(
    ulong ObjectId,
    string Name,
    byte Level,
    Vector3 Position,
    float Rotation,
    float SightRadius,
    float ProximityRadius,
    float ConeHalfAngleRad)
{
    /// <summary>
    /// The direction the enemy is facing, in world XZ.
    ///
    /// FFXIV stores rotation in radians with forward = (sin, 0, cos) - the convention every
    /// plugin that does facing maths uses.
    /// </summary>
    public Vector3 Forward => new(MathF.Sin(Rotation), 0f, MathF.Cos(Rotation));

    /// <summary>
    /// How far apart in altitude two points must be before they stop being on the same floor.
    ///
    /// Bozja and especially Zadnor are stacked, multi-level terrain, and every distance in this
    /// file is horizontal. Without a vertical gate a mob on the gantry forty yalms below the
    /// route is a full-strength blocker: it consumes the detour budget for that leg, and the
    /// sidestep raised to clear something that was never a threat is a real cost, because a
    /// refused or wasted detour is what puts the character back on the direct line.
    /// </summary>
    public const float VerticalBand = 8f;

    /// <summary>Would standing at this point put us inside the enemy's aggro footprint?</summary>
    public bool Contains(Vector3 point)
    {
        // Different floor: not a threat at any horizontal distance.
        if (MathF.Abs(point.Y - Position.Y) > VerticalBand)
            return false;

        var offset = point - Position;
        offset.Y = 0f;
        var distance = offset.Length();

        // Proximity aggro ignores facing entirely - you can be pulled from directly behind.
        if (distance <= ProximityRadius)
            return true;

        if (distance > SightRadius || distance <= 0.01f)
            return false;

        // Sight aggro only fires inside the enemy's forward cone.
        var cos = Vector3.Dot(offset / distance, Forward);
        return cos >= MathF.Cos(ConeHalfAngleRad);
    }

    /// <summary>How far out this zone reaches in any direction.</summary>
    public float OuterRadius => MathF.Max(SightRadius, ProximityRadius);
}

/// <summary>
/// Keeps the character out of enemy aggro range while travelling between objectives.
///
/// WHY: running FATE to FATE across Bozja and Zadnor drags the route straight through field
/// mobs, and the heavier ones there will delete a character that stops to fight three of them at
/// once. Combat avoidance (BossMod) does not help - by the time a mechanic is telegraphed you are
/// already tagged. The fix has to happen at the pathing layer: do not enter the cone in the
/// first place.
///
/// THE MODEL. FFXIV aggro is two-part and this mirrors it:
///   - a SIGHT cone in front of the enemy (the wide one), and
///   - a smaller PROXIMITY ring that fires from any angle, including from behind.
/// So passing behind an enemy is safe at a distance that would aggro it head-on, which is what
/// makes cone-aware routing worth doing rather than just keeping a flat radius clear.
///
/// FLYING SKIPS ALL OF IT. Ground enemies cannot reach an airborne character, so when actually
/// in flight the whole scan is bypassed - it would only produce pointless detours.
/// </summary>
public sealed class AggroAvoidance(Configuration config)
{
    private readonly Configuration _config = config;

    /// <summary>Enemy object id -> the tick after which it may be routed around again.</summary>
    private readonly Dictionary<ulong, long> _suppressed = [];

    /// <summary>How long a suppressed enemy stays ignored.</summary>
    private const long SuppressMs = 30_000;

    /// <summary>
    /// Stop routing around this enemy for a while.
    ///
    /// Called when a detour raised for it wedged the character. The terrain around that
    /// particular mob will not have changed by the next tick, so re-raising the same detour just
    /// wedges again; walking past it is the better failure.
    /// </summary>
    public void Suppress(ulong objectId)
    {
        var now = Environment.TickCount64;

        // Prune while we are here. Entries were only ever removed by a zone change, so a long
        // session in one zone grew this without bound and every scan paid for the dead keys.
        if (_suppressed.Count > 0)
        {
            List<ulong>? expired = null;
            foreach (var (id, until) in _suppressed)
                if (until <= now)
                    (expired ??= []).Add(id);

            if (expired != null)
                foreach (var id in expired)
                    _suppressed.Remove(id);
        }

        _suppressed[objectId] = now + SuppressMs;
    }

    /// <summary>Forget every suppression, e.g. on a zone change.</summary>
    public void ClearSuppressions() => _suppressed.Clear();

    /// <summary>Enemies currently being ignored because detouring around them wedged us.</summary>
    public int SuppressedCount
    {
        get
        {
            var now = Environment.TickCount64;
            var count = 0;
            foreach (var until in _suppressed.Values)
                if (until > now)
                    count++;
            return count;
        }
    }

    /// <summary>
    /// Why enemies were dropped by the last <see cref="Scan"/>, so "avoidance does nothing" is
    /// answerable without a debugger.
    ///
    /// THE ONE THING THAT CANNOT BE SETTLED FROM SOURCE is whether
    /// <c>StatusFlags.Hostile</c> reads true for an idle, un-aggroed Bozja field mob. If it does
    /// not, the filter below silently empties the scan and the character walks the direct line
    /// through everything with the feature switched on and every setting looking correct. These
    /// counters make that a ten-second reading in the config window instead of a guess: a large
    /// <c>Combatants</c> with a zero <c>Accepted</c> and a large <c>NotHostile</c> is that bug,
    /// and nothing else produces that shape.
    /// </summary>
    public readonly record struct ScanCensus(
        int Combatants,
        int NotHostile,
        int BelowLevel,
        int AlreadyOnUs,
        int Suppressed,
        int OtherFloor,
        int OutOfRange,
        int Accepted);

    /// <summary>Breakdown of the last scan. See <see cref="ScanCensus"/>.</summary>
    public ScanCensus LastCensus { get; private set; }

    /// <summary>Nearby enemies whose aggro is worth routing around, for the UI and for pathing.</summary>
    public List<DangerZone> Scan(float scanRadius = 120f)
    {
        var zones = new List<DangerZone>();

        var me = Svc.Objects.LocalPlayer;
        if (me == null)
            return zones;

        int combatants = 0, notHostile = 0, belowLevel = 0, alreadyOnUs = 0;
        int suppressed = 0, otherFloor = 0, outOfRange = 0;

        var coneHalf = MathF.PI * _config.DangerConeDegrees / 360f; // degrees -> half-angle radians

        try
        {
            foreach (var obj in Svc.Objects)
            {
                if (obj is not IBattleNpc npc)
                    continue;

                // Only actual combatants - pets, chocobos, trust NPCs and weak-spot parts are
                // not threats. ("Combatant" is the game's own name for enemies and guards.)
                if (npc.BattleNpcKind != BattleNpcSubKind.Combatant)
                    continue;

                if (npc.CurrentHp == 0)
                    continue;

                combatants++;

                if (!npc.StatusFlags.HasFlag(StatusFlags.Hostile))
                {
                    notHostile++;
                    continue;
                }

                if (npc.Level < _config.DangerousEnemyMinLevel)
                {
                    belowLevel++;
                    continue;
                }

                // Already on us: routing around something that is chasing achieves nothing, it
                // just follows. Travel keeps going and lets it leash (or RunDefend clears it,
                // when the user has asked to fight back). Avoiding the enemies that have NOT
                // noticed us yet still matters, and is what the rest of this scan is for.
                if (npc.TargetObjectId == me.GameObjectId)
                {
                    alreadyOnUs++;
                    continue;
                }

                // Detouring around this one has already wedged us once; see Suppress.
                if (_suppressed.TryGetValue(npc.GameObjectId, out var until) && until > Environment.TickCount64)
                {
                    suppressed++;
                    continue;
                }

                if (Movement.HorizontalDistance(npc.Position, me.Position) > scanRadius)
                {
                    outOfRange++;
                    continue;
                }

                // Enemies scanned are also enemies COUNTED, in the UI and in every "is the route
                // clear" decision below. A mob several floors down is neither, so it is dropped
                // here rather than being carried through the whole pipeline to be discarded by
                // DangerZone.Contains one point at a time. The band is generous, because a slope
                // is still the same floor.
                if (MathF.Abs(npc.Position.Y - me.Position.Y) > DangerZone.VerticalBand * 2f)
                {
                    otherFloor++;
                    continue;
                }

                zones.Add(new DangerZone(
                    ObjectId: npc.GameObjectId,
                    Name: npc.Name.TextValue,
                    Level: npc.Level,
                    Position: npc.Position,
                    Rotation: npc.Rotation,
                    SightRadius: _config.DangerSightRadius,
                    ProximityRadius: _config.DangerProximityRadius,
                    ConeHalfAngleRad: coneHalf));
            }
        }
        catch
        {
            zones.Clear();
        }

        LastCensus = new ScanCensus(
            combatants, notHostile, belowLevel, alreadyOnUs,
            suppressed, otherFloor, outOfRange, zones.Count);

        return zones;
    }

    /// <summary>True when avoidance should not run at all right now.</summary>
    public bool Disabled =>
        !_config.AvoidDangerousEnemies
        // Airborne: ground enemies cannot touch us, so detouring would be wasted travel.
        || Svc.Condition[ConditionFlag.InFlight];

    /// <summary>
    /// The first enemy footprint the straight route would walk into, or null if the line is
    /// clear.
    /// </summary>
    /// <param name="ignoreWithinOfDestination">
    /// Enemies this close to the destination are ignored - they are almost certainly the
    /// objective's own mobs, and routing around those means never arriving.
    /// </param>
    public DangerZone? FirstBlocking(
        Vector3 from,
        Vector3 to,
        IReadOnlyList<DangerZone> zones,
        float ignoreWithinOfDestination)
    {
        var direction = to - from;
        direction.Y = 0f;
        var length = direction.Length();
        if (length < 0.5f)
            return null;

        direction /= length;

        // Walk the route and find the blocker we would meet first, so the detour is computed
        // against the nearest problem rather than an arbitrary one.
        const float Step = 3f;
        DangerZone? nearest = null;
        var nearestAlong = float.MaxValue;

        foreach (var zone in zones)
        {
            if (Movement.HorizontalDistance(zone.Position, to) <= ignoreWithinOfDestination)
                continue;

            // ALREADY INSIDE IT: routing cannot undo that, only walking can. Sampling starts at
            // `from`, so without this a footprint the character is standing in reports as a
            // blocker on EVERY candidate route including the good ones - which deadlocks the
            // caller into rejecting every sidestep and travelling direct, the exact failure the
            // detour exists to prevent. Leaving is what helps here, and going somewhere is how
            // that happens.
            if (zone.Contains(from))
                continue;

            for (var along = 0f; along <= length; along += Step)
            {
                var point = from + direction * along;
                if (!zone.Contains(point))
                    continue;

                if (along < nearestAlong)
                {
                    nearestAlong = along;
                    nearest = zone;
                }
                break;
            }
        }

        return nearest;
    }

    /// <summary>
    /// A point to route via that clears the blocking enemy, offset perpendicular to the route
    /// on whichever side the enemy is not.
    /// </summary>
    public Vector3 ComputeDetour(Vector3 from, Vector3 to, DangerZone blocking)
    {
        var direction = to - from;
        direction.Y = 0f;
        var length = direction.Length();
        if (length < 0.5f)
            return to;

        direction /= length;

        // Perpendicular in the horizontal plane.
        var perpendicular = new Vector3(-direction.Z, 0f, direction.X);

        var toEnemy = blocking.Position - from;
        toEnemy.Y = 0f;

        // Step out on the opposite side to the enemy.
        var side = Vector3.Dot(perpendicular, toEnemy) > 0f ? -1f : 1f;

        // How far along the route the enemy actually sits.
        var along = Math.Clamp(Vector3.Dot(toEnemy, direction), 0f, length);

        var clearance = blocking.OuterRadius + _config.DangerClearance;

        // THE WAYPOINT MUST BE A SIDESTEP, NOT A CORNER, or the caller will refuse it and the
        // character walks straight through the enemy instead.
        //
        // The old form put the waypoint beside the ENEMY: basePoint (the enemy's projection onto
        // the route) offset laterally by `clearance`. That point is clear, but the LEG TO IT is
        // not - it runs diagonally from here toward the enemy before turning out, and its closest
        // approach is only a*C/sqrt(a^2+C^2) for an enemy a yalms ahead. With the shipped radii
        // (C = 22 + 6 = 28) that clears the 22y sight radius only past a = 35y, and never clears
        // the 10y proximity ring inside 10.7y. Movement.EvaluateAvoidance re-checks exactly that
        // segment before accepting, so the closer and more urgent the blocker, the more certainly
        // the detour was thrown away - which is precisely backwards, and is why avoidance looked
        // switched off in practice.
        //
        // Offsetting from HERE instead makes the first leg a pure lateral step: it opens the gap
        // immediately, is perpendicular to the enemy rather than aimed at it, and stays short.
        // The bulge past the enemy is then just the next detour, raised on arrival from the new
        // position - which is the chaining the class was always designed around.
        //
        // Bias forward slightly so the sidestep does not throw away all forward progress on a
        // long haul, but never past the enemy itself.
        var forward = MathF.Min(along * 0.5f, clearance);
        var basePoint = from + direction * forward;

        var detour = basePoint + perpendicular * side * clearance;
        detour.Y = from.Y;

        return detour;
    }
}
