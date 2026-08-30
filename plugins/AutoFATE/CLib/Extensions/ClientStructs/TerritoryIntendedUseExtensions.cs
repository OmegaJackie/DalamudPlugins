using Lumina.Excel.Sheets;

namespace AutoFATE.CLib.Extensions;

public static class TerritoryIntendedUseExtensions {
    extension(TerritoryIntendedUse row) {
        public FFXIVClientStructs.FFXIV.Client.Enums.TerritoryIntendedUse StructsEnum => (FFXIVClientStructs.FFXIV.Client.Enums.TerritoryIntendedUse)row.RowId;
    }
}
