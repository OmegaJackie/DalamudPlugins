using AutoFATE.CLib.ImGuiHelpers;
using AutoFATE.Core;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Text;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;

namespace AutoFATE.UI;

/// <summary>
/// Per-phase progress tracker: the Yo-kai window's shape — run controls, phase picker, totals with a
/// progress bar, a per-row table and a detail pane — generalised to every other grind phase. One
/// window that redraws for whichever phase is selected rather than seven near-identical ones; the
/// Yo-kai phase keeps its own window (it tracks minions, not items) and is only summarised here.
/// </summary>
public sealed class GrindTrackerWindow : MinimisableWindow {
    private readonly FateModule _module;
    private bool _showCompleted = true;
    private uint _selectedItemId;

    /// <summary>Set by the plugin so the Yo-kai phase can hand off to the window that owns it.</summary>
    public System.Action? ToggleYokaiWindow { get; set; }

    public GrindTrackerWindow(FateModule module) : base($"Grind Tracker##{nameof(GrindTrackerWindow)}") {
        _module = module;
        Size = new Vector2(640, 540);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    protected override Vector2 MinimisedSize => new(340, 380);

    public override bool DrawConditions() => IObjectTable.Get().LocalPlayer.Available;

    protected override void DrawContent(bool minimised) {
        _module.SyncRunningState();
        DrawRunButton();

        // Multibox first, for the same reason the Yo-kai window leads with it: this window can start a
        // run on its own, and on a client it is the only thing saying the run is host-driven.
        StatusChips.Multibox(_module);

        if (minimised) {
            ImGui.Spacing();
            GrindTracker.Draw(_module);
            return;
        }

        var mode = _module.GetCurrentMode();

        ImGui.SameLine();
        StatusChips.Chip($"State: {_module.CurrentState}", _module.Running ? Colors.ChipGold : Colors.ChipMuted, Colors.Grey2);
        ImGui.TooltipOnHover("What the grind loop is doing right now.");

        if (mode.GetRemainingDisplay(_module) is { Length: > 0 } remaining) {
            ImGui.SameLine();
            var done = remaining.Equals("Done", StringComparison.OrdinalIgnoreCase);
            StatusChips.Chip(remaining, done ? Colors.ChipMuted : Colors.ChipInfo, Colors.Grey2);
            ImGui.TooltipOnHover("What this phase still owes, as the main window reports it.");
        }

        DrawPhaseSelector(mode);
        mode = _module.GetCurrentMode(); // the selector may have just switched phases

        DrawBlockedReason(mode);

        if (mode is YokaiGrindMode) {
            DrawYokaiSummary();
            return;
        }

        var all = GrindTracker.Rows(mode);
        if (all.Count == 0) {
            ImGui.SpacedSeparator();
            ImGui.TextColored(Colors.Grey3.Vector4, mode is NoneGrindMode
                ? "No phase selected — pick one above to track its items, or run without one to clear fates for their own sake."
                : "This phase tracks no items.");
            return;
        }

        // The detail pane picks from every row, not just the visible ones, so hiding completed items
        // can't blank the pane for an item you deliberately clicked. Its subject is resolved once and
        // handed to the table, which would otherwise highlight a different row than the one shown.
        var visible = GrindTracker.SortedRows(mode, _showCompleted);
        var subject = Selected(_showCompleted ? visible : GrindTracker.SortedRows(mode, showCompleted: true));

        ImGui.SpacedSeparator();
        DrawTotals(mode, all);
        DrawItemTable(visible, subject.ItemId);
        DrawDetail(subject);
    }

    private void DrawRunButton() {
        using var rounding = ImRaii.PushStyle(ImGuiStyleVar.FrameRounding, 6f);
        using var color = ImRaii.PushColor(ImGuiCol.Button, (uint)(_module.Running ? Colors.Negative : Colors.Positive))
            .Push(ImGuiCol.ButtonHovered, (uint)(_module.Running ? Colors.NegativeHover : Colors.PositiveHover))
            .Push(ImGuiCol.ButtonActive, (uint)(_module.Running ? Colors.NegativeActive : Colors.PositiveActive));

        if (ImGui.Button(_module.Running ? _module.PendingStopWhenSafe ? "Stopping" : "Stop" : "Start")) {
            if (!_module.Running) {
                _module.ToggleRunning();
            }
            else if (ImGui.GetIO().KeyCtrl) {
                _module.PendingStopWhenSafe = true;
            }
            else {
                _module.ToggleRunning();
                Svc.Navmesh.Stop();
            }
        }

        // Unlike the Yo-kai window this never switches phase for you — the phase combo below is the
        // only thing that changes what is being farmed.
        ImGui.TooltipOnHover(_module.Running
            ? $"Stop. Ctrl+{SeIconChar.MouseLeftClick.ToIconString()} finishes the current fate first."
            : $"Start grinding '{_module.GetCurrentMode().DisplayName}'.");
    }

    private void DrawPhaseSelector(IFateGrindMode current) {
        ImGui.SetNextItemWidth(280f);
        using (var combo = ImRaii.Combo("Phase##GrindTrackerPhase", current.DisplayName)) {
            if (combo) {
                foreach (var mode in FateGrindModes.All) {
                    if (ImGui.Selectable(mode.DisplayName, mode.DisplayName == current.DisplayName)) {
                        _module.SelectedModeId = mode.DisplayName;
                        _selectedItemId = 0; // the old selection names an item the new phase doesn't track
                    }
                }
            }
        }
        ImGui.TooltipOnHover("Which grind phase to run and track. Same list as the main window's mode button.");

        if (current is not YokaiGrindMode)
            return;

        // The Yo-kai phase has a phase of its own; showing it here keeps the picker meaningful rather
        // than sending you to another window just to change what "Yo-kai" currently means.
        ImGui.SameLine();
        var phase = _module.Config.YokaiPhase;
        if (ImGui.Enum("Yo-kai##YokaiPhase", ref phase)) {
            _module.Config.YokaiPhase = phase;
            _module.RefreshMode(); // the phase decides the zone set and item targets
        }
        ImGui.TooltipOnHover(
            "Auto: farm plain medals until you can afford the minions you're missing, then switch to weapons.\n"
            + "Minions: plain Yo-kai Medals only (any event fate, watch equipped).\n"
            + "Weapons: Legendary Medals only (correct minion out, per-minion zones).");
    }

    private static void DrawBlockedReason(IFateGrindMode mode) {
        if (GrindTracker.BlockedReason(mode) is not { Length: > 0 } blocked)
            return;

        ImGui.Icon(FontAwesomeIcon.ExclamationTriangle, Colors.Gold);
        ImGui.SameLine();
        ImGui.TextWrapped(blocked);
    }

    private void DrawTotals(IFateGrindMode mode, IReadOnlyList<TrackedItem> all) {
        var done = all.Count(r => r.IsComplete);
        var owed = all.Sum(r => r.Remaining);
        // Held is clamped per item: 900 spare of one drop must not paper over a second one sitting at zero.
        var held = all.Sum(r => Math.Min(r.Have, r.Required));
        var required = all.Sum(r => r.Required);

        ImGui.TextV($"Items {done}/{all.Count} complete — {owed} still to collect");
        ImGui.TooltipOnHover("Every item this phase tracks, summed. Surplus beyond an item's target isn't counted.");

        ImGui.SameLine();
        if (ImGui.Button(_showCompleted ? "Hide completed" : "Show completed"))
            _showCompleted = !_showCompleted;
        ImGui.TooltipOnHover($"{done} item(s) already at target.");

        ImGui.DrawProgressBar(held, Math.Max(required, 1), Colors.Gold);

        if (GrindTracker.TrackerNote(mode) is { Length: > 0 } note) {
            using var _ = ImRaii.PushColor(ImGuiCol.Text, (uint)Colors.Grey3);
            ImGui.TextWrapped(note);
        }
    }

    private void DrawItemTable(List<TrackedItem> rows, uint selected) {
        var here = GrindTracker.CurrentTerritory;

        // Bounded height so the detail pane below always has room; the table scrolls within it.
        var height = Math.Max(ImGui.GetFrameHeight() * 3f, ImGui.GetContentRegionAvail().Y * 0.55f);
        using var table = ImRaii.Table("##GrindTrackerItems", 4,
            ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.ScrollY | ImGuiTableFlags.SizingStretchProp,
            new Vector2(0, height));
        if (!table)
            return;

        ImGui.TableSetupColumn("Item", ImGuiTableColumnFlags.WidthFixed, 190);
        ImGui.TableSetupColumn("Zones", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Progress", ImGuiTableColumnFlags.WidthFixed, 90);
        ImGui.TableSetupColumn("Left", ImGuiTableColumnFlags.WidthFixed, 45);
        GrindTracker.FreezeHeaderAndPinned(rows);
        ImGui.TableHeadersRow();

        foreach (var row in rows) {
            using var id = ImRaii.PushId((int)row.ItemId);
            ImGui.TableNextRow();

            ImGui.TableNextColumn();
            GrindTracker.DrawStatusIcon(row);
            ImGui.SameLine();
            // Selection wins over the zone/completion colouring, so the detail pane's subject is
            // always identifiable in the table.
            using (var _ = ImRaii.PushColor(ImGuiCol.Text, (uint)(row.ItemId == selected ? Colors.Gold : GrindTracker.NameColour(row))))
                ImGui.TextV(row.Name);
            if (ImGui.IsItemHovered())
                ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            if (ImGui.IsItemClicked())
                _selectedItemId = row.ItemId;
            ImGui.TooltipOnHover("Click for where it drops and how much is left.");

            ImGui.TableNextColumn();
            DrawZoneCell(row, here);

            ImGui.TableNextColumn();
            GrindTracker.DrawProgressCell(row);

            ImGui.TableNextColumn();
            GrindTracker.DrawRemainingCell(row);
        }
    }

    private static void DrawZoneCell(TrackedItem row, uint here) {
        if (row.Zones.Count == 0) {
            ImGui.TextDisabled("—");
            return;
        }

        // One zone plus a count — the full list is a tooltip away, and several phases list a dozen.
        // The zone you're standing in leads when it's one of them, so the cell answers "does this drop
        // where I am" without opening the tooltip.
        var current = row.Zones.FirstOrDefault(z => z.TerritoryId == here);
        var inHere = current.TerritoryId != 0;
        var lead = inHere ? current : row.Zones[0];
        var label = row.Zones.Count == 1 ? lead.Name : $"{lead.Name}  +{row.Zones.Count - 1}";
        using (var _ = ImRaii.PushColor(ImGuiCol.Text, (uint)(inHere ? Colors.Gold : Colors.Grey3)))
            ImGui.TextV(label);
        ImGui.TooltipOnHover((inHere ? "You're in one of these now.\n\n" : "") + string.Join("\n", row.Zones.Select(z => z.Name)));
    }

    /// <summary>Falls back to the first row — which sorting has already made the one for this zone,
    /// or the closest to done — so the detail pane is never empty.</summary>
    private TrackedItem Selected(IReadOnlyList<TrackedItem> rows)
        => rows.FirstOrDefault(r => r.ItemId == _selectedItemId, rows.Count > 0 ? rows[0] : default);

    private static void DrawDetail(TrackedItem row) {
        if (row is not { Name.Length: > 0 })
            return; // no rows at all — the caller's empty state already said so

        ImGui.SpacedSeparator();

        using (var _ = ImRaii.PushColor(ImGuiCol.Text, (uint)Colors.Gold))
            ImGui.TextV(row.Name);

        if (row.IsComplete) {
            ImGui.Icon(FontAwesomeIcon.Check, Colors.ChipPositive);
            ImGui.SameLine();
            ImGui.TextWrapped($"{row.Have}/{row.Required} — nothing more owed for this item.");
        }
        else {
            ImGui.DrawProgressBar(Math.Min(row.Have, row.Required), Math.Max(row.Required, 1), Colors.Gold);
            ImGui.TextV($"{row.Remaining} more to collect");
        }

        if (row.Searchable) {
            ImGui.SameLine();
            if (ImGui.Button("Find in inventory"))
                GrindTracker.SearchForItem(row.ItemId);
            ImGui.TooltipOnHover("Runs the game's own item search — the same highlight as \"Search for Item\".");
        }

        if (row.Zones.Count == 0 || row.IsComplete)
            return; // nothing left to farm, so the zone list would only be noise

        var here = GrindTracker.CurrentTerritory;
        ImGui.TextDisabled("Run fates in:");
        foreach (var zone in row.Zones) {
            using var _ = ImRaii.PushColor(ImGuiCol.Text, (uint)(zone.TerritoryId == here ? Colors.Gold : Colors.Grey2));
            ImGui.BulletText(zone.TerritoryId == here ? $"{zone.Name} — you are here" : zone.Name);
        }
    }

    private void DrawYokaiSummary() {
        var (owned, total, held, needed) = YokaiDraw.Totals;
        var progress = YokaiDraw.Progress;
        var weapons = progress.Count(p => p.WeaponOwned);

        ImGui.SpacedSeparator();

        if (YokaiGrindMode.ActivePhase == YokaiPhase.Minions) {
            ImGui.TextV($"Minions {owned}/{total} — {held}/{needed} Yo-kai Medals to buy the remaining {Math.Max(0, total - owned)}");
            ImGui.DrawProgressBar(Math.Min(held, needed), Math.Max(needed, 1), Colors.Gold);
        }
        else {
            var medalsLeft = progress.Where(p => p.MinionOwned).Sum(p => p.MedalsRemaining);
            ImGui.TextV($"Weapons {weapons}/{progress.Count} — {medalsLeft} Legendary Medals owed on minions you own");
            ImGui.TooltipOnHover("Minions you don't own yet aren't counted; each will need 10 more once bought.");
            ImGui.DrawProgressBar(weapons, Math.Max(progress.Count, 1), Colors.Gold);
        }

        if (ImGui.Button("Open the Yo-kai tracker"))
            ToggleYokaiWindow?.Invoke();
        ImGui.TooltipOnHover("Per-minion detail, the book overlay and click-to-summon live in that window.");

        ImGui.SameLine();
        if (ImGui.Button(_showCompleted ? "Hide completed" : "Show completed"))
            _showCompleted = !_showCompleted;

        ImGui.Spacing();
        YokaiDraw.DrawCompactTable(_showCompleted);
    }
}
