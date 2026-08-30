using System.Numerics;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin;
using ECommons.DalamudServices;

namespace BozjaBuddyReborn.External;

/// <summary>
/// Drives the two combat plugins in their separate, non-overlapping roles.
///
///     BossMod (Reborn or original) -> AoE avoidance and positioning ONLY
///     RSR                          -> the rotation
///
/// This split is the reason both can run at once. The classic failure when people enable
/// BossMod and RSR together is that BossMod's autorotation and RSR both queue actions and
/// stall each other; <see cref="BossModAvoidance.ApplyAvoidanceOnlyConfig"/> removes BossMod
/// from the action-queue business entirely, leaving it as a pure dodge engine. How that is done
/// differs per fork and lives in <see cref="BossModAvoidance"/>; this class is fork-agnostic
/// except where the fork's BEHAVIOUR differs (see <see cref="AvoidanceOwnsApproach"/>).
///
/// Everything here is edge-triggered inside the two wrappers, so the controller can call
/// Engage()/Disengage() every tick for free.
/// </summary>
public sealed class CombatDirector(IDalamudPluginInterface pi, Configuration config)
{
    private readonly Configuration _config = config;

    /// <summary>
    /// Whether the last call was Engage. The controller calls Engage every tick while fighting,
    /// so this is what turns "re-apply before every fight" into a genuine per-fight edge instead
    /// of five chat commands a second.
    /// </summary>
    private bool _engaged;

    /// <summary>What the controller last asked for avoidance, so the fork-behaviour queries below can honour it.</summary>
    private bool _useAvoidance;

    public BossModAvoidance Avoidance { get; } = new(pi);
    public RotationSolverIpc Rotation { get; } = new(pi);

    /// <summary>True when the avoidance half is installed and answering.</summary>
    public bool AvoidanceAvailable => Avoidance.Available;

    /// <summary>True when the rotation half is installed and answering.</summary>
    public bool RotationAvailable => Rotation.Available;

    /// <summary>
    /// Full combat: avoidance on, rotation on.
    /// </summary>
    /// <param name="useAvoidance">
    /// User setting. When false, BossMod is left alone entirely and RSR runs solo.
    /// </param>
    public void Engage(bool useAvoidance)
    {
        var startingFight = !_engaged;
        _engaged = true;
        _useAvoidance = useAvoidance;

        SyncReassertInterval();
        Avoidance.SetEnabled(useAvoidance);

        // SetEnabled only applies the avoidance-only config on its own on-edge. If the user has
        // asked for it, re-assert at the start of each fight so a mid-session change to BossMod's
        // settings cannot leave its autorotation live alongside RSR.
        if (useAvoidance && startingFight && _config.ReapplyAvoidanceConfigEachFight)
            Avoidance.ApplyAvoidanceOnlyConfig(force: true);

        // A fight starting is the one moment worth spending a guaranteed re-assert on: whatever
        // dropped the state while we were travelling, this is where it costs damage.
        if (startingFight)
            Rotation.Resync();

        // The rotation half is opt-out. This was previously read nowhere, so unticking it in the
        // settings did nothing at all - RSR was armed regardless.
        if (_config.UseRotationSolver)
            Rotation.Engage();
        else
            Rotation.Disengage();
    }

    /// <summary>
    /// Travelling: no rotation, but avoidance may stay on so the character dodges anything
    /// it walks through on the way to an engagement.
    /// </summary>
    public void Travel(bool useAvoidance)
    {
        _engaged = false;
        _useAvoidance = useAvoidance;
        SyncReassertInterval();
        Avoidance.SetEnabled(useAvoidance);
        Rotation.Disengage();

        // THE ORIGINAL FORK WALKS TO ITS TARGET. Its FollowSlot module adds a goal zone around the
        // player's hostile hard target whenever no other module has one, and NormalMovement then
        // pathfinds to it - in or out of combat, dead or alive, it does not check. While we are
        // pathing that is moot (BossMod yields to a running vnavmesh path), but at every hold - the
        // registration wait inside a CE, the idle staging point, dismounting - a target left over
        // from the last fight would have the character walk off to stand next to it, and if that
        // mob is alive, pull it with the rotation off. Travel means "not fighting", so the hard
        // target goes with it. Reborn does not close solo, so this is not needed there, and RSR
        // in Auto picks its own targets the moment we Engage again.
        if (useAvoidance && Avoidance.ClosesToTargetItself && Avoidance.Available)
            ClearHostileHardTarget();
    }

