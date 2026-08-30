using FFXIVClientStructs.FFXIV.Client.Game.UI;

namespace AutoFATE.CLib.TaskSystem;

/// <summary>
/// Areas where pathing must stay on the ground even though the zone allows flight. vnavmesh's
/// volume (flight) mesh is unpolished in these spots — fly paths clip terrain or wedge the
/// follower against cave ceilings — while the ground mesh routes fine. Stock vnavmesh ships no
/// mesh customization for these zones and exposes no IPC to add one, so walking is the fix.
/// </summary>
public static class FlightRestrictions {
    private readonly record struct GroundOnlyArea(uint TerritoryId, uint AreaPlaceNameId, Vector2 Min, Vector2 Max) {
        // XZ only: the pit spans many height levels and the cave sits under open terrain,
        // so a Y bound would let a fly path start from the surface directly above it.
        public bool Contains(Vector3 p) => p.X >= Min.X && p.X <= Max.X && p.Z >= Min.Y && p.Z <= Max.Y;
    }

    // U'Ghamaro Mines, Outer La Noscea (territory 180, PlaceName 238): the kobold pit and cave
    // network in the zone's north. Box traced around the fate rings clustered inside it
    // (X 20..290, Z -780..-505 in the Level sheet), padded on every side.
    private static readonly GroundOnlyArea[] _areas = [
        new(180, 238, new Vector2(-20, -840), new Vector2(330, -470)),
    ];

    /// <summary>Is this world position inside a ground-only area of the given territory?</summary>
    public static bool IsGroundOnly(uint territoryId, Vector3 position)
        => _areas.Any(a => a.TerritoryId == territoryId && a.Contains(position));

    /// <summary>
    /// The player's own membership additionally consults the live sub-area name, which tracks the
    /// cave interior more precisely than a bounding box can.
    /// </summary>
    public static unsafe bool IsPlayerInGroundOnlyArea() {
        var territory = IClientState.Get().TerritoryType;
        var info = TerritoryInfo.Instance();
        var areaPlaceNameId = info is not null ? info->AreaPlaceNameId : 0u;
        return _areas.Any(a => a.TerritoryId == territory
            && (areaPlaceNameId != 0 && a.AreaPlaceNameId == areaPlaceNameId
                || IObjectTable.Get().LocalPlayer?.Position is { } pos && a.Contains(pos)));
    }

    /// <summary>
    /// False when either endpoint of a move sits in a ground-only area: flying in would cross the
    /// unpolished volume mesh, and flying out of the cave is blocked by its ceiling.
    /// </summary>
    public static bool AllowFlight(uint territoryId, Vector3 destination)
        => !IsGroundOnly(territoryId, destination) && !IsPlayerInGroundOnlyArea();
}
