using System;
using BozjaBuddyReborn.Game;
using ECommons.DalamudServices;

namespace BozjaBuddyReborn.Automation;

/// <summary>
/// Uses configured Lost Actions out of the holster during an engagement.
///
/// ONE CALL, TWO BEHAVIOURS. PublicContentBozja.UseFromHolster(holsterIndex, slot) is documented
/// as "use lost action from holster INTO specified duty action slot (slot is ignored for items,
/// which are used directly)" - the contrast is the whole point, and reading it the other way is
/// what left this class half a feature until 1.0.21.0. For an ITEM-type holster entry (every
/// Essence, the kits, Dynamis Dice, Lodestone, Light Curtain, Resistance Elixir - see
/// LostActionCatalog) that call IS the use and there is nothing further to do. For an ACTION-type
/// entry it only LOADS the duty slot; the charge is spent by pressing that slot, which is
/// DutyActions.Press. This class used to make the first call for both kinds and report true, so
/// it reloaded a slot on a timer and never fired a single action-type Lost Action.
///
/// THE LOAD IS NOT INSTANT, so the press is a second step and not a second line. UseFromHolster
/// returns before the game has populated the duty slot - pressing in the same tick reads whatever
/// was in the slot beforehand, which is the one outcome worse than not firing: a different, and
/// possibly much more expensive, Lost Action spent by accident. So a load moves the driver into
/// WaitingForLoad and the press happens on a later tick, only once the slot itself reports the
/// action id we asked for, with that same id handed to DutyActions.Press so its own mismatch guard
/// is a second lock on the same door. If the slot never comes up - the game refused, the fight
/// moved on - the attempt is abandoned on a timeout and reported rather than pressed hopefully.
///
/// SPENDING IS A SEPARATE OPT-IN. AutoUseLostActions has never once spent an action-type charge,
/// so quietly completing it would have changed what a switch the user already ticked costs them.
/// Configuration.AutoFireLostActions is that decision, taken explicitly and default off; with it
/// off, action-type entries are skipped entirely rather than loaded, because loading a slot
/// nothing will press is pure churn that also stamps on whatever loadout was sitting in it.
///
/// AND IT WILL NOT RE-APPLY WHAT YOU ALREADY HAVE. Everything here runs on a timer, which is the
/// one arrangement guaranteed to re-buff something still running: an Essence ticked in the list
/// used to be consumed every cooldown window for the whole half-hour it was already up, and Lost
/// Protect re-pressed every eight seconds. So an entry whose status LostActionStatuses can name is
/// skipped while that status is on the player, and the window goes to the next thing in the
/// priority order instead. An entry with no known status is never refused - see
/// LostActionStatuses for why that is the right direction to fail.
///
/// Deliberately conservative throughout: opt-in, one action per cooldown window, and only while
/// actually in combat inside an engagement. Lost Actions are a finite, farmed resource - burning
/// them on trash because a timer elapsed is worse than not using them at all.
/// </summary>
public sealed class HolsterDriver(Configuration config, LostActionCatalog catalog)
{
    /// <summary>
    /// The duty slot the driver loads into.
    ///
    /// Slot 1 on screen, and yes, that means a loadout parked there is replaced when the driver
    /// has to load. It only loads when the action is in NEITHER slot already, so a loadout built
    /// out of the actions the driver is configured to use is pressed where it stands and never
    /// disturbed - which is the arrangement worth having.
    /// </summary>
    private const int DriverSlot = 0;

    /// <summary>
    /// How long to let the game populate the duty slot before giving the attempt up.
    ///
    /// Generous on purpose: giving up too late costs one skipped cooldown window, while giving up
    /// too early leaves a loaded slot with nothing fired - and the next window would find it
    /// already loaded and press it anyway, just later and without having said so.
    /// </summary>
    private const int LoadTimeoutMs = 2500;

    private enum Phase
    {
        /// <summary>Nothing outstanding - free to choose and use something.</summary>
        Idle,

