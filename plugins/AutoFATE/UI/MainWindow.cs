using AutoFATE.CLib.ImGuiHelpers;
using AutoFATE.Core;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Text;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using ECommons.ImGuiMethods;
using System.Text;

namespace AutoFATE.UI;

public class MainWindow : MinimisableWindow {
    private readonly FateModule _module;
    private bool _focusSettingsTab;

    /// <summary>Set by the plugin so the Yo-kai title bar button can reach that window.</summary>
    public System.Action? ToggleYokaiWindow { get; set; }

    /// <summary>Set by the plugin so the tracker title bar button can reach that window.</summary>
    public System.Action? ToggleTrackerWindow { get; set; }

    public MainWindow(FateModule module) : base($"AutoFATE##{nameof(MainWindow)}") {
        _module = module;
        TitleBarButtons.Add(new TitleBarButton {
            Icon = FontAwesomeIcon.Cog,
            // Expand too: minimised draws no tabs, and a latched flag would otherwise fire the
            // Settings jump on some unrelated expand minutes later.
            Click = _ => {
                _focusSettingsTab = true;
                Expand();
            },
            ShowTooltip = () => {
                using var _ = ImRaii.Tooltip();
                ImGui.Text("Settings");
            },
        });
        TitleBarButtons.Add(new TitleBarButton {
            Icon = FontAwesomeIcon.Book,
            Click = _ => ToggleYokaiWindow?.Invoke(),
            ShowTooltip = () => {
                using var _ = ImRaii.Tooltip();
                ImGui.Text("Yo-kai tracker");
            },
        });
        TitleBarButtons.Add(new TitleBarButton {
            Icon = FontAwesomeIcon.Tasks,
            Click = _ => ToggleTrackerWindow?.Invoke(),
            ShowTooltip = () => {
                using var _ = ImRaii.Tooltip();
                ImGui.Text("Grind tracker (every phase)");
            },
        });
    }

    // With a grind mode selected the minimised view keeps that mode's tracker on screen,
    // so it needs the height of a small table rather than a single header row.
    protected override Vector2 MinimisedSize => new(700, GrindTracker.HasContent(_module) ? 340 : 90);

    public override bool DrawConditions() => IObjectTable.Get().LocalPlayer.Available;

    protected override void DrawContent(bool minimised) {
        _module.SyncRunningState();

        DrawMissingDependencies();
        DrawHeader(minimised);

        if (minimised) {
            if (GrindTracker.HasContent(_module)) {
                ImGui.Spacing();
                GrindTracker.Draw(_module);
            }
            return;
        }

        ImGui.Spacing();
        using var tabs = ImRaii.TabBar("##MainTabs");
        if (!tabs)
            return;

        using (var tab = ImRaii.TabItem("FATEs")) {
            if (tab)
                DrawFateList();
        }

        var focusSettings = _focusSettingsTab;
        _focusSettingsTab = false;
        using (var tab = ImRaii.TabItem("Settings", focusSettings ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None)) {
            if (tab)
                DrawSettings();
        }

        using (var tab = ImRaii.TabItem("Commands")) {
            if (tab)
                DrawCommandsTab();
        }

        using (var tab = ImRaii.TabItem("Dependencies")) {
            if (tab)
                DrawDependenciesTab();
        }
    }

    public override void OnClose() {
        base.OnClose();
        _module.Config.Save();
    }

