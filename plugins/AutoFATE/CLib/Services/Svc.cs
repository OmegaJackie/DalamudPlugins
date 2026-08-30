using Dalamud.Configuration;
using Dalamud.IoC;
using Dalamud.Plugin;
using Newtonsoft.Json;
using System.Collections.Concurrent;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace AutoFATE.CLib.Services;

// Stand-in for clib's CLibMain: vendored files reference it for the chat/log tag.
public static class CLibMain {
    public const string Name = "AutoFATE";
}

// Trimmed rebuild of clib's Svc (BSD-3, (c) Puni.sh / Jaksuhn) — only what AutoFATE needs.
public class Svc {
    [PluginService] public static IAddonLifecycle AddonLifecycle { get; private set; } = null!;
    [PluginService] public static IAetheryteList AetheryteList { get; private set; } = null!;
    [PluginService] public static IDalamudPluginInterface Interface { get; private set; } = null!;
    [PluginService] public static IChatGui Chat { get; private set; } = null!;
    [PluginService] public static IClientState ClientState { get; private set; } = null!;
    [PluginService] public static ICommandManager Commands { get; private set; } = null!;
    [PluginService] public static ICondition Condition { get; private set; } = null!;
    [PluginService] public static IContextMenu ContextMenu { get; private set; } = null!;
    [PluginService] public static IDataManager Data { get; private set; } = null!;
    [PluginService] public static IFateTable Fates { get; private set; } = null!;
    [PluginService] public static IFramework Framework { get; private set; } = null!;
    [PluginService] public static IGameGui GameGui { get; private set; } = null!;
    [PluginService] public static IGameInteropProvider Hook { get; private set; } = null!;
    [PluginService] public static IKeyState KeyState { get; private set; } = null!;
    [PluginService] public static IObjectTable Objects { get; private set; } = null!;
    [PluginService] public static IPartyList Party { get; private set; } = null!;
    [PluginService] public static IPlayerState PlayerState { get; private set; } = null!;
    [PluginService] public static IPluginLog Log { get; private set; } = null!;
    [PluginService] public static ISigScanner SigScanner { get; private set; } = null!;
    [PluginService] public static ITargetManager Targets { get; private set; } = null!;
    [PluginService] public static ITextureProvider Texture { get; private set; } = null!;
    [PluginService] public static IToastGui Toasts { get; private set; } = null!;

    public static Automation Automation { get; private set; } = null!;
    internal static NavmeshIPC Navmesh { get; private set; } = null!;

    private static readonly ConcurrentDictionary<Type, object> Singletons = new();

    public static void Register<T>() where T : class, new()
        => Register(() => new T());

    public static void Register<T>(Func<T> singleton) where T : class {
        ArgumentNullException.ThrowIfNull(singleton);
        var key = typeof(T);
        var instance = singleton();
        if (!Singletons.TryAdd(key, instance))
            throw new InvalidOperationException($"[{nameof(Svc)}] {key.FullName} is already registered.");
    }

    public static T Get<T>() where T : class {
        if (!Singletons.TryGetValue(typeof(T), out var instance))
            throw new InvalidOperationException($"[{nameof(Svc)}] {typeof(T).FullName} has not been registered.");
        return (T)instance;
    }

    internal static void Init(IDalamudPluginInterface pi, object pluginInstance) {
        if (pi.Create<Svc>() is null)
            throw new InvalidOperationException($"[{nameof(Svc)}] Dalamud service injection failed — a [PluginService] on {nameof(Svc)} could not be resolved.");
        Navmesh = new NavmeshIPC();
        Automation = new();
        RegisterPluginServices(pluginInstance.GetType().Assembly);
    }

    internal static void Dispose() {
        Automation?.Dispose();
        foreach (var s in Singletons.Values) {
            try {
                if (s is IDisposable disposable)
                    disposable.Dispose();
            }
            catch {
                Log?.Error($"[{nameof(Svc)}] Failed disposal of {s.GetType().FullName}");
            }
        }
        Singletons.Clear();
    }

    private static void RegisterPluginServices(Assembly assembly) {
        foreach (var type in assembly.GetTypes()
                .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IPluginService).IsAssignableFrom(t))
                .OrderBy(t => ((IPluginService)RuntimeHelpers.GetUninitializedObject(t)).InitOrder)
                .ThenBy(t => t.FullName)) {
            if (!Singletons.TryAdd(type, CreatePluginService(type)))
                throw new InvalidOperationException($"[{nameof(Svc)}] {type.FullName} is already registered.");
        }
    }

    // Replace collections instead of appending — field-initialized defaults (e.g. SortOrder)
    // would otherwise grow by their defaults on every load.
    private static readonly JsonSerializerSettings ConfigLoadSettings = new() { ObjectCreationHandling = ObjectCreationHandling.Replace };

    private static object CreatePluginService(Type type) {
        if (typeof(IPluginConfiguration).IsAssignableFrom(type) && Interface.ConfigFile is { Exists: true } file) {
            try {
                if (JsonConvert.DeserializeObject(File.ReadAllText(file.FullName), type, ConfigLoadSettings) is { } loaded)
                    return loaded;
            }
            catch (Exception ex) {
                Log.Error(ex, $"[{nameof(Svc)}] Failed to load config for {type.Name}; starting with defaults.");
            }
        }
        return Activator.CreateInstance(type)!;
    }
}

internal static class LogExtensions {
    public static void Print(this IPluginLog log, string message) => log.Debug($"[{CLibMain.Name}] {message}");
    public static void PrintWarning(this IPluginLog log, string message) => log.Warning($"[{CLibMain.Name}] {message}");
    public static void PrintError(this IPluginLog log, string message) => log.Error($"[{CLibMain.Name}] {message}");
}
