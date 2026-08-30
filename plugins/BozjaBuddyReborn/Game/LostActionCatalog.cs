using System;
using System.Collections.Generic;
using ECommons.DalamudServices;
using Lumina.Excel.Sheets;

namespace BozjaBuddyReborn.Game;

/// <summary>
/// Names, holster weights and KIND for the Lost Actions, read from the MYCTemporaryItem sheet.
///
/// The holster (BozjaState.HolsterActions) stores MYCTemporaryItem *row ids*, not Action ids
/// nor Item ids - so this sheet is the only correct way to name a holster slot.
///
/// Columns (EXDSchema latest): Action -> Action sheet, Category, Type, Max, Weight, Order.
///
/// WHY Type MATTERS, having been ignored until 1.0.21.0. PublicContentBozja.UseFromHolster is
/// documented as "use lost action from holster INTO specified duty action slot (slot is ignored
/// for items, which are used directly)" - one call with two completely different behaviours, and
/// Type is what says which one you get. Read out of the live sheet, it is exactly two values:
///   1 = ACTION. 33 rows - Lost Cure, Lost Manawall, Lost Font of Power, the Banners, and so on.
///       UseFromHolster LOADS these into a duty slot and stops. Something has to press the slot.
///   2 = ITEM. 66 rows - and this is much wider than the potions it is easy to assume: every
///       Essence and Deep/Pure Essence, Dynamis Dice, Resistance Phoenix, Reraiser, the potion,
///       ether and medi kits, Lodestone, Light Curtain, Resistance Elixir. UseFromHolster
///       CONSUMES these outright; pressing a duty slot afterwards would be pressing whatever the
///       slot already held.
/// The split is corroborated by Category: every Type 2 row and no Type 1 row sits in
/// MYCTemporaryItemUICategory row 7, "Item-related".
/// </summary>
public sealed class LostActionCatalog
{
    /// <summary>MYCTemporaryItem has 100 defined rows (0 is the empty sentinel).</summary>
    public const uint MaxRow = 100;

    /// <summary>MYCTemporaryItem.Type for an entry that loads into a duty slot to be pressed.</summary>
    public const byte ActionType = 1;

    /// <summary>MYCTemporaryItem.Type for an entry the game consumes straight out of the holster.</summary>
    public const byte ItemType = 2;

    /// <summary>
    /// MYCTemporaryItem rows that RESTORE HP on a party member - the Lost Cure family.
    ///
    /// An explicit list because nothing in the sheets says "this is a heal": there is no healing
    /// flag, ActionCategory only distinguishes Spell from Ability, and StatusGainSelf is empty for
    /// these. What IS read from the sheet is everything around it - all four are CanTargetParty
    /// with Range 30 and grant no status - and that description also fits Lost Reflect and Lost
    /// Stoneskin, which is exactly why the heals are named rather than inferred.
    /// Lost Full Cure (97) is deliberately absent: it is CanTargetSelf only, a 15y burst centred on
    /// the caster, so there is no target to choose.
    /// </summary>
    private static readonly byte[] HealRows = [26, 27, 28, 29];

    public readonly record struct Entry(
        byte RowId,
        uint ActionId,
        string Name,
        byte Weight,
        byte MaxHeld,
        byte Type,
        uint StatusId,
        bool IsEssence,
        bool CanTargetParty,
        bool TargetsDead,
        float Range,
        float DurationSeconds)
    {
        /// <summary>Consumed directly by UseFromHolster - no duty slot involved. Essences and kits.</summary>
        public bool IsItem => Type == ItemType;

        /// <summary>Loaded into a duty slot by UseFromHolster, and pressable only once it lands.</summary>
        public bool IsAction => Type == ActionType;

        /// <summary>
        /// True when using this while <see cref="StatusId"/> is already up would spend the charge
        /// for nothing. Zero means "no known status", which is deliberately never refused.
        /// </summary>
        public bool HasStatus => StatusId != 0;

        /// <summary>
        /// A raise. Read from Action.DeadTargetBehaviour, which is nonzero for exactly three rows -
        /// Lost Arise, Lost Sacrifice and Resistance Phoenix - and zero for every other Lost Action.
        /// </summary>
        public bool IsRaise => TargetsDead;

        /// <summary>Restores HP on a party member: the Lost Cure family.</summary>
        public bool IsPartyHeal => CanTargetParty && !TargetsDead && Array.IndexOf(HealRows, RowId) >= 0;

        /// <summary>
        /// A single-target buff you can put on someone else, AND whose status we can name.
        ///
        /// The status requirement is not decoration - it is what a "do not re-apply what is already
        /// up" task is FOR, so an action we cannot check that rule against has no business being in
        /// one. It excludes exactly two party-targetable rows: Mimic, which copies a holster rather
        /// than applying anything, and Lost Stoneskin, whose status could not be pinned down (the
        /// sheet has four unrelated "Stoneskin" rows and none of them sits in the Bozja block).
        /// </summary>
        public bool IsPartyBuff => CanTargetParty && !TargetsDead && HasStatus && !IsPartyHeal;

        /// <summary>Anything this feature can aim at a party member.</summary>
        public bool IsPartySupport => IsPartyBuff || IsPartyHeal;

        /// <summary>
        /// True when we know how long the buff runs, and so can say how far through one is.
        /// False for the Essences (which run until you die) and for every instant.
        /// </summary>
        public bool HasDuration => DurationSeconds > 0f;
    }