    private void DrawHeader(bool minimised) {
        using var rounding = ImRaii.PushStyle(ImGuiStyleVar.FrameRounding, 6f);
        using var runButtonColor = ImRaii.PushColor(ImGuiCol.Button, _module.Running ? (uint)Colors.Negative : (uint)Colors.Positive)
            .Push(ImGuiCol.ButtonHovered, _module.Running ? (uint)Colors.NegativeHover : (uint)Colors.PositiveHover)
            .Push(ImGuiCol.ButtonActive, _module.Running ? (uint)Colors.NegativeActive : (uint)Colors.PositiveActive);

        if (ImGui.Button(_module.Running ? (_module.PendingStopWhenSafe ? "Stopping" : "Stop") : "Start")) {
            if (_module.Running) {
                if (ImGui.GetIO().KeyCtrl) {
                    _module.PendingStopWhenSafe = true;
                }
                else {
                    _module.ToggleRunning();
                    Svc.Navmesh.Stop();
                }
            }
            else {
                _module.ToggleRunning();
            }
        }
        ImGui.TooltipOnHover(_module.Running, $"Stop. Ctrl+{SeIconChar.MouseLeftClick.ToIconString()} soft stop");

        ImGui.SameLine();
        DrawHeaderChip(
            $"Automation: {(_module.Running ? Svc.Automation.Status : "Stopped")}",
            _module.Running ? Colors.ChipGold : Colors.ChipMuted,
            Colors.Grey2
        );

        ImGui.SameLine();
        DrawHeaderChip(
            $"State: {_module.CurrentState}",
            _module.Running && !_module.CurrentState.Equals("Idle", StringComparison.OrdinalIgnoreCase) ? Colors.ChipGold : Colors.ChipMuted,
            Colors.Grey2
        );

        ImGui.SameLine();
        DrawHeaderChip($"Completed: {_module.CompletedCount}", Colors.ChipInfo, Colors.Grey2);

        if (_module.RemainingUntilCompleted is { } remaining && remaining > 0) {
            ImGui.SameLine();
            DrawHeaderChip($"Remaining: {remaining}", Colors.ChipInfo, Colors.Grey2);
        }

        var modeRemaining = _module.GetCurrentMode().GetRemainingDisplay(_module);
        if (!string.IsNullOrEmpty(modeRemaining)) {
            ImGui.SameLine();
            var (bg, fg) = modeRemaining.Equals("Done", StringComparison.OrdinalIgnoreCase) ? (Colors.ChipMuted, Colors.Grey2) : (Colors.ChipInfo, Colors.Grey2);
            DrawHeaderChip(modeRemaining, bg, fg);
        }

        StatusChips.Multibox(_module);

        ImGui.SameLine();
        var style = ImGui.GetStyle();
        var rightButtonWidth = (ImGui.GetFrameHeight() + style.FramePadding.X * 2f) * 2f + style.ItemSpacing.X;
        var leftRightGap = style.ItemSpacing.X;
        var leftContentRight = ImGui.GetItemRectMax().X;
        if (Math.Max(0f, ImGui.GetContentRegionAvail().X - rightButtonWidth) is > 0 and var spacer) {
            ImGui.Dummy(new Vector2(spacer, 0f));
            ImGui.SameLine();
        }
        DrawModeButton();
        ImGui.SameLine();
        using (var _ = ImRaii.Disabled(_module.ModeSuppliesSwapZones))
        using (var zoneButtonColor = ImRaii.PushColor(ImGuiCol.Text, _module.HasSelectedSwapZones ? (uint)Colors.Gold : ImGui.GetColorU32(ImGuiCol.Text))) {
            if (ImGuiComponents.IconButton("###ZoneSelector", FontAwesomeIcon.Globe))
                _module.OpenZoneSelector();
        }
        if (_module.ModeSuppliesSwapZones) {
            // TooltipOnHover re-checks IsItemHovered without AllowWhenDisabled, which is always
            // false for a disabled item — set the tooltip directly under the flagged check instead
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                ImGui.SetTooltip("Zone list is defined by the current grind mode. Switch to None to select zones manually.");
        }
        else if (_module.HasSelectedSwapZones)
            ImGui.TooltipOnHover($"Swap Zones: {_module.SelectedSwapZones.Count}");
        else
            ImGui.TooltipOnHover("Swap Zones (uses default swap behaviour if none selected)");

        if (minimised) {
            MinimisedContentWidth = Math.Max(400, leftContentRight - ImGui.GetWindowPos().X + leftRightGap + rightButtonWidth + style.WindowPadding.X * 2);
        }
    }

