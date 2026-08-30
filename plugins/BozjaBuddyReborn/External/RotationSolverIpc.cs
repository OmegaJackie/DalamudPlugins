using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using ECommons.Automation;

namespace BozjaBuddyReborn.External;

/// <summary>
/// Rotation Solver Reborn - the ROTATION half of the combat split (BossMod owns avoidance).
///
/// Gate and signature verified against RSR's own IPC/IPCProvider.cs, which registers via
/// ECommons EzIPC under the prefix "RotationSolverReborn":
///     RotationSolverReborn.ChangeOperatingMode -> Action&lt;StateCommandType&gt;
///
/// StateCommandType is an RSR-internal byte enum, mirrored below. Note it has SEVEN members
/// as of current RSR - Henched and PvP were added after the commonly-circulated five-member
/// listing, and the numeric values are what actually cross the wire.
///
/// If the typed gate cannot bind (RSR absent, or the enum fails to marshal across the
/// assembly boundary) this degrades to RSR's /rotation command, which RSR registers with
/// Dalamud and which therefore reaches it through ECommons' Chat helper.
/// </summary>
public sealed class RotationSolverIpc
{
    /// <summary>Mirror of RSR's StateCommandType (byte-backed). Values must match RSR.</summary>
    public enum StateCommand : byte
    {
        Off = 0,
        Auto = 1,
        TargetOnly = 2,
        Manual = 3,
        AutoDuty = 4,
        Henched = 5,
        PvP = 6,
    }

    private readonly ICallGateSubscriber<StateCommand, object>? _changeOperatingMode;
    private readonly EdgeTrigger<StateCommand> _mode;

    public RotationSolverIpc(IDalamudPluginInterface pi)
    {
        try
        {
            _changeOperatingMode = pi.GetIpcSubscriber<StateCommand, object>(
                "RotationSolverReborn.ChangeOperatingMode");
        }
        catch
        {
            _changeOperatingMode = null;
        }

        _mode = new EdgeTrigger<StateCommand>(Send);
    }

    public bool Available
    {
        get { try { return _changeOperatingMode?.HasAction ?? false; } catch { return false; } }
    }

    /// <summary>The mode last actually dispatched, for the UI.</summary>
    public StateCommand? CurrentMode => _mode.Current;

    /// <summary>Seconds since the mode was last actually sent, for the UI.</summary>
    public float SecondsSinceSent => _mode.SecondsSinceSent;

    /// <summary>
    /// How often to re-send the current mode even when it has not changed. RSR exposes no way to
    /// read its state back (verified against its IPCProvider - ChangeOperatingMode is write-only
    /// and there is no getter), so a periodic re-assert is the only defence against it being
    /// switched off behind us. 0 disables.
    /// </summary>
    public long ReassertIntervalMs
    {
        get => _mode.ReassertIntervalMs;
        set => _mode.ReassertIntervalMs = value;
    }

    /// <summary>
    /// Auto, not Manual. Inside a Critical Engagement every mob is already hostile and this
    /// plugin does not hard-target for RSR, so RSR should select and attack on its own.
    /// (Manual exists for pulling neutral open-world mobs, which never applies in Bozja.)
    /// </summary>
    public void Engage() => _mode.Request(StateCommand.Auto);

    /// <summary>
    /// Rotation without target selection - used when BossMod's AI is steering the character
    /// through a mechanic and we do not want RSR yanking the target mid-dodge.
    /// </summary>
    public void EngageTargetOnly() => _mode.Request(StateCommand.TargetOnly);

    public void Disengage() => _mode.Request(StateCommand.Off);

    public void Resync() => _mode.Resync();

    private void Send(StateCommand mode)
    {
        try
        {
            if (_changeOperatingMode is { } gate)
            {
                gate.InvokeAction(mode);
                return;
            }
        }
        catch
        {
            // Fall through to the command path.
        }

        try { Chat.ExecuteCommand($"/rotation {mode}"); }
        catch { /* RSR not installed - no rotation driver, which is a valid state */ }
    }
}
