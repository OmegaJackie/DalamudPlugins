using FFXIVClientStructs.FFXIV.Component.GUI;

namespace AutoFATE.CLib.Extensions;

public static unsafe class AtkComponentButtonExtensions {
    extension(scoped ref AtkComponentButton target) {
        // https://github.com/AtmoOmen/OmenTools/blob/64f7cda301e0b59bc9a72fee13aadb03253ee645/Extensions/AtkComponentExtension.cs#L254
        public void Click() {
            fixed (AtkComponentButton* ptr = &target) {
                if (ptr == null) return;

                var ownerNode = ptr->OwnerNode;
                var evt = ownerNode->AtkResNode.AtkEventManager.Event;
                ownerNode->OwnerAddon->ReceiveEvent(evt->State.EventType, (int)evt->Param, evt);
            }
        }
    }
}