    private void DrawFateList() {
        if (_module.GetOrderedFates().ToList() is not { Count: > 0 } fates) {
            ImGui.Spacing();
            ImGui.TextColored(Colors.Grey3.Vector4, "No fates match the current filters.");
            return;
        }

        ImGui.Spacing();
        foreach (var (fate, isAvailable) in fates) {
            using var id = ImRaii.PushId($"fate_{fate.Id}");

            var availableWidth = ImGui.GetContentRegionAvail().X;
            var displayName = FormatDisplayName(fate);
            var nameWidth = Math.Min(200f.Scale(), availableWidth * 0.4f);
            var progressWidth = Math.Max(1f, availableWidth - nameWidth - ImGui.GetStyle().ItemSpacing.X);

            using (var buttonStyle = ImRaii.PushStyle(ImGuiStyleVar.ButtonTextAlign, new Vector2(0, 0.5f)))
            using (var color = ImRaii.PushColor(ImGuiCol.Button, 0).Push(ImGuiCol.ButtonHovered, ImGui.GetColorU32(ImGuiCol.ButtonHovered)).Push(ImGuiCol.ButtonActive, ImGui.GetColorU32(ImGuiCol.ButtonActive))) {
                var isBlacklisted = _module.IsBlacklisted(fate);

                if (fate.HasBonus) {
                    ImGui.Image(ITextureProvider.Get().GetFromGameIcon(new Dalamud.Interface.Textures.GameIconLookup(65001)).GetWrapOrEmpty().Handle, new Vector2(ImGui.IconUnitHeight()));
                    ImGui.SameLine(0f, 0f);
                }

                using (var nameCol = ImRaii.PushColor(ImGuiCol.Text, isAvailable && !isBlacklisted ? (uint)EzColor.White : Colors.Grey3)) {
                    if (ImGui.Button(displayName, new Vector2(
                        fate.HasBonus
                            ? Math.Max(1f, nameWidth - ImGui.IconUnitWidth())
                            : nameWidth,
                        0
                    ))) {
                        if (Svc.Navmesh.IsRunning()) {
                            Svc.Navmesh.Stop();
                        }
                        else {
                            // taller search box: fate rings on slopes routinely miss the 5y default
                            var destination = fate.Position.RandomPoint(fate.Radius * 0.5f).OnMesh(5, 20);
                            // ground-only areas (e.g. U'Ghamaro Mines) must be entered on foot
                            var inFlight = ICondition.Get()[ConditionFlag.InFlight];
                            var fly = inFlight && FlightRestrictions.AllowFlight(IClientState.Get().TerritoryType, destination);
                            if (inFlight && !fly)
                                // a ground pathfind from mid-air faults silently inside vnav — say why nothing happened
                                Svc.Chat.Print("[AutoFATE] That fate is in a ground-only area (unpolished flight mesh) — land first, then click it again.");
                            else
                                Svc.Navmesh.PathfindAndMoveTo(destination, fly);
                        }
                    }
                }

                if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
                    _module.ToggleBlacklist(fate);

                ImGui.TooltipOnHover(BuildFateTooltip(fate, isBlacklisted));
            }

            ImGui.SameLine();

            var percentage = fate.Progress / 100f;
            var progressLabel = $"{fate.Progress}%";

            var cursorPos = ImGui.GetCursorPos();
            var labelSize = ImGui.CalcTextSize(progressLabel);
            var textX = Math.Max(0f, progressWidth - labelSize.X - 4f);

            using (var color = ImRaii.PushColor(ImGuiCol.PlotHistogram, _module.Config.BarColour))
                ImGui.ProgressBar(percentage, new Vector2(progressWidth, ImGui.GetFrameHeight()), "");

            ImGui.SetCursorPos(new Vector2(cursorPos.X + textX, cursorPos.Y + (ImGui.GetFrameHeight() - labelSize.Y) * 0.5f));
            ImGui.TextColored(
                ImGui.GetProgressBarTextColor(_module.Config.BarColour, ImGui.GetStyle().Colors[(int)ImGuiCol.FrameBg], percentage, textX, labelSize.X, progressWidth),
                progressLabel
            );

            ImGui.SpacedSeparator();
        }
    }

    private static void DrawMissingDependencies() {
        if (FateModule.MissingDependencies() is not { Count: > 0 } missing)
            return;

        ImGui.TextColored(Colors.Danger.Vector4, $"Missing required plugins: {string.Join(", ", missing)} — see the Dependencies tab.");
        ImGui.SpacedSeparator();
    }

