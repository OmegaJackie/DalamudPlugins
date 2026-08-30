using System;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;

namespace LineMeUp;

public sealed class Plugin : IDalamudPlugin
{
    private const string Command = "/lineup";
    private const string CommandAlias = "/lmu";

    private readonly WindowSystem windowSystem = new("LineMeUp");
    private readonly Configuration config;
    private readonly MovementOverride movement;
    private readonly Aligner aligner;
    private readonly MainWindow mainWindow;

    public Plugin(IDalamudPluginInterface pluginInterface)
    {
        pluginInterface.Create<Svc>();

        config = pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        movement = new MovementOverride();
        aligner = new Aligner(config, movement);
        mainWindow = new MainWindow(config, aligner);
        windowSystem.AddWindow(mainWindow);

        Svc.Commands.AddHandler(Command, new CommandInfo(OnCommand)
        {
            HelpMessage = "Walk to your target's spot and match their facing. "
                        + "Subcommands: pos, rot, follow, stop, config.",
        });

        Svc.Commands.AddHandler(CommandAlias, new CommandInfo(OnCommand)
        {
            HelpMessage = "Alias for /lineup.",
            ShowInHelp = false,
        });

        pluginInterface.UiBuilder.Draw += windowSystem.Draw;
        pluginInterface.UiBuilder.OpenMainUi += ToggleUi;
        pluginInterface.UiBuilder.OpenConfigUi += ToggleUi;
    }

    public void Dispose()
    {
        Svc.PluginInterface.UiBuilder.Draw -= windowSystem.Draw;
        Svc.PluginInterface.UiBuilder.OpenMainUi -= ToggleUi;
        Svc.PluginInterface.UiBuilder.OpenConfigUi -= ToggleUi;

        Svc.Commands.RemoveHandler(Command);
        Svc.Commands.RemoveHandler(CommandAlias);

        windowSystem.RemoveAllWindows();
        mainWindow.Dispose();
        aligner.Dispose();
        movement.Dispose();
    }

    private void ToggleUi() => mainWindow.Toggle();

    private void OnCommand(string command, string arguments)
    {
        switch (arguments.Trim().ToLowerInvariant())
        {
            case "":
            case "go":
                aligner.Align();
                break;

            case "pos":
            case "position":
                aligner.Align(position: true, rotation: false);
                break;

            case "rot":
            case "rotation":
            case "face":
                aligner.Align(position: false, rotation: true);
                break;

            case "follow":
                aligner.ToggleFollow();
                break;

            case "stop":
            case "cancel":
                aligner.StopAll();
                Svc.Chat.Print("[Line Me Up] Stopped.");
                break;

            case "config":
            case "cfg":
            case "settings":
                ToggleUi();
                break;

            default:
                Svc.Chat.PrintError(
                    "[Line Me Up] Unknown subcommand. Use: pos, rot, follow, stop, config — "
                  + "or /lineup on its own to walk over and match their facing.");
                break;
        }
    }
}
