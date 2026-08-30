using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using BozjaBuddyReborn.Game;

namespace BozjaBuddyReborn.Multibox;

/// <summary>One box's duty-action loadout as seen by every other box.</summary>
public readonly record struct PeerDuty(string Name, DutySlot Slot0, DutySlot Slot1, bool IsSelf)
{
    public DutySlot Slot(int i) => i == 0 ? Slot0 : Slot1;
}

/// <summary>
/// Wire format for the shared duty-action hotbar.
///
/// Kept deliberately flat and textual so it rides the existing newline-delimited pipe without a
/// second transport or any versioning ceremony. One entry per box:
///
///     name~a0,c0,m0,cd0,t0~a1,c1,m1,cd1,t1
///
/// with cooldowns in tenths of a second (an integer keeps the line short and the parse
/// culture-proof; a tenth is finer than the eye reads a cooldown sweep anyway). Entries are
/// joined with '|' by the caller, which is the pipe's own field separator, so an entry may never
/// contain one - hence the '~' inside an entry and the sanitising of names below.
/// </summary>
public static class DutyRoster
{
    private const char SlotSep = '~';
    private const char FieldSep = ',';

    /// <summary>Characters that would corrupt the framing if they appeared in a character name.</summary>
    private static readonly char[] Illegal = ['|', '~', ',', '\n', '\r'];

    public static string SanitiseName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "unknown";

        var sb = new StringBuilder(name.Length);
        foreach (var c in name)
            sb.Append(Array.IndexOf(Illegal, c) >= 0 ? ' ' : c);
        return sb.ToString().Trim() is { Length: > 0 } cleaned ? cleaned : "unknown";
    }

    /// <summary>Encode one box's loadout.</summary>
    public static string Encode(string name, DutySlot slot0, DutySlot slot1) =>
        string.Concat(SanitiseName(name), SlotSep.ToString(), EncodeSlot(slot0), SlotSep.ToString(), EncodeSlot(slot1));

    private static string EncodeSlot(DutySlot s) => string.Join(FieldSep,
        s.ActionId.ToString(CultureInfo.InvariantCulture),
        s.CurCharges.ToString(CultureInfo.InvariantCulture),
        s.MaxCharges.ToString(CultureInfo.InvariantCulture),
        ((int)Math.Round(s.CooldownRemaining * 10f)).ToString(CultureInfo.InvariantCulture),
        ((int)Math.Round(s.CooldownTotal * 10f)).ToString(CultureInfo.InvariantCulture));

    /// <summary>Decode one entry. Returns false on anything malformed rather than throwing.</summary>
    public static bool TryDecode(string entry, bool isSelf, out PeerDuty peer)
    {
        peer = default;
        if (string.IsNullOrEmpty(entry))
            return false;

        var parts = entry.Split(SlotSep);
        if (parts.Length < 3)
            return false;

        if (!TryDecodeSlot(parts[1], out var s0) || !TryDecodeSlot(parts[2], out var s1))
            return false;

        peer = new PeerDuty(parts[0], s0, s1, isSelf);
        return true;
    }

    private static bool TryDecodeSlot(string raw, out DutySlot slot)
    {
        slot = DutySlot.Empty;

        var f = raw.Split(FieldSep);
        if (f.Length < 5)
            return false;

        if (!uint.TryParse(f[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ||
            !byte.TryParse(f[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var cur) ||
            !byte.TryParse(f[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var max) ||
            !int.TryParse(f[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var cdTenths) ||
            !int.TryParse(f[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out var totalTenths))
            return false;

        slot = new DutySlot(id, cur, max, cdTenths / 10f, totalTenths / 10f);
        return true;
    }

    /// <summary>Decode a whole roster line's worth of entries.</summary>
    public static List<PeerDuty> DecodeAll(IReadOnlyList<string> entries, int from, string selfName)
    {
        var list = new List<PeerDuty>();
        for (var i = from; i < entries.Count; i++)
        {
            if (TryDecode(entries[i], false, out var p))
            {
                // The host includes itself in the roster; mark whichever entry is us so the
                // window can label it and sort it first.
                list.Add(p.Name == SanitiseName(selfName) ? p with { IsSelf = true } : p);
            }
        }
        return list;
    }
}
