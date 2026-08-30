using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace AutoFATE.CLib.Extensions;

public static unsafe class AtkResNodeExtensions {
    extension(ref AtkResNode node) {
        public AtkUnitBase* OwnerAddon {
            get {
                fixed (AtkResNode* ptr = &node)
                    return RaptureAtkUnitManager.Instance()->GetAddonByNode(ptr);
            }
        }

        public bool Visible {
            get => node.IsVisible();
            set => node.ToggleVisibility(value);
        }

        // https://github.com/MidoriKami/KamiToolKit/blob/726eecf87b84d998b26220d1425b57a112ba72d2/Extensions/AtkResNodeExtensions.cs#L214
        public bool IsActuallyVisible {
            get {
                if (!node.Visible) return false;

                var targetNode = node.ParentNode;
                while (targetNode is not null) {
                    if (!targetNode->Visible) return false;
                    targetNode = targetNode->ParentNode;
                }

                return true;
            }
        }
    }
}
