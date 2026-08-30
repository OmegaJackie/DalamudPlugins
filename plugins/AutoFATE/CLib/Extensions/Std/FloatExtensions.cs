using AutoFATE.CLib.Services;

namespace AutoFATE.CLib.Extensions;

public static class FloatExtensions {
    public static float Scaled(this float f) => f * Dalamud.Interface.Utility.ImGuiHelpers.GlobalScale * (Svc.Interface.UiBuilder.DefaultFontSpec.SizePt / 12f);
}
