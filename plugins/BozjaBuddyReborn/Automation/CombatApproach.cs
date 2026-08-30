using System;
using System.Collections.Generic;
using System.Numerics;
using BozjaBuddyReborn.External;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using ECommons.DalamudServices;

namespace BozjaBuddyReborn.Automation;

/// <summary>
/// Walks the character into range of whatever it is fighting, and keeps it there.
///
/// WHY THIS HAS TO EXIST. The role split hands positioning to BossMod Reborn, and for mechanics
/// that is right - but Reborn will not close to a target in the configuration this plugin puts it
/// in, and reading its source is the only way to see that. AIBehaviour only adds the
/// follow-the-target goal zone inside `if (_followMaster)`, and `_followMaster` is
/// `master != player`. Solo - which is how a field-operation runner works - the master IS the
/// player, so that whole branch never runs. The fallback below it needs `targeting.Target`, which
/// is only populated when an AI preset is loaded, and this plugin deliberately clears the preset
/// (AI.SetPreset("")) to keep Reborn from pressing buttons alongside RSR. Net effect: Reborn
/// dodges AoEs and does nothing else, so a melee job stands wherever travel left it and RSR, with
/// a target it cannot reach, falls back to the ranged filler - Enpi on Samurai, Shadow Fang on
/// Ninja, and so on, which is exactly what a stalled melee looks like from the outside.
///
/// So closing the gap is ours to do, through vnavmesh, like all our other movement.
///
/// RANGE. Melee actions have a 3y range measured between hitbox edges, so the centre-to-centre
/// distance to aim for is both hitboxes plus a band comfortably inside that. Everything else
/// (physical ranged, casters, healers) works at 25y and only needs to be pulled in when travel or
/// a target swap has left it further out than that.
///
/// DODGING STILL WINS. Reborn steering the character is the one thing that outranks this, exactly
/// as it outranks travel - a dodge overridden by an approach path is a death.
///
/// THE ORIGINAL BOSSMOD DOES THIS ITSELF. Its FollowSlot module (part of the "VBM Multibox" AI
/// preset) has a fallback that adds a goal zone around the player's hostile target whenever no
/// other module has one - 3y for melee and tanks, 25y otherwise - and NormalMovement pathfinds to
/// it around the forbidden zones, which is strictly better than a vnavmesh path that cannot see
/// them. Both forks also refuse to steer at all while a vnavmesh path is running, so an approach
/// path issued alongside the original would not just be redundant, it would switch the dodging
/// off. When the director reports the avoidance plugin owns the approach, this class stands down.
/// </summary>
public sealed class CombatApproach(NavmeshIpc navmesh, Configuration config)
{
    private readonly NavmeshIpc _navmesh = navmesh;
    private readonly Configuration _config = config;

    /// <summary>
    /// Jobs that have to be inside 3y to do anything at all. Ids verified against
    /// ClassJob.csv (NameEnglish): the four melee base classes plus every melee and tank job.
    /// Everything else in the sheet is a 25y job and is handled by the ranged band.
    /// </summary>
    private static readonly HashSet<uint> MeleeJobs =
    [
        1,  // Gladiator
        2,  // Pugilist
        3,  // Marauder
        4,  // Lancer
        19, // Paladin
        20, // Monk
        21, // Warrior
        22, // Dragoon
        29, // Rogue
        30, // Ninja
        32, // Dark Knight
        34, // Samurai
        37, // Gunbreaker
        39, // Reaper
        41, // Viper
        43, // Beastmaster
    ];

    /// <summary>
    /// Where inside melee range to sit. 3y is the hard limit; aiming at 2y leaves room for the
    /// target to shuffle without dropping us out of range on the next server tick.
    /// </summary>
    private const float MeleeBand = 2f;

    /// <summary>
    /// Where to pull a ranged job in to. Well inside the 25y action range, so drifting a couple
    /// of yards does not immediately cost a cast.
    /// </summary>
    private const float RangedBand = 15f;

    /// <summary>Approach paths are re-issued at most this often.</summary>
    private const long RepathIntervalMs = 700;

    /// <summary>How far the target must move before the path to it is worth recomputing.</summary>
    private const float TargetDriftThreshold = 3f;

    /// <summary>
    /// Extra distance tolerated before an approach already in range gives up and re-closes.
    /// See the release test - a zero-width band is what made the approach oscillate.
    /// </summary>
    private const float ReengageMargin = 1.5f;

    private long _lastIssueMs;
    private Vector3 _pathedTo = Vector3.Zero;
    private bool _moving;

    /// <summary>An issued stop vnavmesh has not finished honouring. Same latch as Movement.</summary>
    private bool _stopPending;

    /// <summary>Approach requests vnavmesh refused because it was already computing one.</summary>
    public int RejectedIssues { get; private set; }

    /// <summary>The target being closed on, for the UI.</summary>
    public string? ClosingOn { get; private set; }

    /// <summary>How far short of the wanted range we currently are, for the UI.</summary>
    public float ShortfallYalms { get; private set; }

