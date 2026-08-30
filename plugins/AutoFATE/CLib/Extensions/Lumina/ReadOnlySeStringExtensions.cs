using Dalamud.Utility;
using Lumina.Text.ReadOnly;
using System.Text;

namespace AutoFATE.CLib.Extensions;

public static class ReadOnlySeStringExtensions {
    public static bool ContainsText(this ReadOnlySeString seString, string needle) {
        if (seString.IsEmpty) return false;
        var bytes = new ReadOnlySpan<byte>(Encoding.UTF8.GetBytes(needle));
        return seString.ContainsText(bytes);
    }

    public static bool ContainsText(this ReadOnlySeString seString, ReadOnlySeString needle)
        => !seString.IsEmpty && !needle.IsEmpty && seString.ContainsText(needle.AsSpan());

    public static bool ContainsAny(this ReadOnlySeString seString, IEnumerable<string> needles)
        => !seString.IsEmpty && needles.Any(n => !n.IsNullOrEmpty() && seString.ContainsText(new ReadOnlySpan<byte>(Encoding.UTF8.GetBytes(n))));
}
