using System;
using ECommons.DalamudServices;
using Lumina.Excel.Sheets;

namespace BozjaBuddyReborn.Game;

/// <summary>
/// Conversion between the two-decimal map coordinates players read off the map and world
/// space, for the current zone.
///
/// These are NOT the same units. The conversion needs three columns of the Map sheet:
/// SizeFactor (zoom), OffsetX and OffsetY. Both field zones happen to be SizeFactor 100 with
/// Zadnor at zero offsets and Bozja at (-127, -424), but the values are read from the sheet
/// rather than assumed.
///
/// The forward formula is the one Questionable uses; the inverse below is its algebraic
/// solution for v. Sanity-checked against a known Zadnor point: map (16.1, 14.7) resolves to
/// world (-268.7, -338.7), which sits 95y from the Northern Plateau's own map label anchor and
/// 650y+ from the other two plateaus' - i.e. it lands in the region it should.
/// </summary>
public static class MapCoords
{
    private readonly record struct MapInfo(float Scale, short OffsetX, short OffsetY);

    private static MapInfo? Lookup(uint territory)
    {
        try
        {
            var territories = Svc.Data.GetExcelSheet<TerritoryType>();
            var row = territories?.GetRowOrDefault(territory);
            if (row == null)
                return null;

            var map = row.Value.Map.ValueNullable;
            if (map == null)
                return null;

            var scale = map.Value.SizeFactor / 100f;
            if (scale <= 0f)
                return null;

            return new MapInfo(scale, map.Value.OffsetX, map.Value.OffsetY);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Map coordinate to a world axis. Returns null if the zone's map cannot be read.</summary>
    public static float? ToWorld(uint territory, float mapCoord, bool isXAxis)
    {
        if (Lookup(territory) is not { } info)
            return null;

        var offset = isXAxis ? info.OffsetX : info.OffsetY;
        return ((mapCoord - 1f) * 2048f * info.Scale / 41f - 1023f) / info.Scale - offset;
    }

    /// <summary>World axis to a map coordinate. Returns null if the zone's map cannot be read.</summary>
    public static float? ToMap(uint territory, float world, bool isXAxis)
    {
        if (Lookup(territory) is not { } info)
            return null;

        var offset = isXAxis ? info.OffsetX : info.OffsetY;
        return 41f * ((MathF.Truncate(world) + offset) * info.Scale + 1024f - 1f) / 2048f / info.Scale + 1f;
    }

    /// <summary>
    /// A map point as world X/Z. Altitude is not representable in map coordinates, so the
    /// caller must resolve Y against the navmesh.
    /// </summary>
    public static (float X, float Z)? ToWorldXZ(uint territory, float mapX, float mapY)
    {
        var x = ToWorld(territory, mapX, isXAxis: true);
        var z = ToWorld(territory, mapY, isXAxis: false);
        return x is { } wx && z is { } wz ? (wx, wz) : null;
    }

    /// <summary>The player's current position as map coordinates, for "use where I am" buttons.</summary>
    public static (float X, float Y)? PlayerMapPosition()
    {
        var me = Svc.Objects.LocalPlayer;
        if (me == null)
            return null;

        var territory = Svc.ClientState.TerritoryType;
        var x = ToMap(territory, me.Position.X, isXAxis: true);
        var y = ToMap(territory, me.Position.Z, isXAxis: false);
        return x is { } mx && y is { } my ? (mx, my) : null;
    }
}
