using System;
using System.Collections.Generic;
using System.Numerics;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game.UI;

namespace BozjaBuddyReborn.Game;

/// <summary>
/// The three sub-regions each field zone is divided into, in the numbering players use.
/// </summary>
public enum FieldRegionId : byte
{
    Unknown = 0,
    Zone1 = 1,
    Zone2 = 2,
    Zone3 = 3,
}

/// <summary>
/// Sub-region ("zone") handling for the Bozjan Southern Front and Zadnor.
///
/// THIS IS LOAD-BEARING FOR RELIC FARMING. Both field zones are split into three named
/// regions, and the relic materials are region-specific - farming in the wrong third of the
/// map yields nothing you need. Zadnor goes further: within one plateau, skirmishes and
/// Critical Engagements drop DIFFERENT items.
///
///   Bozjan Southern Front (920)          Zadnor (975)
///   Z1  Southern Entrenchment  (3536)    Z1  The Southern Plateau  (3668)
///   Z2  Old Bozja              (3537)    Z2  The Western Plateau   (3669)
///   Z3  The Alermuc Climb      (3538)    Z3  The Northern Plateau  (3670)
///
/// PlaceName ids are from PlaceName.csv; the Z-numbering is confirmed by the map-marker
/// north-south ordering (Z3 sits at the lowest map Y, i.e. furthest north) and matches the
/// relic wiki's own "(Zone 1/2/3)" labelling.
///
/// HOW THE CURRENT REGION IS KNOWN: exactly, from TerritoryInfo, which the game maintains as
/// the player moves between map ranges. No geometry, no guessing.
///
/// HOW AN OBJECTIVE'S REGION IS KNOWN: there is no static table shipping the region of each
/// Critical Engagement, so it is LEARNED. When the character is standing at an objective, the
/// controller records "this engagement is in the region TerritoryInfo currently reports" and
/// persists it. Until an objective has been learned, <see cref="ClassifyByPosition"/> gives an
/// approximate answer from the region label anchors so first-run behaviour is not blind.
/// </summary>
public static unsafe class FieldRegions
{
    // --- PlaceName ids -------------------------------------------------------
    public const uint SouthernEntrenchment = 3536; // Bozja Z1
    public const uint OldBozja = 3537;             // Bozja Z2
    public const uint AlermucClimb = 3538;         // Bozja Z3

    public const uint SouthernPlateau = 3668;      // Zadnor Z1
    public const uint WesternPlateau = 3669;       // Zadnor Z2
    public const uint NorthernPlateau = 3670;      // Zadnor Z3

    /// <summary>
    /// Approximate world anchor for each region's map label.
    ///
    /// Derived from MapMarker.csv (marker range 418 for Bozja map 606, 446 for Zadnor map 665)
    /// through world = (pixel - 1023) / (SizeFactor/100) - Offset, which is the algebraic
    /// inverse of the verified world-to-map-coordinate formula composed with the standard
    /// marker-pixel-to-map-coordinate relation. Both maps are SizeFactor 100; Bozja carries
    /// OffsetX -127 / OffsetY -424, Zadnor none.
    ///
    /// These are LABEL anchors, not region centroids, so they are only used as the pre-learning
    /// fallback. The learned table from TerritoryInfo supersedes them permanently.
    /// </summary>
    private static readonly Dictionary<(uint Territory, FieldRegionId Region), Vector2> Anchors = new()
    {
        // Bozjan Southern Front - a north-south corridor, so Z (north-south) is what separates them.
        [(BozjaZones.BozjanSouthernFront, FieldRegionId.Zone1)] = new Vector2(252f, 741f),
        [(BozjaZones.BozjanSouthernFront, FieldRegionId.Zone2)] = new Vector2(187f, 439f),
        [(BozjaZones.BozjanSouthernFront, FieldRegionId.Zone3)] = new Vector2(211f, 77f),

        // Zadnor.
        [(BozjaZones.Zadnor, FieldRegionId.Zone1)] = new Vector2(177f, 601f),
        [(BozjaZones.Zadnor, FieldRegionId.Zone2)] = new Vector2(-541f, 259f),
        [(BozjaZones.Zadnor, FieldRegionId.Zone3)] = new Vector2(-297f, -429f),
    };

    /// <summary>Map a region PlaceName id to its zone number, for the territory it belongs to.</summary>
    public static FieldRegionId FromPlaceName(uint placeNameId) => placeNameId switch
    {
        SouthernEntrenchment or SouthernPlateau => FieldRegionId.Zone1,
        OldBozja or WesternPlateau => FieldRegionId.Zone2,
        AlermucClimb or NorthernPlateau => FieldRegionId.Zone3,
        _ => FieldRegionId.Unknown,
    };

