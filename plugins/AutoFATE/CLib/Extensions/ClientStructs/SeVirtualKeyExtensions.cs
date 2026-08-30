using FFXIVClientStructs.FFXIV.Client.System.Input;
using FFXIVClientStructs.FFXIV.Client.UI;

namespace AutoFATE.CLib.Extensions;

public static unsafe class SeVirtualKeyExtensions {
    extension(SeVirtualKey key) {
        public bool IsPressed() => UIInputData.Instance()->IsKeyPressed(key);
        public bool IsHeld() => UIInputData.Instance()->IsKeyHeld(key);
        public bool IsReleased() => UIInputData.Instance()->IsKeyReleased(key);
        public bool IsDown() => UIInputData.Instance()->IsKeyDown(key);
    }
}