    private void DrawModeButton() {
        if (ImGuiComponents.IconButton("###GrindMode", FontAwesomeIcon.List))
            ImGui.OpenPopup("###GrindModePopup");
        ImGui.TooltipOnHover($"Grind mode: {_module.GetCurrentMode().DisplayName}");

        using var popup = ImRaii.Popup("###GrindModePopup");
        if (popup) {
            foreach (var mode in FateGrindModes.All) {
                if (ImGui.Selectable(mode.DisplayName, mode.DisplayName == _module.SelectedModeId)) {
                    _module.SelectedModeId = mode.DisplayName;
                    ImGui.CloseCurrentPopup();
                }
            }
        }
    }

    private static void DrawHeaderChip(string text, EzColor background, EzColor textColor) => StatusChips.Chip(text, background, textColor);

    private void DrawSettings() {
        ImGui.Spacing();
        DrawMultiboxSettings();

        var settingsMirrored = _module.Multibox.Role == MultiboxRole.Client && _module.Multibox.SyncSettingsEnabled;
        if (settingsMirrored)
            ImGui.TextColored(new Vector4(1f, 0.85f, 0.4f, 1f), "Settings below are mirrored from the host and can't be edited here.");
        using var mirrorLock = ImRaii.Disabled(settingsMirrored);

        ImGui.DrawSection("Display");
        ImGui.TextV("Name format:");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(400f);
        ImGui.InputText("###DisplayNameFormat", ref _module.Config.DisplayNameFormat, 256);
        ImGuiComponents.HelpMarker("Available tokens: {Level}, {Name}, {Id}, {Progress}, {TimeRemaining}, {Distance}, {State}");
        ImGui.ColorEdit4("Progress bar colour", ref _module.Config.BarColour, ImGuiColorEditFlags.NoInputs);

        ImGui.DrawSection("Fate Filters");
        ImGui.SetNextItemWidth(120f);
        ImGui.InputInt("Max Duration (s)", ref _module.Config.MaxDuration);
        ImGuiComponents.HelpMarker("Skip fates whose total duration exceeds this.");
        ImGui.SetNextItemWidth(120f);
        ImGui.InputInt("Min Time Remaining (s)", ref _module.Config.MinTimeRemaining);
        ImGuiComponents.HelpMarker("Skip active fates with less time left than this.");
        ImGui.SetNextItemWidth(120f);
        ImGui.InputInt("Max Progress (%)", ref _module.Config.MaxProgress);
        ImGuiComponents.HelpMarker("Skip fates that are already past this completion percentage.");
        ImGui.Checkbox("Swap zones when no fates are available", ref _module.Config.SwapZones);

        ImGui.DrawSection("Priority Order");
        ImGui.TextWrapped("Fates are picked in this order, top criteria first. Drag the handles to reprioritise.");
        ImGui.Spacing();

        var sortOrder = _module.Config.SortOrder.ToList();
        for (var i = 0; i < sortOrder.Count; i++) {
            using var id = ImRaii.PushId($"sort_{i}");
            var item = sortOrder[i];
            var criteria = item.Criteria;

            var handleSize = new Vector2(ImGui.GetFrameHeight());
            ImGui.Button($"##Drag{i}", handleSize);

            ImGui.DragDropSource(i, "FATE_SORT_ITEM"u8, criteria.ToString().Replace("_", " "));
            ImGui.DragDropTarget(i, "FATE_SORT_ITEM"u8, sortOrder.Count, (sourceIndex, insertIndex) => {
                var dragged = sortOrder[sourceIndex];
                sortOrder.RemoveAt(sourceIndex);
                if (sourceIndex < insertIndex)
                    insertIndex--;
                sortOrder.Insert(insertIndex, dragged);
                _module.Config.SortOrder = sortOrder;
            });

            ImGui.TooltipOnHover("Drag to change priority order");

            ImGui.SameLine();

            ImGui.SetNextItemWidth(200);
            using (var critCombo = ImRaii.Combo($"###Criteria{i}", criteria.ToString().Replace("_", " "))) {
                if (critCombo) {
                    foreach (var crit in Enum.GetValues<FateSortCriteria>()) {
                        if (ImGui.Selectable(crit.ToString().Replace("_", " "), crit == criteria)) {
                            item.Criteria = crit;
                            _module.Config.SortOrder[i] = item;
                        }
                    }
                }
            }

            ImGui.SameLine();
            var arrowIcon = item.Descending ? FontAwesomeIcon.ArrowDown : FontAwesomeIcon.ArrowUp;
            if (ImGuiComponents.IconButton($"###Dir{i}", arrowIcon)) {
                item.Descending = !item.Descending;
                _module.Config.SortOrder[i] = item;
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(item.Descending ? "Descending (highest first)" : "Ascending (lowest first)");

            ImGui.SameLine();
            if (ImGuiComponents.IconButton($"###Remove{i}", FontAwesomeIcon.Trash)) {
                sortOrder.RemoveAt(i);
                _module.Config.SortOrder = sortOrder;
                i--;
                continue;
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Remove this sort criteria");
        }

        ImGui.Spacing();
        if (ImGui.Button("Add Sort Criteria"))
            ImGui.OpenPopup("###AddSortCriteria");

        using (var popup = ImRaii.Popup("###AddSortCriteria")) {
            if (popup) {
                foreach (var crit in Enum.GetValues<FateSortCriteria>()) {
                    if (ImGui.Selectable(crit.ToString().Replace("_", " "))) {
                        sortOrder.Add(new FateSortOrder { Criteria = crit, Descending = true });
                        _module.Config.SortOrder = sortOrder;
                    }
                }
            }
        }

        ImGui.SameLine();
        if (ImGui.Button("Reset to Default"))
            _module.Config.SortOrder = Configuration.DefaultSortOrder();

        ImGui.SameLine();
        if (ImGui.Button("Save Config"))
            _module.Config.Save();

        ImGui.Spacing();
    }

    private void DrawMultiboxSettings() {
        ImGui.DrawSection("Multibox", PushDown: false);

        var loggedIn = Svc.PlayerState.ContentId != 0;
        using (var _ = ImRaii.Disabled(!loggedIn)) {
            var role = _module.Multibox.Role;
            ImGui.SetNextItemWidth(160f);
            using (var combo = ImRaii.Combo("Role", role.ToString())) {
                if (combo) {
                    foreach (var candidate in Enum.GetValues<MultiboxRole>()) {
                        if (ImGui.Selectable(candidate.ToString(), candidate == role))
                            _module.Multibox.SetRole(candidate);
                    }
                }
            }
        }
        ImGuiComponents.HelpMarker("One game client is the Host (it picks fates and leads); the others are Clients (they mirror the host's start/stop, zone, and target fate). The role is saved per character. Run exactly one Host per machine.");
        if (!loggedIn)
            ImGui.TextColored(Colors.Grey3.Vector4, "Log in to choose a role for this character.");

        switch (_module.Multibox.Role) {
            case MultiboxRole.Host: {
                var clients = _module.Multibox.ConnectedClients;
                ImGui.TextV($"Status: {_module.Multibox.StatusText} — {clients.Count} client(s) connected");
                if (clients.Count > 0)
                    DrawClientTable(clients);
                else
                    ImGui.TextColored(Colors.Grey3.Vector4, "No clients yet. Set another game client's role to Client.");
                break;
            }
            case MultiboxRole.Client: {
                var multibox = _module.Multibox;
                ImGui.TextV($"Status: {multibox.StatusText}{(multibox.HostName is { Length: > 0 } host ? $" — host: {host}" : "")}");
                if (multibox.IsFollowingHost) {
                    ImGui.TextColored(Colors.Grey3.Vector4,
                        $"Mirroring: {ZoneName(multibox.HostTerritoryId ?? 0)} · {FateLabel(multibox.RemoteTargetFateId)}");
                }
                else if (multibox.Connected) {
                    ImGui.TextColored(new Vector4(1f, 0.85f, 0.4f, 1f), "Connected but no recent host update — waiting.");
                }
                var syncSettings = multibox.SyncSettingsEnabled;
                if (ImGui.Checkbox("Adopt the host's fate settings (filters, sort order, grind mode, zones)", ref syncSettings))
                    multibox.SetSyncSettings(syncSettings);
                break;
            }
        }

        if (ImGui.Checkbox("Only ride along with the host's teleports", ref _module.Config.DeclinePartyTeleportOffers))
            _module.Config.Save();
        ImGuiComponents.HelpMarker("Teleporting in a party offers everyone in the zone a free ride along. Multibox tools that auto-accept it "
            + "(e.g. BardToolbox's \"Auto Accept Teleport Invite\") take every offer, so this character gets dragged to whichever one teleported "
            + "first, overriding the aetheryte AutoFATE picked for its own fate. With this on, the host's offer is still accepted — it is free and "
            + "you are following the host anyway — and other characters' offers are refused. Only applies while a grind is running, so accepting "
            + "works normally when you are driving.");
    }

    private static void DrawClientTable(List<MultiboxClientInfo> clients) {
        using var table = ImRaii.Table("###MultiboxClients", 4, ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp);
        if (!table)
            return;

        ImGui.TableSetupColumn("Character", ImGuiTableColumnFlags.WidthStretch, 0.30f);
        ImGui.TableSetupColumn("State", ImGuiTableColumnFlags.WidthStretch, 0.22f);
        ImGui.TableSetupColumn("Zone", ImGuiTableColumnFlags.WidthStretch, 0.24f);
        ImGui.TableSetupColumn("Fate", ImGuiTableColumnFlags.WidthStretch, 0.24f);
        ImGui.TableHeadersRow();

        foreach (var client in clients) {
            ImGui.TableNextRow();

            ImGui.TableNextColumn();
            ImGui.TextV(client.Name);

            ImGui.TableNextColumn();
            var (state, color) = !client.Responding ? ("No response", new Vector4(1f, 0.4f, 0.4f, 1f))
                : client.Running && client.Following ? ("Synced", new Vector4(0.45f, 0.85f, 0.45f, 1f))
                : client.Running ? ("Running (unsynced)", new Vector4(1f, 0.85f, 0.4f, 1f))
                : ("Idle", Colors.Grey3.Vector4);
            ImGui.TextColored(color, state);

            ImGui.TableNextColumn();
            ImGui.TextV(client.TerritoryId == 0 ? "—" : ZoneName(client.TerritoryId));

            ImGui.TableNextColumn();
            ImGui.TextV(FateLabel(client.TargetFateId));
        }
    }

    private static readonly (string Command, string Description)[] _commandList = [
        ("/autofate", "Open or close this window."),
        ("/autofate run <count>", "Start, then stop automatically once <count> fates have been completed."),
        ("/autofate stop", "Stop the current run."),
        ("/autofate tracker", "Open or close the grind tracker — per-item progress, zones and totals for whichever phase is selected."),
        ("/autofate yokai", "Open or close the Yo-kai Watch tracker."),
        ("/autofate role <host|client|off>", "Set this character's multibox role (saved per character)."),
        ("/autofate multibox", "Print the multibox connection status to chat."),
        ("/autofate yokai debug", "Print the Yo-kai click-to-summon state to chat, for troubleshooting."),
        ("/autofate yokai trace", "Toggle Yo-kai event tracing; details land in /xllog."),
    ];

    private static void DrawCommandsTab() {
        ImGui.Spacing();
        ImGui.TextWrapped("Every command below also works with the shorter aliases /af and /dwd.");
        ImGui.Spacing();

        using var table = ImRaii.Table("###Commands", 2, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.SizingStretchProp);
        if (!table)
            return;

        ImGui.TableSetupColumn("Command", ImGuiTableColumnFlags.WidthFixed, 250);
        ImGui.TableSetupColumn("What it does", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableHeadersRow();

        foreach (var (command, description) in _commandList) {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.TextColored(Colors.Gold.Vector4, command);
            ImGui.TableNextColumn();
            ImGui.TextWrapped(description);
        }
    }

    private static void DrawDependenciesTab() {
        ImGui.Spacing();
        ImGui.TextWrapped("AutoFATE orchestrates other plugins rather than reimplementing them: Rotation Solver Reborn fights, "
            + "BossMod dodges and targets, vnavmesh walks and flies. Everything below must be installed and enabled.");
        ImGui.Spacing();

        using (var table = ImRaii.Table("###Dependencies", 3, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.SizingStretchProp)) {
            if (table) {
                ImGui.TableSetupColumn("Plugin", ImGuiTableColumnFlags.WidthFixed, 175);
                ImGui.TableSetupColumn("Role", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn("Status", ImGuiTableColumnFlags.WidthFixed, 80);
                ImGui.TableHeadersRow();

                DrawDependencyRow("vnavmesh",
                    "Pathing — walks and flies the character between fates.",
                    Svc.Navmesh.IsAvailable);
                DrawDependencyRow("BossMod Reborn",
                    "AoE avoidance, targeting, positioning, and fate level sync via the injected 'AutoFATE' preset. Vanilla BossMod works too.",
                    Service.BossMod.IsLoaded);
                DrawDependencyRow("Rotation Solver Reborn",
                    "Combat — runs your job's rotation against the targets BossMod picks.",
                    Service.RotationSolver.IsLoaded);
                DrawDependencyRow("TextAdvance",
                    "Skips talk and hand-in dialogue when activating fates and turning in collect-fate items.",
                    Service.TextAdvance.IsLoaded);
            }
        }

        ImGui.Spacing();
        ImGui.TextDisabled("vnavmesh is in the Puni.sh repo; BossMod Reborn and Rotation Solver Reborn are in the CombatReborn repo; "
            + "TextAdvance is in the main Dalamud repo.");
    }

    private static void DrawDependencyRow(string name, string role, bool loaded) {
        ImGui.TableNextRow();

        ImGui.TableNextColumn();
        ImGui.TextV(name);

        ImGui.TableNextColumn();
        using (var _ = ImRaii.PushColor(ImGuiCol.Text, (uint)Colors.Grey2))
            ImGui.TextWrapped(role);

        ImGui.TableNextColumn();
        if (loaded) {
            ImGui.Icon(FontAwesomeIcon.Check, Colors.Success);
            ImGui.SameLine();
            ImGui.TextColored(Colors.Success.Vector4, "Loaded");
        }
        else {
            ImGui.Icon(FontAwesomeIcon.Times, Colors.Danger);
            ImGui.SameLine();
            ImGui.TextColored(Colors.Danger.Vector4, "Missing");
        }
    }

    private static string ZoneName(uint territoryId)
        => Sheets.TerritoryType.GetRowOrNull(territoryId)?.PlaceName.ValueNullable?.Name.ToString() is { Length: > 0 } name
            ? name
            : $"Zone {territoryId}";

    private static string FateLabel(uint? fateId)
        => fateId is not { } id || id == 0
            ? "—"
            : Sheets.Fate.GetRowOrNull(id)?.Name.ToString() is { Length: > 0 } name ? name : $"FATE {id}";

    private string BuildFateTooltip(PublicEvent fate, bool isBlacklisted) {
        var sb = new StringBuilder();

        sb.AppendLine(fate.Name);
        sb.AppendLine($"Level {fate.Level}{(fate.HasBonus ? " · Bonus EXP" : "")} · {fate.Progress}% complete");
        sb.AppendLine($"Time remaining: {(fate.TimeRemaining >= 0 ? TimeSpan.FromSeconds(fate.TimeRemaining).ToString(@"mm\:ss") : "not started")}");
        if (IObjectTable.Get().LocalPlayer?.DistanceTo(fate.Position) is { } distance)
            sb.AppendLine($"Distance: {distance:F0}y");

        sb.AppendLine();
        var (isEligible, failedConditions) = _module.GetFateConditionDetails(fate);
        if (isEligible) {
            sb.AppendLine("Will be automated.");
        }
        else {
            sb.AppendLine("Skipped because:");
            foreach (var reason in failedConditions)
                sb.AppendLine($" · {reason}");
        }

        sb.Append($"Click: travel there (click again to stop) · Right-click: {(isBlacklisted ? "remove from blacklist" : "blacklist")}");
        return sb.ToString();
    }

    public string FormatDisplayName(PublicEvent fate) => _module.Config.DisplayNameFormat
        .Replace("{Level}", fate.Level.ToString())
        .Replace("{Name}", fate.Name)
        .Replace("{Id}", fate.Id.ToString())
        .Replace("{Progress}", fate.Progress.ToString())
        .Replace("{TimeRemaining}", fate.TimeRemaining >= 0 ? TimeSpan.FromSeconds(fate.TimeRemaining).ToString(@"mm\:ss") : "∞")
        .Replace("{Distance}", IObjectTable.Get().LocalPlayer?.DistanceTo(fate.Position).ToString("F1") ?? "?")
        .Replace("{State}", fate.State.ToString());
}
