using System;
using System.Collections.Generic;
using System.Numerics;
using BozjaBuddyReborn.Game;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Types;
using ECommons.DalamudServices;

namespace BozjaBuddyReborn.Automation;

/// <summary>
/// Keeps the party's Lost Action buffs up, and heals whoever is worst off.
///
/// A STOPPABLE TASK, not a background habit. It is in the ErrandRunner mould - Begin, Stop, Active,
/// Status - because what it does is finite and aimed at other people: it spends farmed charges,
/// repeatedly, for as long as it runs. A thing like that needs an off switch that is one click and
/// always present, not a settings checkbox two windows away.
///
/// IT NEVER TOUCHES YOUR SELECTION. Casts name their target through DutyActions.PressAt rather than
/// aiming by moving the hard target. That was the shape until 1.0.24.0 and it was wrong twice: a
/// slot press takes the SOFT target in preference to the hard one, so setting the hard target could
/// silently send a charge somewhere else entirely; and the selection is shared - CombatApproach
/// decides what to close on by reading it and CombatDirector clears a hostile one every travel
/// tick - so borrowing it at all meant fighting this plugin's own combat handling.
///
/// WHO IT CAN AIM AT, and why that is a structural answer rather than a check. Every candidate
/// comes from PartyView, which is built from IPartyList and therefore cannot contain an alliance
/// member or a passer-by - there is no way to NAME a non-party target here, so there is no rule to
/// forget. On top of that the party membership is re-derived live in the instant before the press,
/// because the list a decision was made from is a tick old and someone can leave a party inside a
/// tick.
///
/// THE ORDER IT APPLIES IN, which is the request restated:
///   1. party members who do not have the buff at all;
///   2. then those more than 80% through it, most-expired first - "Bravery lasts 10 minutes, if
///      person has less than 2 mins apply". The total comes from LostActionDurations, parsed out of
///      the game's own tooltip text; where a duration cannot be established, step 2 is SKIPPED
///      rather than guessed at, and the status line says so.
/// Those are two sweeps over the WHOLE configured list, not two steps within each action - so an
/// unmet need anywhere outranks a top-up anywhere, which is what "apply first to those who don't
/// have it" says. Doing it the other way would finish Bravery entirely, topping somebody up from
/// 15%, while a second member still had no Protect at all.
/// Healing joins the first sweep, keyed on HP rather than on a status: lowest current HP first,
/// re-decided before every cast, so the person who is worst off when it goes out is the one who
/// gets it. There is no second sweep for healing - there is no such thing as topping someone up
/// towards full.
///
/// IT STOPS ITSELF WHEN THE STOCK IS GONE. "If no more actions available, stop task" - so when no
/// configured action is loaded with a charge up NOR sitting in the holster, the task ends and says
/// which it ran out of, rather than spinning on a slot it can never fire.
/// </summary>
public sealed class PartySupportDriver(Configuration config, LostActionCatalog catalog)
{
    /// <summary>
    /// How long to let the game populate a duty slot after loading, before giving the attempt up.
    /// The same two-step, and the same reasoning, as HolsterDriver - see there.
    /// </summary>
    private const int LoadTimeoutMs = 2500;

    /// <summary>
    /// How long a party member the game refuses to target is passed over for.
    ///
    /// Without this, the first unbuffed member is re-picked every gap window forever when the game
    /// will not take them - mid-raise, in a cutscene, flagged untargetable - and the rest of the
    /// party never gets the buff at all. The refusal is remembered rather than merely reported, so
    /// the sweep moves on to somebody it CAN serve and comes back to them shortly.
    /// </summary>
    private const int RefusalCooldownMs = 10_000;

    private readonly Configuration _config = config;
    private readonly LostActionCatalog _catalog = catalog;

    private enum Phase { Idle, WaitingForLoad }

    /// <summary>
    /// Which of the two sweeps is being run.
    ///
    /// <see cref="Need"/> is "this person is getting nothing from this action right now" - no buff
    /// at all, or HP under the floor. <see cref="TopUp"/> is "they have it but it is nearly out".
    /// Every Need across every configured action is served before any TopUp is considered.
    /// </summary>
    private enum Pass { Need, TopUp }

    private Phase _phase;
    private byte _pendingRow;
    private uint _pendingActionId;
    private ulong _pendingTargetId;
    private string _pendingTargetName = string.Empty;
    private Pass _pendingPass;
    private long _loadIssuedMs;
    private long _lastActionMs;

