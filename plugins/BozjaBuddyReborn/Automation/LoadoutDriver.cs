using System;
using System.Collections.Generic;
using System.Globalization;
using BozjaBuddyReborn.Game;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;

namespace BozjaBuddyReborn.Automation;

/// <summary>
/// A named pair of Lost Actions to keep in the two duty slots, plus the Essence to be running.
///
/// Stored as MYCTemporaryItem row ids - the same ids the holster uses - rather than Action ids,
/// because the holster is what a loadout has to be satisfiable FROM, and the row id is the only
/// thing that identifies a Lost Action in it.
/// </summary>
[Serializable]
public sealed class Loadout
{
    public string Name { get; set; } = "New loadout";

    /// <summary>MYCTemporaryItem row id for duty slot 1, or 0 to leave the slot alone.</summary>
    public byte Slot0 { get; set; }

    /// <summary>MYCTemporaryItem row id for duty slot 2, or 0 to leave the slot alone.</summary>
    public byte Slot1 { get; set; }

    /// <summary>
    /// MYCTemporaryItem row id of the Essence to be running, or 0 for "do not touch my Essence".
    ///
    /// NOT A SLOT, unlike the two above, and the difference is the whole reason it is a separate
    /// field rather than a third duty slot. An Essence is an item: the game consumes it out of the
    /// holster and you carry the buff for the next half hour, so "applying" one SPENDS it. Exactly
    /// one can be running at a time, which is what makes it a loadout's business at all - it is
    /// part of how a box is set up for a fight in the same way its two duty actions are.
    /// </summary>
    public byte Essence { get; set; }

    /// <summary>
    /// Which box this loadout is for: a box name, <see cref="AllBoxes"/> for every box, or empty
    /// for this one.
    ///
    /// Saved ON the loadout rather than chosen at the moment of applying, because that is what a
    /// loadout IS in a multibox group - "the tank box runs Manawall and Bravery" is a fact about
    /// that box, not a decision to re-make on every press. A name that is not connected is
    /// reported rather than sent: a BoxCommand addressed to nobody is ignored by every box, which
    /// is the safe failure but a completely silent one.
    /// </summary>
    public string Target { get; set; } = string.Empty;

    /// <summary>Target value meaning "every box". Matches BoxCommand.All.</summary>
    public const string AllBoxes = "*";

    /// <summary>True when this loadout is addressed at a specific peer rather than here or all.</summary>
    public bool TargetsPeer => Target.Length > 0 && Target != AllBoxes;

    public string Encode() => string.Join(',',
        Slot0.ToString(CultureInfo.InvariantCulture),
        Slot1.ToString(CultureInfo.InvariantCulture),
        Essence.ToString(CultureInfo.InvariantCulture));

