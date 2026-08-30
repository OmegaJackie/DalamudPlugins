using Dalamud.Game.Text.SeStringHandling.Payloads;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Lumina.Excel.Sheets;
using System.Text;
using System.Text.RegularExpressions;

namespace AutoFATE.CLib.Extensions;

public static unsafe partial class AddonAreaMapExtensions {
    public static Vector2? GetMouseWorldCoords(ref this AddonAreaMap areaMap)
        => MapToWorld(areaMap.MouseCoords, Map.GetRowRef(AgentMap.Instance()->SelectedMapId).Value);

    private static string ConvertSubscriptToNumber(string input) {
        var result = new StringBuilder();
        foreach (var c in input) {
            if (c is >= '\u2080' and <= '\u2089') {
                result.Append((char)('0' + (c - '\u2080'))); // subscripts to regular
            }
            else if (char.IsDigit(c) || c == '.') {
                result.Append(c); // keep digits and dots
            }
            else if (c == '\uE03F') {
                result.Append('.'); // convert special dot to dot
            }
        }
        return result.ToString();
    }

    private static Vector2 MapToWorld(Vector2 pos, Map zone) {
        MapLinkPayload maplink = new(zone.TerritoryType.Value.RowId, zone.RowId, pos.X, pos.Y);
        return new(maplink.RawX / 1000f, maplink.RawY / 1000f);
    }

    [GeneratedRegex(@"X:\s*(.*?)\s+Y:\s*(.*)", RegexOptions.IgnoreCase, "en-US")]
    private static partial Regex ExtractCoords();
}
