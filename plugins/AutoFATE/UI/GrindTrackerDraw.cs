using AutoFATE.Core;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using ECommons.ImGuiMethods;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;

namespace AutoFATE.UI;

/// <summary>
/// Compact per-mode progress tracker for the minimised main window — the Yo-kai tracker's idea,
/// generalised to every grind mode. Clicking a row runs the game's own item search (the same
/// highlight you get from "Search for Item" in an item's menu). The row source, ordering and cell
/// drawing are shared with <see cref="GrindTrackerWindow"/> so the two never disagree.
/// </summary>
internal static class GrindTracker {
    private const long CacheMs = 250;
    private static List<TrackedItem>? _rows;
    private static string? _blocked;
    private static string? _note;
    private static string? _cachedForMode;
    private static long _cachedAt;

    internal static unsafe void SearchForItem(uint itemId) => ItemFinderModule.Instance()->SearchForItem(itemId);

    /// <summary>Zone the player is standing in — rows that drop here sort to the top and read gold.</summary>
    internal static uint CurrentTerritory => IPlayerState.Get().Territory.RowId;

    /// <summary>Whether the current mode has anything for the minimised window to show.</summary>
    internal static bool HasContent(FateModule module) {
        var mode = module.GetCurrentMode();
        return mode is YokaiGrindMode || Rows(mode).Count > 0;
    }

    internal static void Draw(FateModule module) {
        var mode = module.GetCurrentMode();
        if (mode is YokaiGrindMode) {
            // The Yo-kai mode has its own richer tracker — reuse it rather than flattening it.
            YokaiDraw.DrawCompactTable(showCompleted: false);
            return;
        }

        // Completed rows stay: unlike the seventeen Yo-kai minions no phase has more than a dozen
        // items, and hiding them would leave a finished phase drawing an empty table.
        var rows = SortedRows(mode, showCompleted: true);
        if (rows.Count == 0)
            return;

        using var table = ImRaii.Table("##GrindTracker", 3,
            ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.ScrollY | ImGuiTableFlags.SizingStretchProp);
        if (!table)
            return;

        ImGui.TableSetupColumn("Item", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Progress", ImGuiTableColumnFlags.WidthFixed, 90);
        ImGui.TableSetupColumn("Left", ImGuiTableColumnFlags.WidthFixed, 45);
        FreezeHeaderAndPinned(rows);
        ImGui.TableHeadersRow();

        foreach (var row in rows) {
            using var id = ImRaii.PushId((int)row.ItemId);
            ImGui.TableNextRow();

            ImGui.TableNextColumn();
            DrawStatusIcon(row);
            ImGui.SameLine();
            using (var _ = ImRaii.PushColor(ImGuiCol.Text, (uint)NameColour(row)))
                ImGui.TextV(row.Name);
            MakeItemSearchable(row);

            ImGui.TableNextColumn();
            DrawProgressCell(row);

            ImGui.TableNextColumn();
            DrawRemainingCell(row);
        }
    }

    internal static void DrawStatusIcon(TrackedItem row) {
        if (row.IsComplete)
            ImGui.Icon(FontAwesomeIcon.Check, Colors.Success, "Complete");
        else
            ImGui.Icon(FontAwesomeIcon.Times, Colors.Danger, "Still needed");
    }

    /// <summary>Gold for the zone you are in, dim for finished — the same reading the Yo-kai tracker
    /// gives its summoned/owned/unowned minions.</summary>
    internal static EzColor NameColour(TrackedItem row)
        => row.DropsIn(CurrentTerritory) && !row.IsComplete ? Colors.Gold
        : row.IsComplete ? Colors.Grey3
        : Colors.Grey2;

    /// <summary>Held/required, clickable: runs the game's item search for that item.</summary>
    internal static void DrawProgressCell(TrackedItem row) {
        ImGui.TextV($"{row.Have}/{row.Required}");
        MakeItemSearchable(row);
    }

    internal static void DrawRemainingCell(TrackedItem row) {
        if (row.Remaining == 0) {
            ImGui.TextDisabled("0");
            return;
        }
        using var left = ImRaii.PushColor(ImGuiCol.Text, (uint)Colors.Gold);
        ImGui.TextV(row.Remaining.ToString());
    }

    /// <summary>
    /// Freezes the header plus the pinned row, so the item for the zone you're standing in stays put
    /// while the rest scrolls. Only freezes two when such a row actually sorted first.
    /// </summary>
    internal static void FreezeHeaderAndPinned(IReadOnlyList<TrackedItem> rows)
        => ImGui.TableSetupScrollFreeze(0, rows.Count > 0 && rows[0].DropsIn(CurrentTerritory) ? 2 : 1);

    /// <summary>Click-to-search on whatever was just drawn, with a tooltip naming the zones it drops in.</summary>
    internal static void MakeItemSearchable(TrackedItem row) {
        if (row.Searchable) {
            if (ImGui.IsItemHovered())
                ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            if (ImGui.IsItemClicked())
                SearchForItem(row.ItemId);
        }

        var searchLine = row.Searchable ? $"Click to search your inventory for {row.Name}." : null;
        var zonesLine = row.Zones.Count > 0 ? $"Drops from fates in:\n{string.Join("\n", row.Zones.Select(z => z.Name))}" : null;
        if (searchLine is not null || zonesLine is not null)
            ImGui.TooltipOnHover(string.Join("\n", new[] { searchLine, zonesLine }.Where(s => s is not null)));
    }

    /// <summary>
    /// Display order: whatever drops in the zone you're in first, then unfinished before finished,
    /// then closest-to-done. The pinned row survives <paramref name="showCompleted"/> being off, so
    /// hiding completed items can't silently promote some unrelated row into the frozen slot.
    /// </summary>
    internal static List<TrackedItem> SortedRows(IFateGrindMode mode, bool showCompleted) {
        var here = CurrentTerritory;
        return [.. Rows(mode)
            .Where(r => showCompleted || !r.IsComplete || r.DropsIn(here))
            .OrderByDescending(r => r.DropsIn(here))
            .ThenBy(r => r.IsComplete)
            .ThenByDescending(r => r.Required <= 0 ? 1f : Math.Min(r.Have, r.Required) / (float)r.Required)];
    }

    internal static IReadOnlyList<TrackedItem> Rows(IFateGrindMode mode) {
        Refresh(mode);
        return _rows!;
    }

    /// <summary>Cached — the gemstone phase answers this by re-scanning the territory sheet for
    /// unlocked aetherytes, which is not something to do sixty times a second.</summary>
    internal static string? BlockedReason(IFateGrindMode mode) {
        Refresh(mode);
        return _blocked;
    }

    internal static string? TrackerNote(IFateGrindMode mode) {
        Refresh(mode);
        return _note;
    }

    /// <summary>
    /// Every row is an inventory scan and these windows draw each frame; a quarter second is still
    /// "instant" for an item counter (same policy as the Yo-kai tracker's cache).
    /// </summary>
    private static void Refresh(IFateGrindMode mode) {
        var now = Environment.TickCount64;
        if (_rows is not null && _cachedForMode == mode.DisplayName && now - _cachedAt < CacheMs)
            return;

        _rows = [.. mode.GetTrackedItems() ?? []];
        _blocked = mode.GetBlockedReason();
        _note = mode.GetTrackerNote();
        _cachedForMode = mode.DisplayName;
        _cachedAt = now;
    }
}
