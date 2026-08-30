using System;
using System.Collections.Generic;
using ECommons.DalamudServices;
using Lumina.Excel.Sheets;

namespace BozjaBuddyReborn.Game;

/// <summary>
/// Which status effect a Lost Action or Essence leaves on you, so that re-applying one you
/// already have can be refused instead of spending the charge.
///
/// WHY THIS IS DERIVED RATHER THAN READ. The obvious column is Action.StatusGainSelf, and it is
/// empty for 98 of the 99 Lost Actions (only Lost Stealth fills it in) - so the sheet does not
/// answer this question directly and something has to bridge the gap. Two bridges, in this order,
/// and the difference between them matters:
///
///   1. THE ESSENCES ARE A TABLE, because they cannot be derived from anything. "Essence of the
///      Aetherweaver" grants "Spirit of the Aetherweaver" - different words, no shared icon (the
///      action icons are 646xx, the status icons 216xxx), no shared row-id arithmetic that holds
///      across all three families. What IS true, and is what <see cref="EssenceRuns"/> encodes, is
///      that each family is contiguous and in the same order in both sheets.
///
///   2. EVERYTHING ELSE IS AN EXACT NAME MATCH, which is better than it sounds. "Lost Protect" the
///      action and "Lost Protect" the status are the same string in whatever language the client
///      is running, because both sheets are localised together - so equality holds in every locale
///      in a way that any "strip this English prefix" rule would not. It is required to be exactly
///      one status row of that name, and the action is required to be self-targetable and NOT
///      hostile-targetable, which is what throws out the three matches that are wrong:
///        - Mimic -> status 1056 "Mimic", a CHOCOBO RACING status. A pure name collision, and the
///          reason uniqueness alone is not enough of a filter.
///        - Lost Incense and Lost Rend Armor, whose statuses land on the enemy, not on you.
///      That leaves 27 self-buffs matched with no hand-written ids at all: Lost Protect/Shell and
///      their IIs, Manawall, Swift, Bravery, Stealth, Spellforge, Steelsting, both Fonts, all six
///      Banners, Aethershield, Dervish, Burst, Rampage, Chainspell, Bubble, Excellence, Blood Rage.
///
/// WHAT IS NOT COVERED, and is left uncovered on purpose. An entry with no status here is simply
/// never refused - the check costs it nothing and it behaves exactly as it did before. That is the
/// right direction to fail: a missing entry wastes at worst one charge, while a WRONG entry would
/// silently prevent an action from ever being used.
/// </summary>
public static class LostActionStatuses
{
    /// <summary>
    /// The Essence families: MYCTemporaryItem rows FirstRow..LastRow map, in order, onto Status
    /// rows starting at FirstStatus.
    ///
    /// Read out of the sheets rather than off a wiki:
    ///   rows 41-55  Essence of the Aetherweaver .. Templar        -> 2311-2325 Spirit of the ...
    ///   rows 56-70  Deep Essence of the same fifteen, same order  -> 2311-2325, the SAME statuses
    ///   rows 73-78  Pure Essence of the Gambler .. Divine         -> 2434-2439 Spirit of the ...
    /// The middle line is the one worth stating twice: there is no separate "Deep Spirit of the X"
    /// status row anywhere in the sheet, and the fifteen Deep Essences carry the same fifteen
    /// status names as the plain ones. See <see cref="SharesStatusWithDeeper"/> for what that costs.
    /// </summary>
    private static readonly (byte FirstRow, byte LastRow, uint FirstStatus)[] EssenceRuns =
    [
        (41, 55, 2311),
        (56, 70, 2311),
        (73, 78, 2434),
    ];

    /// <summary>
    /// The two entries that grant Reraise. NAME-INFERRED, not matched: "Resistance Reraiser" and
    /// "Lost Reraise" both plainly grant status 2355 "Reraise", but neither name is equal to it, so
    /// the rule above will not find them and they are asserted here instead. Kept separate from the
    /// Essence runs because the confidence is different, and both are listed in the README seams.
    /// </summary>
    private static readonly (byte Row, uint Status)[] Inferred =
    [
        (36, 2355),   // Resistance Reraiser (item)
        (89, 2355),   // Lost Reraise (action)

        // Lost Reflect. Its status is named just "Reflect", which the sheet uses twice (518 and
        // 2337), so the uniqueness rule correctly declines to guess. 2337 is the right one for a
        // reason that is structural rather than textual: every other Lost Action status sits in the
        // contiguous 2326-2356 block, and 2337 is inside it while 518 is a Heavensward-era row.
        (11, 2337),
    ];

    // NOT MAPPED, and left that way: Lost Stoneskin (row 12). The sheet has four unrelated
    // "Stoneskin" rows (151, 152, 153, 3422) and not one of them is in the Bozja block, so there is
    // no honest way to pick. The cost is that Lost Stoneskin is excluded from the party-support
    // list rather than being applied blindly over itself.

