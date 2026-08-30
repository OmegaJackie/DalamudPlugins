using AutoFATE.CLib.ImGuiHelpers;
using AutoFATE.Core;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.Text;
using Dalamud.Game.Gui.ContextMenu;
using Dalamud.Game.NativeWrapper;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using CSDetailKind = FFXIVClientStructs.FFXIV.Client.Enums.DetailKind;

namespace AutoFATE.UI;

/// <summary>Shared state and drawing for the tracker, the book overlay, and the right-click entry.</summary>
internal static class YokaiDraw {
    /// <summary>Main command 62 = Minion Guide, the general minion collection.</summary>
    private const uint MinionGuideCommand = 62;

    internal const string GuideAddon = "MinionNoteBook";

    private static unsafe AgentInterface* MedalliumAgent => AgentModule.Instance()->GetAgentByInternalId(AgentId.YkwNote);

    /// <summary>The Yo-kai Watch Medallium — the event's own book, not the general minion list.</summary>
    internal static unsafe void OpenMedallium() {
        var agent = MedalliumAgent;
        if (agent is not null)
            agent->Show();
    }

    /// <summary>Weapon completion reads achievements, and the game only sends that list once
    /// something asks for it — opening this window is what asks.</summary>
    internal static unsafe void OpenAchievements() => AgentAchievement.Instance()->Show();

    internal static unsafe void OpenMinionGuide() {
        var ui = UIModule.Instance();
        if (ui is not null)
            ui->ExecuteMainCommand(MinionGuideCommand);
    }

    /// <summary>
    /// The Medallium's uld is MinionNotebookYKW, but addon names don't reliably match their uld casing
    /// — the sibling guide's uld is MinionNotebook while its addon is MinionNoteBook. The earlier
    /// "YkwNote" guess (from the agent name) matched nothing at all, so these are tried in order and
    /// whatever the client actually answers to is cached.
    /// </summary>
    private static readonly string[] _medalliumCandidates = ["YKWNote", "MinionNotebookYKW", "YkwNote"];

    private static string? _medalliumName;
    private static long _lastScanAt;

    internal static string MedalliumAddon => _medalliumName ?? _medalliumCandidates[0];

    internal static AtkUnitBasePtr Medallium {
        get {
            if (_medalliumName is { } known)
                return Svc.GameGui.GetAddonByName(known, 1);

            foreach (var candidate in _medalliumCandidates) {
                if (Svc.GameGui.GetAddonByName(candidate, 1) is { IsNull: false } found) {
                    _medalliumName = candidate;
                    return found;
                }
            }

            // None matched, so fall back to scanning the loaded units for it. Throttled: NameString
            // allocates per entry and this is reached from a per-frame draw path.
            var now = Environment.TickCount64;
            if (now - _lastScanAt < 500)
                return default;
            _lastScanAt = now;

            if (DiscoverMedallium() is not { } discovered)
                return default;
            _medalliumName = discovered;
            Svc.Log.Info($"[AutoFATE] Discovered Yo-kai Medallium addon: {discovered}");
            return Svc.GameGui.GetAddonByName(discovered, 1);
        }
    }

    /// <summary>Scan every loaded addon for the YKW minion notebook, so a casing change can't break it.</summary>
    private static unsafe string? DiscoverMedallium() {
        var units = &RaptureAtkUnitManager.Instance()->AllLoadedUnitsList;
        for (var i = 0; i < units->Count && i < units->Entries.Length; i++) {
            var unit = units->Entries[i].Value;
            if (unit is null)
                continue;
            var name = unit->NameString;
            if (name.Contains("YKW", StringComparison.OrdinalIgnoreCase))
                return name;
        }
        return null;
    }

    internal static AtkUnitBasePtr Guide => Svc.GameGui.GetAddonByName(GuideAddon, 1);

