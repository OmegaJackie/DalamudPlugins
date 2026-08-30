using AutoFATE.CLib.Services;

namespace AutoFATE.CLib.Extensions;

public static class IDalamudServiceExtensions {
    // https://github.com/MidoriKami/VanillaPlus/blob/ca83f78fa9c89f5231a1053ff0ce7f74f34862ff/VanillaPlus/Service.cs#L12-L26
    extension<T>(T) where T : class, IDalamudService {
        public static T Get() => ServiceInstance<T>.Instance ?? throw new InvalidOperationException($"Service {typeof(T).Name} not found.");
    }

    private static class ServiceInstance<T> where T : class, IDalamudService {
        public static T? Instance => field ??= Svc.Interface.GetService(typeof(T)) as T;
    }
}
