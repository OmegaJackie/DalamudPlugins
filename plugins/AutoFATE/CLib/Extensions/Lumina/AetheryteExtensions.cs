using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Lumina.Excel.Sheets;

namespace AutoFATE.CLib.Extensions;

public static class AetheryteExtensions {
    extension(Aetheryte row) {
        public unsafe bool IsUnlocked => UIState.Instance()->IsAetheryteUnlocked(row.RowId);
    }
}
