using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;

namespace AutoFATE.CLib.Extensions;

public static class AgentInterfaceExtensions {
    extension(ref AgentInterface agentInterface) {
        public unsafe void CloseAddon() {
            var addon = RaptureAtkUnitManager.Instance()->GetAddonById((ushort)agentInterface.AddonId);
            if (addon != null && addon->IsVisible)
                addon->Close(false);
        }
    }
}
