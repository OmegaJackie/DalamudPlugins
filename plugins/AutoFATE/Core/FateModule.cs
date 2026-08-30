using ECommons.ImGuiMethods.TerritorySelection;
using Lumina.Excel.Sheets;
using TerritoryIntendedUse = FFXIVClientStructs.FFXIV.Client.Enums.TerritoryIntendedUse;

namespace AutoFATE.Core;

/// <summary>
/// Port of ffxiv-bundleoftweaks' FateToolKit ("Date With Destiny") as a standalone module,
/// with combat handled by BossMod Reborn.
/// </summary>
public class FateModule : IFateGrindRunState, IDisposable {
    private const int MinTimeToPrioritise = 240;

    private static readonly Dictionary<FateSortCriteria, Func<PublicEvent, IComparable>> SortKeys = new() {
        [FateSortCriteria.HasBonusWithTwist] = f => f.HasBonus && (IObjectTable.Get().LocalPlayer?.StatusList.HasTwistOfFate() ?? false),
        [FateSortCriteria.Progress] = f => f.Progress,
        [FateSortCriteria.HasBonus] = f => f.HasBonus,
        // Unactivated fates report negative time; treat them as non-urgent.
        [FateSortCriteria.TimeRemainingUrgent] = f => f.TimeRemaining is >= 0 and < MinTimeToPrioritise,
        [FateSortCriteria.Distance] = f => IObjectTable.Get().LocalPlayer?.DistanceTo(f.Position) ?? 0,
        // Only rank by remaining time for active + urgent fates.
        // Non-urgent and unactivated fates tie here so later criteria (e.g. distance) can decide.
        [FateSortCriteria.TimeRemaining] = f => f.TimeRemaining is >= 0 and < MinTimeToPrioritise ? f.TimeRemaining : MinTimeToPrioritise,
        [FateSortCriteria.Level] = f => f.Level,
        [FateSortCriteria.Name] = f => f.Name,
    };

    public Configuration Config => Service.Config;
    public System.Action? ToggleWindow { get; set; }
    public MultiboxSync Multibox { get; }

    /// <summary>Keeps other clients' teleports from dragging this one off its own fate — see <see cref="TeleportOfferGuard"/>.</summary>
    private readonly TeleportOfferGuard? _teleportOfferGuard;

    public string CurrentState { get; internal set; } = "Idle";
    /// <summary>Fate the grind task is currently pathing to or engaged in; published to multibox clients.</summary>
    public uint? CurrentTargetFateId { get; internal set; }

    /// <summary>
    /// Live target for multibox broadcasting. Reads the task's NextFate directly because the
    /// grind loop blocks inside one iteration for a whole travel leg — the loop-cached
    /// <see cref="CurrentTargetFateId"/> would make clients trail the host by one fate.
    /// </summary>
    internal uint? LiveTargetFateId => !Running ? null : (Svc.Automation.CurrentTask as FateGrind)?.NextFate?.Id ?? PublicEvent.CurrentFate?.Id;
    public int CompletedCount { get; private set; }
    public int? RunUntilCompleted { get; private set; }
    public int? RemainingUntilCompleted => RunUntilCompleted is { } runUntil ? Math.Max(0, runUntil - CompletedCount) : null;
    internal HashSet<uint> SelectedSwapZones => Config.SelectedSwapZones;
    internal string SelectedModeId {
        get => Config.SelectedModeId;
        set {
            if (Config.SelectedModeId == value)
                return;
            Config.SelectedModeId = value;
            RefreshZoneItemTargets();
        }
    }
    internal bool PendingStopWhenSafe { get; set; } // task sets running = false once no CurrentFate and !InCombat
    private List<ZoneItemTarget> ZoneItemTargets { get; set; } = [];

    /// <summary>Required companion plugins that are not currently loaded.</summary>
    public static List<string> MissingDependencies() {
        var missing = new List<string>();
        if (!Svc.Navmesh.IsAvailable)
            missing.Add("vnavmesh");
        if (!Service.BossMod.IsLoaded)
            missing.Add("BossMod Reborn");
        if (!Service.RotationSolver.IsLoaded)
            missing.Add("Rotation Solver Reborn");
        if (!Service.TextAdvance.IsLoaded)
            missing.Add("TextAdvance");
        return missing;
    }

