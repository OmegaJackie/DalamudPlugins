using Dalamud.Hooking;
using System.Runtime.InteropServices;

namespace AutoFATE.CLib.Extensions;

public static class IGameInteropProviderExtensions {
    extension(IGameInteropProvider gameInteropProvider) {
        public unsafe Hook<T> HookFromVTable<T>(void* vtblAddress, int vfIndex, T detour) where T : Delegate
            => gameInteropProvider.HookFromVTable((nint)vtblAddress, vfIndex, detour);

        public unsafe Hook<T> HookFromVTable<T>(nint vtblAddress, int vfIndex, T detour) where T : Delegate
            => gameInteropProvider.HookFromAddress(*(nint*)(vtblAddress + vfIndex * 0x08), detour);

        public unsafe T GetDelegate<T>(string signature, int offset = 0)
            => Marshal.GetDelegateForFunctionPointer<T>(ISigScanner.Get().ScanText(signature) + offset);
    }
}
