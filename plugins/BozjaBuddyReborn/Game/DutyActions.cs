using System;
using System.Collections.Generic;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.InstanceContent;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;

namespace BozjaBuddyReborn.Game;

/// <summary>
/// One duty-action slot as the game presents it on the duty hotbar.
///
/// This is exactly the information the game shows you for your OWN slot - which action is
/// loaded, how many charges are up, and how long until the next one - so that a peer's slot can
/// be rendered identically rather than approximated.
/// </summary>
public readonly record struct DutySlot(
    uint ActionId,
    byte CurCharges,
    byte MaxCharges,
    float CooldownRemaining,
    float CooldownTotal)
{
    public static readonly DutySlot Empty = new(0, 0, 0, 0f, 0f);

    public bool IsSet => ActionId != 0;

    /// <summary>True when at least one charge is ready to press.</summary>
    public bool Ready => IsSet && CurCharges > 0;

    /// <summary>0..1 fill of the recharge in progress, for the cooldown sweep.</summary>
    public float ChargeProgress => CooldownTotal <= 0f
        ? 1f
        : 1f - Math.Clamp(CooldownRemaining / CooldownTotal, 0f, 1f);
}

/// <summary>
/// Reads the local player's two duty-action slots.
///
/// WHERE THIS COMES FROM. Bozja's Lost Actions are surfaced through the game's generic DUTY
/// ACTION mechanism: the holster (BozjaState.HolsterActions) is the stock of actions you own,
/// while the two duty slots are what is loaded and pressable. BozjaState itself carries no slot
/// data - the slots live on the CONTENT DIRECTOR, as ContentDirector.DutyActionManager, which
/// PublicContentBozja inherits.
///
/// THREE SOURCES, TRIED IN ORDER, because the obvious one is the least reliable:
///   1. PublicContentBozja.GetInstance() cast to ContentDirector, reading its embedded
///      DutyActionManager. This is the path the rest of the plugin already proves works - it is
///      the same director we read mettle and the holster from - so it needs no extra signature.
///   2. DutyActionManager.GetInstanceIfReady(), a standalone signature-scanned function. Tried
///      second precisely because a stale signature returns null silently, which is exactly how
///      every slot reads empty while the duty bar plainly has actions on it.
///   3. RaptureHotbarModule.GetDutyActionSlot(i) - the UI's own copy of the bar. Charges and
///      recast then come from ActionManager instead. This answers even when the director is not
///      reachable at all.
/// <see cref="Diagnostic"/> reports which one answered, so an empty bar is debuggable from the
/// window rather than by guesswork.
///
/// Every call is a pointer read into live client memory: null-guarded, framework thread only.
/// </summary>
public static unsafe class DutyActions
{
    /// <summary>The game keeps two duty-action slots (the charge arrays are sized 2).</summary>
    public const int SlotCount = 2;

    /// <summary>Which source last answered, and what it saw. For the UI when the bar reads empty.</summary>
    public static string Diagnostic { get; private set; } = "not read yet";

    /// <summary>True when any source is currently giving us a loaded slot.</summary>
    public static bool Available { get; private set; }

    /// <summary>
    /// The Bozja director's own DutyActionManager.
    ///
    /// PublicContentBozja inherits PublicContentDirector inherits ContentDirector, and every
    /// layout is explicit, so the manager sits at a fixed offset inside the director we already
    /// hold - no second signature to go stale.
    /// </summary>
    private static DutyActionManager* FromDirector()
    {
        try
        {
            var bozja = PublicContentBozja.GetInstance();
            if (bozja == null)
                return null;
            return &((ContentDirector*)bozja)->DutyActionManager;
        }
        catch { return null; }
    }

    private static DutyActionManager* FromStatic()
    {
        try { return DutyActionManager.GetInstanceIfReady(); }
        catch { return null; }
    }

