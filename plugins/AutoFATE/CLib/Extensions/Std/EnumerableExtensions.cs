namespace AutoFATE.CLib.Extensions;

public static class EnumerableExtensions {
    extension(Enumerable) {
        public static IEnumerable<uint> Range(uint start, uint count) {
            if ((ulong)start + count - 1 > uint.MaxValue && count != 0)
                throw new ArgumentOutOfRangeException(nameof(count));

            for (uint i = 0; i < count; i++)
                yield return start + i;
        }
    }
}