    /// <summary>Medallium wins when both are open — it is the Yo-kai-specific book.</summary>
    internal static AtkUnitBasePtr AnchorWindow => Medallium.IsVisible ? Medallium : Guide;

    private static uint _selected;
    private static long _selectedAt;

    /// <summary>
    /// Unlatched: the Yo-kai entry under the cursor right now, or 0. Clicks must use this — the
    /// latched value would summon whatever was hovered minutes ago on a click over empty space.
    /// </summary>
    internal static uint CurrentHover() {
        var action = Svc.GameGui.HoveredAction;
        if ((int)action.DetailKind == (int)CSDetailKind.Companion && YokaiGrindMode.IsYokaiMinion(action.ActionId))
            return action.ActionId;

        // The Medallium lists medals, not summonable minions, so its hover arrives as an item id.
        // High-quality ids are offset by a million; nothing here is ever HQ, but normalise anyway.
        if (Svc.GameGui.HoveredItem is not 0 and var hovered
            && YokaiGrindMode.MinionForItem((uint)(hovered % 1_000_000)) is { } minion)
            return minion;

        return 0;
    }

    /// <summary>
    /// Latched, because the book clears its hover state the moment the cursor moves toward the panel.
    /// Display uses this so the detail stays readable; clicks use <see cref="CurrentHover"/>.
    /// </summary>
    internal static uint TrackHover() {
        if (CurrentHover() is not 0 and var minion) {
            _selected = minion;
            _selectedAt = Environment.TickCount64;
        }
        return _selected;
    }

    /// <summary>
    /// What was hovered within the last <paramref name="maxAgeMs"/>. Clicking can dismiss the game's
    /// hover detail before the next frame reads it, which would leave a genuine click on an entry
    /// resolving to nothing; a short window covers that without reaching back to a stale selection.
    /// </summary>
    internal static uint RecentHover(long maxAgeMs)
        => _selected != 0 && Environment.TickCount64 - _selectedAt <= maxAgeMs ? _selected : 0;

    private const long CacheMs = 250;
    private static List<YokaiGrindMode.YokaiProgress>? _progress;
    private static YokaiGrindMode.YokaiTotals _totals;
    private static uint? _pinned;
    private static long _cachedAt;

    /// <summary>
    /// Every field here is an inventory scan, and these windows draw each frame — recomputing all
    /// seventeen at 60fps costs far more than a tracker is worth. A quarter second is well inside
    /// "instant" for a medal counter.
    /// </summary>
    private static void Refresh() {
        var now = Environment.TickCount64;
        if (_progress is not null && now - _cachedAt < CacheMs)
            return;

        _progress = [.. YokaiGrindMode.GetProgress()];
        _totals = YokaiGrindMode.GetTotals();
        _pinned = YokaiGrindMode.PinnedFor(IPlayerState.Get().Territory.RowId);
        _cachedAt = now;
    }

    /// <summary>Minion the current zone is about, pinned to the top of the tracker table.</summary>
    internal static uint? Pinned {
        get {
            Refresh();
            return _pinned;
        }
    }

    internal static IReadOnlyList<YokaiGrindMode.YokaiProgress> Progress {
        get {
            Refresh();
            return _progress!;
        }
    }

    internal static YokaiGrindMode.YokaiTotals Totals {
        get {
            Refresh();
            return _totals;
        }
    }

    internal static YokaiGrindMode.YokaiProgress? Find(uint companionId) {
        foreach (var p in Progress)
            if (p.CompanionId == companionId)
                return p;
        return null;
    }

    /// <summary>Weapon rows in display order: the zone's pinned minion first, then unfinished before
    /// finished, owned before unowned, closest-to-done first.</summary>
    internal static List<YokaiGrindMode.YokaiProgress> SortedRows(bool showCompleted, uint? pinned)
        // The pinned row is kept even when completed and even when completed rows are hidden —
        // dropping it would silently freeze some other minion's row in its place.
        => [.. Progress
            .Where(p => showCompleted || !p.WeaponOwned || p.CompanionId == pinned)
            .OrderByDescending(p => p.CompanionId == pinned)
            .ThenBy(p => p.WeaponOwned)
            .ThenByDescending(p => p.MinionOwned)
            .ThenByDescending(p => p.Medals)];

