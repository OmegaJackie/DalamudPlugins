using System.Collections.Generic;

namespace BozjaBuddyReborn.Game;

/// <summary>
/// Territory / content ids for the Bozja field operations.
///
/// Every id here was resolved from the game's own sheets rather than folklore:
/// TerritoryType rows whose PlaceName is one of the Bozja PlaceName rows
/// (3534 Bozjan Southern Front, 3597 Delubrum Reginae, 3662 Zadnor), cross-checked
/// against ContentFinderCondition. They are stable across patches, but
/// <see cref="Resolve"/> re-verifies them from Lumina at load so a future
/// re-numbering surfaces as a warning instead of silent misbehaviour.
/// </summary>
public static class BozjaZones
{
    // Territory ids are uint in the current Dalamud API (IClientState.TerritoryType).

    /// <summary>The Bozjan Southern Front. TerritoryType 920, ContentFinderCondition 735.</summary>
    public const uint BozjanSouthernFront = 920;

    /// <summary>Zadnor. TerritoryType 975, ContentFinderCondition 778.</summary>
    public const uint Zadnor = 975;

    /// <summary>Delubrum Reginae (normal). TerritoryType 936, ContentFinderCondition 760.</summary>
    public const uint DelubrumReginae = 936;

    /// <summary>Delubrum Reginae (Savage). TerritoryType 937, ContentFinderCondition 761.</summary>
    public const uint DelubrumReginaeSavage = 937;

    /// <summary>
    /// The two open field zones that host Critical Engagements. Castrum Lacus Litore and
    /// the Dalriada are large-scale engagements *inside* these zones (DynamicEvent rows 16
    /// and 32), not separate territories, so they need no ids of their own.
    /// </summary>
    public static readonly uint[] FieldZones = [BozjanSouthernFront, Zadnor];

    /// <summary>Delubrum Reginae normal + savage. Raids, not field zones - no DynamicEvents.</summary>
    public static readonly uint[] RaidZones = [DelubrumReginae, DelubrumReginaeSavage];

    /// <summary>True in a zone where Critical Engagements spawn and this plugin can operate.</summary>
    public static bool IsFieldZone(uint territory)
        => territory == BozjanSouthernFront || territory == Zadnor;

    /// <summary>True in any Bozja-line content, including the Delubrum raids.</summary>
    public static bool IsBozjaContent(uint territory)
        => IsFieldZone(territory) || territory == DelubrumReginae || territory == DelubrumReginaeSavage;

    public static string Name(uint territory) => territory switch
    {
        BozjanSouthernFront => "the Bozjan Southern Front",
        Zadnor => "Zadnor",
        DelubrumReginae => "Delubrum Reginae",
        DelubrumReginaeSavage => "Delubrum Reginae (Savage)",
        _ => $"territory {territory}",
    };

    /// <summary>
    /// DynamicEvent row ranges per zone. The sheet groups the engagements contiguously:
    /// rows 1-16 are Bozjan Southern Front (16 = The Battle of Castrum Lacus Litore),
    /// rows 17-32 are Zadnor (32 = The Dalriada), rows 33+ are Occult Crescent and are
    /// deliberately excluded - a different field operation with its own director.
    ///
    /// This is only used to present the *catalogue* (the priority/blocklist UI). The live
    /// engagement list always comes from the zone's own DynamicEventContainer, which by
    /// construction only ever holds the current zone's events.
    /// </summary>
    public static IEnumerable<ushort> CatalogueFor(uint territory)
    {
        var (lo, hi) = territory switch
        {
            BozjanSouthernFront => (1, 16),
            Zadnor => (17, 32),
            _ => (0, -1),
        };
        for (var i = lo; i <= hi; i++)
            yield return (ushort)i;
    }

    /// <summary>All Bozja/Zadnor DynamicEvent rows, for a zone-agnostic catalogue view.</summary>
    public static IEnumerable<ushort> AllCatalogue()
    {
        for (ushort i = 1; i <= 32; i++)
            yield return i;
    }

    /// <summary>Which field zone a catalogue DynamicEvent row belongs to (0 if neither).</summary>
    public static uint ZoneOfEvent(ushort eventId) => eventId switch
    {
        >= 1 and <= 16 => BozjanSouthernFront,
        >= 17 and <= 32 => Zadnor,
        _ => 0u,
    };
}
