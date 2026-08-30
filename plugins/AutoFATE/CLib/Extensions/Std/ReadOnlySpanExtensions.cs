namespace AutoFATE.CLib.Extensions;

public static class ReadOnlySpanExtensions {
    extension<T>(ReadOnlySpan<T> span) {
        public ReadOnlySpan<(int Index, T Item)> Index() {
            var length = span.Length;
            var indexedSpan = new (int Index, T Item)[length];
            for (var i = 0; i < length; i++) {
                indexedSpan[i] = (i, span[i]);
            }
            return new ReadOnlySpan<(int Index, T Item)>(indexedSpan);
        }
    }
}