    /// <summary>
    /// Read a loadout argument off the wire.
    ///
    /// The Essence field is optional so that a two-field argument - the shape every build through
    /// 1.0.21.0 sent - still decodes, as "leave the Essence alone". Boxes in a group are not
    /// guaranteed to be on the same build at the same moment, and the failure that matters here is
    /// an instruction silently doing nothing, not one arriving a field short.
    /// </summary>
    public static bool TryDecode(string arg, out byte slot0, out byte slot1, out byte essence)
    {
        slot0 = slot1 = essence = 0;
        var p = arg.Split(',');
        if (p.Length < 2
            || !byte.TryParse(p[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out slot0)
            || !byte.TryParse(p[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out slot1))
            return false;

        if (p.Length > 2 && !byte.TryParse(p[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out essence))
            essence = 0;

        return true;
    }
}

/// <summary>
/// Applies a loadout to this box's two duty-action slots.
///
/// HOW A SLOT IS SET. RaptureHotbarModule.SetDutyActionSlot(index, actionId) is the game's own
/// setter for the duty bar and takes an ACTION id, while a loadout stores MYCTemporaryItem ROW
/// ids - so the row is resolved through the catalog to the action it grants before being set.
///
/// AND WHY THE HOLSTER IS CHECKED FIRST. Setting a slot to an action you do not hold is a
/// silently useless state: the bar shows it, nothing can be pressed, and the box looks configured
/// while being unable to act. Lost Actions are a farmed, finite resource and the boxes routinely
/// hold different stock, so a loadout pushed to four clients will legitimately be unsatisfiable on
/// some of them. Refusing per slot and reporting which is the honest behaviour - the alternative
/// is an operator who believes the group is loaded when it is not.
///
/// THE ESSENCE IS THE ONE PART THAT SPENDS. The two duty slots only move an icon onto a bar; the
/// Essence is an item, so applying it consumes a copy and the buff runs for the next half hour.
/// That makes "apply to all" a real cost across a group, and it makes the status check the point
/// rather than a nicety: an Essence re-applied over itself is a copy burned for nothing, and on
/// four boxes on a habit it is a farming session. So the Essence is used only when the status it
/// grants is NOT already running, and a refusal says which Essence is up and for how long.
/// </summary>
public sealed class LoadoutDriver(LostActionCatalog catalog)
{
    private readonly LostActionCatalog _catalog = catalog;

    /// <summary>What the last apply did, for the UI and for the operator's box to read back.</summary>
    public string LastResult { get; private set; } = "nothing applied yet";

    /// <summary>
    /// Put a loadout into the duty slots.
    /// </summary>
    /// <returns>True when every requested slot was set.</returns>
    public unsafe bool Apply(byte slot0Row, byte slot1Row, byte essenceRow)
    {
        if (!Svc.Framework.IsInFrameworkUpdateThread)
        {
            LastResult = "refused: not on the framework thread";
            return false;
        }

        var holster = FieldState.Holster();
        if (holster.Length == 0)
        {
            LastResult = "no holster (not in a field operation)";
            return false;
        }

        var held = new HashSet<byte>();
        foreach (var h in holster)
            if (h != 0)
                held.Add(h);

        var notes = new List<string>();
        var ok = true;

        ok &= ApplyOne(0, slot0Row, held, notes);
        ok &= ApplyOne(1, slot1Row, held, notes);
        ok &= ApplyEssence(essenceRow, holster, notes);

        LastResult = notes.Count == 0 ? "nothing to do" : string.Join("; ", notes);
        return ok;
    }

    /// <summary>
    /// Use the loadout's Essence, unless its buff is already running.
    ///
    /// Refusing an Essence you already have is reported as a success, not a failure - the loadout
    /// asked for a state and the box is already in it. "Apply to all" going green across a group
    /// where half of them were already buffed is the correct answer, and treating it as a failure
    /// would train the operator to press it again.
    /// </summary>
    private bool ApplyEssence(byte row, byte[] holster, List<string> notes)
    {
        if (row == 0)
            return true; // deliberately leave whatever Essence is running alone

        var name = _catalog.Name(row);

        if (!_catalog.TryGet(row, out var entry) || !entry.IsEssence)
        {
            notes.Add($"essence: {name} is not an Essence");
            return false;
        }

        if (LostActionStatuses.IsActive(entry.StatusId, out var remaining))
        {
            var upFor = remaining > 0f
                ? $" ({FormatRemaining(remaining)} left)"
                : string.Empty;
            notes.Add($"essence: already running {LostActionStatuses.Name(entry.StatusId)}{upFor} - not spent");
            return true;
        }

        // The index, not just "do we hold it" - UseFromHolster addresses a holster POSITION, so
        // the scan that finds it is the same one that answers whether we have it at all.
        var index = -1;
        for (var i = 0; i < holster.Length; i++)
            if (holster[i] == row)
            {
                index = i;
                break;
            }

        if (index < 0)
        {
            notes.Add($"essence: {name} not in the holster");
            return false;
        }

        // An Essence is item-type, so this call IS the use - the duty-slot argument is ignored and
        // nothing needs pressing afterwards. See HolsterDriver for the other half of that split.
        if (!FieldState.UseFromHolster((uint)index, 0))
        {
            notes.Add($"essence: the game refused {name}");
            return false;
        }

        notes.Add($"essence: used {name}");
        Svc.Log.Information($"[BozjaBuddyReborn] Loadout used {name}.");
        return true;
    }

    private static string FormatRemaining(float seconds)
    {
        var total = (int)seconds;
        return total >= 60 ? $"{total / 60}m{total % 60:00}s" : $"{total}s";
    }

    private unsafe bool ApplyOne(uint slot, byte row, HashSet<byte> held, List<string> notes)
    {
        if (row == 0)
            return true; // deliberately leave this slot as it is

        var name = _catalog.Name(row);

        if (!held.Contains(row))
        {
            notes.Add($"slot {slot + 1}: {name} not in the holster");
            return false;
        }

        if (!_catalog.TryGet(row, out var entry) || entry.ActionId == 0)
        {
            notes.Add($"slot {slot + 1}: {name} has no action id");
            return false;
        }

        try
        {
            var hb = RaptureHotbarModule.Instance();
            if (hb == null)
            {
                notes.Add($"slot {slot + 1}: hotbar module unavailable");
                return false;
            }

            hb->SetDutyActionSlot(slot, entry.ActionId);
            notes.Add($"slot {slot + 1}: {name}");
            return true;
        }
        catch (Exception ex)
        {
            notes.Add($"slot {slot + 1}: failed - {ex.Message}");
            return false;
        }
    }
}
