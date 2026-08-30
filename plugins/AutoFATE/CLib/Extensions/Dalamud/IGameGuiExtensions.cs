using FFXIVClientStructs.FFXIV.Component.GUI;

namespace AutoFATE.CLib.Extensions;

public static class IGameGuiExtensions {
    extension(IGameGui gui) {
        public unsafe bool TryGetAddon<T>(string name, out T* AddonPtr) where T : unmanaged {
            if (IGameGui.Get().GetAddonByName(name) is { Address: var addr }) {
                AddonPtr = (T*)addr;
                return ((AtkUnitBase*)AddonPtr)->IsAddonReady();
            }
            AddonPtr = null;
            return false;
        }
    }
}