    public bool Running {
        get;
        internal set {
            if (field == value)
                return; // e.g. "/autofate stop" while idle must not clear a manually-activated BossMod preset

            if (value && MissingDependencies() is { Count: > 0 } missing) {
                Svc.Chat.Print($"[AutoFATE] Cannot start: missing required plugin(s): {string.Join(", ", missing)}.");
                return;
            }

            field = value;
            if (value) {
                PendingStopWhenSafe = false;
                ZoneItemTargets = [];
                CompletedCount = 0;
                RefreshZoneItemTargets();

                // A mode with nothing left to do stops the task on its first loop iteration, before
                // any scope logs — so pressing Start looks like it did nothing at all. Say so instead.
                // Clients are exempt: they keep running to support the host regardless.
                if (Multibox.Role != MultiboxRole.Client && GetCurrentMode() is { } mode && mode.IsComplete(this))
                    Svc.Chat.Print($"[AutoFATE] '{mode.DisplayName}' has nothing left to do on this character, so the run will stop immediately. "
                        + "Pick a different mode, or check the Yo-kai tracker for what this character still needs.");

                Svc.Automation.Start(new FateGrind(this));
            }
            else {
                PendingStopWhenSafe = false;
                ZoneItemTargets = [];
                CurrentState = "Idle";
                CurrentTargetFateId = null;
                Service.BossMod.ClearActive();
                Service.RotationSolver.Off(); // never leave RSR fighting unattended after a stop
                Svc.Automation.Stop();
                RunUntilCompleted = null;
            }
        }
    }

    public FateModule() {
        Multibox = new MultiboxSync(this);
        try {
            _teleportOfferGuard = new TeleportOfferGuard(this);
            IAddonLifecycle.Get().RegisterListener(AddonEvent.PostSetup, "FateReward", OnFateRewardPostSetup);
        }
        catch {
            // ctor failure means Dispose never runs — don't leak the Framework.Update subscription
            _teleportOfferGuard?.Dispose();
            Multibox.Dispose();
            throw;
        }
    }

    public void Dispose() {
        Multibox.Dispose();
        if (Running)
            Running = false;
        IAddonLifecycle.Get().UnregisterListener(OnFateRewardPostSetup);
        _teleportOfferGuard?.Dispose();
    }

    private void OnFateRewardPostSetup(AddonEvent type, AddonArgs args) {
        if (!Running)
            return;

        CompletedCount++;
        RefreshZoneItemTargets();
        StopIfNoRemaining();
    }

    internal void RunUntil(int runUntil) {
        if (!Running) {
            Running = true;
            if (!Running)
                return; // start was blocked (missing dependencies); don't leave an orphaned target
            RunUntilCompleted = runUntil;
        }
        else {
            RunUntilCompleted = runUntil;
            StopIfNoRemaining();
        }
    }

    internal void StopIfNoRemaining() {
        if (RunUntilCompleted is { } runUntil && CompletedCount >= runUntil) {
            PendingStopWhenSafe = true;
            return;
        }

        // A client mirrors the host's start/stop, so its own mode completing is not a reason to stop:
        // the party shares a phase but not progress against it, and an alt with nothing to earn in the
        // host's phase would otherwise stop on its first loop iteration — before anything logs, which
        // reads as "Start does nothing". It still helps clear the host's fates, and still earns
        // whatever that phase does pay it. The host decides when everyone stops.
        if (Multibox.Role != MultiboxRole.Client && GetCurrentMode().IsComplete(this))
            PendingStopWhenSafe = true;
    }

    internal bool IsZoneItemTargetComplete(uint currentTerritoryId, out uint destinationTerritoryId) {
        destinationTerritoryId = 0;
        RefreshZoneItemTargets();
        if (ZoneItemTargets.Count == 0)
            return false;

        // A wrong/missing minion used to be laundered through here as a same-zone "swap" to force a
        // resync. FateGrind now reconciles that every iteration via IFateGrindMode.EnsureZoneState,
        // which also un-gates it from Twist of Fate and multibox client role.
        var stillNeededHere = ZoneItemTargets.Any(t => t.TerritoryId == currentTerritoryId && !t.IsComplete);
        if (stillNeededHere)
            return false;

        if (GetNextPreferredSwapZone(currentTerritoryId) is { } next) {
            destinationTerritoryId = next;
            return true;
        }
        return false;
    }