    /// <summary>
    /// Push the user's heartbeat setting into both wrappers. Done per call rather than once at
    /// construction so changing it in the settings takes effect immediately.
    /// </summary>
    private void SyncReassertInterval()
    {
        var ms = _config.CombatStateReassertSeconds <= 0
            ? 0L
            : (long)(_config.CombatStateReassertSeconds * 1000f);

        Avoidance.ReassertIntervalMs = ms;
        Rotation.ReassertIntervalMs = ms;
    }

    /// <summary>Everything off. Idempotent.</summary>
    public void Disengage()
    {
        _engaged = false;
        _useAvoidance = false;
        Rotation.Disengage();
        Avoidance.SetEnabled(false);
    }

    /// <summary>
    /// True when BossMod Reborn is currently steering the character out of a MECHANIC. The
    /// controller suspends its own vnavmesh pathing while this holds, so the two movement sources
    /// never fight - dodging always wins over travelling. Always false for the original, which
    /// has neither gate; see <see cref="AvoidanceSteeringKnown"/>.
    ///
    /// BOTH HALVES ARE REQUIRED, and getting this wrong stalled the runner completely.
    /// Reborn's AI.IsNavigating is <c>ai.Controller.NaviTargetPos != null</c> - which means "the
    /// AI has somewhere it would like to be", NOT "it is avoiding something". Its AI sets a
    /// navigation target for uptime, positionals, following, staying in range: essentially
    /// always, the moment the AI is enabled at all. Keyed on that alone, the controller yielded
    /// on every single tick, never issued a travel path, and reported "dodging a mechanic"
    /// indefinitely with nothing anywhere near the character.
    ///
    /// Hints.ForbiddenZonesCount is the half that actually means danger: it is the count of live
    /// telegraphed zones. Requiring both means we hand movement over when BossMod sees something
    /// to avoid AND wants to move because of it - and otherwise we keep travelling, which is
    /// correct: BossMod defers to a running vnavmesh path anyway, so a non-dodge reposition
    /// losing to deliberate travel is the precedence we want.
    /// </summary>
    public bool AvoidanceIsSteering =>
        Avoidance.Available && Avoidance.ForbiddenZones > 0 && Avoidance.IsNavigating;

    /// <summary>Live values behind <see cref="AvoidanceIsSteering"/>, for the UI.</summary>
    public (int Zones, bool Navigating) AvoidanceSignals =>
        Avoidance.Available ? (Avoidance.ForbiddenZones, Avoidance.IsNavigating) : (0, false);

    /// <summary>Whether <see cref="AvoidanceIsSteering"/> is a real signal (Reborn) or a permanent "no" (original).</summary>
    public bool AvoidanceSteeringKnown => Avoidance.SteeringKnown;

    /// <summary>
    /// True when the loaded BossMod closes on the target by itself, so CombatApproach must stand
    /// down: the original's FollowSlot fallback pathfinds the character into range of its hostile
    /// target (around forbidden zones, which our vnavmesh approach cannot see), and a vnavmesh path
    /// running at the same time would just make BossMod yield and lose the dodge. Only meaningful
    /// while avoidance is actually in use and answering.
    /// </summary>
    public bool AvoidanceOwnsApproach => _useAvoidance && Avoidance.Available && Avoidance.ClosesToTargetItself;

    /// <summary>True when telegraphed danger is on the field right now (Reborn only).</summary>
    public bool DangerActive => Avoidance.Available && Avoidance.ForbiddenZones > 0;

    /// <summary>Is this destination currently outside every telegraph? True when unknown.</summary>
    public bool IsPositionSafe(Vector3 world) => Avoidance.IsPositionSafe(world);

    /// <summary>Force both halves to re-send their state (after a zone change or a stop).</summary>
    public void Resync()
    {
        _engaged = false;
        Avoidance.Resync();
        Rotation.Resync();
    }

    /// <summary>Stop driving and hand both plugins back to the user as they were found.</summary>
    public void ReleaseControl()
    {
        _engaged = false;
        _useAvoidance = false;
        Rotation.Disengage();
        Avoidance.ReleaseControl();
    }

    /// <summary>
    /// Drop the hard target if it is a hostile combatant. Cheap enough to run every travel tick:
    /// it is a type check unless there is actually something to clear.
    /// </summary>
    private static void ClearHostileHardTarget()
    {
        try
        {
            if (Svc.Targets.Target is IBattleNpc { BattleNpcKind: BattleNpcSubKind.Combatant })
                Svc.Targets.Target = null;
        }
        catch
        {
            // Target manager unavailable (zoning) - nothing to clear.
        }
    }
}
