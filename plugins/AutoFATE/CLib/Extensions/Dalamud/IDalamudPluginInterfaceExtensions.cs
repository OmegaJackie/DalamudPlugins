using Dalamud.Plugin;
using System.Reflection;

namespace AutoFATE.CLib.Extensions;

public static class IDalamudPluginInterfaceExtensions {
    extension(IDalamudPluginInterface pi) {
        /// <summary>
        /// This is the commit number, not hash
        /// </summary>
        public uint ClientStructsVersion => get_CsVersion(pi).Value;
        internal Lazy<uint> CsVersion => new(() => (uint?)typeof(FFXIVClientStructs.ThisAssembly).Assembly.GetName().Version?.Build ?? 0U);

        public bool IsPluginLoaded(string internalName) => pi.InstalledPlugins.Any(p => p.InternalName == internalName && p.IsLoaded);

        public object? GetService(string serviceName)
            => pi.GetType().Assembly.GetType("Dalamud.Service`1", true)?.MakeGenericType(pi.GetType().Assembly.GetType(serviceName, true)!).GetMethod("Get")?.Invoke(null, BindingFlags.Default, null, [], null);

        public void ToggleDtr() {
            var config = pi.GetService("Dalamud.Configuration.Internal.DalamudConfiguration");
            if (config?.GetFieldOrProperty<List<string>>("DtrIgnore") is { } ignoreList) {
                if (!ignoreList.Contains(pi.InternalName))
                    ignoreList.Remove(pi.InternalName);
                else
                    ignoreList.Add(pi.InternalName);
                config.CallMethod("QueueSave", []);
            }
        }
    }
}