    /// <summary>
    /// True when this row is an Essence - the entries that get their own loadout dropdown, since
    /// exactly one of them can be running at a time and they are the expensive mistake to repeat.
    /// </summary>
    public static bool IsEssence(byte row)
    {
        foreach (var (first, last, _) in EssenceRuns)
            if (row >= first && row <= last)
                return true;
        return false;
    }

    /// <summary>
    /// True when this row's status is shared with a stronger version of the same Essence, so
    /// "you already have it" cannot tell an upgrade from a repeat.
    ///
    /// Only the fifteen plain Essences (41-55) and their Deep counterparts (56-70) are affected,
    /// because they share status rows 2311-2325. Using a Deep Essence over a plain one of the same
    /// name is a real upgrade that this check will nonetheless refuse - which is the conservative
    /// way to be wrong, and is called out in the UI rather than hidden.
    /// </summary>
    public static bool SharesStatusWithDeeper(byte row) => row is >= 41 and <= 70;

    /// <summary>
    /// Build the row -> status map for every Lost Action that leaves a status on YOU.
    /// Rows absent from the result have no known status and are never refused.
    /// </summary>
    public static Dictionary<byte, uint> Resolve(
        Func<byte, (uint ActionId, string Name)?> entry,
        uint maxRow)
    {
        var map = new Dictionary<byte, uint>();

        var statuses = Svc.Data.GetExcelSheet<Status>();
        var actions = Svc.Data.GetExcelSheet<Lumina.Excel.Sheets.Action>();
        if (statuses == null || actions == null)
            return map;

        // ---- 1. the Essence families, and the two inferred Reraise entries.
        foreach (var (first, last, firstStatus) in EssenceRuns)
        {
            for (byte row = first; row <= last; row++)
            {
                var id = firstStatus + (uint)(row - first);

                // A run that has drifted - a patch inserting a row into either sheet - shows up as
                // a status id that resolves to nothing. Drop that entry rather than assert a
                // mapping onto whatever now sits at the id.
                var s = statuses.GetRowOrDefault(id);
                if (s == null || s.Value.Name.ExtractText().Length == 0)
                    continue;

                map[row] = id;
            }
        }

        foreach (var (row, id) in Inferred)
        {
            var s = statuses.GetRowOrDefault(id);
            if (s != null && s.Value.Name.ExtractText().Length > 0)
                map[row] = id;
        }

        // ---- 2. exact name equality, unique, self-targeted and not hostile-targeted.
        var byName = new Dictionary<string, uint>(StringComparer.Ordinal);
        var ambiguous = new HashSet<string>(StringComparer.Ordinal);
        foreach (var s in statuses)
        {
            var n = s.Name.ExtractText();
            if (n.Length == 0)
                continue;
            if (!byName.TryAdd(n, s.RowId))
                ambiguous.Add(n);
        }

        for (byte row = 1; row < maxRow; row++)
        {
            if (map.ContainsKey(row))
                continue;

            var e = entry(row);
            if (e is not { } hit || hit.ActionId == 0 || hit.Name.Length == 0)
                continue;

            if (ambiguous.Contains(hit.Name) || !byName.TryGetValue(hit.Name, out var statusId))
                continue;

            var a = actions.GetRowOrDefault(hit.ActionId);
            if (a == null)
                continue;

            // The filter that throws out Mimic (a chocobo-racing status of the same name) and the
            // two actions whose same-named status lands on the enemy instead of on you.
            if (!a.Value.CanTargetSelf || a.Value.CanTargetHostile)
                continue;

            map[row] = statusId;
        }

        return map;
    }

    /// <summary>Display name for a status id, or an empty string. Cached.</summary>
    public static string Name(uint statusId)
    {
        if (statusId == 0)
            return string.Empty;

        if (_names.TryGetValue(statusId, out var hit))
            return hit;

        try
        {
            var row = Svc.Data.GetExcelSheet<Status>()?.GetRowOrDefault(statusId);
            var name = row?.Name.ExtractText() ?? string.Empty;
            if (name.Length > 0)
                _names[statusId] = name;
            return name;
        }
        catch { return string.Empty; }
    }

    private static readonly Dictionary<uint, string> _names = [];

    /// <summary>
    /// Is this status on the local player right now, and for how much longer?
    ///
    /// Framework thread only - StatusList is a live read. A status id of 0 always answers false,
    /// which is what makes "no known status" mean "never refused" everywhere this is called.
    /// </summary>
    public static bool IsActive(uint statusId, out float remaining)
    {
        remaining = 0f;

        if (statusId == 0)
            return false;

        try
        {
            var me = Svc.Objects.LocalPlayer;
            if (me == null)
                return false;

            foreach (var s in me.StatusList)
            {
                if (s == null || s.StatusId != statusId)
                    continue;

                // Permanent statuses report a negative remaining time; report them as up with no
                // clock rather than as an expiry in the past.
                remaining = s.RemainingTime > 0f ? s.RemainingTime : 0f;
                return true;
            }
        }
        catch { return false; }

        return false;
    }

    public static void InvalidateNames() => _names.Clear();
}