    /// <summary>Read one duty slot (0 or 1). Returns <see cref="DutySlot.Empty"/> when unavailable.</summary>
    public static DutySlot Read(int slot)
    {
        if (slot is < 0 or >= SlotCount)
            return DutySlot.Empty;

        var mgr = FromDirector();
        var source = "director";
        if (mgr == null)
        {
            mgr = FromStatic();
            source = "static";
        }

        if (mgr == null)
        {
            Diagnostic = "no duty action manager (director and static both null)";
            return FromHotbar(slot);
        }

        try
        {
            // Deliberately NOT gated on ActionsPresent. That flag mirrors the UI state
            // (RaptureHotbarModule.SetDutyActionsPresent) rather than whether actions are
            // loaded, and requiring it made every slot read empty while the bar was visibly
            // populated. An action id of zero is the real "nothing here".
            var id = mgr->ActionId[slot];
            if (id == 0)
            {
                Diagnostic = $"{source}: action id 0 (slots={mgr->NumValidSlots}, present={mgr->ActionsPresent})";
                return FromHotbar(slot);
            }

            var max = mgr->MaxCharges[slot];
            var cur = mgr->CurCharges[slot];
            var recast = mgr->Recast[slot];

            // Recast is a resource gauge, not a countdown: Elapsed climbs toward Total, and a
            // multi-charge action subtracts one charge's worth on use rather than zeroing it. So
            // the wait for the NEXT charge is the remainder of the current charge's slice -
            // Total minus Elapsed would be the time to FULL charges.
            var perCharge = max > 1 ? recast.Total / max : recast.Total;
            var remaining = 0f;
            if (recast.IsActive && perCharge > 0f)
                remaining = Math.Max(0f, perCharge - (recast.Elapsed % perCharge));

            Diagnostic = $"{source} (slots={mgr->NumValidSlots}, present={mgr->ActionsPresent})";
            Available = true;
            return new DutySlot(id, cur, max, remaining, perCharge);
        }
        catch (Exception ex)
        {
            Diagnostic = $"{source}: read failed - {ex.Message}";
            return FromHotbar(slot);
        }
    }

    /// <summary>
    /// Last resort: read the UI's own duty-action bar, taking charges and recast from
    /// ActionManager since the hotbar slot does not carry them.
    /// </summary>
    private static DutySlot FromHotbar(int slot)
    {
        try
        {
            var hb = RaptureHotbarModule.Instance();
            if (hb == null)
                return Miss(Diagnostic + " | no hotbar module");

            var s = hb->GetDutyActionSlot((uint)slot);
            if (s == null)
                return Miss(Diagnostic + " | hotbar slot null");

            // DutyActionSlot inherits HotbarSlot by explicit layout; the pinned ClientStructs
            // does not flatten the base members onto the derived type, so read through a cast.
            var id = ((RaptureHotbarModule.HotbarSlot*)s)->ApparentActionId;
            if (id == 0)
                return Miss(Diagnostic + $" | hotbar id 0 (present={hb->DutyActionsPresent})");

            byte cur = 0, max = 0;
            float remaining = 0f, perCharge = 0f;

            var am = ActionManager.Instance();
            if (am != null)
            {
                cur = (byte)Math.Min(255u, am->GetCurrentCharges(id));
                max = (byte)Math.Min(255, (int)ActionManager.GetMaxCharges(id, 0));

                var total = am->GetRecastTime(ActionType.Action, id);
                var elapsed = am->GetRecastTimeElapsed(ActionType.Action, id);
                perCharge = max > 1 ? total / max : total;
                if (perCharge > 0f && elapsed < total)
                    remaining = Math.Max(0f, perCharge - (elapsed % perCharge));
            }

            Diagnostic = "hotbar module";
            Available = true;
            return new DutySlot(id, cur, max, remaining, perCharge);
        }
        catch (Exception ex)
        {
            return Miss($"hotbar read failed - {ex.Message}");
        }
    }

    private static DutySlot Miss(string why)
    {
        Diagnostic = why;
        return DutySlot.Empty;
    }

    // ------------------------------------------------------------------ pressing

