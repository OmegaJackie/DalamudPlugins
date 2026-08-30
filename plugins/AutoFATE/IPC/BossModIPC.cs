using Dalamud.Plugin.Ipc;
using System.Threading.Tasks;

namespace AutoFATE.IPC;

/// <summary>
/// IPC into BossMod Reborn. BMR is a fork of veyn's BossMod; gate names are resolved at
/// construction against whichever fork is actually loaded, preferring BossMod Reborn.
/// </summary>
public sealed class BossModIPC : IPluginService {
    public int InitOrder => 10;

    public const string RebornInternalName = "BossModReborn";
    public const string VanillaInternalName = "BossMod";

    /// <summary>True when BossMod Reborn specifically is loaded.</summary>
    public bool IsRebornLoaded => Svc.Interface.IsPluginLoaded(RebornInternalName);
    /// <summary>True when either fork is loaded.</summary>
    public bool IsLoaded => IsRebornLoaded || Svc.Interface.IsPluginLoaded(VanillaInternalName);
    public string Name => IsRebornLoaded ? RebornInternalName : VanillaInternalName;

    private readonly ICallGateSubscriber<string, string?> _get;
    private readonly ICallGateSubscriber<string, bool, bool> _create;
    private readonly ICallGateSubscriber<string?> _getActive;
    private readonly ICallGateSubscriber<string, bool> _setActive;
    private readonly ICallGateSubscriber<bool> _clearActive;
    private readonly ICallGateSubscriber<string, string, string, string, bool> _addTransientStrategy;
    private readonly ICallGateSubscriber<Vector3, float, bool, bool> _generate;
    private readonly ICallGateSubscriber<TaskStatus> _getGenerationStatus;
    private readonly ICallGateSubscriber<bool> _hasTempMap;
    private readonly ICallGateSubscriber<bool> _clearTempMap;
    private readonly ICallGateSubscriber<BitmapQuality?> _evaluateTempMapQuality;

    public BossModIPC() {
        // Both forks register their gates under the "BossMod." prefix.
        _get = Svc.Interface.GetIpcSubscriber<string, string?>("BossMod.Presets.Get");
        _create = Svc.Interface.GetIpcSubscriber<string, bool, bool>("BossMod.Presets.Create");
        _getActive = Svc.Interface.GetIpcSubscriber<string?>("BossMod.Presets.GetActive");
        _setActive = Svc.Interface.GetIpcSubscriber<string, bool>("BossMod.Presets.SetActive");
        _clearActive = Svc.Interface.GetIpcSubscriber<bool>("BossMod.Presets.ClearActive");
        _addTransientStrategy = Svc.Interface.GetIpcSubscriber<string, string, string, string, bool>("BossMod.Presets.AddTransientStrategy");
        _generate = Svc.Interface.GetIpcSubscriber<Vector3, float, bool, bool>("BossMod.ObstacleMap.Generate");
        _getGenerationStatus = Svc.Interface.GetIpcSubscriber<TaskStatus>("BossMod.ObstacleMap.GetGenerationStatus");
        _hasTempMap = Svc.Interface.GetIpcSubscriber<bool>("BossMod.ObstacleMap.HasTempMap");
        _clearTempMap = Svc.Interface.GetIpcSubscriber<bool>("BossMod.ObstacleMap.ClearTempMap");
        _evaluateTempMapQuality = Svc.Interface.GetIpcSubscriber<BitmapQuality?>("BossMod.ObstacleMap.EvaluateTempMapQuality");
    }

    public string? Get(string name) => _get.HasFunction ? _get.InvokeFunc(name) : null;
    public bool Create(string presetSerialized, bool overwrite) => _create.HasFunction && _create.InvokeFunc(presetSerialized, overwrite);
    // BMR is single-preset and returns null when nothing is active; normalize to empty.
    public string GetActive() => (_getActive.HasFunction ? _getActive.InvokeFunc() : null) ?? string.Empty;
    public bool SetActive(string name) => _setActive.HasFunction && _setActive.InvokeFunc(name);
    public bool ClearActive() => _clearActive.HasFunction && _clearActive.InvokeFunc();
    public bool AddTransientStrategy(string presetName, string moduleTypeName, string trackName, string value)
        => _addTransientStrategy.HasFunction && _addTransientStrategy.InvokeFunc(presetName, moduleTypeName, trackName, value);

    /// <summary>Both forks expose ObstacleMap gates, but feature-detect anyway in case of drift.</summary>
    public bool SupportsObstacleMaps => _generate.HasFunction;

    // Note: on some provider builds a previously faulted generation rethrows its stored
    // exception through this call instead of returning false — callers must catch.
    public bool Generate(Vector3 centerWorld, float radius, bool writeToFile)
        => _generate.HasFunction && _generate.InvokeFunc(centerWorld, radius, writeToFile);
    public TaskStatus GetGenerationStatus() => _getGenerationStatus.HasFunction ? _getGenerationStatus.InvokeFunc() : TaskStatus.Canceled;
    public bool HasTempMap() => _hasTempMap.HasFunction && _hasTempMap.InvokeFunc();
    public bool ClearTempMap() => _clearTempMap.HasFunction && _clearTempMap.InvokeFunc();
    public BitmapQuality? EvaluateTempMapQuality() => _evaluateTempMapQuality.HasFunction ? _evaluateTempMapQuality.InvokeFunc() : null;

    public readonly record struct BitmapQuality(
        float BlockedFraction,
        float LargestPassableComponentFraction,
        float TinyPassableComponentFraction,
        float SpeckleFraction,
        int PassableComponents
    ) {
        public bool BlockedIdeal => BlockedFraction < 0.95f;
        // Higher = more passable cells clustered in one connected area = MORE navigable
        // (the original consumer had this inverted, discarding nearly every good map).
        public bool LargestCompIdeal => LargestPassableComponentFraction > 0.25f;
        public bool TinyCompIdeal => TinyPassableComponentFraction < 0.03f;
        public bool SpeckleIdeal => SpeckleFraction < 0.003f;

        /// <summary>
        /// Only the two structural metrics disqualify a map, because rejecting one does not make BossMod
        /// careful — it makes BossMod pathfind over an arena with no obstacles in it at all, i.e. straight
        /// through cliffs. A speckled or fragmented-but-mostly-right bitmap still tells it where the walls
        /// are, so it beats nothing every time. TinyComp/Speckle measure noise rather than navigability and
        /// are kept as logged advisories only; the old AND-of-four (speckle &lt; 0.3%) rejected most real
        /// fate terrain. The surviving two catch the genuinely useless cases: a map that is almost entirely
        /// blocked, and one whose walkable space is shattered into disconnected islands.
        /// </summary>
        public bool IsBad => !BlockedIdeal || !LargestCompIdeal;
        public override string ToString() => $"Blocked: {BlockedFraction:P1}/{BlockedIdeal}, LargestComp: {LargestPassableComponentFraction:P1}/{LargestCompIdeal}, TinyComp: {TinyPassableComponentFraction:P1}/{TinyCompIdeal}, Speckle: {SpeckleFraction:P1}/{SpeckleIdeal}, PassableComps: {PassableComponents}";
    }
}
