using System;
using System.Numerics;
using BozjaBuddyReborn.Automation;
using BozjaBuddyReborn.External;
using BozjaBuddyReborn.Game;
using BozjaBuddyReborn.Multibox;
using BozjaBuddyReborn.Relic;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game.InstanceContent;

namespace BozjaBuddyReborn.Windows;

public sealed class MainWindow : Window
{
    private static readonly Vector4 Green = new(0.40f, 0.85f, 0.40f, 1f);
    private static readonly Vector4 Yellow = new(0.95f, 0.80f, 0.30f, 1f);
    private static readonly Vector4 Red = new(0.95f, 0.45f, 0.45f, 1f);
    private static readonly Vector4 Grey = new(0.70f, 0.70f, 0.70f, 1f);
    private static readonly Vector4 Blue = new(0.50f, 0.75f, 1.00f, 1f);

    private readonly Configuration _config;
    private readonly BozjaController _controller;
    private readonly CombatDirector _director;
    private readonly NavmeshIpc _navmesh;
    private readonly MultiboxLink _link;
    private readonly CeCatalog _catalog;

    public MainWindow(
        Configuration config,
        BozjaController controller,
        CombatDirector director,
        NavmeshIpc navmesh,
        MultiboxLink link,
        CeCatalog catalog)
        : base("Bozja Buddy Reborn###BozjaBuddyRebornMain")
    {
        _config = config;
        _controller = controller;
        _director = director;
        _navmesh = navmesh;
        _link = link;
        _catalog = catalog;

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(560, 380),
            MaximumSize = new Vector2(1600, 1200),
        };
    }

    public override void Draw()
    {
        DrawControls();
        ImGui.Separator();

        if (ImGui.BeginTabBar("##bbr_tabs"))
        {
            if (ImGui.BeginTabItem("Engagements"))
            {
                DrawFieldState();
                ImGui.Separator();
                DrawEngagementTable();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Multibox"))
            {
                DrawMultibox();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Dependencies"))
            {
                DrawDependencies();
                ImGui.EndTabItem();
            }

            ImGui.EndTabBar();
        }
    }

    private void DrawControls()
    {
        var running = _controller.Running;

        if (ImGui.Button(running ? "Stop" : "Start", new Vector2(90, 0)))
            _controller.Toggle();

        ImGui.SameLine();
        var stateColour = _controller.State switch
        {
            ControllerState.Engaged => Green,
            ControllerState.Travelling => Blue,
            ControllerState.Holding => Yellow,
            ControllerState.Blocked => Red,
            _ => Grey,
        };
        ImGui.TextColored(stateColour, _controller.State.ToString());

        ImGui.SameLine();
        ImGui.TextUnformatted("-");
        ImGui.SameLine();
        ImGui.TextWrapped(_controller.Status);

        // The Lost Action driver gets its own line only while it has something to say. Its presses
        // also appear under the duty-action bar, but a load that never lands is reported nowhere
        // else - and "the driver quietly did nothing" is precisely the failure this line exists
        // to make visible.
        if (_config.AutoUseLostActions && _controller.LastLostAction.Length > 0)
            ImGui.TextColored(Grey, $"Lost Actions: {_controller.LastLostAction}");

        DrawPartySupport();
        DrawZonePicker();
    }

    /// <summary>
    /// Start and stop the party-support task, and say what it is doing.
    ///
    /// It gets a control on the main window rather than living in settings because it is a TASK,
    /// not a preference: it spends farmed charges on other people for as long as it runs, and the
    /// request for it asked for a stop in the same breath as the feature. A stop you have to go
    /// and find is not a stop.
    /// </summary>
    private void DrawPartySupport()
    {
        var task = _controller.PartySupport;

        // Nothing configured and not running: stay out of the way entirely rather than showing a
        // button that can only refuse.
        if (!task.Active && _config.PartySupportActions.Count == 0)
            return;

        if (ImGui.Button(task.Active ? "Stop party support" : "Start party support", new Vector2(170, 0)))
            task.Toggle();

        ImGui.SameLine();
        ImGui.TextColored(task.Active ? Green : Grey,
            task.Status.Length > 0 ? task.Status : "Party support is idle.");

        if (task.Active && task.Applied > 0)
        {
            ImGui.SameLine();
            ImGui.TextColored(Grey, $"({task.Applied} cast)");
        }
    }

    /// <summary>
    /// Restrict work to one third of the zone. Sits next to the Start button because it is the
    /// setting most likely to be changed between runs.
    /// </summary>
    private void DrawZonePicker()
    {
        var territory = Svc.ClientState.TerritoryType;
        var farm = _config.FarmMaterialItemId == 0 ? null : ZoneDrops.For(_config.FarmMaterialItemId);

        ImGui.TextUnformatted("Work zone:");
        ImGui.SameLine();

        if (farm is { } locked)
        {
            // A farm material already pins the region AND the activity, so leaving the picker
            // live here would just let the two contradict each other.
            ImGui.TextColored(Blue, FieldRegions.Label(locked.Territory, locked.Region));
            ImGui.SameLine();
            ImGui.TextColored(Grey, "(set by the farm target)");
            return;
        }

        var labelTerritory = BozjaZones.IsFieldZone(territory) ? territory : BozjaZones.Zadnor;
        var current = (int)_config.PreferredRegion;

        string[] options =
        [
            "Anywhere",
            FieldRegions.Label(labelTerritory, FieldRegionId.Zone1),
            FieldRegions.Label(labelTerritory, FieldRegionId.Zone2),
            FieldRegions.Label(labelTerritory, FieldRegionId.Zone3),
        ];

        ImGui.SetNextItemWidth(240);
        if (ImGui.Combo("##bbr_zone", ref current, options, options.Length))
        {
            _config.PreferredRegion = (byte)current;
            ConfigSaver.Save(_config);
        }

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(
                "Only take engagements and skirmishes in this third of the zone.\n" +
                "Zone names shown are for the zone you are in; the number (Z1/Z2/Z3) is what is stored,\n" +
                "so the same choice carries across both Bozja and Zadnor.");
    }

    private void DrawFieldState()
    {
        var territory = Svc.ClientState.TerritoryType;
        if (!BozjaZones.IsFieldZone(territory))
        {
            ImGui.TextColored(Yellow, $"Not in a Bozja field zone (currently {BozjaZones.Name(territory)}).");
            return;
        }

        ImGui.TextColored(Green, BozjaZones.Name(territory));
        ImGui.SameLine();

        // The zone third is the thing that decides which relic material drops here, so it gets
        // equal billing with the zone name.
        var region = _controller.CurrentRegion;
        ImGui.TextColored(region == FieldRegionId.Unknown ? Grey : Blue,
            $"- {FieldRegions.Label(territory, region)}");

        if (_config.FarmMaterialItemId != 0 && ZoneDrops.For(_config.FarmMaterialItemId) is { } farm)
        {
            var right = farm.Territory == territory && farm.Region == region;
            ImGui.TextColored(right ? Green : Yellow,
                right
                    ? $"Farming here: {farm.Describe()}"
                    : $"Farm target is {farm.Describe()} - you are not there.");
        }

        if (!FieldState.Available)
        {
            ImGui.TextColored(Yellow, "Bozja director state is not initialised yet.");
            return;
        }

        var rank = FieldState.ResistanceRank;
        var mettle = FieldState.Mettle;
        var needed = FieldState.MettleNeeded;

        ImGui.TextUnformatted($"Resistance Rank {rank}");
        ImGui.SameLine();
        if (needed > 0)
        {
            var fraction = Math.Clamp((float)mettle / needed, 0f, 1f);
            ImGui.ProgressBar(fraction, new Vector2(220, 0), $"{mettle:N0} / {needed:N0} mettle");
        }
        else
        {
            ImGui.TextUnformatted($"{mettle:N0} mettle");
        }
    }

    private void DrawEngagementTable()
    {
        var engagements = _controller.Engagements;
        if (engagements.Count == 0)
        {
            ImGui.TextColored(Grey, "No Critical Engagements published for this zone.");
            return;
        }

        if (!ImGui.BeginTable("##bbr_ce", 6,
                ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp |
                ImGuiTableFlags.ScrollY))
            return;

        ImGui.TableSetupColumn("Engagement", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("State", ImGuiTableColumnFlags.WidthFixed, 90);
        ImGui.TableSetupColumn("Time", ImGuiTableColumnFlags.WidthFixed, 60);
        ImGui.TableSetupColumn("Players", ImGuiTableColumnFlags.WidthFixed, 70);
        ImGui.TableSetupColumn("Progress", ImGuiTableColumnFlags.WidthFixed, 70);
        ImGui.TableSetupColumn("Skip", ImGuiTableColumnFlags.WidthFixed, 45);
        ImGui.TableHeadersRow();

        foreach (var ce in engagements)
        {
            ImGui.TableNextRow();

            ImGui.TableNextColumn();
            var label = ce.Name;
            if (ce.IsDuel)
                label += "  [duel]";
            else if (_catalog.IsLargeScale(ce.EventId))
                label += "  [large-scale]";

            var nameColour = ce.State switch
            {
                DynamicEventState.Battle => Green,
                DynamicEventState.Warmup => Yellow,
                DynamicEventState.Register => Blue,
                _ => Grey,
            };
            ImGui.TextColored(nameColour, label);
            if (ImGui.IsItemHovered() && _catalog.TryGet(ce.EventId, out var entry) && entry.Description.Length > 0)
                ImGui.SetTooltip(entry.Description);

            ImGui.TableNextColumn();
            ImGui.TextColored(nameColour, ce.StateText);

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(ce.IsLive ? FormatSeconds(ce.SecondsLeft) : "-");

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(ce.IsLive ? $"{ce.Participants}/{ce.MaxParticipants}" : "-");

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(ce.State == DynamicEventState.Battle ? $"{ce.Progress}%" : "-");

            ImGui.TableNextColumn();
            var blocked = _config.BlockedEngagements.Contains(ce.EventId);
            if (ImGui.Checkbox($"##skip{ce.EventId}", ref blocked))
            {
                if (blocked)
                    _config.BlockedEngagements.Add(ce.EventId);
                else
                    _config.BlockedEngagements.Remove(ce.EventId);
                ConfigSaver.Save(_config);
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Never travel to this engagement.");
        }

        ImGui.EndTable();
    }

    private void DrawMultibox()
    {
        var enabled = _config.MultiboxEnabled;
        if (ImGui.Checkbox("Coordinate with other game clients on this PC", ref enabled))
        {
            _config.MultiboxEnabled = enabled;
            ConfigSaver.Save(_config);
        }
        ImGui.TextColored(Grey,
            "Uses a local named pipe, the same approach AutoDuty takes. One client is the host and\n" +
            "picks the objective; the others follow it, so the boxes never split up.");

        if (!enabled)
            return;

        var isHost = _config.MultiboxIsHost;
        if (ImGui.Checkbox("This client is the host", ref isHost))
        {
            _config.MultiboxIsHost = isHost;
            ConfigSaver.Save(_config);
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Exactly one client in the group should be the host.");

        var barrier = _config.MultiboxArrivalBarrier;
        if (ImGui.Checkbox("Wait for every box to arrive before committing", ref barrier))
        {
            _config.MultiboxArrivalBarrier = barrier;
            ConfigSaver.Save(_config);
        }

        if (barrier)
        {
            var timeout = _config.MultiboxBarrierTimeoutSeconds;
            ImGui.SetNextItemWidth(160);
            if (ImGui.SliderInt("Barrier timeout (s)", ref timeout, 10, 180))
            {
                _config.MultiboxBarrierTimeoutSeconds = timeout;
                ConfigSaver.Save(_config);
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("The host releases the group anyway after this long, so one stuck box cannot stall everyone.");
        }

        ImGui.Separator();

        // Three states, not two. "Link down" covering both "still looking" and "there is no host
        // on this PC" is why the only available diagnostic was to toggle the checkbox and hope.
        switch (_link.State)
        {
            case LinkState.Connected:
                ImGui.TextColored(Green, "Link up");
                break;

            case LinkState.Connecting when _link.SecondsSearching < 8f:
                ImGui.TextColored(Yellow,
                    $"Connecting to the host... (attempt {_link.ConsecutiveFailures + 1})");
                break;

            case LinkState.Connecting:
                ImGui.TextColored(Red, "No host is listening on this PC.");
                ImGui.TextColored(Grey,
                    $"Searching for {_link.SecondsSearching:F0}s ({_link.ConsecutiveFailures} attempts" +
                    (_link.LastLinkError is { } err ? $", last: {err}" : "") + ").\n" +
                    "Tick \"This client is the host\" on exactly ONE of your boxes - every box\n" +
                    "defaults to client, so a fresh setup has no host at all. Running alone\n" +
                    "meanwhile: the deterministic pick still converges on the same objective.");
                break;

            default:
                ImGui.TextColored(_link.IsHost ? Green : Grey,
                    _link.IsHost ? "Hosting - waiting for boxes to join" : "Link idle");
                break;
        }

        ImGui.TextUnformatted($"Role: {(_link.IsHost ? "host" : "client")}   Peers: {_link.PeerCount}");

        if (_link.IsHost && _link.PeerCount == 0)
        {
            ImGui.TextColored(Grey,
                "No other box has connected yet. The others must have multibox enabled and must\n" +
                "NOT be ticked as host.");
        }

        if (_link.IsHost && _config.MultiboxArrivalBarrier)
            ImGui.TextUnformatted($"Arrived: {_link.ArrivedCount}/{_link.PeerCount}");

        var objective = _link.Objective;
        ImGui.TextUnformatted(objective.IsSet
            ? $"Shared objective: {objective.Kind} #{objective.Id}"
            : "Shared objective: none");

        if (_link.IsHost)
        {
            if (ImGui.Button("Start all"))
                _link.BroadcastRunState(true);
            ImGui.SameLine();
            if (ImGui.Button("Stop all"))
                _link.BroadcastRunState(false);
        }

        ImGui.Separator();
        if (ImGui.Button("Open the multiboxer panel"))
            OnOpenMultiboxer?.Invoke();
        ImGui.TextColored(Grey,
            "Drive every box from one place - start/stop, push Lost Action loadouts, and send" +
            " boxes to the nearest aetheryte or cache. Also on /bbr boxes.");

        ImGui.Spacing();
        if (ImGui.Button("Open the group duty-action hotbar"))
            OnOpenDutyActions?.Invoke();
        ImGui.TextColored(Grey,
            "Every connected box's two Duty Action slots on one bar - icon, charges and recharge,\n" +
            "the same information you see for your own. Also on /bbr duty.");

        ImGui.Separator();
        ImGui.TextColored(Grey,
            "Even with the link down, every box runs the same deterministic pick, so they still\n" +
            "converge on the same engagement instead of scattering.");
    }

    /// <summary>Raised by the "open the hotbar" button; the plugin owns that window.</summary>
    public Action? OnOpenDutyActions { get; set; }

    /// <summary>Raised by the "open the multiboxer panel" button.</summary>
    public Action? OnOpenMultiboxer { get; set; }

    private void DrawDependencies()
    {
        Row("vnavmesh", _navmesh.Available, "movement and pathfinding - required");

        var avoidance = _director.Avoidance;
        var fork = avoidance.Fork;

        Row(avoidance.ForkName, _director.AvoidanceAvailable, fork switch
        {
            BossModFork.Reborn => "AoE avoidance only - AI in ForbidActions, no AI preset, autorotation force-disabled",
            BossModFork.Original => "AoE avoidance only - AI mode with auto-target off, no rotation preset active",
            _ => "AoE avoidance - either fork works; Reborn is preferred",
        });

        if (avoidance.UnavailableReason is { } reason)
        {
            ImGui.Indent();
            ImGui.TextColored(Red, reason);
            ImGui.TextColored(Grey,
                "BossMod Reborn (FFXIV-CombatReborn/BossmodReborn) gives the fullest integration - it reports\n" +
                "when it is dodging, so travel yields to it. The original awgil BossMod also works, with\n" +
                "the differences listed below.");
            ImGui.Unindent();
        }

        if (avoidance.BothForksLoaded)
        {
            ImGui.Indent();
            ImGui.TextColored(Yellow,
                "Both BossMod forks are loaded. They register the same BossMod.* IPC gates, so whichever\n" +
                "loaded last owns them and unloading either strips them - unload one. Driving Reborn.");
            ImGui.Unindent();
        }

        Row("Rotation Solver Reborn", _director.RotationAvailable, "the rotation");

        ImGui.Separator();
        ImGui.TextColored(Grey, "Roles");
        ImGui.BulletText($"{avoidance.ForkName} moves you out of telegraphed AoEs. It presses no buttons.");
        ImGui.BulletText("RSR runs the rotation. It is the only thing queueing actions.");

        if (fork == BossModFork.Original)
        {
            ImGui.BulletText("The original closes on the target itself (FollowSlot); our own approach stands down.");
            ImGui.BulletText("It cannot say when it is dodging, so travel cannot yield to a dodge mid-path -");
            ImGui.Indent();
            ImGui.TextColored(Grey,
                "it only dodges while we are not pathing (holds, and once arrived). Reborn dodges en route too.");
            ImGui.Unindent();
        }

        if (_director.AvoidanceAvailable)
        {
            ImGui.Separator();
            ImGui.TextColored(Grey, "Avoidance telemetry");
            if (avoidance.SteeringKnown)
            {
                var (zones, navigating) = _director.AvoidanceSignals;

                // Both halves shown separately, because they mean different things and confusing
                // them is what stalled the runner: "wants to move" is true almost always once
                // Reborn's AI is on, and only "danger zones > 0" means there is a mechanic.
                ImGui.TextUnformatted($"Danger zones active: {zones}");
                ImGui.TextUnformatted($"BossMod wants to move: {(navigating ? "yes" : "no")}");
                ImGui.TextColored(_director.AvoidanceIsSteering ? Yellow : Grey,
                    $"=> yielding movement: {(_director.AvoidanceIsSteering ? "yes" : "no")}" +
                    (_controller.SecondsYielding > 0f ? $" ({_controller.SecondsYielding:F0}s)" : ""));

                if (navigating && zones == 0)
                {
                    ImGui.TextColored(Grey,
                        "BossMod wants to move but reports no telegraphed danger - that is\n" +
                        "repositioning, not a dodge, so travel keeps the path. This is normal.");
                }
            }
            else
            {
                ImGui.TextColored(Grey, "Danger zones / steering: not reported by this fork (no Hints.* or AI.* gates).");
            }

            // Reborn cannot be asked what state it is in, so what we can honestly show is what we
            // last SENT and how long ago. A last-sent age that keeps climbing past the re-assert
            // interval means the heartbeat is off or is being withheld. (The original's AI on/off
            // is a config value and IS read back before each heartbeat write.)
            ImGui.TextUnformatted(
                $"Last sent - AI: {Age(avoidance.SecondsSinceSent)}, " +
                $"rotation: {_director.Rotation.CurrentMode?.ToString() ?? "nothing"} " +
                $"{Age(_director.Rotation.SecondsSinceSent)}");
            if (ImGui.Button("Re-apply avoidance-only config"))
                avoidance.ApplyAvoidanceOnlyConfig(force: true);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(fork == BossModFork.Original
                    ? "Re-asserts (read-before-write): AIConfig.ForbidActions = true, AIConfig.ForbidMovement = false,\n" +
                      "and Presets.ClearActive if any user preset or force-disable is in the active list.\n" +
                      "Deliberately NOT SetForceDisabled - in the original that removes the AI preset (the dodging)."
                    : "Re-sends: /bmrai forbidactions on, /bmrai forbidmovement off,\n" +
                      "AI.SetPreset(\"\") and Presets.SetForceDisabled().");

            ImGui.SameLine();
            if (ImGui.Button($"Restore {avoidance.ForkName}"))
                avoidance.ReleaseControl();
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Stop the AI and put back the settings and presets we changed.");
        }
        return;

        static void Row(string name, bool ok, string note)
        {
            ImGui.TextColored(ok ? Green : Red, ok ? "OK  " : "MISSING  ");
            ImGui.SameLine();
            ImGui.TextUnformatted(name);
            ImGui.SameLine();
            ImGui.TextColored(Grey, $"- {note}");
        }

        static string Age(float seconds) => seconds < 0f ? "(never)" : $"{seconds:F0}s ago";
    }

    private static string FormatSeconds(uint seconds)
    {
        var span = TimeSpan.FromSeconds(seconds);
        return span.TotalHours >= 1
            ? $"{(int)span.TotalHours}:{span.Minutes:D2}:{span.Seconds:D2}"
            : $"{span.Minutes}:{span.Seconds:D2}";
    }
}