    /// <summary>What a press attempt did, so the caller can report it rather than guess.</summary>
    /// <param name="Fired">True only when the action actually went out.</param>
    /// <param name="Message">Human-readable outcome, shown under the bar and in the panels.</param>
    /// <param name="TargetRefused">
    /// True when the game rejected THIS TARGET specifically, as opposed to the action being
    /// recharging, the slot being empty, or the character being dead.
    ///
    /// The distinction is not cosmetic: a caller sweeping a party needs to pass over a member the
    /// game will not accept, and must NOT pass them over because the action happened to be on
    /// cooldown. Carried as a flag because the alternative - reading it back out of the message
    /// text - is the kind of coupling that breaks the day someone rewords a string.
    /// </param>
    public readonly record struct PressResult(bool Fired, string Message, bool TargetRefused = false)
    {
        public static PressResult No(string why) => new(false, why);

        public static PressResult NoTarget(string why) => new(false, why, true);
    }

    /// <summary>The last press attempt made on this box, for the hotbar window to echo back.</summary>
    public static string LastPress { get; private set; } = string.Empty;

    /// <summary>When that attempt happened, so the window can let the message fade.</summary>
    public static long LastPressMs { get; private set; }

    /// <summary>
    /// Press one of the two duty-action slots on THIS box.
    ///
    /// The press itself is RaptureHotbarModule.ExecuteDutyActionSlot - the game's own duty-bar
    /// button. That choice is the whole design: every rule about what a duty action does when you
    /// press it (which target it takes, whether it needs a hostile one, what the ground-target
    /// cursor does, what refusal message the game prints) then behaves exactly as it does for a
    /// manual click, instead of this plugin re-deriving those rules out of the Action sheet and
    /// getting one of them quietly wrong. For a press that should follow whatever the operator is
    /// pointing at, that is exactly right, and it is why this method stays the one behind the
    /// hotbar window.
    ///
    /// A CORRECTION, because the reasoning recorded here through 1.0.23.0 was wrong and was load
    /// bearing. It said an ActionManager.UseAction call "would have to carry the right extra
    /// parameter to be a duty-action press at all", on the strength of ClientStructs noting that
    /// the game identifies a duty action by the Action sheet's PrimaryCostType (20 for slot 1, 21
    /// for slot 2). That is a runtime LOOKUP the game performs, not a parameter anything passes:
    /// it reads the row's PrimaryCostType, scans the loaded duty slots for the action id, and
    /// returns 20+slotIndex when it finds one. UseAction takes no such argument, and the duty bar's
    /// own press is literally UseAction(Action, slot->CommandId, GetTargetObjectId(), 0, None, 0)
    /// - the same call, differing only in the target. See <see cref="PressAt"/>.
    ///
    /// WHAT THE GAME WILL NOT DO FOR US. ClientStructs documents ExecuteDutyActionSlot as not
    /// validating that the slot is in an executable state, and as returning true regardless - so
    /// its return value carries no information and every guard below is ours to make. That is
    /// also why the charge check is not decorative: pressing an empty slot is silent.
    ///
    /// Framework thread only - it reads live game memory and calls into the client. The guard
    /// below is worth having for an IPC or task caller, but do NOT read it as the thing that makes
    /// a button press safe: Dalamud runs the ImGui draw callback on the same OS thread it stamps
    /// as the framework thread, so IsInFrameworkUpdateThread answers true inside Draw and the
    /// guard would wave a click straight through. What actually defers a click is structural - the
    /// window enqueues an instruction and the controller's pump calls this. See DutyActionWindow.
    /// </summary>
    /// <param name="slot">0 or 1.</param>
    /// <param name="expectedActionId">
    /// The action the caller believed was loaded, or 0 to press whatever is there. See below for
    /// why a remote press should always pass it.
    /// </param>
    public static PressResult Press(int slot, uint expectedActionId = 0)
    {
        var result = Attempt(slot, expectedActionId);
        LastPress = result.Message;
        LastPressMs = Environment.TickCount64;

        if (result.Fired)
            Svc.Log.Information($"[BozjaBuddyReborn] Duty action {slot + 1}: {result.Message}");
        else
            Svc.Log.Debug($"[BozjaBuddyReborn] Duty action {slot + 1} not pressed: {result.Message}");

        return result;
    }

