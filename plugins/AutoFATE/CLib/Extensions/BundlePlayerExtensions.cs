using Dalamud.Game.ClientState.Objects.SubKinds;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;

namespace AutoFATE.CLib.Extensions;

public static unsafe class PlayerExtensions {
    extension(IPlayerCharacter pc) {
        public byte ReviveState => pc.IsDead ? AgentRevive.Instance()->ReviveState : (byte)0;
    }
}
