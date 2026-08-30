using Dalamud.Plugin.Ipc;

namespace AutoFATE.IPC;

/// <summary>
/// IPC into Rotation Solver Reborn, which owns the combat rotation while BossMod only steers
/// (targeting, AoE avoidance, positioning). RSR registers its gates via EzIPC under the
/// "RotationSolverReborn." prefix; the plugin's InternalName is "RotationSolver".
/// </summary>
public sealed class RotationSolverIPC : IPluginService {
    public int InitOrder => 10;

    public const string InternalName = "RotationSolver";
    public string Name => "Rotation Solver Reborn";
    public bool IsLoaded => Svc.Interface.IsPluginLoaded(InternalName);

    /// <summary>
    /// Mirror of RSR's StateCommandType. Only the member values must match — Dalamud converts
    /// between mismatched enum types across the gate, which is how AutoDuty consumes the same gate.
    /// </summary>
    public enum StateCommandType : byte {
        Off,
        Auto,
        TargetOnly,
        Manual,
        AutoDuty,
        Henched,
        PvP,
    }

    // Registered by EzIPC as an Action (void method), so HasAction/InvokeAction, never InvokeFunc.
    private readonly ICallGateSubscriber<StateCommandType, object?> _changeOperatingMode;

    public RotationSolverIPC() {
        _changeOperatingMode = Svc.Interface.GetIpcSubscriber<StateCommandType, object?>("RotationSolverReborn.ChangeOperatingMode");
    }

    /// <summary>
    /// Henched mode: RSR runs the rotation on whatever is currently targeted and never picks targets
    /// itself — target selection belongs to BossMod's AutoTarget module. Henched, not Manual: RSR's
    /// own enum documents it as the mode for "any other plugin that requires RSR just do rotation and
    /// not targetting", and unlike Manual it is exempt from RSR's AutoOffAfterCombat idle timer
    /// (default ON, 30s), which would otherwise silently switch RSR off during any out-of-combat gap
    /// inside a fate — escort walks, collect hand-ins, waiting on boss waves.
    /// </summary>
    public bool Engage() => Invoke(StateCommandType.Henched);

    public bool Off() => Invoke(StateCommandType.Off);

    private bool Invoke(StateCommandType state) {
        if (!IsLoaded || !_changeOperatingMode.HasAction)
            return false;
        try {
            _changeOperatingMode.InvokeAction(state);
            return true;
        }
        catch (Exception ex) {
            // provider unloading between the check and the call — treat as "not available right now"
            Svc.Log.Warning($"[AutoFATE] RSR ChangeOperatingMode({state}) failed: {ex.Message}");
            return false;
        }
    }
}
