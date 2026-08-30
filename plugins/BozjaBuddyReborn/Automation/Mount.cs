using System;
using Dalamud.Game.ClientState.Conditions;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace BozjaBuddyReborn.Automation;

/// <summary>
/// Mount and dismount handling for long-distance travel.
///
/// WHY THIS EXISTS: vnavmesh moves the character but has no mount IPC - summoning a mount is
/// the orchestrator's job. Without it the character jogs across the entire zone on foot, which
/// in Zadnor is minutes per objective.
///
/// AND WHY IT MATTERS FOR CORRECTNESS, not just speed: passing vnavmesh fly=true while the
/// character is grounded and unmounted hands it a 3D flight path it physically cannot follow,
/// and it stalls partway. So the flight flag must reflect the ACTUAL flight state
/// (ConditionFlag.InFlight), never merely "flying is allowed" - see <see cref="ShouldFly"/>.
///
/// Bozja and Zadnor have no aetherytes and no in-zone teleport network of any kind (verified:
/// neither territory has a single row in the Aetheryte sheet), so mount travel IS the fast
/// travel. There is nothing faster to reach for.
///
/// Action ids verified against GeneralAction.csv: 9 = Mount Roulette, 23 = Dismount.
/// </summary>
public static unsafe class Mount
{
    private const uint MountRoulette = 9;
    private const uint DismountAction = 23;

    /// <summary>Below this, mounting costs more time than it saves.</summary>
    private const float MinMountDistance = 30f;

    private const long MountThrottleMs = 3000;
    private const long DismountThrottleMs = 1000;

    private static long _lastMount;
    private static long _lastDismount;

    /// <summary>True when the character can act: not mounted, airborne, jumping or diving.</summary>
    public static bool IsGrounded =>
        !Svc.Condition[ConditionFlag.Mounted]
        && !Svc.Condition[ConditionFlag.InFlight]
        && !Svc.Condition[ConditionFlag.Jumping]
        && !Svc.Condition[ConditionFlag.Diving];

    public static bool IsMounted => Svc.Condition[ConditionFlag.Mounted];

    /// <summary>
    /// The flight flag to hand vnavmesh: only true when the character is genuinely airborne.
    /// A grounded character told to fly cannot follow the path and stalls.
    /// </summary>
    public static bool ShouldFly(bool allowFlight)
        => allowFlight && Svc.Condition[ConditionFlag.InFlight];

    /// <summary>Summon a mount when the remaining haul is long enough to be worth it.</summary>
    public static void EnsureMounted(Configuration config, float distance)
    {
        if (!config.UseMount || distance < MinMountDistance)
            return;

        if (IsMounted)
            return;

        // Never mount out of combat-readiness or mid-cast; the game would refuse anyway and
        // spamming a refused action is noise.
        if (Svc.Condition[ConditionFlag.InCombat]
            || Svc.Condition[ConditionFlag.Casting]
            || Svc.Condition[ConditionFlag.Occupied]
            || Svc.Condition[ConditionFlag.BetweenAreas])
            return;

        var now = Environment.TickCount64;
        if (now - _lastMount < MountThrottleMs)
            return;
        _lastMount = now;

        var am = ActionManager.Instance();
        if (am != null)
            am->UseAction(ActionType.GeneralAction, MountRoulette);
    }

    /// <summary>
    /// Get off the mount so the character can fight. You cannot attack while mounted, so this
    /// must succeed before the rotation is armed or RSR simply has nothing it can press.
    /// </summary>
    /// <returns>True once grounded.</returns>
    public static bool EnsureDismounted()
    {
        if (IsGrounded)
            return true;

        if (!IsMounted && !Svc.Condition[ConditionFlag.InFlight])
            return false; // mid-jump; it resolves on its own

        var now = Environment.TickCount64;
        if (now - _lastDismount < DismountThrottleMs)
            return false;
        _lastDismount = now;

        var am = ActionManager.Instance();
        if (am != null)
            am->UseAction(ActionType.GeneralAction, DismountAction);

        return false;
    }

    /// <summary>Clear the throttles, e.g. after a zone change.</summary>
    public static void Reset()
    {
        _lastMount = 0;
        _lastDismount = 0;
    }
}
