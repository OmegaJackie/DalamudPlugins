using System.Collections.Generic;
using BozjaBuddyReborn.Game;

namespace BozjaBuddyReborn.Relic;

/// <summary>Which kind of field activity drops a material.</summary>
public enum DropActivity : byte
{
    /// <summary>Either - the material drops from anything in the region.</summary>
    Any = 0,

    /// <summary>Skirmishes (the zone's FATEs) only.</summary>
    Skirmish = 1,

    /// <summary>Critical Engagements only.</summary>
    CriticalEngagement = 2,
}

/// <summary>Where one relic material actually comes from inside the field zones.</summary>
public readonly record struct DropSource(
    uint ItemId,
    uint Territory,
    FieldRegionId Region,
    DropActivity Activity,
    int PerClear)
{
    public string Describe()
    {
        var where = FieldRegions.Label(Territory, Region);
        var what = Activity switch
        {
            DropActivity.Skirmish => "skirmishes",
            DropActivity.CriticalEngagement => "Critical Engagements",
            _ => "skirmishes and Critical Engagements",
        };
        return $"{what} in {where} ({PerClear} per clear)";
    }
}

/// <summary>
/// The region-and-activity map for the field-farmed relic materials.
///
/// THIS IS THE REASON ZONE DETECTION MATTERS. Every one of these materials only drops in one
/// specific third of its zone, and in Zadnor the skirmishes and the Critical Engagements inside
/// the SAME plateau drop different items. Farming without honouring both axes wastes the run.
///
/// Bozjan Southern Front - Augmented Resistance stage, 20 of each:
///   Z1 Southern Entrenchment  Tortured Memory of the Dying
///   Z2 Old Bozja              Sorrowful Memory of the Dying
///   Z3 The Alermuc Climb      Harrowing Memory of the Dying
///
/// Zadnor - the "A Done Deal" one-time grind, 30 of each, split by activity:
///   Z1 Southern Plateau   skirmish -> Compact Axle (1)          CE -> Compact Spring (2)
///   Z2 Western Plateau    skirmish -> Battles for the Realm (1) CE -> Beyond the Rift (2)
///   Z3 Northern Plateau   skirmish -> Bleak Memory (1)          CE -> Lurid Memory (2)
///
/// Materials farmed OUTSIDE the field zones (Bitter, Loathsome, Haunting, Vexatious, Timeworn
/// Artifact, Raw Emotion) are deliberately absent - this plugin cannot route to alliance raids
/// or deep dungeons, and claiming a zone for them would be wrong.
/// </summary>
public static class ZoneDrops
{
    public static readonly IReadOnlyList<DropSource> All =
    [
        // --- Bozjan Southern Front: the three augment memories ---------------
        new(ResistanceRelic.TorturedMemory, BozjaZones.BozjanSouthernFront, FieldRegionId.Zone1, DropActivity.Any, 1),
        new(ResistanceRelic.SorrowfulMemory, BozjaZones.BozjanSouthernFront, FieldRegionId.Zone2, DropActivity.Any, 1),
        new(ResistanceRelic.HarrowingMemory, BozjaZones.BozjanSouthernFront, FieldRegionId.Zone3, DropActivity.Any, 1),

        // --- Zadnor: six items, three plateaus, split skirmish vs CE ---------
        new(ResistanceRelic.CompactAxle, BozjaZones.Zadnor, FieldRegionId.Zone1, DropActivity.Skirmish, 1),
        new(ResistanceRelic.CompactSpring, BozjaZones.Zadnor, FieldRegionId.Zone1, DropActivity.CriticalEngagement, 2),

        new(ResistanceRelic.BattlesForTheRealm, BozjaZones.Zadnor, FieldRegionId.Zone2, DropActivity.Skirmish, 1),
        new(ResistanceRelic.BeyondTheRift, BozjaZones.Zadnor, FieldRegionId.Zone2, DropActivity.CriticalEngagement, 2),

        new(ResistanceRelic.BleakMemory, BozjaZones.Zadnor, FieldRegionId.Zone3, DropActivity.Skirmish, 1),
        new(ResistanceRelic.LuridMemory, BozjaZones.Zadnor, FieldRegionId.Zone3, DropActivity.CriticalEngagement, 2),
    ];

    /// <summary>Where a material drops in the field, or null if it is not field-farmed.</summary>
    public static DropSource? For(uint itemId)
    {
        foreach (var d in All)
            if (d.ItemId == itemId)
                return d;
        return null;
    }

    /// <summary>True when this material can be farmed by this plugin at all.</summary>
    public static bool IsFieldFarmed(uint itemId) => For(itemId) != null;

    /// <summary>Every material that drops in a given zone.</summary>
    public static IEnumerable<DropSource> ForTerritory(uint territory)
    {
        foreach (var d in All)
            if (d.Territory == territory)
                yield return d;
    }

    /// <summary>
    /// Does an objective satisfy the current farm target? Region must match, and the activity
    /// must match when the material is activity-specific.
    /// </summary>
    public static bool Matches(DropSource source, FieldRegionId objectiveRegion, DropActivity objectiveActivity)
    {
        if (objectiveRegion != source.Region)
            return false;

        return source.Activity == DropActivity.Any || source.Activity == objectiveActivity;
    }
}