    /// <summary>
    /// Party members the game recently refused as a target, and when they become eligible again.
    /// See <see cref="RefusalCooldownMs"/>.
    /// </summary>
    private readonly Dictionary<ulong, long> _refusedUntil = [];

    /// <summary>True while the task is running. The controller and the UI both read this.</summary>
    public bool Active { get; private set; }

    /// <summary>What it is doing, or why it stopped. Always worth showing - it spends things.</summary>
    public string Status { get; private set; } = string.Empty;

    /// <summary>How many charges this run has actually fired, for the panel.</summary>
    public int Applied { get; private set; }

    /// <summary>Start maintaining the configured party support actions.</summary>
    public void Begin()
    {
        // ALREADY RUNNING IS A NO-OP, and that is not merely tidiness. Begin clears the pending
        // state, so a second Begin arriving mid-load - trivially easy, since the instruction queue
        // drains at frame rate while this ticks every 200ms - would strand a charge that had
        // already been pulled out of the holster into a slot nobody would then press, and the next
        // tick would find neither slot nor holster entry and stop the task reporting that the
        // action was never in the holster at all.
        if (Active)
            return;

        if (_config.PartySupportActions.Count == 0)
        {
            Active = false;
            Status = "Nothing configured - tick some party actions under Settings, Lost Actions.";
            return;
        }

        Active = true;
        Applied = 0;
        _lastActionMs = 0;
        Abandon();
        Status = "Starting party support.";
    }

    /// <summary>Stop, for any reason, and say which. Safe to call when not running.</summary>
    public void Stop(string reason = "Party support stopped.")
    {
        if (Active)
            Svc.Log.Information($"[BozjaBuddyReborn] Party support stopped: {reason}");

        Active = false;
        Abandon();
        _refusedUntil.Clear();
        Status = reason;

        // Nothing to clean up target-wise: the borrow is returned inside Fire, before Fire returns,
        // so there is never an outstanding one to unwind here. That is deliberate - see Fire.
    }

    public void Toggle()
    {
        if (Active)
            Stop("Party support stopped from the panel.");
        else
            Begin();
    }

