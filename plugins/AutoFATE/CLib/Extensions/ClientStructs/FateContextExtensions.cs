using FFXIVClientStructs.FFXIV.Client.Game.Fate;
using Lumina.Excel.Sheets;

namespace AutoFATE.CLib.Extensions;

public static class FateContextExtensions {
    extension(ref FateContext ctx) {
        public Fate GameData => Fate.GetRow(ctx.FateId);
    }
}