        /// <summary>UseFromHolster has been called; waiting for the slot to report the action.</summary>
        WaitingForLoad,
    }

    /// <summary>What one pass at a configured entry did, so the caller knows whether to move on.</summary>
    private enum Outcome
    {
        /// <summary>Not usable right now - try the next action in the priority order.</summary>
        Skip,

        /// <summary>A load was issued; the press comes on a later tick. Nothing else this tick.</summary>
        Loading,

        /// <summary>A charge was actually spent.</summary>
        Fired,
    }

    private readonly Configuration _config = config;
    private readonly LostActionCatalog _catalog = catalog;

    private long _lastUseMs;

    private Phase _phase;
    private byte _pendingRow;
    private uint _pendingActionId;
    private long _loadIssuedMs;

    /// <summary>MYCTemporaryItem row id of the last action actually used, or 0. For the UI.</summary>
    public byte LastUsedRow { get; private set; }

    /// <summary>
    /// What the driver last did, or last refused to do, in the same words the duty-action bar
    /// uses. A driver that silently does nothing is indistinguishable from a broken one.
    /// </summary>
    public string LastResult { get; private set; } = string.Empty;

    /// <summary>
    /// Advance the driver by one tick.
    /// </summary>
    /// <returns>True only when a charge was actually spent this tick.</returns>
    public bool Tick(bool inCombat)
    {
        if (!_config.AutoUseLostActions || _config.AutoLostActions.Count == 0)
        {
            Abandon();
            return false;
        }

        // A load still settling when the fight ends has nothing left to fire at, and carrying it
        // across the gap would spend it on the opening of the NEXT fight instead.
        if (!inCombat)
        {
            Abandon();
            return false;
        }

        var now = Environment.TickCount64;

        // Turning the press switch off mid-wait must not let one last charge through the door -
        // the whole point of it being a separate switch is that it decides what gets spent.
        if (!_config.AutoFireLostActions)
            Abandon();

        // Deliberately ahead of the cooldown gate. The gate spaces out USES, and finishing a load
        // is the second half of a use that has already been paid for - making it wait would strand
        // a loaded slot for a whole window.
        if (_phase == Phase.WaitingForLoad)
            return FinishLoad(now);

        if (now - _lastUseMs < _config.LostActionCooldownMs)
            return false;

        var me = Svc.Objects.LocalPlayer;
        if (me == null || me.IsCasting)
            return false;

        var holster = FieldState.Holster();
        if (holster.Length == 0)
            return false;

        // Walk the user's priority order, not the holster order.
        foreach (var wanted in _config.AutoLostActions)
        {
            if (wanted == 0)
                continue;

            // An unresolved row cannot be classified, and classifying one wrongly as an item means
            // consuming it outright. Skip it rather than guess.
            if (!_catalog.TryGet(wanted, out var entry) || entry.ActionId == 0)
                continue;

            // Ahead of both branches on purpose: an Essence still running and a Lost Protect still
            // running are the same waste, and neither wants the window.
            if (LostActionStatuses.IsActive(entry.StatusId, out var remaining))
            {
                LastResult = $"{LostActionStatuses.Name(entry.StatusId)} is already up" +
                             (remaining > 0f ? $" ({remaining:F0}s)" : string.Empty);
                continue;
            }

            if (entry.IsItem)
            {
                if (UseItem(wanted, entry, holster, now))
                    return true;
                continue;
            }

            // Neither kind, which today cannot happen - Type is 1 or 2 across all 99 rows. If a
            // patch ever adds a third, it gets left alone rather than handled as whichever branch
            // it happens to fall through into.
            if (!entry.IsAction)
                continue;

            if (!_config.AutoFireLostActions)
                continue;

            switch (UseAction(wanted, entry, holster, now))
            {
                case Outcome.Fired:
                    return true;
                case Outcome.Loading:
                    return false;
                default:
                    continue;
            }
        }

        return false;
    }