    /// <summary>Rebuild zone/item targets after a setting the current mode derives them from changed
    /// (e.g. the Yo-kai phase, which swaps both the zone set and the tracked item).</summary>
    internal void RefreshMode() => RefreshZoneItemTargets();

    internal IFateGrindMode GetCurrentMode() {
        var displayName = SelectedModeId;
        if (string.IsNullOrEmpty(displayName))
            return FateGrindModes.None;
        return FateGrindModes.GetByDisplayName(displayName) ?? FateGrindModes.None;
    }

    /// <summary>Returns whether the relic (by item ID) has completed the associated quest for this step. Fill in with quest/achievement check.</summary>
    public static bool IsRelicStepComplete(uint relicItemId) {
        // TODO: check quest (or achievement) for this relic; return true when the step is done for that relic
        return false;
    }

    internal static int GetRelicsCompletedForStep(IReadOnlyList<uint>? relicItemIds)
        => relicItemIds is { Count: > 0 } ids ? ids.Count(IsRelicStepComplete) : 0;

    /// <summary>Zones used for swap rotation: mode's allowed zones if set, otherwise selected swap zones.</summary>
    internal IReadOnlySet<uint>? GetEffectiveSwapZones() => GetCurrentMode().GetAllowedZones() ?? (SelectedSwapZones.Count > 0 ? SelectedSwapZones : null);

    /// <summary>True when the current mode defines its own zones; territory selector is disabled to avoid confusion.</summary>
    internal bool ModeSuppliesSwapZones => GetCurrentMode().GetAllowedZones() != null;

    /// <summary>Next zone to swap to; prefers zones where a mode item target is not yet met (e.g. relic atma).</summary>
    internal uint? GetNextPreferredSwapZone(uint currentTerritoryId) {
        // The no-fates swap path reaches this without going through IsZoneItemTargetComplete, so the
        // cached targets can still name the previous minion's zones. Stale here means teleporting to a
        // zone we finished with.
        RefreshZoneItemTargets();
        return GetNextPreferredSwapZoneCore(currentTerritoryId);
    }

    private uint? GetNextPreferredSwapZoneCore(uint currentTerritoryId)
        => ZoneItemTargets.Count > 0 && ZoneItemTargets.Where(t => !t.IsComplete).Select(t => t.TerritoryId).Distinct().ToList() is { Count: > 0 } incomplete
            ? incomplete.Where(z => z != currentTerritoryId).ToList() is { Count: > 0 } others
                ? others[Random.Shared.Next(others.Count)]
                : incomplete[0]
            : GetNextSelectedSwapZone(currentTerritoryId);

    private static unsafe int GetItemCount(uint itemId) => FFXIVClientStructs.FFXIV.Client.Game.InventoryManager.Instance()->GetInventoryItemCount(itemId);

    private void RefreshZoneItemTargets() {
        if (GetCurrentMode().GetZoneItemTargets(this) is not { } targets) {
            ZoneItemTargets = [];
            return;
        }

        ZoneItemTargets = [.. targets];
        CheckItemTargetCompletion();
    }

    private void CheckItemTargetCompletion() {
        if (ZoneItemTargets.Count == 0)
            return;

        for (var i = 0; i < ZoneItemTargets.Count; i++) {
            var target = ZoneItemTargets[i];
            target.IsComplete = GetItemCount(target.ItemId) >= target.RequiredCount;
            ZoneItemTargets[i] = target;
        }
    }

    internal void SyncRunningState() {
        if (Running && !Svc.Automation.Running)
            Running = false;
    }

    internal bool HasSelectedSwapZones => SelectedSwapZones.Count > 0;

    private int _selectedZoneRotation = -1;

    private static List<uint> GetOrderedSwapZones(IReadOnlySet<uint> pool) {
        var distinct = pool.Where(id => id != 0).Distinct().ToList();
        return distinct.Count == 0 ? [] : [.. distinct.OrderBy(id => id)];
    }

