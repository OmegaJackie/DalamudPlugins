using FFXIVClientStructs.FFXIV.Client.System.Input;
using FFXIVClientStructs.FFXIV.Client.UI;

namespace AutoFATE.CLib.Extensions;

public static unsafe class InputIdExtensions {
    extension(InputId id) {
        public bool IsPressed() => UIInputData.Instance()->InputData.IsInputIdPressed(id);
        public bool IsReleased() => UIInputData.Instance()->InputData.IsInputIdReleased(id);
        public bool IsDown() => UIInputData.Instance()->InputData.IsInputIdDown(id);
        public bool IsHeld() => UIInputData.Instance()->InputData.IsInputIdHeld(id);
    }
}