    /// <summary>Reset the cooldown so the next engagement can open with an action.</summary>
    public void Reset()
    {
        _lastUseMs = 0;
        LastResult = string.Empty;
        Abandon();
    }

    // ------------------------------------------------------------------ items

    /// <summary>
    /// An item-type entry is used by the holster call itself - the duty-slot argument is ignored
    /// for these, and pressing a slot afterwards would fire whatever else was already in it.
    /// </summary>
    private bool UseItem(byte row, LostActionCatalog.Entry entry, byte[] holster, long now)
    {
        var index = IndexOf(holster, row);
        if (index < 0)
            return false;

        if (!FieldState.UseFromHolster((uint)index, DriverSlot))
            return false;

        _lastUseMs = now;
        LastUsedRow = row;
        LastResult = $"used {entry.Name}";
        Svc.Log.Information($"[BozjaBuddyReborn] Auto Lost Action: {LastResult}");
        return true;
    }

    // ---------------------------------------------------------------- actions

    private Outcome UseAction(byte row, LostActionCatalog.Entry entry, byte[] holster, long now)
    {
        // ALREADY LOADED IS THE COMMON CASE once the driver has run for a window or two, and it is
        // also how a loadout the operator set by hand gets used without being disturbed. Press it
        // where it stands rather than reloading a slot that already holds it.
        for (var slot = 0; slot < DutyActions.SlotCount; slot++)
        {
            if (DutyActions.Read(slot).ActionId != entry.ActionId)
                continue;

            var press = DutyActions.Press(slot, entry.ActionId);
            LastResult = press.Message;

            // Recharging, or otherwise refused: let a lower-priority action have the window rather
            // than sit on it. The refusal is already recorded above.
            if (!press.Fired)
                return Outcome.Skip;

            _lastUseMs = now;
            LastUsedRow = row;
            return Outcome.Fired;
        }

        var index = IndexOf(holster, row);
        if (index < 0)
            return Outcome.Skip;

        if (!FieldState.UseFromHolster((uint)index, DriverSlot))
        {
            LastResult = $"the game refused to load {entry.Name}";
            return Outcome.Skip;
        }

        _phase = Phase.WaitingForLoad;
        _pendingRow = row;
        _pendingActionId = entry.ActionId;
        _loadIssuedMs = now;
        LastResult = $"loading {entry.Name} into duty slot {DriverSlot + 1}";
        return Outcome.Loading;
    }

    /// <summary>
    /// Second half of a use: press the slot, but only once it actually reports the action that was
    /// loaded. Reading the slot is what makes this safe - the id is checked here and again inside
    /// DutyActions.Press, so a slot changed underneath us by a loadout, by a peer instruction or by
    /// the player's own hand refuses by name instead of firing the replacement.
    /// </summary>
    private bool FinishLoad(long now)
    {
        var name = _catalog.Name(_pendingRow);

        if (DutyActions.Read(DriverSlot).ActionId != _pendingActionId)
        {
            if (now - _loadIssuedMs < LoadTimeoutMs)
            {
                LastResult = $"waiting for duty slot {DriverSlot + 1} to come up as {name}";
                return false;
            }

            LastResult = $"duty slot {DriverSlot + 1} never came up as {name} - nothing fired";
            Svc.Log.Debug($"[BozjaBuddyReborn] Auto Lost Action: {LastResult}");
            _lastUseMs = now;
            Abandon();
            return false;
        }

        var row = _pendingRow;
        var result = DutyActions.Press(DriverSlot, _pendingActionId);

        LastResult = result.Message;
        _lastUseMs = now;
        Abandon();

        if (!result.Fired)
            return false;

        LastUsedRow = row;
        return true;
    }

    private void Abandon()
    {
        _phase = Phase.Idle;
        _pendingRow = 0;
        _pendingActionId = 0;
        _loadIssuedMs = 0;
    }

    private static int IndexOf(byte[] holster, byte row)
    {
        for (var i = 0; i < holster.Length; i++)
            if (holster[i] == row)
                return i;
        return -1;
    }
}