    /// <summary>
    /// Close on the current target if it is out of range.
    /// </summary>
    /// <param name="avoidanceIsSteering">
    /// True when BossMod Reborn is walking the character out of a mechanic. Approach yields
    /// completely.
    /// </param>
    /// <param name="avoidanceOwnsApproach">
    /// True when the loaded BossMod closes on the target by itself (the original fork). Approach
    /// stands down entirely so as not to hold a vnavmesh path that would make BossMod yield.
    /// </param>
    /// <returns>True when an approach is being driven, so the caller can say so.</returns>
    public bool Tick(bool avoidanceIsSteering, bool avoidanceOwnsApproach = false)
    {
        if (!_config.CloseToTarget)
            return Release();

        if (avoidanceIsSteering || avoidanceOwnsApproach)
            return Release();

        if (!_navmesh.Available || !_navmesh.MeshReady)
            return Release();

        var me = Svc.Objects.LocalPlayer;
        if (me == null)
            return Release();

        // Mounted or airborne we cannot attack anyway, and the controller is already dismounting.
        if (!Mount.IsGrounded)
            return Release();

        if (CurrentEnemy() is not { } enemy)
            return Release();

        var wanted = WantedDistance(me.HitboxRadius, enemy.HitboxRadius);
        var distance = Movement.HorizontalDistance(me.Position, enemy.Position);

        // HYSTERESIS, because this decides between "hold an unthrottled global Path.Stop" and
        // "issue a path", and the two thresholds used to be the same number. A target that
        // strafes across the boundary - which every mob does - therefore had the approach
        // releasing and re-issuing against a 700ms lockout that Release did not reset, so the
        // character lurched forward and halted at roughly 1.4Hz for the whole fight.
        //
        // Release a little inside the wanted range, re-engage a little outside it, and let the
        // band absorb the shuffle.
        if (_moving ? distance <= wanted + ReengageMargin : distance <= wanted)
        {
            // In range. Hand movement back so the character stands and fights rather than
            // creeping the last few inches forever.
            return Release();
        }

        ShortfallYalms = distance - wanted;
        ClosingOn = enemy.Name.TextValue;

        var now = Environment.TickCount64;
        var drifted = Movement.HorizontalDistance(enemy.Position, _pathedTo) > TargetDriftThreshold;

        // "Is MY path running", not "is A path running". vnavmesh's Busy flag is process-global,
        // so a leftover travel path satisfied it and this branch declined to issue at all - the
        // character kept running toward the old objective while the status said it was closing on
        // the mob, which is precisely the stalled-melee failure this class exists to prevent.
        if (!_navmesh.OwnedBy(NavClient.Approach) || drifted)
        {
            if (now - _lastIssueMs < RepathIntervalMs)
                return true;

            // vnavmesh refuses a request that lands on a pending pathfind, so issuing here would
            // be discarded while we recorded it as live. Retry next tick instead.
            if (_navmesh.PathfindInProgress)
                return true;

            // Never fly an approach: an airborne character cannot attack, so a flight path here
            // would close the distance and still leave the rotation with nothing to press.
            if (!_navmesh.MoveCloseTo(enemy.Position, wanted, fly: false, NavClient.Approach))
            {
                RejectedIssues++;
                return true; // nothing committed; the next tick retries
            }

            // A fresh path supersedes any stop we were still chasing.
            _stopPending = false;
            _lastIssueMs = now;
            _pathedTo = enemy.Position;
            _moving = true;
        }

        return true;
    }

    /// <summary>
    /// Give movement back and forget the approach state.
    ///
    /// Latched like Movement.Stop: vnavmesh cannot cancel a pathfind that is already computing,
    /// so one stop can be silently undone when that pathfind lands. The old guard here also made
    /// every release after the first a no-op.
    /// </summary>
    public bool Release()
    {
        if (_moving || _stopPending)
        {
            _stopPending = true;
            _moving = false;
            PumpStop();
        }

        _pathedTo = Vector3.Zero;
        ClosingOn = null;
        ShortfallYalms = 0f;

        // Releasing is a decision to stop, not a failed issue, so the next genuine approach must
        // not inherit this one's lockout. Leaving it armed meant a release followed by a target
        // stepping back out cost up to 700ms of standing still doing nothing.
        _lastIssueMs = 0;
        return false;
    }

    /// <summary>Drive an outstanding stop to completion. Called every frame from the plugin.</summary>
    public void PumpStop()
    {
        if (!_stopPending)
            return;

        // Travel has legitimately taken the path since our stop was issued. Stopping it would
        // strand the run, and the two pumps would spend forever cancelling each other.
        if (_navmesh.Owner == NavClient.Travel)
        {
            _stopPending = false;
            return;
        }

        _navmesh.Stop(NavClient.Approach);

        if (!_navmesh.PathfindInProgress && !_navmesh.PathRunning)
            _stopPending = false;
    }

    /// <summary>
    /// Centre-to-centre distance to aim for. Both hitboxes are subtracted from the game's range
    /// check, so both are added back here - erring closer than the game strictly requires, which
    /// for landing a melee action is never the wrong direction to err.
    /// </summary>
    private float WantedDistance(float myHitbox, float targetHitbox)
    {
        var me = Svc.Objects.LocalPlayer;
        var job = me?.ClassJob.RowId ?? 0;
        var band = MeleeJobs.Contains(job) ? MeleeBand : RangedBand;
        return myHitbox + targetHitbox + band;
    }

    /// <summary>
    /// What we are actually fighting: the hard target, which is what RSR sets while it is in Auto
    /// mode. No target means nothing to close on - picking one ourselves would be this plugin
    /// choosing pulls, which is RSR's job.
    /// </summary>
    private static IBattleNpc? CurrentEnemy()
    {
        try
        {
            if (Svc.Targets.Target is not IBattleNpc npc)
                return null;

            if (npc.CurrentHp == 0)
                return null;

            if (npc.BattleNpcKind != BattleNpcSubKind.Combatant)
                return null;

            return npc;
        }
        catch
        {
            return null;
        }
    }
}
