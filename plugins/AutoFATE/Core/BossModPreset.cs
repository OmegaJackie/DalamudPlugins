namespace AutoFATE.Core;

/// <summary>
/// The BossMod (Reborn) autorotation preset AutoFATE drives. Pure steering — targeting, fate level
/// sync, and pathfinding movement that dodges AoEs via the obstacle map. It deliberately contains
/// no rotation modules: combat actions come from Rotation Solver Reborn, so a rotation module here
/// would fight RSR over the same GCDs.
/// </summary>
public sealed class BossModPreset : IPluginService, IDisposable {
    public int InitOrder => 20; // after BossModIPC — EnsureInstalled reads Service.BossMod

    public const string PresetName = "AutoFATE";

    /// <summary>Bump when <see cref="Json"/> changes so existing installs get the update exactly once.</summary>
    public const int Version = 2;

    // Both forks deserialize leniently (unknown modules/tracks are logged and skipped), so BMR-only
    // tracks like CollectFATE coexist with vanilla BossMod. MaxTargets is overridden per-job at
    // runtime via AddTransientStrategy — it must be present here because transients require the
    // module to exist in the preset.
    public const string Json = """
    {
        "Name": "AutoFATE",
        "Modules": {
            "BossMod.Autorotation.MiscAI.AutoTarget": [
                { "Track": "FATE", "Option": "Enabled" },
                { "Track": "Retarget", "Option": "Always" },
                { "Track": "CollectFATE", "Option": "Enabled" },
                { "Track": "MaxTargets", "Value": 3 }
            ],
            "BossMod.Autorotation.MiscAI.FateUtils": [
                { "Track": "Sync", "Option": "Enable" }
            ],
            "BossMod.Autorotation.MiscAI.NormalMovement": [
                { "Track": "Destination", "Option": "Pathfind" }
            ]
        }
    }
    """;

    private const long RetryIntervalMs = 5_000;
    private long _nextAttemptAt;
    private bool _installed;

    public BossModPreset() {
        Svc.Framework.Update += OnUpdate;
    }

    public void Dispose() => Svc.Framework.Update -= OnUpdate;

    /// <summary>
    /// Install-time injection: create (or update) the preset as soon as BossMod is loaded, not only
    /// when a run starts, so it exists in BossMod's UI right after installing AutoFATE. Polled
    /// because BossMod can load after us or be installed mid-session.
    /// </summary>
    private void OnUpdate(IFramework _) {
        if (_installed)
            return;

        var now = Environment.TickCount64;
        if (now < _nextAttemptAt)
            return;
        _nextAttemptAt = now + RetryIntervalMs;

        if (!Service.BossMod.IsLoaded)
            return;

        _installed = EnsureInstalled();
    }

    /// <summary>
    /// Create the preset if missing; overwrite it when this build ships a newer version. Version-
    /// gated so a user's manual edits to the preset survive everything except a genuine update.
    /// Returns true once the preset verifiably exists.
    /// </summary>
    public static bool EnsureInstalled() {
        if (!Service.BossMod.IsLoaded)
            return false;

        var config = Service.Config;
        if (Service.BossMod.Get(PresetName) is null || config.InstalledPresetVersion < Version) {
            if (!Service.BossMod.Create(Json, overwrite: true))
                return false;
            Svc.Log.Info($"[AutoFATE] Installed BossMod preset '{PresetName}' v{Version} into {Service.BossMod.Name}");
        }

        if (config.InstalledPresetVersion != Version) {
            config.InstalledPresetVersion = Version;
            config.Save();
        }
        return true;
    }
}
