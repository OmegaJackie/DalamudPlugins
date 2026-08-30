using FFXIVClientStructs.FFXIV.Client.System.Input;
using FFXIVClientStructs.FFXIV.Client.UI;

namespace AutoFATE.CLib.Extensions;

public static unsafe class MouseButtonFlagsExtensions {
    extension(MouseButtonFlags flag) {
        public bool IsPressed() => UIInputData.Instance()->CursorInputs.MouseButtonPressedFlags.HasFlag(flag);
        public bool IsReleased() => UIInputData.Instance()->CursorInputs.MouseButtonReleasedFlags.HasFlag(flag);
        public bool IsHeld() => UIInputData.Instance()->CursorInputs.MouseButtonHeldFlags.HasFlag(flag);
    }
}
