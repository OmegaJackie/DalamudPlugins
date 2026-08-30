using AutoFATE.Core;
using AutoFATE.UI;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using ECommons;

namespace AutoFATE;

public sealed class Plugin : IDalamudPlugin {
    public const string PluginName = "AutoFATE";

    private readonly WindowSystem _windowSystem = new(PluginName);
    private readonly MainWindow _mainWindow;
    private readonly GrindTrackerWindow _trackerWindow;
    private readonly YokaiWindow _yokaiWindow;
    private readonly YokaiBookOverlay _yokaiOverlay;
    private readonly YokaiClickToSummon _yokaiClickToSummon;
    private readonly FateModule _module;

    private static readonly string[] _commands = ["/autofate", "/af", "/dwd"];

    public Plugin(IDalamudPluginInterface pluginInterface) {
        // Dispose is never called on a plugin whose ctor throws — unwind everything on failure.
        ECommonsMain.Init(pluginInterface, this);
        try {
            Svc.Init(pluginInterface, this);

            _module = new FateModule();
            _mainWindow = new MainWindow(_module);
            _trackerWindow = new GrindTrackerWindow(_module);
            _yokaiWindow = new YokaiWindow(_module);
            _yokaiOverlay = new YokaiBookOverlay();
            _yokaiClickToSummon = new YokaiClickToSummon();
            _module.ToggleWindow = _mainWindow.Toggle;
            _mainWindow.ToggleYokaiWindow = _yokaiWindow.Toggle;
            _mainWindow.ToggleTrackerWindow = _trackerWindow.Toggle;
            _trackerWindow.ToggleYokaiWindow = _yokaiWindow.Toggle;
            _windowSystem.AddWindow(_mainWindow);
            _windowSystem.AddWindow(_trackerWindow);
            _windowSystem.AddWindow(_yokaiWindow);
            _windowSystem.AddWindow(_yokaiOverlay);

            pluginInterface.UiBuilder.Draw += _windowSystem.Draw;
            pluginInterface.UiBuilder.OpenMainUi += _mainWindow.Toggle;
            pluginInterface.UiBuilder.OpenConfigUi += _mainWindow.Toggle;

            for (var i = 0; i < _commands.Length; i++) {
                Svc.Commands.AddHandler(_commands[i], new CommandInfo(OnCommand) {
                    HelpMessage = i == 0
                        ? "Opens the FATE tracker. '/autofate run <count>' runs until <count> fates are completed; '/autofate stop' stops; '/autofate tracker' opens the grind tracker; '/autofate yokai' opens the Yo-kai tracker; '/autofate role <host|client|off>' sets this character's multibox role. Aliases: /af, /dwd."
                        : "Alias for /autofate.",
                    ShowInHelp = i == 0,
                });
            }
        }
        catch {
            // it subscribes to the context menu and UiBuilder on construction; Svc.Dispose won't unhook either
            try { _yokaiClickToSummon?.Dispose(); } catch { /* best-effort unwind */ }
            try { _module?.Dispose(); } catch { /* best-effort unwind */ }
            try { Svc.Dispose(); } catch { /* best-effort unwind */ }
            ECommonsMain.Dispose();
            throw;
        }
    }

    private void OnCommand(string command, string arguments) {
        var args = arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        switch (args.FirstOrDefault()?.ToLowerInvariant()) {
            case null:
                _mainWindow.Toggle();
                break;
            case "run":
                if (args.Length > 1 && int.TryParse(args[1], out var count) && count >= 1)
                    _module.RunUntil(count);
                else
                    Svc.Chat.Print($"[{PluginName}] Usage: {command} run <count> — count must be a positive number.");
                break;
            case "stop":
                _module.Running = false;
                break;
            case "tracker":
                _trackerWindow.Toggle();
                break;
            case "yokai":
                switch (args.Length > 1 ? args[1].ToLowerInvariant() : null) {
                    case "debug": YokaiClickToSummon.ReportState(); break;
                    case "trace": YokaiClickToSummon.ToggleTrace(); break;
                    default: _yokaiWindow.Toggle(); break;
                }
                break;
            case "multibox":
                _module.Multibox.Report();
                break;
            case "role":
                if (args.Length <= 1 || !Enum.TryParse<Core.MultiboxRole>(args[1], ignoreCase: true, out var role) || !Enum.IsDefined(role)) {
                    Svc.Chat.Print($"[{PluginName}] Usage: {command} role <host|client|off>");
                }
                else if (Svc.PlayerState.ContentId == 0) {
                    // a logged-out role would be overwritten by the character's saved role on login anyway
                    Svc.Chat.Print($"[{PluginName}] Log in first — the multibox role is saved per character.");
                }
                else {
                    _module.Multibox.SetRole(role);
                    Svc.Chat.Print($"[{PluginName}] Multibox role set to {role}.");
                }
                break;
            default:
                Svc.Chat.Print($"[{PluginName}] Unknown subcommand '{args[0]}'. Use '{command}', '{command} run <count>', '{command} stop', '{command} tracker', '{command} yokai' or '{command} role <host|client|off>'. The Commands tab in the main window lists everything.");
                break;
        }
    }

    public void Dispose() {
        foreach (var command in _commands)
            Svc.Commands.RemoveHandler(command);
        _yokaiClickToSummon.Dispose();
        Svc.Interface.UiBuilder.Draw -= _windowSystem.Draw;
        Svc.Interface.UiBuilder.OpenMainUi -= _mainWindow.Toggle;
        Svc.Interface.UiBuilder.OpenConfigUi -= _mainWindow.Toggle;
        _windowSystem.RemoveAllWindows();
        _module.Dispose();
        Service.Config.Save();
        Svc.Dispose();
        ECommonsMain.Dispose();
    }
}