    /// <summary>Two-column tracker table used by the minimised Yo-kai window and the minimised main
    /// window's grind tracker.</summary>
    internal static void DrawCompactTable(bool showCompleted) {
        var pinned = Pinned;
        var progress = SortedRows(showCompleted, pinned);

        using var table = ImRaii.Table("##YokaiCompact", 2,
            ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.ScrollY | ImGuiTableFlags.SizingStretchProp);
        if (!table)
            return;

        ImGui.TableSetupColumn("Minion", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Medals", ImGuiTableColumnFlags.WidthFixed, 70);
        FreezeHeaderAndPinned(pinned, progress);
        ImGui.TableHeadersRow();

        foreach (var p in progress) {
            using var id = ImRaii.PushId((int)p.CompanionId);
            ImGui.TableNextRow();

            ImGui.TableNextColumn();
            DrawStatusIcon(p);
            ImGui.SameLine();
            DrawMinionName(p);

            ImGui.TableNextColumn();
            DrawMedalCell(p);
        }
    }

    /// <summary>
    /// Freezes the header plus the pinned row, so the minion for the zone you're standing in stays put
    /// while the rest scrolls. Only freezes two when that row is actually first — otherwise it would
    /// pin whichever entry happened to sort there.
    /// </summary>
    internal static void FreezeHeaderAndPinned(uint? pinned, List<YokaiGrindMode.YokaiProgress> rows)
        => ImGui.TableSetupScrollFreeze(0, pinned is { } id && rows.Count > 0 && rows[0].CompanionId == id ? 2 : 1);

    internal static void DrawStatusIcon(YokaiGrindMode.YokaiProgress p) {
        if (p.WeaponOwned)
            ImGui.Icon(FontAwesomeIcon.Check, Colors.Success, "Weapon acquired");
        else
            ImGui.Icon(FontAwesomeIcon.Times, Colors.Danger, p.MinionOwned ? "Weapon not acquired" : "Minion not owned yet");
    }

    internal static void DrawMinionName(YokaiGrindMode.YokaiProgress p) {
        // Summoned and not-owned moved off the status icon (now purely done/not-done) into the name,
        // so both still read at a glance without a second icon column.
        var colour = p.IsSummoned ? Colors.Gold : p.MinionOwned ? Colors.Grey2 : Colors.Grey3;
        using (var _ = ImRaii.PushColor(ImGuiCol.Text, (uint)colour))
            ImGui.TextV(p.MinionName);

        // Clicking our own row is the one summon path that cannot be swallowed by the game's
        // input handling, so it stays regardless of what the book's right-click does.
        if (p.MinionOwned && !p.IsSummoned) {
            if (ImGui.IsItemHovered())
                ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            if (ImGui.IsItemClicked())
                YokaiGrindMode.SummonNow(p.CompanionId);
        }

        ImGui.TooltipOnHover(p.IsSummoned
            ? $"Currently summoned.\n{string.Join("\n", p.Zones)}"
            : p.MinionOwned
                ? $"Click to summon.\n{string.Join("\n", p.Zones)}"
                : $"Not owned yet.\n{string.Join("\n", p.Zones)}");
    }

    /// <summary>Medal progress, clickable: runs the game's item search for that medal.</summary>
    internal static void DrawMedalCell(YokaiGrindMode.YokaiProgress p) {
        ImGui.TextV(p.WeaponOwned ? "—" : $"{p.Medals}/{p.MedalsRequired}");
        if (p.WeaponOwned)
            return;
        if (ImGui.IsItemHovered())
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        if (ImGui.IsItemClicked())
            GrindTracker.SearchForItem(p.MedalItemId);
        ImGui.TooltipOnHover($"Click to search your inventory for {p.MedalName}.");
    }

    /// <summary>Per-minion detail: weapon state, medals owed, and where to farm them.</summary>
    internal static void DrawDetail(YokaiGrindMode.YokaiProgress p) {
        using (var _ = ImRaii.PushColor(ImGuiCol.Text, (uint)Colors.Gold))
            ImGui.TextV(p.MinionName);

        if (p.IsSummoned) {
            ImGui.SameLine();
            ImGui.Icon(FontAwesomeIcon.Paw, Colors.Gold, "Currently summoned");
        }

        ImGui.Separator();

        if (!p.MinionOwned) {
            ImGui.Icon(FontAwesomeIcon.Lock, Colors.Grey3);
            ImGui.SameLine();
            ImGui.TextWrapped("Not owned yet — buy it with plain Yo-kai Medals before its weapon can be farmed.");
        }
        else if (p.WeaponOwned) {
            ImGui.Icon(FontAwesomeIcon.Check, Colors.ChipPositive);
            ImGui.SameLine();
            ImGui.TextWrapped($"{p.WeaponName} — acquired");
        }
        else {
            ImGui.TextWrapped(p.WeaponName);
            ImGui.DrawProgressBar(Math.Min(p.Medals, p.MedalsRequired), p.MedalsRequired, Colors.Gold);
            ImGui.TextV($"{p.MedalsRemaining} more {p.MedalName}");
        }

        if (p.WeaponOwned || !p.MinionOwned)
            return; // nothing to farm, so the zone list would only be noise

        ImGui.Separator();
        ImGui.TextDisabled("Summon it and run fates in:");
        foreach (var zone in p.Zones)
            ImGui.BulletText(zone);
    }
}

/// <summary>
/// Per-minion Yo-kai progress: which weapons are still owed, how many Legendary Medals each needs,
/// and how far off the plain medals for the minions you don't own yet are.
/// </summary>
public sealed class YokaiWindow : MinimisableWindow {
    private readonly FateModule _module;

