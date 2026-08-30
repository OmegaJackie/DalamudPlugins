using System;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game.InstanceContent;
using FFXIVClientStructs.FFXIV.Client.Game.UI;

namespace BozjaBuddyReborn.Game;

/// <summary>
/// Live reads of the player's Bozja field-operation state.
///
/// Every member below is backed by a verified FFXIVClientStructs definition:
///   PublicContentBozja.GetInstance() / GetState() / UseFromHolster(uint,uint)
///   BozjaState.CurrentExperience (mettle), NeededExperience, HolsterActions[100]
///   PlayerState.GetContentValue(5) -> "Bozja: Current Resistance Rank"
/// All of these are pointer reads into live client memory, so every call is
/// null-guarded and must run on the Framework thread.
/// </summary>
public static unsafe class FieldState
{
    /// <summary>ContentKeyValueData key for the Bozja resistance rank (documented in PlayerState).</summary>
    private const uint ResistanceRankKey = 5;

    /// <summary>Holster capacity - BozjaState.HolsterActions is a fixed 100-byte array.</summary>
    public const int HolsterSize = 100;

    /// <summary>The Bozja director, or null when not inside a Bozja field operation.</summary>
    public static PublicContentBozja* Director()
    {
        try { return PublicContentBozja.GetInstance(); }
        catch { return null; }
    }

    /// <summary>
    /// The Bozja state block, or null when the director is absent or its state has not
    /// initialised yet (the game itself null-checks StateInitialized before handing this out).
    /// </summary>
    public static BozjaState* State()
    {
        try { return PublicContentBozja.GetState(); }
        catch { return null; }
    }

    /// <summary>True when the Bozja director exists and its state block is live.</summary>
    public static bool Available => State() != null;

    /// <summary>True when standing in a zone that hosts Critical Engagements.</summary>
    public static bool InFieldZone => BozjaZones.IsFieldZone(Svc.ClientState.TerritoryType);

    /// <summary>Mettle accumulated toward the next resistance rank.</summary>
    public static uint Mettle
    {
        get { var s = State(); return s == null ? 0u : s->CurrentExperience; }
    }

    /// <summary>Mettle required for the next resistance rank (0 when unknown or capped).</summary>
    public static uint MettleNeeded
    {
        get { var s = State(); return s == null ? 0u : s->NeededExperience; }
    }

    /// <summary>Current resistance rank, read from PlayerState's content key/value block.</summary>
    public static uint ResistanceRank
    {
        get
        {
            try
            {
                var ps = PlayerState.Instance();
                return ps == null ? 0u : ps->GetContentValue(ResistanceRankKey);
            }
            catch { return 0u; }
        }
    }

    /// <summary>
    /// The Lost Action holster: 100 slots of MYCTemporaryItem row ids (0 = empty slot).
    /// Copied out rather than handed back as a Span so callers cannot hold a pointer into
    /// game memory across frames.
    /// </summary>
    public static byte[] Holster()
    {
        var s = State();
        if (s == null)
            return [];

        var slots = new byte[HolsterSize];
        try
        {
            var live = s->HolsterActions;
            var n = Math.Min(HolsterSize, live.Length);
            for (var i = 0; i < n; i++)
                slots[i] = live[i];
        }
        catch
        {
            return [];
        }
        return slots;
    }

    /// <summary>
    /// Fire a Lost Action out of the holster.
    /// </summary>
    /// <param name="holsterIndex">Index into <see cref="Holster"/>.</param>
    /// <param name="slot">Duty-action slot, 0 or 1 (ignored by the game for item-type entries).</param>
    /// <returns>False when the director is absent or the game refused the use.</returns>
    public static bool UseFromHolster(uint holsterIndex, uint slot)
    {
        if (holsterIndex >= HolsterSize || slot > 1)
            return false;

        var director = Director();
        if (director == null)
            return false;

        try { return director->UseFromHolster(holsterIndex, slot); }
        catch { return false; }
    }
}
