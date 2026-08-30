using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace AutoFATE.CLib.Extensions;

public static unsafe class AddonSelectStringExtensions {
    extension(AddonSelectString) {
        public static void Select(int index) {
            var addon = RaptureAtkUnitManager.Instance()->GetAddonByName("SelectString");
            if (addon != null && addon->IsReady) {
                AtkValue val = default;
                val.SetInt(index);
                addon->FireCallback(1, &val, true);
            }
        }
    }
}