    public YokaiWindow(FateModule module) : base($"Yo-kai Tracker##{nameof(YokaiWindow)}") {
        _module = module;
        Size = new Vector2(620, 520);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    private bool _showCompleted;

    protected override Vector2 MinimisedSize => new(300, 420);

    public override bool DrawConditions() => IObjectTable.Get().LocalPlayer.Available;

    protected override void DrawContent(bool minimised) {
        _module.SyncRunningState();
        DrawRunButton();

        // Multibox first: on a client this is the only thing that says whether the run you're looking
        // at is being driven by the host, and this window can start a run without MainWindow open.
        StatusChips.Multibox(_module);

        if (minimised) {
            ImGui.Spacing();
            YokaiDraw.DrawCompactTable(_showCompleted);
            return;
        }

        ImGui.SameLine();
        StatusChips.Chip($"State: {_module.CurrentState}", _module.Running ? Colors.ChipGold : Colors.ChipMuted, Colors.Grey2);
        ImGui.TooltipOnHover("What the grind loop is doing right now.");

        ImGui.SameLine();
        DrawPhaseControls();
        DrawMinionPhase();

        ImGui.SpacedSeparator();
        DrawWeaponTable();
    }

    private void DrawRunButton() {
        using var rounding = ImRaii.PushStyle(ImGuiStyleVar.FrameRounding, 6f);
        using var color = ImRaii.PushColor(ImGuiCol.Button, (uint)(_module.Running ? Colors.Negative : Colors.Positive))
            .Push(ImGuiCol.ButtonHovered, (uint)(_module.Running ? Colors.NegativeHover : Colors.PositiveHover))
            .Push(ImGuiCol.ButtonActive, (uint)(_module.Running ? Colors.NegativeActive : Colors.PositiveActive));

        if (ImGui.Button(_module.Running ? _module.PendingStopWhenSafe ? "Stopping" : "Stop" : "Start")) {
            if (!_module.Running) {
                // starting from the Yo-kai tracker should grind Yo-kai, not whatever mode was last picked
                _module.SelectedModeId = YokaiGrindMode.ModeName;
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

        ImGui.TooltipOnHover(_module.Running
            ? $"Stop. Ctrl+{SeIconChar.MouseLeftClick.ToIconString()} finishes the current fate first."
            : "Start grinding, switching the mode to Yo-kai Watch.");
    }

    private void DrawPhaseControls() {
        var phase = _module.Config.YokaiPhase;
        if (ImGui.Enum("Phase##YokaiPhase", ref phase)) {
            _module.Config.YokaiPhase = phase;
            // the phase decides the zone set and item targets, so they have to be rebuilt now
            _module.RefreshMode();
        }
        ImGui.TooltipOnHover(
            "Auto: farm plain medals until you can afford the minions you're missing, then switch to weapons.\n"
            + "Minions: plain Yo-kai Medals only (any event fate, watch equipped).\n"
            + "Weapons: Legendary Medals only (correct minion out, per-minion zones).");

        ImGui.Checkbox("Wear the watch while farming weapons", ref _module.Config.YokaiWearWatchInWeaponsPhase);
        ImGui.TooltipOnHover(
            "Off by default — Legendary Medals come from the summoned minion, so the weapons phase\n"
            + "leaves your wrist slot alone instead of equipping the watch over it.\n"
            + "Tick it to wear the watch anyway, so plain medals keep trickling in while you farm weapons.\n"
            + "The minion phase always equips it regardless: plain medals don't drop without it.");

        // third row: the run button and phase combo fill the first, the watch toggle the second
        if (ImGui.Button("Open Yo-kai Book"))
            YokaiDraw.OpenMedallium();
        ImGui.TooltipOnHover("Opens the Yo-kai Watch Medallium. Hover an entry for its weapon progress, right-click to summon.");

        ImGui.SameLine();
        if (ImGui.Button("Minion Guide"))
            YokaiDraw.OpenMinionGuide();
        ImGui.TooltipOnHover("Opens the general minion collection instead. The panel attaches to whichever is open.");
    }

    private static void DrawMinionPhase() {
        var (owned, total, held, needed) = YokaiDraw.Totals;

        ImGui.Spacing();
        if (owned >= total) {
            ImGui.Icon(FontAwesomeIcon.Check, Colors.ChipPositive);
            ImGui.SameLine();
            ImGui.TextV($"All {total} minions owned.");
            return;
        }

        ImGui.TextV($"Minions {owned}/{total} — {needed} Yo-kai Medals to buy the remaining {total - owned}");
        ImGui.DrawProgressBar(Math.Min(held, needed), Math.Max(needed, 1), Colors.Gold);

        if (YokaiDraw.Totals.MedalsRemaining == 0) {
            ImGui.Icon(FontAwesomeIcon.ExclamationTriangle, Colors.Gold);
            ImGui.SameLine();
            ImGui.TextWrapped("You have enough medals. AutoFATE can't use the vendor — buy the minions yourself, then the weapon phase can farm them.");
        }
    }

    private void DrawWeaponTable() {
        var pinned = YokaiDraw.Pinned;
        var progress = YokaiDraw.SortedRows(_showCompleted, pinned);

        if (!YokaiGrindMode.AchievementsLoaded) {
            ImGui.Icon(FontAwesomeIcon.ExclamationTriangle, Colors.Gold);
            ImGui.SameLine();
            ImGui.TextWrapped("Achievements aren't loaded, so weapons are being detected by inventory instead — "
                + "one you obtained and later discarded will read as still owed.");
            if (ImGui.Button("Load achievements"))
                YokaiDraw.OpenAchievements();
            ImGui.TooltipOnHover("Opens the Achievements window, which is what makes the game send the list.");
            ImGui.Spacing();
        }

        var all = YokaiDraw.Progress;
        var done = all.Count(p => p.WeaponOwned);
        var medalsLeft = all.Where(p => p.MinionOwned).Sum(p => p.MedalsRemaining);
        ImGui.TextV($"Weapons {done}/{all.Count} — {medalsLeft} Legendary Medals owed on minions you own");
        ImGui.TooltipOnHover("Minions you don't own yet aren't counted; each will need 10 more once bought.");

        ImGui.SameLine();
        if (ImGui.Button(_showCompleted ? "Hide completed" : "Show completed"))
            _showCompleted = !_showCompleted;
        ImGui.TooltipOnHover($"{done} weapon(s) already acquired.");
        ImGui.Spacing();

        using var table = ImRaii.Table("##YokaiWeapons", 4,
            ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.ScrollY | ImGuiTableFlags.SizingStretchProp);
        if (!table)
            return;

        ImGui.TableSetupColumn("Minion", ImGuiTableColumnFlags.WidthFixed, 150);
        ImGui.TableSetupColumn("Weapon", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Medals", ImGuiTableColumnFlags.WidthFixed, 70);
        ImGui.TableSetupColumn("Left", ImGuiTableColumnFlags.WidthFixed, 45);
        YokaiDraw.FreezeHeaderAndPinned(pinned, progress);
        ImGui.TableHeadersRow();

        foreach (var p in progress) {
            using var id = ImRaii.PushId((int)p.CompanionId);
            ImGui.TableNextRow();

            ImGui.TableNextColumn();
            YokaiDraw.DrawStatusIcon(p);
            ImGui.SameLine();
            YokaiDraw.DrawMinionName(p);

            ImGui.TableNextColumn();
            using (var _ = ImRaii.PushColor(ImGuiCol.Text, (uint)(p.WeaponOwned ? Colors.Grey3 : Colors.Grey2)))
                ImGui.TextV(p.WeaponName);

            ImGui.TableNextColumn();
            YokaiDraw.DrawMedalCell(p);

            ImGui.TableNextColumn();
            if (p.WeaponOwned) {
                ImGui.TextDisabled("0");
            }
            else {
                using var left = ImRaii.PushColor(ImGuiCol.Text, (uint)(p.MinionOwned ? Colors.Gold : Colors.Grey3));
                ImGui.TextV(p.MedalsRemaining.ToString());
            }
        }
    }
}

/// <summary>
/// Panel pinned beside the game's Minion Guide showing the hovered Yo-kai minion's weapon progress.
/// Selection comes from the hover-detail state the guide already publishes rather than from reading
/// the addon's node tree, which shifts between patches.
/// </summary>
public sealed class YokaiBookOverlay : Window {
    public YokaiBookOverlay() : base($"Yo-kai##{nameof(YokaiBookOverlay)}",
        ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.AlwaysAutoResize
        | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoNavFocus | ImGuiWindowFlags.NoScrollbar) {
        IsOpen = true; // visibility is entirely DrawConditions; there is no close button
        RespectCloseHotkey = false;
        DisableWindowSounds = true;
    }

    public override bool DrawConditions()
        => IObjectTable.Get().LocalPlayer.Available && YokaiDraw.AnchorWindow.IsVisible;

    public override void PreDraw() {
        var book = YokaiDraw.AnchorWindow;
        if (book.IsNull)
            return;

        // pinned to the guide's right edge so it reads as part of that window
        Position = new Vector2(book.X + book.ScaledWidth + 2, book.Y);
        PositionCondition = ImGuiCond.Always;
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(240, 0), MaximumSize = new Vector2(320, 600) };
    }

    public override void Draw() {
        var selected = YokaiDraw.TrackHover();

        using (var _ = ImRaii.PushColor(ImGuiCol.Text, (uint)Colors.Grey3))
            ImGui.TextV("AutoFATE");

        if (selected == 0 || YokaiDraw.Find(selected) is not { } progress) {
            ImGui.TextDisabled("Hover a Yo-kai entry.");
            return;
        }

        YokaiDraw.DrawDetail(progress);
        ImGui.TextDisabled("Right-click an entry to summon.");
    }
}

/// <summary>
/// Summon-on-right-click for the two Yo-kai books. The Minion Guide has a native context menu, so it
/// gets a proper entry there. The Medallium has none at all — right-clicking it opens nothing for an
/// entry to be added to — so that one reads the mouse directly instead.
/// </summary>
internal sealed partial class YokaiClickToSummon : IDisposable {
    private const int VkRButton = 0x02;

    // Neither ImGui nor the addon's own event stream sees a right-click on this window: tracing showed
    // MouseOver arriving at YKWNote while no ImGui click and no addon click event were ever raised. The
    // icons simply don't register a right-click listener, and Dalamud's ImGui never gets the message.
    // Polling the OS key state is the remaining source that actually observes the button.
    [System.Runtime.InteropServices.LibraryImport("user32.dll", SetLastError = false)]
    private static partial short GetAsyncKeyState(int vKey);

    [System.Runtime.InteropServices.LibraryImport("user32.dll", SetLastError = false)]
    private static partial nint GetForegroundWindow();

    private static readonly nint _gameWindow = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;

    private bool _rightWasDown;
    /// <summary>When on, every event the Medallium receives is logged with its type and param, plus
    /// whether ImGui saw the mouse. One right-click then says exactly which layer is dropping it.</summary>
    internal static bool Trace;

    /// <summary>The trace listener needs an instance method to register; the plugin only ever makes one.</summary>
    internal static YokaiClickToSummon? Instance { get; private set; }

    public YokaiClickToSummon() {
        Instance = this;
        Svc.ContextMenu.OnMenuOpened += OnMenuOpened;
        Svc.Framework.Update += OnUpdate;
    }

    public void Dispose() {
        Svc.ContextMenu.OnMenuOpened -= OnMenuOpened;
        Svc.Framework.Update -= OnUpdate;
        if (Trace)
            Svc.AddonLifecycle.UnregisterListener(OnAnyAddonEvent);
        Trace = false;
        Instance = null;
    }

    /// <summary>
    /// Unnamed listener, filtered by name at the callback. Registering by name is what failed last
    /// time — a wrong guess attaches to nothing and looks identical to "the addon emits no events".
    /// Only registered while tracing, since this fires for every addon event in the game.
    /// </summary>
    private void OnAnyAddonEvent(AddonEvent type, AddonArgs args) {
        if (args is not AddonReceiveEventArgs e || args.AddonName is not { } name)
            return;
        if (!name.Contains("YKW", StringComparison.OrdinalIgnoreCase) && !name.Contains("Minion", StringComparison.OrdinalIgnoreCase))
            return;
        Svc.Log.Info($"[AutoFATE] addon={name} event={e.AtkEventType} param={e.EventParam} hovered={YokaiDraw.CurrentHover()}");
    }

    /// <summary>Minion Guide only — see the class remarks for why the Medallium isn't handled here.</summary>
    private void OnMenuOpened(IMenuOpenedArgs args) {
        if (args.MenuType != ContextMenuType.Default || args.AddonName != YokaiDraw.GuideAddon)
            return;

        // Live hover, not the latched value: the right-click target is whatever the cursor is on.
        if (Resolve() is not { } progress)
            return;

        args.AddMenuItem(new MenuItem {
            Name = $"Summon {progress.MinionName}",
            PrefixChar = 'A',
            PrefixColor = 539,
            OnClicked = _ => YokaiGrindMode.SummonNow(progress.CompanionId),
        });
    }

    /// <summary>
    /// Dalamud feeds real mouse input into ImGui even when the cursor is over a game window, so a
    /// right-click on the book is visible here; WantCaptureMouse keeps clicks on our own windows out.
    /// </summary>
    /// <remarks>
    /// Deliberately does not check which addon is open. The hover gate is the real guard: the game
    /// only reports a Yo-kai companion under the cursor when the cursor is genuinely on one of these
    /// entries, and that holds without having to resolve the Medallium's addon at all — the previous
    /// attempt gated on exactly that and never got as far as reading the click.
    /// </remarks>
    private void OnUpdate(IFramework framework) {
        // Latch every tick. The overlay is the only other caller of this and it stops drawing when
        // turned off, so the grace window in Resolve needs this running independently.
        YokaiDraw.TrackHover();

        var down = (GetAsyncKeyState(VkRButton) & 0x8000) != 0;
        var pressed = down && !_rightWasDown;
        _rightWasDown = down;
        if (!pressed)
            return;

        // GetAsyncKeyState is machine-wide, so a right-click in another app would otherwise reach
        // here while the book sits open behind it.
        if (_gameWindow != nint.Zero && GetForegroundWindow() != _gameWindow)
            return;

        // Only while the Medallium is actually open, and never while the Guide is — that one has a
        // native context menu entry, and firing here too would summon and then dismiss.
        if (!YokaiDraw.Medallium.IsVisible || YokaiDraw.Guide.IsVisible)
            return;

        var hovered = Resolve();
        if (Trace)
            Svc.Log.Info($"[AutoFATE] right-click observed — hovered={YokaiDraw.CurrentHover()}, "
                + $"resolved={hovered?.MinionName ?? "none"}, medallium={YokaiDraw.Medallium.IsVisible}");

        if (hovered is not { } progress)
            return;

        YokaiGrindMode.SummonNow(progress.CompanionId);
    }

    /// <summary>Half a second of grace covers the click clearing the hover before this frame reads it,
    /// while still being far too short to resolve to something the cursor left long ago.</summary>
    private const long HoverGraceMs = 500;

    private static YokaiGrindMode.YokaiProgress? Resolve() {
        var minion = YokaiDraw.CurrentHover();
        if (minion == 0)
            minion = YokaiDraw.RecentHover(HoverGraceMs);
        return minion != 0 && YokaiDraw.Find(minion) is { MinionOwned: true } progress ? progress : null;
    }

    /// <summary>Reports every gate the right-click path passes through, so a miss can be diagnosed
    /// from one line of chat instead of guessing at which check swallowed it.</summary>
    internal static void ReportState() {
        var hover = YokaiDraw.CurrentHover();
        var name = hover != 0 && YokaiDraw.Find(hover) is { } p ? $"{p.MinionName} (owned: {p.MinionOwned})" : "none";
        var book = YokaiDraw.Medallium;
        Svc.Chat.Print($"[AutoFATE] Yo-kai click state — trace: {Trace}, "
            + $"medallium: {(book.IsNull ? $"addon not found (tried {YokaiDraw.MedalliumAddon})" : $"'{book.Name}' {(book.IsVisible ? "visible" : "hidden")} at {book.X},{book.Y} w={book.ScaledWidth}")}, "
            + $"guide visible: {YokaiDraw.Guide.IsVisible}, game focused: {_gameWindow != nint.Zero && GetForegroundWindow() == _gameWindow}, "
            + $"hovered: {name}, summoned: {YokaiGrindMode.GetProgress().FirstOrDefault(x => x.IsSummoned).MinionName ?? "none"}");
    }

    internal static void ToggleTrace() {
        Trace = !Trace;
        if (Trace)
            Svc.AddonLifecycle.RegisterListener(AddonEvent.PreReceiveEvent, Instance!.OnAnyAddonEvent);
        else
            Svc.AddonLifecycle.UnregisterListener(Instance!.OnAnyAddonEvent);

        Svc.Chat.Print($"[AutoFATE] Yo-kai event trace {(Trace ? "on — right-click a Medallium entry, then check /xllog" : "off")}.");
    }
}
