using Dalamud.Game.ClientState.Fates;

namespace AutoFATE.CLib.Extensions;

public static class DalamudExtensions {
    public static string Stringify(this IFate fate) => $"[{fate.FateId}] {fate.Position} {fate.Progress}%% {TimeSpan.FromSeconds(fate.TimeRemaining):mm\\:ss} / {TimeSpan.FromSeconds(fate.Duration):mm\\:ss} [{fate.GameData.Value.Rule}]";
}