    internal uint? GetNextSelectedSwapZone(uint currentTerritoryId) {
        var pool = GetEffectiveSwapZones();
        if (pool is null || pool.Count == 0)
            return null;

        var zones = GetOrderedSwapZones(pool);

        if (zones.Count == 0)
            return null;

        if (zones.Count == 1)
            return zones[0];

        _selectedZoneRotation = (_selectedZoneRotation + 1) % zones.Count;
        if (zones[_selectedZoneRotation] == currentTerritoryId)
            _selectedZoneRotation = (_selectedZoneRotation + 1) % zones.Count;
        return zones[_selectedZoneRotation];
    }

    internal void OpenZoneSelector() {
        var selector = new TerritorySelector(SelectedSwapZones, (_, selected) => {
            SelectedSwapZones.Clear();
            foreach (var zoneId in selected)
                SelectedSwapZones.Add(zoneId);
        }, "AutoFATE Zones");

        var allowedIds = TerritoryType.Where(row => row.IsInUse && row.TerritoryIntendedUse.Value.StructsEnum is TerritoryIntendedUse.Overworld && !row.IsPvpZone).Select(row => row.RowId).ToHashSet();
        selector.HiddenTerritories = [.. TerritoryType.Select(row => row.RowId).Where(id => !allowedIds.Contains(id))];

        selector.HiddenCategories = [TerritorySelector.Category.All];
        selector.SelectedCategory = TerritorySelector.Category.World;
    }

    public void ToggleRunning() {
        RunUntilCompleted = null;
        Running ^= true;
    }

    internal bool IsBlacklisted(PublicEvent f)
        => Config.Blacklist.TryGetValue(f.FateType, out var set) && set.Contains(f.Id);

    public void ToggleBlacklist(PublicEvent f) {
        if (!Config.Blacklist.TryGetValue(f.FateType, out var set))
            Config.Blacklist[f.FateType] = set = [];

        if (!set.Add(f.Id))
            set.Remove(f.Id);
    }

    public bool FateConditions(PublicEvent f)
        => f.Duration <= Config.MaxDuration
        && f.Progress <= Config.MaxProgress
        && (f.TimeRemaining < 0 || f.TimeRemaining > Config.MinTimeRemaining)
        && !IsBlacklisted(f)
        && !f.IsPending;

    public (bool IsEligible, List<string> FailedConditions) GetFateConditionDetails(PublicEvent f) {
        var failed = new List<string>();

        if (f.Duration > Config.MaxDuration)
            failed.Add($"Duration {f.Duration}s > MaxDuration {Config.MaxDuration}s");

        if (f.Progress > Config.MaxProgress)
            failed.Add($"Progress {f.Progress}% > MaxProgress {Config.MaxProgress}%");

        if (f.TimeRemaining >= 0 && f.TimeRemaining <= Config.MinTimeRemaining)
            failed.Add($"TimeRemaining {f.TimeRemaining:F0}s <= MinTimeRemaining {Config.MinTimeRemaining}s");

        if (IsBlacklisted(f))
            failed.Add("Blacklisted");

        if (f.IsPending)
            failed.Add("Pending (not yet active / not on map)");

        return (failed.Count == 0, failed);
    }

    public IEnumerable<(PublicEvent Fate, bool IsAvailable)> GetOrderedFates() {
        var all = PublicEvent.Fates.ToList();
        if (all.Count == 0)
            yield break;

        var available = all.Where(FateConditions).ToList();
        var unavailable = all.Where(f => !FateConditions(f)).ToList();

        foreach (var f in ApplySortOrder(available, Config.SortOrder))
            yield return (f, true);

        foreach (var f in ApplySortOrder(unavailable, Config.SortOrder))
            yield return (f, false);
    }

    internal static IOrderedEnumerable<PublicEvent> ApplySortOrder(IEnumerable<PublicEvent> source, IReadOnlyList<FateSortOrder> sortOrder) {
        if (!sortOrder.Any())
            return source.OrderBy(_ => 0);

        IOrderedEnumerable<PublicEvent>? ordered = null;

        foreach (var sort in sortOrder) {
            var keySelector = SortKeys.TryGetValue(sort.Criteria, out var key) ? key : (_ => 0);
            ordered = ordered == null
                ? sort.Descending ? source.OrderByDescending(keySelector) : source.OrderBy(keySelector)
                : sort.Descending ? ordered.ThenByDescending(keySelector) : ordered.ThenBy(keySelector);
        }

        return ordered ?? source.OrderBy(_ => 0);
    }
}