    private static PressResult Attempt(int slot, uint expectedActionId)
    {
        if (slot is < 0 or >= SlotCount)
            return PressResult.No($"there is no duty action slot {slot + 1}");

        if (!Svc.Framework.IsInFrameworkUpdateThread)
            return PressResult.No("refused: not on the framework thread");

        var me = Svc.Objects.LocalPlayer;
        if (me == null)
            return PressResult.No("no character loaded");

        if (me.CurrentHp == 0)
            return PressResult.No("refused: the character is dead");

        // Read live rather than trusting the frame's cached row. On the operator's own box the
        // difference is a frame; on a box being driven remotely it is the whole point.
        var current = Read(slot);
        if (!current.IsSet)
            return PressResult.No($"slot {slot + 1} is empty");

        // A REMOTE PRESS NAMES THE ACTION, NOT JUST THE SLOT. A peer's row on screen is up to half
        // a second old and any box can reload its slots from the holster at any moment, so
        // pressing "slot 1" blind could fire an Essence the operator never chose. Lost Actions are
        // farmed and finite, so a mismatch is refused and reported rather than resolved by
        // guessing - the operator can look and press again.
        if (expectedActionId != 0 && expectedActionId != current.ActionId)
            return PressResult.No(
                $"slot {slot + 1} now holds {Describe(current.ActionId).Name}, " +
                $"not {Describe(expectedActionId).Name} - nothing fired");

        var name = Describe(current.ActionId).Name;

        if (!current.Ready)
        {
            return PressResult.No(current.CooldownRemaining > 0f
                ? $"{name} is recharging ({current.CooldownRemaining:F1}s)"
                : $"{name} has no charges");
        }

        try
        {
            var hb = RaptureHotbarModule.Instance();
            if (hb == null)
                return PressResult.No("hotbar module unavailable");

            hb->ExecuteDutyActionSlot((uint)slot);
            return new PressResult(true, $"used {name}");
        }
        catch (Exception ex)
        {
            return PressResult.No($"{name} failed - {ex.Message}");
        }
    }

    /// <summary>
    /// Fire the action in a duty slot at ONE NAMED TARGET, without touching what is selected.
    ///
    /// WHY THIS EXISTS ALONGSIDE Press. Pressing the slot takes its target from
    /// TargetSystem.GetTargetObjectId(), which is the SOFT target when one is set and only then
    /// the hard target - so aiming a slot press by setting the hard target is doing two wrong
    /// things at once. It loses to any soft target a controller or another plugin has set, which
    /// is silent and is precisely the "fired at somebody I did not choose" failure that matters
    /// when the charge is farmed. And it makes the selection itself a shared resource: this
    /// plugin's own CombatApproach decides what to close on by READING the hard target, and
    /// CombatDirector clears a hostile one every travel tick, so borrowing it even briefly means
    /// fighting them.
    ///
    /// Passing the target id removes the whole question. The action is still the one loaded in the
    /// slot, so its charges and recast are still the slot's; only the target is stated outright
    /// rather than inferred from what happens to be selected.
    ///
    /// GetActionStatus is asked first because UseAction alone answers with a bare bool. Recast is
    /// deliberately NOT part of that question (checkRecastActive: false) - charges and cooldown are
    /// already the caller's business through <see cref="DutySlot.Ready"/>, and letting recast into
    /// this gate would report a recharging action as an invalid TARGET, which is a different and
    /// much more alarming thing to read.
    /// </summary>
    /// <param name="slot">0 or 1.</param>
    /// <param name="expectedActionId">The action the caller believes is loaded. Required here.</param>
    /// <param name="targetId">GameObjectId of who to hit. Never 0.</param>
    /// <param name="targetName">For the message only.</param>
    public static PressResult PressAt(int slot, uint expectedActionId, ulong targetId, string targetName)
    {
        var result = AttemptAt(slot, expectedActionId, targetId, targetName);
        LastPress = result.Message;
        LastPressMs = Environment.TickCount64;

        if (result.Fired)
            Svc.Log.Information($"[BozjaBuddyReborn] Duty action {slot + 1}: {result.Message}");
        else
            Svc.Log.Debug($"[BozjaBuddyReborn] Duty action {slot + 1} not used: {result.Message}");

        return result;
    }

