using AutoFATE.CLib.Services;

namespace AutoFATE.CLib.Extensions;

public static class IPluginServiceExtensions {
    extension<T>(T) where T : class, IPluginService {
        public static T Get() => Svc.Get<T>();
    }
}
