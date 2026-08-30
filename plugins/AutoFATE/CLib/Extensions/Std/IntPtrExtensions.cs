namespace AutoFATE.CLib.Extensions;

public static unsafe class IntPtrExtensions {
    public static T* Cast<T>(this IntPtr ptr) where T : unmanaged => (T*)ptr;
}