    private readonly Dictionary<byte, Entry> _byRow = [];
    private bool _resolved;

    /// <summary>Every Lost Action, ordered by row id.</summary>
    public IEnumerable<Entry> All
    {
        get
        {
            Ensure();
            for (byte i = 1; i < MaxRow; i++)
                if (_byRow.TryGetValue(i, out var e))
                    yield return e;
        }
    }

    public bool TryGet(byte rowId, out Entry entry)
    {
        Ensure();
        return _byRow.TryGetValue(rowId, out entry);
    }

    /// <summary>Display name for a holster slot value, or a placeholder when unresolved.</summary>
    public string Name(byte rowId)
    {
        if (rowId == 0)
            return "(empty)";
        return TryGet(rowId, out var e) && e.Name.Length > 0 ? e.Name : $"Lost Action #{rowId}";
    }

    /// <summary>Holster weight of one copy (the holster capacity is weight-based, not slot-based).</summary>
    public byte Weight(byte rowId) => TryGet(rowId, out var e) ? e.Weight : (byte)0;

    /// <summary>
    /// True when this row is consumed straight out of the holster rather than loaded into a duty
    /// slot. An unresolved row answers false, which is the safe direction: the caller then has to
    /// establish what it is rather than consuming it on the strength of a lookup that failed.
    /// </summary>
    public bool IsItem(byte rowId) => TryGet(rowId, out var e) && e.IsItem;

    /// <summary>
    /// Every entry that can go in a loadout's Essence slot - the 36 Essence rows, plain, Deep and
    /// Pure. Only one Essence can be running at a time, which is why they get a slot of their own
    /// rather than sharing the auto-use list.
    /// </summary>
    public IEnumerable<Entry> Essences
    {
        get
        {
            foreach (var e in All)
                if (e.IsEssence)
                    yield return e;
        }
    }

    /// <summary>Every entry that can be loaded into a duty slot.</summary>
    public IEnumerable<Entry> DutyActions
    {
        get
        {
            foreach (var e in All)
                if (e.IsAction)
                    yield return e;
        }
    }

    /// <summary>
    /// Every entry that can be aimed at a party member: the single-target buffs whose status we can
    /// name, plus the Lost Cure family. Raises are excluded - see Entry.IsPartyBuff.
    /// </summary>
    public IEnumerable<Entry> PartySupport
    {
        get
        {
            foreach (var e in All)
                if (e.IsPartySupport)
                    yield return e;
        }
    }

    public void Invalidate()
    {
        _byRow.Clear();
        _resolved = false;
        LostActionStatuses.InvalidateNames();
    }

    private void Ensure()
    {
        if (_resolved)
            return;

        try
        {
            var temp = Svc.Data.GetExcelSheet<MYCTemporaryItem>();
            var actions = Svc.Data.GetExcelSheet<Lumina.Excel.Sheets.Action>();
            if (temp == null || actions == null)
                return;

            for (uint i = 1; i < MaxRow; i++)
            {
                var row = temp.GetRowOrDefault(i);
                if (row == null)
                    continue;

                var r = row.Value;
                var actionId = r.Action.RowId;
                if (actionId == 0)
                    continue;

                var name = string.Empty;
                var canTargetParty = false;
                var targetsDead = false;
                var range = 0;
                var action = actions.GetRowOrDefault(actionId);
                if (action != null)
                {
                    name = action.Value.Name.ExtractText();
                    canTargetParty = action.Value.CanTargetParty;
                    range = Math.Max(0, (int)action.Value.Range);

                    // DeadTargetBehaviour is the sheet's own "this one is aimed at a corpse" flag,
                    // and it is nonzero for exactly the three raises.
                    targetsDead = action.Value.DeadTargetBehaviour != 0;
                }

                _byRow[(byte)i] = new Entry(
                    RowId: (byte)i,
                    ActionId: actionId,
                    Name: name,
                    Weight: r.Weight,
                    MaxHeld: r.Max,
                    Type: r.Type,
                    StatusId: 0,
                    IsEssence: LostActionStatuses.IsEssence((byte)i),
                    CanTargetParty: canTargetParty,
                    TargetsDead: targetsDead,
                    Range: range,
                    DurationSeconds: LostActionDurations.Seconds(actionId));
            }

            // Second pass, because the status derivation needs every row's action id and name to
            // already be resolved - it matches action names against the Status sheet.
            var statuses = LostActionStatuses.Resolve(
                row => _byRow.TryGetValue(row, out var e) ? (e.ActionId, e.Name) : null,
                MaxRow);

            foreach (var (row, statusId) in statuses)
                if (_byRow.TryGetValue(row, out var e))
                    _byRow[row] = e with { StatusId = statusId };
        }
        catch
        {
            return;
        }

        if (_byRow.Count > 0)
            _resolved = true;
    }
}