    /// <summary>
    /// One tick. Framework thread - it reads live objects, walks the object table and presses
    /// buttons, all three of which Dalamud asserts the thread for.
    /// </summary>
    public void Tick()
    {
        if (!Active)
            return;

        var now = Environment.TickCount64;

        var me = Svc.Objects.LocalPlayer;
        if (me == null)
        {
            Stop("Party support stopped - no character.");
            return;
        }

        if (me.CurrentHp == 0)
        {
            // Dying is a pause, not a failure. The buffs are still wanted when we get up.
            Status = "Waiting - the character is dead.";
            Abandon();
            return;
        }

        // ZONING IS A PAUSE, AND HAS TO BE TESTED FIRST. A loading screen inside Bozja drops the
        // director for a second or two, so without this the task would terminate itself mid-zone
        // with "not in a field operation" - true at that instant, and completely misleading.
        // Leaving the zone for real is handled where it belongs, on the territory-change event.
        if (Svc.Condition[ConditionFlag.BetweenAreas] || Svc.Condition[ConditionFlag.BetweenAreas51])
        {
            Status = "Waiting - zoning.";
            Abandon();
            return;
        }

        if (!FieldState.Available)
        {
            Stop("Party support stopped - not in a field operation.");
            return;
        }

        if (_phase == Phase.WaitingForLoad)
        {
            FinishLoad(now);
            return;
        }

        if (now - _lastActionMs < Math.Max(500, _config.PartySupportGapMs))
            return;

        if (me.IsCasting)
            return;

        var party = PartyView.Snapshot();
        if (party.Count == 0)
        {
            Status = "Waiting - nobody in range to support.";
            return;
        }

        var holster = FieldState.Holster();
        var anyStock = false;
        var notes = new List<string>();

        // TWO PASSES OVER THE WHOLE LIST, not one pass per action, and the difference is the
        // request read literally: "apply FIRST to those who don't have it in the party, THEN those
        // who are more than 80% through it". Nesting it the other way - decide everything about
        // Bravery before looking at Protect - would top somebody's Bravery up from 15% while a
        // second party member still had no Protect at all, which is the opposite of what was asked.
        // An unmet need anywhere outranks a top-up anywhere.
        //
        // Heals belong in the first pass: someone under the HP floor is an unmet need in exactly
        // the same sense, and their position among the other needs is the priority order the
        // operator ticked them in.
        foreach (var pass in new[] { Pass.Need, Pass.TopUp })
        {
            foreach (var row in _config.PartySupportActions)
            {
                if (row == 0 || !_catalog.TryGet(row, out var entry) || !entry.IsPartySupport)
                    continue;

                var slot = SlotHolding(entry.ActionId);
                var loaded = slot >= 0 ? DutyActions.Read(slot) : DutySlot.Empty;
                var inHolster = IndexOf(holster, row) >= 0;

                // "STOCK" IS NOT THE SAME AS "FIRABLE RIGHT NOW", and conflating the two would end
                // the task every time an action went on recast. A loaded slot with no charge but a
                // recast running is a charge on its way; a loaded slot with no charge and NO recast,
                // and nothing left in the holster, is the real end of the line and the thing the
                // request means by "no more actions available".
                var ready = loaded.Ready;
                var recharging = slot >= 0 && !ready && loaded.CooldownRemaining > 0f;

                if (!ready && !recharging && !inHolster)
                    continue;

                // Counted once, on the first pass only - the second pass walks the same rows.
                if (pass == Pass.Need)
                    anyStock = true;

                var pick = Choose(entry, pass, party, me.Position, now, out var why);
                if (pick is not { } member)
                {
                    if (why.Length > 0 && pass == Pass.TopUp)
                        notes.Add($"{entry.Name}: {why}");
                    continue;
                }

                if (slot >= 0)
                {
                    // Already in a slot: wait for it rather than reloading. A second copy out of
                    // the holster shares the same recast, so loading again would buy nothing and
                    // would stamp on the other slot for the privilege.
                    if (!ready)
                    {
                        if (pass == Pass.Need)
                        {
                            notes.Add(recharging
                                ? $"{entry.Name} is recharging ({loaded.CooldownRemaining:F0}s)"
                                : $"{entry.Name} has no charges left");
                        }
                        continue;
                    }

                    if (Fire(slot, entry, member, now))
                        return;

                    continue;
                }

                // Not in either slot: load it, and fire on a later tick once the slot says so.
                var index = IndexOf(holster, row);
                if (index < 0)
                    continue;

                var driveSlot = Math.Clamp(_config.PartySupportSlot, 0, DutyActions.SlotCount - 1);
                if (!FieldState.UseFromHolster((uint)index, (uint)driveSlot))
                {
                    // Spend the window on a refusal too. The gap gate is the only thing standing
                    // between "the game will not load this" and the same call being made on every
                    // single controller tick for as long as the task runs.
                    notes.Add($"the game refused to load {entry.Name}");
                    _lastActionMs = now;
                    continue;
                }

                _phase = Phase.WaitingForLoad;
                _pendingRow = row;
                _pendingActionId = entry.ActionId;
                _pendingTargetId = member.Id;
                _pendingTargetName = member.Name;
                _pendingPass = pass;
                _loadIssuedMs = now;
                Status = $"Loading {entry.Name} for {member.Name}.";
                return;
            }
        }

        // NOTHING FIRED THIS TICK. The two reasons are completely different and only one of them
        // ends the task: out of stock is terminal, everybody-covered is the task doing its job.
        if (!anyStock)
        {
            Stop(Applied > 0
                ? $"Party support finished - out of actions after {Applied} cast{(Applied == 1 ? "" : "s")}."
                : "Party support stopped - none of the chosen actions are in the holster.");
            return;
        }

        Status = notes.Count > 0
            ? $"Holding - {string.Join("; ", notes)}."
            : "Holding - everyone is covered.";
    }

    // ------------------------------------------------------------- choosing who

    /// <summary>
    /// Pick who should get this action, or nobody.
    ///
    /// <paramref name="why"/> is filled in only when the answer is interesting - "the duration is
    /// unknown so nobody can be topped up" is worth saying; "everyone already has it" is the
    /// ordinary case and says itself.
    /// </summary>
    private PartyView.Member? Choose(
        LostActionCatalog.Entry entry,
        Pass pass,
        List<PartyView.Member> party,
        Vector3 from,
        long now,
        out string why)
    {
        why = string.Empty;

        // A heal is only ever an unmet need - there is no such thing as topping up someone's HP
        // towards full, so it simply does not appear in the second sweep.
        if (entry.IsPartyHeal)
            return pass == Pass.Need ? ChooseWounded(entry, party, from, now) : null;

        return pass == Pass.Need
            ? ChooseUnbuffed(entry, party, from, now)
            : ChooseNearlyExpired(entry, party, from, now, out why);
    }

