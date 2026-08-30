using Dalamud.Game.Text.SeStringHandling;
using Lumina.Text.ReadOnly;

namespace AutoFATE.CLib.Extensions;

public static class SeStringExtensions {
    public static ReadOnlySeString ToReadOnlySeString(this SeString seString) => new(seString.Encode());
}