    private static PressResult AttemptAt(int slot, uint expectedActionId, ulong targetId, string targetName)
    {
        if (slot is < 0 or >= SlotCount)
            return PressResult.No($"there is no duty action slot {slot + 1}");

        if (!Svc.Framework.IsInFrameworkUpdateThread)
            return PressResult.No("refused: not on the framework thread");

        if (targetId == 0)
            return PressResult.No("no target chosen");

        var me = Svc.Objects.LocalPlayer;
        if (me == null)
            return PressResult.No("no character loaded");

        if (me.CurrentHp == 0)
            return PressResult.No("refused: the character is dead");

        var current = Read(slot);
        if (!current.IsSet)
            return PressResult.No($"slot {slot + 1} is empty");

        if (expectedActionId != 0 && expectedActionId != current.ActionId)
            return PressResult.No(
                $"slot {slot + 1} now holds {Describe(current.ActionId).Name}, " +
                $"not {Describe(expectedActionId).Name} - nothing fired");

        var name = Describe(current.ActionId).Name;

        if (!current.Ready)
        {
            return PressResult.No(current.CooldownRemaining > 0f
                ? $"{name} is recharging ({current.CooldownRemaining:F1}s)"
                : $"{name} has no charges");
        }

        try
        {
            var am = ActionManager.Instance();
            if (am == null)
                return PressResult.No("action manager unavailable");

            // The game's own verdict on this action against this target, asked before spending
            // anything. Unlike the hotbar press - which returns true whatever happens - this
            // actually distinguishes "cannot be used on them" from "went out".
            var status = am->GetActionStatus(ActionType.Action, current.ActionId, targetId, false, false);
            if (status != 0)
                return PressResult.NoTarget($"{name} on {targetName}: the game refused it (status {status})");

            var ok = am->UseAction(ActionType.Action, current.ActionId, targetId, 0u,
                ActionManager.UseActionMode.None, 0u, null);

            return ok
                ? new PressResult(true, $"used {name} on {targetName}")
                : PressResult.No($"{name} on {targetName} did not go out");
        }
        catch (Exception ex)
        {
            return PressResult.No($"{name} failed - {ex.Message}");
        }
    }

    /// <summary>Read both slots at once.</summary>
    public static (DutySlot Slot0, DutySlot Slot1) ReadBoth()
    {
        Available = false;

        var a = Read(0);
        var firstDiagnostic = Diagnostic;
        var b = Read(1);

        // Slot 1 being empty is normal; slot 0's explanation is the more useful one to keep.
        if (a.IsSet && !b.IsSet)
            Diagnostic = firstDiagnostic;

        Available = a.IsSet || b.IsSet;
        return (a, b);
    }

    // ------------------------------------------------------------------ naming

    private static readonly Dictionary<uint, (string Name, uint Icon)> _cache = [];

    /// <summary>Display name and icon id for an action, cached. Placeholder when unresolvable.</summary>
    public static (string Name, uint Icon) Describe(uint actionId)
    {
        if (actionId == 0)
            return (string.Empty, 0u);

        if (_cache.TryGetValue(actionId, out var hit))
            return hit;

        var result = (Name: $"Action #{actionId}", Icon: 0u);
        try
        {
            var sheet = Svc.Data.GetExcelSheet<Lumina.Excel.Sheets.Action>();
            var row = sheet?.GetRowOrDefault(actionId);
            if (row != null)
            {
                var name = row.Value.Name.ExtractText();
                result = (name.Length > 0 ? name : result.Name, row.Value.Icon);
            }
        }
        catch { /* sheet not ready - retry next call rather than caching a miss */ }

        if (result.Icon != 0)
            _cache[actionId] = result;

        return result;
    }

    public static void InvalidateNames() => _cache.Clear();
}