    /// <summary>Lowest current HP first, among those actually hurt and actually reachable.</summary>
    private PartyView.Member? ChooseWounded(
        LostActionCatalog.Entry entry,
        List<PartyView.Member> party,
        Vector3 from,
        long now)
    {
        PartyView.Member? best = null;
        var bestFraction = float.MaxValue;

        foreach (var m in party)
        {
            // A corpse needs a raise, and this task deliberately does not do raises.
            if (m.IsDead)
                continue;

            if (m.HpFraction >= _config.PartyHealBelowFraction)
                continue;

            if (!InRange(from, m, entry.Range) || IsRefused(m.Id, now))
                continue;

            if (m.HpFraction >= bestFraction)
                continue;

            best = m;
            bestFraction = m.HpFraction;
        }

        return best;
    }

    /// <summary>
    /// The first sweep: anyone who does not have the buff at all.
    ///
    /// Party-list order, which is the game's own order - but STARTED AT THIS BOX'S OWN POSITION in
    /// it and wrapped, which is the opposite correction to the one the first version of this made.
    /// Plain party order is perfectly deterministic, and that is exactly the problem in a multibox
    /// group: every box runs the same algorithm over the same party, so all of them pick the same
    /// first unbuffed member and eight charges buy one buff. Rotating the start point keeps each
    /// box deterministic and reproducible while systematically sending different boxes at
    /// different people.
    /// </summary>
    private PartyView.Member? ChooseUnbuffed(
        LostActionCatalog.Entry entry,
        List<PartyView.Member> party,
        Vector3 from,
        long now)
    {
        var start = SelfIndex(party);
        for (var i = 0; i < party.Count; i++)
        {
            var m = party[(start + i) % party.Count];

            if (m.IsDead || !InRange(from, m, entry.Range) || IsRefused(m.Id, now))
                continue;

            if (!PartyView.HasStatus(m.Chara, entry.StatusId, out _))
                return m;
        }

        return null;
    }

    /// <summary>Where this box sits in the party list, so the sweep can start there. 0 if unknown.</summary>
    private static int SelfIndex(List<PartyView.Member> party)
    {
        for (var i = 0; i < party.Count; i++)
            if (party[i].IsSelf)
                return i;
        return 0;
    }

    /// <summary>
    /// The second sweep: whoever is furthest through the buff, provided they are past the
    /// threshold at all.
    ///
    /// Most-expired-first rather than party order, because unlike the first sweep these candidates
    /// are not equivalent - the one with eleven seconds left needs it before the one with a
    /// hundred, and the gap between two casts is long enough for that to matter.
    /// </summary>
    private PartyView.Member? ChooseNearlyExpired(
        LostActionCatalog.Entry entry,
        List<PartyView.Member> party,
        Vector3 from,
        long now,
        out string why)
    {
        why = string.Empty;

        // No total, no percentage. Refusing to compute one is the point: a made-up duration would
        // either re-apply a fresh buff or never top one up, and both spend or waste silently.
        if (!entry.HasDuration)
        {
            why = "no duration in the game data, so nothing is topped up";
            return null;
        }

        var threshold = entry.DurationSeconds * Math.Clamp(_config.PartyBuffRefreshFraction, 0.01f, 0.9f);

        PartyView.Member? best = null;
        var least = float.MaxValue;

        foreach (var m in party)
        {
            if (m.IsDead || !InRange(from, m, entry.Range) || IsRefused(m.Id, now))
                continue;

            if (!PartyView.HasStatus(m.Chara, entry.StatusId, out var remaining))
                continue;

            if (remaining >= threshold || remaining >= least)
                continue;

            best = m;
            least = remaining;
        }

        return best;
    }

    /// <summary>
    /// Is this member currently passed over because the game just refused to target them?
    /// </summary>
    private bool IsRefused(ulong id, long now) =>
        _refusedUntil.TryGetValue(id, out var until) && now < until;

    private static bool InRange(Vector3 from, PartyView.Member m, float range)
    {
        if (range <= 0f)
            return true; // a self-only action has no reach to fail

        try { return Vector3.Distance(from, m.Chara.Position) <= range; }
        catch { return false; }
    }

    // ---------------------------------------------------------------- applying