    /// <summary>The in-game name of a region, e.g. "The Northern Plateau".</summary>
    public static string Name(uint territory, FieldRegionId region)
    {
        if (region == FieldRegionId.Unknown)
            return "unknown zone";

        if (territory == BozjaZones.BozjanSouthernFront)
            return region switch
            {
                FieldRegionId.Zone1 => "Southern Entrenchment",
                FieldRegionId.Zone2 => "Old Bozja",
                FieldRegionId.Zone3 => "The Alermuc Climb",
                _ => "unknown zone",
            };

        if (territory == BozjaZones.Zadnor)
            return region switch
            {
                FieldRegionId.Zone1 => "The Southern Plateau",
                FieldRegionId.Zone2 => "The Western Plateau",
                FieldRegionId.Zone3 => "The Northern Plateau",
                _ => "unknown zone",
            };

        return "unknown zone";
    }

    /// <summary>Short label, e.g. "Z3 - The Northern Plateau".</summary>
    public static string Label(uint territory, FieldRegionId region)
        => region == FieldRegionId.Unknown
            ? "unknown zone"
            : $"Z{(byte)region} - {Name(territory, region)}";

    /// <summary>
    /// The region the character is standing in right now, read from the game's own
    /// TerritoryInfo. Checks the area name first and the sub-area second, because which of the
    /// two carries the plateau depends on how the zone's map ranges are nested.
    /// Framework thread only.
    /// </summary>
    public static FieldRegionId Current()
    {
        try
        {
            var info = TerritoryInfo.Instance();
            if (info == null)
                return FieldRegionId.Unknown;

            var byArea = FromPlaceName(info->AreaPlaceNameId);
            if (byArea != FieldRegionId.Unknown)
                return byArea;

            return FromPlaceName(info->SubAreaPlaceNameId);
        }
        catch
        {
            return FieldRegionId.Unknown;
        }
    }

    /// <summary>
    /// How much closer the nearest anchor must be than the runner-up before the answer is
    /// trusted.
    ///
    /// THE ESTIMATE HAS TO BE ABLE TO SAY "I DO NOT KNOW", and it could not. Anchors always holds
    /// all three entries for a field zone, so the old nearest-anchor query always returned
    /// Zone1/2/3 - never Unknown - which meant the caller's "region unknown" branch was
    /// unreachable in both field zones, and ticking "skip objectives whose region is not yet
    /// known" did not skip anything on grounds of uncertainty. It silently became hard filtering
    /// on a three-point Voronoi over what this file itself calls LABEL anchors, not centroids.
    ///
    /// A margin makes the abstain real: near a boundary the two nearest anchors are comparable
    /// and the honest answer is that we cannot tell from position alone.
    /// </summary>
    private const float AnchorMarginYalms = 60f;

    /// <summary>
    /// Best-effort region for an arbitrary world position: nearest region label anchor on the
    /// horizontal plane, or <see cref="FieldRegionId.Unknown"/> when the two nearest anchors are
    /// too close together to call. Approximate - use only until the objective's region has been
    /// learned from <see cref="Current"/>.
    /// </summary>
    public static FieldRegionId ClassifyByPosition(uint territory, Vector3 world)
    {
        if (!BozjaZones.IsFieldZone(territory))
            return FieldRegionId.Unknown;

        var best = FieldRegionId.Unknown;
        var bestDistance = float.MaxValue;
        var runnerUpDistance = float.MaxValue;

        foreach (var region in All)
        {
            if (!Anchors.TryGetValue((territory, region), out var anchor))
                continue;

            var dx = world.X - anchor.X;
            var dz = world.Z - anchor.Y; // anchor.Y holds world Z
            var distance = MathF.Sqrt(dx * dx + dz * dz);

            if (distance < bestDistance)
            {
                runnerUpDistance = bestDistance;
                bestDistance = distance;
                best = region;
            }
            else if (distance < runnerUpDistance)
            {
                runnerUpDistance = distance;
            }
        }

        // Too close to call: abstain rather than guess. The caller decides what an unknown
        // region means - which is the whole point of being able to return one.
        if (best != FieldRegionId.Unknown && runnerUpDistance - bestDistance < AnchorMarginYalms)
            return FieldRegionId.Unknown;

        return best;
    }

    public static readonly FieldRegionId[] All =
        [FieldRegionId.Zone1, FieldRegionId.Zone2, FieldRegionId.Zone3];

    /// <summary>Which field zone a region belongs to is ambiguous by number alone, so ask the map.</summary>
    public static uint CurrentTerritory => Svc.ClientState.TerritoryType;
}