    /// <summary>
    /// Aim and press.
    ///
    /// Three gates, and each one is guarding against a different tick-sized race: the member must
    /// still be IN THE PARTY (re-derived live, not read off the snapshot the decision came from),
    /// the game must have ACCEPTED the target we set, and the duty slot must still hold the action
    /// we think it does - which DutyActions.Press checks for us when handed the expected id.
    /// </summary>
    private bool Fire(int slot, LostActionCatalog.Entry entry, PartyView.Member member, long now)
    {
        // EVERY EXIT BELOW SPENDS THE WINDOW, refusals included. Without that, a target the game
        // keeps declining would be retried on every single tick.
        _lastActionMs = now;

        // The last gate, and re-derived live rather than read off the snapshot the decision came
        // from: a tick is long enough to leave a party in.
        if (!PartyView.IsInParty(member.Id))
        {
            Status = $"{member.Name} is no longer in the party - nothing fired.";
            return false;
        }

        // NOTHING IS SELECTED, AND THAT IS THE POINT. The cast names its target outright rather
        // than borrowing the hard target to aim - see DutyActions.PressAt for why aiming by
        // selection is wrong twice over (it loses to a soft target, and the selection is shared
        // with CombatApproach and CombatDirector, which read and clear it every tick).
        var press = DutyActions.PressAt(slot, entry.ActionId, member.Id, member.Name);

        if (!press.Fired)
        {
            // A refusal aimed at THIS member is remembered, not merely reported: otherwise they
            // are first among the unbuffed again next window, forever, and everybody behind them
            // in the sweep is starved. A refusal about the ACTION - recharging, empty slot, dead
            // caster - is not their fault and must not pass them over, which is why the flag comes
            // back from the press rather than being sniffed out of its message.
            if (press.TargetRefused)
                _refusedUntil[member.Id] = now + RefusalCooldownMs;

            Status = press.Message;
            return false;
        }

        Applied++;
        Status = $"{entry.Name} on {member.Name}.";
        return true;
    }

    /// <summary>Second half of a load: fire once the slot reports the action we asked for.</summary>
    private void FinishLoad(long now)
    {
        var driveSlot = Math.Clamp(_config.PartySupportSlot, 0, DutyActions.SlotCount - 1);
        var name = _catalog.Name(_pendingRow);

        if (DutyActions.Read(driveSlot).ActionId != _pendingActionId)
        {
            if (now - _loadIssuedMs < LoadTimeoutMs)
                return;

            Status = $"Duty slot {driveSlot + 1} never came up as {name} - nothing fired.";
            _lastActionMs = now;
            Abandon();
            return;
        }

        if (!_catalog.TryGet(_pendingRow, out var entry))
        {
            Abandon();
            return;
        }

        // The chosen member is re-resolved rather than held across the load: two and a half seconds
        // is long enough for them to die, heal up, or walk out of range, and the whole point of
        // choosing was that it reflected the state at the time.
        var party = PartyView.Snapshot();
        PartyView.Member? still = null;
        foreach (var m in party)
            if (m.Id == _pendingTargetId)
            {
                still = m;
                break;
            }

        if (still is not { } member)
        {
            Status = $"{_pendingTargetName} is no longer reachable - {name} was loaded but not fired.";
            _lastActionMs = now;
            Abandon();
            return;
        }

        // STILL THE RIGHT PERSON FOR THE RIGHT REASON. Two and a half seconds is long enough for
        // somebody else to have buffed them, or for them to have been healed - so the same test
        // that chose them is re-run against the same sweep, and a member who no longer qualifies is
        // dropped rather than having a charge spent on them out of momentum.
        var me = Svc.Objects.LocalPlayer;
        var stillWanted = me != null
            && Choose(entry, _pendingPass, [member], me.Position, now, out _) is not null;

        if (!stillWanted)
        {
            Status = $"{member.Name} no longer needs {name} - loaded but not fired.";
            _lastActionMs = now;
            Abandon();
            return;
        }

        Fire(driveSlot, entry, member, now);
        Abandon();
    }

    private void Abandon()
    {
        _phase = Phase.Idle;
        _pendingRow = 0;
        _pendingActionId = 0;
        _pendingTargetId = 0;
        _pendingTargetName = string.Empty;
        _pendingPass = Pass.Need;
        _loadIssuedMs = 0;
    }

    private static int SlotHolding(uint actionId)
    {
        for (var i = 0; i < DutyActions.SlotCount; i++)
            if (DutyActions.Read(i).ActionId == actionId)
                return i;
        return -1;
    }

    private static int IndexOf(byte[] holster, byte row)
    {
        for (var i = 0; i < holster.Length; i++)
            if (holster[i] == row)
                return i;
        return -1;
    }
}
