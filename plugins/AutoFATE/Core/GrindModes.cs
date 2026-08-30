using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Lumina.Excel;
using Lumina.Excel.Sheets;
using System.Threading;
using System.Threading.Tasks;
using TerritoryIntendedUse = FFXIVClientStructs.FFXIV.Client.Enums.TerritoryIntendedUse;

namespace AutoFATE.Core;

public record struct ZoneItemTarget(uint TerritoryId, uint ItemId, int RequiredCount) {
    public bool IsComplete { get; set; }
}

/// <summary>
/// A zone one tracked item drops in. Carries the territory id as well as the name so the tracker can
/// pin the row for the zone you are standing in; <see cref="ToString"/> keeps it printable as a bare
/// name wherever only the label matters.
/// </summary>
public readonly record struct TrackedZone(uint TerritoryId, string Name) {
    public override string ToString() => Name;
}

/// <summary>One tracked item's progress, flattened for the tracker UI.</summary>
/// <param name="Searchable">False for wallet currencies — ItemFinderModule only searches real
/// containers (bags, armoury, retainers, …), so a currency search would always come up empty.</param>
public readonly record struct TrackedItem(uint ItemId, string Name, int Have, int Required, IReadOnlyList<TrackedZone> Zones, bool Searchable = true) {
    public int Remaining => Math.Max(0, Required - Have);
    public bool IsComplete => Have >= Required;

    /// <summary>Whether this item drops in <paramref name="territoryId"/> — the tracker pins the row
    /// for the zone the player is actually standing in.</summary>
    public bool DropsIn(uint territoryId) => territoryId != 0 && Zones.Any(z => z.TerritoryId == territoryId);
}

public interface IFateGrindRunState {
    int CompletedCount { get; }
    int? RunUntilCompleted { get; }
    int? RemainingUntilCompleted { get; }
}

internal interface IFateGrindMode {
    string DisplayName { get; }
    int UiPriority => 0;

    IReadOnlySet<uint>? GetAllowedZones();
    bool IsComplete(IFateGrindRunState state);
    string? GetRemainingDisplay(IFateGrindRunState state);

    IEnumerable<ZoneItemTarget>? GetZoneItemTargets(IFateGrindRunState? state = null);

    /// <summary>
    /// Rows for the compact tracker shown in the minimised main window (the Yo-kai mode draws its
    /// own richer table instead). Null when this mode has nothing item-shaped to track.
    /// </summary>
    IEnumerable<TrackedItem>? GetTrackedItems() => null;

    /// <summary>
    /// Why this phase cannot pay out right now — a quest not accepted, no relic equipped, no Yo-kai
    /// Watch. Null when nothing blocks it. The tracker leads with this rather than showing counts
    /// that are quietly unreachable.
    /// </summary>
    string? GetBlockedReason() => null;

    /// <summary>Where this phase's required counts come from, or any caveat attached to them. Printed
    /// under the totals; null when the numbers speak for themselves.</summary>
    string? GetTrackerNote() => null;

    /// <summary>
    /// Idempotent reconcile of whatever this mode needs to be true in <paramref name="territoryId"/>
    /// (Yo-kai: right minion summoned, watch equipped). Called every grind iteration rather than only
    /// after our own teleports, because world visits, DC travel, deaths and duty exits all clear that
    /// state without ever passing through the zone-swap path. Must return promptly when there is
    /// nothing to do, and skip rather than block whenever the game would reject the change.
    /// </summary>
    Task EnsureZoneState(uint territoryId, Func<Task> dismount, CancellationToken cancellationToken = default) => Task.CompletedTask;

    /// <summary>
    /// Cheap synchronous companion to <see cref="EnsureZoneState"/>: are this mode's requirements
    /// already met in <paramref name="territoryId"/>? While false the grind loop parks instead of
    /// engaging fates, because a fate completed without them pays nothing.
    /// </summary>
    bool IsZoneStateSatisfied(uint territoryId) => true;
}

public static class FateGrindModes {
    internal static IReadOnlyList<IFateGrindMode> All { get; } = Build();
    internal static IFateGrindMode? GetByDisplayName(string displayName) => All.FirstOrDefault(m => m.DisplayName == displayName);
    internal static IFateGrindMode None => All.First(m => m.UiPriority == -1);

    private static IReadOnlyList<IFateGrindMode> Build() {
        var zeniths = RelicItem.GetItemsByStep(2);
        var animateds = QuestClassJobReward.GetRelicsByRow(3);
        var augmented = QuestClassJobReward.GetRelicsByRow(17);

        return [
            new NoneGrindMode(),
            new GemstoneGrindMode(),
            new YokaiGrindMode(),

            new ZoneItemGrindMode {
                DisplayName = "Atma (Zodiac)",
                Goals = [
                    new(7851, [148]), new(7852, [146]), new(7853, [139]), new(7854, [152]),
                    new(7855, [145]), new(7856, [134]), new(7857, [140]), new(7858, [180]),
                    new(7859, [135]), new(7860, [154]), new(7861, [141]), new(7862, [138]),
                ],
                Kind = ZoneItemGoalKind.PerRelicRemaining,
                PerRelic = (1, zeniths.Count - 1), // subtract pld shield
                RelicItemIds = [.. zeniths.Select(r => r.RowId)],
                IsAvailable = () => zeniths.Any(i => i.Value.Handle.IsEquipped),
                UnavailableMessage = "Need relic equipped!",
            },
            new ZoneItemGrindMode {
                DisplayName = "Luminous Crystals (Anima)",
                Goals = [
                    new(13569, [397]), new(13570, [401]), new(13571, [402]),
                    new(13572, [398]), new(13573, [400]), new(13574, [399]),
                ],
                Kind = ZoneItemGoalKind.PerRelicRemaining,
                PerRelic = (1, animateds.Count - 1), // subtract pld shield
                RelicItemIds = [.. animateds.Select(r => r.RowId)],
            },
            new ZoneItemGrindMode {
                DisplayName = "Memories (Resistance)",
                Goals = [
                    new(31573, [397, 401]), // Coerthas Western Highlands, Sea of Clouds
                    new(31574, [398, 400]), // Dravanian Forelands, Churning Mists
                    new(31575, [399, 402]), // Dravanian Hinterlands, Azys Lla
                ],
                Kind = ZoneItemGoalKind.PerRelicRemaining,
                PerRelic = (20, augmented.Count - 1), // subtract pld shield
                RelicItemIds = [.. augmented.Select(r => r.RowId)],
            },
            new ZoneItemGrindMode {
                DisplayName = "Law's Order (Resistance)",
                Goals = [
                    new(32957, [612, 620, 621], 18), // Fringes, Peaks, Lochs
                    new(32958, [613, 614, 622], 18), // Ruby Sea, Yanxia, Azim Steppe
                ],
                IsAvailable = () => QuestAccepted(69575), // The Resistance Remembers
                IsFullyDone = () => QuestComplete(69575),
                UnavailableMessage = $"Need Quest {Quest.GetRow(69575).Name}",
            },
            new ZoneItemGrindMode {
                DisplayName = "Demiatmas (Phantom)",
                Goals = [
                    new(47744, [1187], 3), // Urqopacha
                    new(47745, [1188], 3), // Kozama'uka
                    new(47746, [1189], 3), // Yak T'el
                    new(47747, [1190], 3), // Shaaloani
                    new(47748, [1191], 3), // Heritage Found
                    new(47749, [1192], 3), // Living Memory
                ],
                IsAvailable = () => QuestAccepted(70855), // Arcane Artistry
                IsFullyDone = () => QuestComplete(70855),
                UnavailableMessage = $"Need Quest {Quest.GetRow(70855).Name}",
            },
            new ZoneItemGrindMode {
                DisplayName = "Paste (Phantom)",
                Goals = [
                    new(50059, [.. TerritoryType
                        .Where(r => r.IsInUse && !r.IsPvpZone && r.TerritoryIntendedUse.Value.StructsEnum is TerritoryIntendedUse.Overworld && r.ExVersion.RowId is 5)
                        .Select(r => r.RowId)], 1200),
                ],
                IsAvailable = () => QuestAccepted(70991), // In Pursuit of Perfection
                IsFullyDone = () => QuestComplete(70991),
                UnavailableMessage = $"Need Quest {Quest.GetRow(70991).Name}",
            },
        ];
    }

    private static unsafe bool QuestAccepted(uint questId) => QuestManager.Instance()->IsQuestAccepted(questId);
    private static bool QuestComplete(uint questId) => QuestManager.IsQuestComplete(questId);

    /// <summary>Place name for a territory, falling back to the id so an unmapped zone still reads as
    /// something. Shared by every mode's tracked-item rows.</summary>
    internal static string ZoneName(uint territoryId)
        => TerritoryType.GetRowOrNull(territoryId)?.PlaceName.ValueNullable?.Name.ToString() is { Length: > 0 } name
            ? name
            : $"Zone {territoryId}";

    internal static IReadOnlyList<TrackedZone> ZoneList(IEnumerable<uint>? territoryIds)
        => territoryIds is null
            ? []
            : [.. territoryIds.Where(id => id != 0).Distinct().Select(id => new TrackedZone(id, ZoneName(id))).OrderBy(z => z.Name, StringComparer.CurrentCulture)];
}

public sealed class NoneGrindMode : IFateGrindMode {
    public string DisplayName => "None";
    public int UiPriority => -1;

    public IReadOnlySet<uint>? GetAllowedZones() => null;
    public bool IsComplete(IFateGrindRunState _) => false;
    public string? GetRemainingDisplay(IFateGrindRunState state) => state.RemainingUntilCompleted is { } r && r > 0 ? $"{r} fates" : null;
    public IEnumerable<ZoneItemTarget>? GetZoneItemTargets(IFateGrindRunState? state = null) => null;
}

public sealed class GemstoneGrindMode : IFateGrindMode {
    private const uint BicolorGemstone = 26807;

    public string DisplayName => "Gemstones";

    // shb+ zones, prio highest expac
    public IReadOnlySet<uint>? GetAllowedZones() {
        var unlocked = TerritoryType.Where(r => r.IsInUse && r.TerritoryIntendedUse.Value.StructsEnum is TerritoryIntendedUse.Overworld && r.ExVersion.RowId >= 3 && !r.IsPvpZone && r.IsPrimaryAetheryteUnlocked).ToList();
        if (unlocked.Count == 0)
            return new HashSet<uint>();
        var topEx = unlocked.Max(r => r.ExVersion.RowId);
        return unlocked.Where(r => r.ExVersion.RowId == topEx).Select(r => r.RowId).ToHashSet();
    }

    public bool IsComplete(IFateGrindRunState _) => GetGemstoneRemaining() == 0;

    public string? GetRemainingDisplay(IFateGrindRunState _) {
        var remaining = GetGemstoneRemaining();
        return remaining > 0 ? $"{remaining} left" : null;
    }

    public IEnumerable<ZoneItemTarget>? GetZoneItemTargets(IFateGrindRunState? state = null) => null;

    public IEnumerable<TrackedItem> GetTrackedItems() {
        // Tracked against the wallet cap: the mode's goal is "as many as you can hold".
        // Not searchable — gemstones live in the currency wallet, which the item search can't see.
        yield return new TrackedItem(BicolorGemstone, "Bicolor Gemstone", (int)GetGemstoneHeld(), (int)GetGemstoneCap(),
            FateGrindModes.ZoneList(GetAllowedZones()), Searchable: false);
    }

    public string? GetTrackerNote()
        => "Every fate in the newest expansion you have unlocked pays gemstones. The target is the wallet cap, "
        + "not a quest requirement — they stop dropping once it is reached, so spend before topping up.";

    public string? GetBlockedReason()
        => GetAllowedZones() is { Count: 0 }
            ? "No Shadowbringers-or-later zone has its aetheryte unlocked, so there is nowhere to farm gemstones yet."
            : null;

    private static unsafe uint GetGemstoneHeld() => CurrencyManager.Instance()->GetItemCount(BicolorGemstone);
    private static unsafe uint GetGemstoneCap() => CurrencyManager.Instance()->GetItemMaxCount(BicolorGemstone);
    private static unsafe uint GetGemstoneRemaining() => CurrencyManager.Instance()->GetItemCountRemaining(BicolorGemstone);
}

public enum ZoneItemGoalKind {
    FixedPerItem,
    PerRelicRemaining,
}

public readonly record struct ItemZoneGoal(uint ItemId, IReadOnlyList<uint> Zones, int? FixedRequired = null);

public sealed class ZoneItemGrindMode : IFateGrindMode {
    public required string DisplayName { get; init; }
    public int UiPriority { get; init; } = 100;
    public required IReadOnlyList<ItemZoneGoal> Goals { get; init; }
    public ZoneItemGoalKind Kind { get; init; } = ZoneItemGoalKind.FixedPerItem;
    public (int PerRelic, int TotalRelics)? PerRelic { get; init; }
    public IReadOnlyList<uint>? RelicItemIds { get; init; }
    public Func<bool>? IsAvailable { get; init; }
    public Func<bool>? IsFullyDone { get; init; }
    public string? UnavailableMessage { get; init; }

    public IReadOnlySet<uint>? GetAllowedZones()
        => Goals.SelectMany(g => g.Zones).Where(id => id != 0).ToHashSet();

    public bool IsComplete(IFateGrindRunState state) {
        if (IsFullyDone?.Invoke() ?? false)
            return true;
        if (!(IsAvailable?.Invoke() ?? true))
            return false;
        foreach (var goal in Goals)
            if (GetItemCount(goal.ItemId) < GetEffectiveRequired(goal)) return false;
        return true;
    }

    public string? GetRemainingDisplay(IFateGrindRunState state) {
        if (IsComplete(state))
            return "Done";
        if (!(IsAvailable?.Invoke() ?? true))
            return UnavailableMessage ?? "Unavailable";
        var total = Goals.Sum(g => Math.Max(0, GetEffectiveRequired(g) - GetItemCount(g.ItemId)));
        return total == 0 ? null : $"{total} left";
    }

    public IEnumerable<ZoneItemTarget>? GetZoneItemTargets(IFateGrindRunState? state = null) {
        foreach (var goal in Goals) {
            var total = GetEffectiveRequired(goal);
            if (total <= 0) continue;
            var remaining = Math.Max(0, total - GetItemCount(goal.ItemId));
            if (remaining <= 0) continue;
            foreach (var territoryId in goal.Zones.Where(id => id != 0))
                yield return new ZoneItemTarget(territoryId, goal.ItemId, total);
        }
    }

    public IEnumerable<TrackedItem> GetTrackedItems() {
        foreach (var goal in Goals) {
            var required = GetEffectiveRequired(goal);
            if (required <= 0)
                continue; // e.g. every relic done with this step — nothing owed for the item at all

            yield return new TrackedItem(
                goal.ItemId,
                Item.GetRowOrNull(goal.ItemId)?.Name.ToString() is { Length: > 0 } name ? name : $"Item {goal.ItemId}",
                GetItemCount(goal.ItemId),
                required,
                FateGrindModes.ZoneList(goal.Zones));
        }
    }

    /// <summary>The quest gate, reported as-is. <see cref="IsFullyDone"/> wins: a finished relic step
    /// has no unmet prerequisite, it simply has nothing left to farm.</summary>
    public string? GetBlockedReason()
        => (IsFullyDone?.Invoke() ?? false) || (IsAvailable?.Invoke() ?? true)
            ? null
            : UnavailableMessage ?? "Unavailable";

    /// <summary>
    /// Spells out where a per-relic target came from — "10 needed" is meaningless without knowing it
    /// is one per outstanding relic — and states plainly that relic completion isn't detected, since
    /// otherwise the count looks like it should fall as weapons are finished.
    /// </summary>
    public string? GetTrackerNote() {
        if (Kind != ZoneItemGoalKind.PerRelicRemaining || PerRelic is not (var per, var totalRelics))
            return null;

        var done = FateModule.GetRelicsCompletedForStep(RelicItemIds);
        var outstanding = Math.Max(0, totalRelics - done);
        return $"Targets scale with the relics still owed at this step: {per} per relic × {outstanding} relic(s)."
            + (done == 0 ? " Per-relic completion isn't detected yet, so every relic counts as outstanding." : "");
    }

    private int GetEffectiveRequired(ItemZoneGoal goal) {
        if (Kind == ZoneItemGoalKind.PerRelicRemaining && PerRelic is (var per, var totalRelics)) {
            var done = FateModule.GetRelicsCompletedForStep(RelicItemIds);
            return Math.Max(0, (totalRelics - done) * per);
        }
        return goal.FixedRequired ?? 0;
    }

    private static unsafe int GetItemCount(uint itemId) => InventoryManager.Instance()->GetInventoryItemCount(itemId);
}

public enum YokaiPhase {
    /// <summary>Buy minions first, then farm their weapons once nothing more is owed for minions.</summary>
    [System.ComponentModel.Description("Auto (minions, then weapons)")]
    Auto,
    /// <summary>Farm plain Yo-kai Medals — the currency for buying the minions you don't own.</summary>
    [System.ComponentModel.Description("Minions (plain medals)")]
    Minions,
    /// <summary>Farm each owned minion's Legendary Medals for its weapon.</summary>
    [System.ComponentModel.Description("Weapons (legendary medals)")]
    Weapons,
}

public sealed class YokaiGrindMode : IFateGrindMode {
    private const uint WatchItemId = 15222;
    private const uint MedalItemId = 15167;

    // 1 Yo-kai Medal for your first minion, 3 for every one after — 49 for all seventeen.
    private const int FirstMinionCost = 1;
    private const int LaterMinionCost = 3;

    public static int MinionsTotal => Yokai.Count;
    public static int MinionsOwned => Yokai.Count(e => e.Unlocked);
    public static int RegularMedals => GetItemCount(MedalItemId);

    /// <summary>Medals to buy every minion still missing. Owning none means exactly one of them is
    /// cheap, so the discount is applied once rather than to each.</summary>
    public static int RegularMedalsNeeded {
        get {
            var missing = MinionsTotal - MinionsOwned;
            if (missing <= 0)
                return 0;
            return MinionsOwned == 0 ? FirstMinionCost + LaterMinionCost * (missing - 1) : LaterMinionCost * missing;
        }
    }

    public static int RegularMedalsRemaining => Math.Max(0, RegularMedalsNeeded - RegularMedals);

    /// <summary>
    /// Auto sits in the minion phase only while medals are still owed. Once you can afford what is
    /// missing it moves on — the plugin cannot buy from the vendor, so waiting on a purchase it can
    /// never make would stall the grind indefinitely.
    /// </summary>
    public static YokaiPhase ActivePhase => Service.Config.YokaiPhase switch {
        YokaiPhase.Minions => YokaiPhase.Minions,
        YokaiPhase.Weapons => YokaiPhase.Weapons,
        _ => RegularMedalsRemaining > 0 ? YokaiPhase.Minions : YokaiPhase.Weapons,
    };

    // Lazily built: static field initialisers run in declaration order, and Yokai is declared below.
    private static IReadOnlySet<uint>? _allZones;

    /// <summary>Every event fate zone — the union of all seventeen minions' zones. A regular medal
    /// drops from any fate in these while the watch is on; legendary medals are per-minion.</summary>
    private static IReadOnlySet<uint> AllZones => _allZones ??= Yokai.SelectMany(e => e.Zones.Select(z => z.RowId)).ToHashSet();

    /// <summary>Learned minion whose weapon is still unearned — the medal-target-independent half of
    /// <see cref="NeedsFarm"/>, so <see cref="DiscountEntry"/> can use it without recursing. Uses
    /// Learned, not Unlocked: a minion sitting unused in the bag cannot be summoned to farm with.</summary>
    private static bool IsUnfinished(YokaiEntry entry) => entry.Learned && !entry.WeaponObtained;

    private static bool OwnsAnyWeapon => Yokai.Any(e => e.WeaponObtained);

    /// <summary>
    /// The vendor charges 5 Legendary Medals for your first weapon and 10 for each one after. That
    /// discount is per <em>purchase</em>, not per weapon-you-lack, so exactly one entry may target 5 —
    /// a flat 5 while owning none would stop farming each minion five medals short of its real cost.
    /// Which entry gets it can only be a guess (the plugin never buys), so it tracks whichever minion
    /// the grind is actually working and self-corrects the moment any weapon is owned.
    /// </summary>
    private static YokaiEntry? DiscountEntry
        => OwnsAnyWeapon ? null
        : GetCurrentMinionEntry() is { } summoned && IsUnfinished(summoned) ? summoned
        : Yokai.FirstOrDefault(IsUnfinished);

    private static int RequiredFor(YokaiEntry entry)
        => DiscountEntry is { } discounted && discounted.Minion.RowId == entry.Minion.RowId ? 5 : 10;

    public const string ModeName = "Yo-kai Watch (Medals)";

    public string DisplayName => ModeName;

    /// <summary>
    /// The entry this zone is about: the summoned minion when it belongs here, otherwise whichever
    /// still needs farming here, otherwise any that lists it. Null outside the event's zones.
    /// </summary>
    public static uint? PinnedFor(uint territoryId) {
        if (territoryId == 0)
            return null;
        if (GetCurrentMinionEntry() is { } summoned && summoned.Zones.Any(z => z.RowId == territoryId))
            return summoned.Minion.RowId;
        if (Yokai.FirstOrDefault(e => NeedsFarm(e) && e.Zones.Any(z => z.RowId == territoryId)) is { } needed)
            return needed.Minion.RowId;
        return Yokai.FirstOrDefault(e => e.Zones.Any(z => z.RowId == territoryId))?.Minion.RowId;
    }

    /// <summary>
    /// The one minion being farmed right now: the summoned one while it still needs medals, otherwise
    /// the next that does. Everything zone-related keys off this so the grind finishes one minion
    /// before moving on.
    /// </summary>
    private static YokaiEntry? ActiveEntry
        => GetCurrentMinionEntry() is { } current && NeedsFarm(current) ? current : EntriesNeedingFarm().FirstOrDefault();

    public IReadOnlySet<uint>? GetAllowedZones() {
        if (ActivePhase == YokaiPhase.Minions)
            return AllZones; // any event fate pays a regular medal, so roam all of them

        // Only the active minion's zones. The union of every unfinished minion's zones would let a
        // no-fates swap land somewhere the summoned minion earns nothing, and the reconcile would then
        // swap the minion to match — churning between minions instead of finishing one. Once its
        // medals are done ActiveEntry moves on and this opens up to the next minion's zones.
        if (ActiveEntry is { } active)
            return active.Zones.Select(z => z.RowId).ToHashSet();

        // keep zone selector disabled when mode supplies zones
        return AllZones;
    }

    public bool IsComplete(IFateGrindRunState _)
        => ActivePhase == YokaiPhase.Minions ? RegularMedalsRemaining == 0 : !EntriesNeedingFarm().Any();

    public string? GetRemainingDisplay(IFateGrindRunState state) {
        if (state.RemainingUntilCompleted is { } r && r > 0) return $"{r} fates";

        if (ActivePhase == YokaiPhase.Minions) {
            if (RegularMedalsRemaining > 0)
                return $"Minions: {RegularMedals}/{RegularMedalsNeeded} medals";
            // the plugin never buys, so say so rather than reporting a finished-looking "Done"
            return MinionsOwned < MinionsTotal ? "Enough medals — go buy your minions" : "Done";
        }

        if (IsComplete(state)) return "Done";

        var entry = GetCurrentMinionEntry();
        if (entry is not null && NeedsFarm(entry)) {
            var count = GetItemCount(entry.Medal.RowId);
            var name = entry.Medal.Value.Name.ToString() ?? $"Item {entry.Medal.RowId}";
            return $"{name} {count}/{RequiredFor(entry)}";
        }

        // Counts only minions you already own — medals for weapons of minions still to be bought are
        // not included, so this is a floor on the remaining grind, not the full 165.
        var remaining = EntriesNeedingFarm().Sum(e => Math.Max(0, RequiredFor(e) - GetItemCount(e.Medal.RowId)));
        return remaining > 0 ? $"{remaining} medals left" : null;
    }

    /// <summary>
    /// Only the two things that stop the phase paying out at all. The watch not being worn is one of
    /// them — the grind equips it itself, so seeing this while running means the equip is failing.
    /// </summary>
    public string? GetBlockedReason() {
        if (ActivePhase == YokaiPhase.Minions && !IsWatchEquipped())
            return GetItemCount(WatchItemId) > 0
                ? "Yo-kai Watch not equipped — plain medals only drop while it is worn."
                : "No Yo-kai Watch in your inventory — plain Yo-kai Medals cannot drop without one. Collect it from the event NPC first.";

        if (ActivePhase == YokaiPhase.Weapons && !Yokai.Any(e => e.Learned))
            return "No Yo-kai minion learned yet — Legendary Medals only drop for the minion that is summoned, so buy one first.";

        return null;
    }

    public IEnumerable<ZoneItemTarget>? GetZoneItemTargets(IFateGrindRunState? state = null) {
        if (ActivePhase == YokaiPhase.Minions)
            return RegularMedalsNeeded is var needed && needed <= 0
                ? null
                : AllZones.Select(z => new ZoneItemTarget(z, MedalItemId, needed));

        // One entry at a time so shared zones (e.g. Enma/Damona) don't block swaps.
        if (ActiveEntry is not { } entry)
            return null;

        return entry.Zones.Select(z => new ZoneItemTarget(z.RowId, entry.Medal.RowId, RequiredFor(entry)));
    }

    private const int RetryFrames = 30;
    private const int MaxAttempts = 10;

    /// <summary>
    /// Legendary medals only drop while the zone's own minion is out, and a minion survives none of
    /// world visit, DC travel, death or a duty exit — none of which route through the swap path. So
    /// this runs every iteration and is written to be cheap on the common no-op path.
    /// </summary>
    public async Task EnsureZoneState(uint territoryId, Func<Task> dismount, CancellationToken cancellationToken) {
        if (ActivePhase == YokaiPhase.Minions) {
            // No minion requirement here — regular medals key off the watch alone.
            if (IsWatchEquipped() || !CanAct())
                return;
            if (GetItemCount(WatchItemId) == 0)
                WarnNoWatch();
            else
                await EquipWatch(cancellationToken);
            return;
        }

        // Opt-in only. Legendary medals drop from the summoned minion, not the watch, so the weapons
        // phase has no reason to occupy a wrist slot; ticking the box equips it anyway for players who
        // want plain medals still trickling in alongside. Leaving it off also keeps the fast path below
        // reachable — an unworn watch used to force a full 17-entry inventory walk every tick.
        var needsWatch = Service.Config.YokaiWearWatchInWeaponsPhase && !IsWatchEquipped() && GetItemCount(WatchItemId) > 0;

        // Fast path for the steady state. NeedsFarm walks the inventory per entry, so testing all 17
        // every tick is worth avoiding while the minion that is already out is the correct one.
        if (!needsWatch && GetCurrentMinionEntry() is { } summoned
            && summoned.Zones.Any(z => z.RowId == territoryId) && NeedsFarm(summoned))
            return;

        if (Yokai.FirstOrDefault(e => NeedsFarm(e) && e.Zones.Any(z => z.RowId == territoryId)) is not { } entry)
            return;

        var needsMinion = IPlayerState.Get().Minion.RowId != entry.Minion.RowId;
        if (!needsMinion && !needsWatch)
            return;

        // Combat, zoning and every Occupied flag reject both changes outright. Skipping costs one
        // iteration; the unbounded waits this replaced hung the grind loop forever on a rejection.
        if (!CanAct())
            return;

        if (needsWatch)
            await EquipWatch(cancellationToken);
        if (needsMinion)
            await SummonMinion(entry, dismount, cancellationToken);
    }

    private static bool CanAct()
        => !ICondition.Get()[ConditionFlag.InCombat] && !ICondition.Get().IsUnavailable() && IObjectTable.Get().LocalPlayer.Interactable;

    /// <summary>
    /// Deliberately ignores the watch: it gates only <em>regular</em> medals, so a player who does not
    /// own one must not be parked forever. The minion is the hard requirement — without it the fate
    /// pays no legendary medal at all.
    /// </summary>
    public bool IsZoneStateSatisfied(uint territoryId) {
        // Minion phase has no per-zone minion, but the watch is absolute: without it a fate pays no
        // regular medal at all, so unlike the weapon phase it is worth parking the grind over.
        if (ActivePhase == YokaiPhase.Minions)
            return IsWatchEquipped();

        if (GetCurrentMinionEntry() is { } summoned && summoned.Zones.Any(z => z.RowId == territoryId) && NeedsFarm(summoned))
            return true; // fast path: what is already out is the right one for work remaining here
        return !Yokai.Any(e => NeedsFarm(e) && e.Zones.Any(z => z.RowId == territoryId));
    }

    /// <remarks>
    /// Best-effort: the watch gates <em>regular</em> medals (the currency for buying minions), not the
    /// legendary medals this mode farms, so failing to equip it must never block the summon.
    /// </remarks>
    private static async Task EquipWatch(CancellationToken cancellationToken) {
        var watch = new ItemHandle(WatchItemId);
        // ItemHandle.Equip() is a silent no-op until the location is resolved — the old code skipped
        // this and then waited forever on an equip that had never been issued.
        if (!watch.TrySetItemLocation() || !ICondition.Get().CanMoveItems())
            return;

        watch.Equip();
        for (var attempt = 0; attempt < MaxAttempts && !IsWatchEquipped(); attempt++)
            await NextFrames(RetryFrames, cancellationToken);
    }

    private static async Task SummonMinion(YokaiEntry entry, Func<Task> dismount, CancellationToken cancellationToken) {
        if (ICondition.Get()[ConditionFlag.Mounted])
            await dismount(); // such a hack lol

        for (var attempt = 0; attempt < MaxAttempts; attempt++) {
            if (cancellationToken.IsCancellationRequested)
                return;

            if (IPlayerState.Get().Minion.RowId == entry.Minion.RowId) {
                _summonWarnedFor = default; // recovered; let a genuine later failure warn again
                return;
            }

            unsafe {
                // Re-issue only when idle — firing into our own cast/animation lock would cancel the
                // summon we are waiting on and the loop would never converge.
                if (!(IObjectTable.Get().LocalPlayer?.IsCasting ?? false) && ActionManager.Instance()->AnimationLock <= 0)
                    ActionManager.Instance()->UseAction(ActionType.Companion, entry.Minion.RowId);
            }
            await NextFrames(RetryFrames, cancellationToken);
        }

        WarnSummonFailed(entry);
    }

    private const long SummonWarnRepeatMs = 60_000;
    private static (uint Territory, uint Minion, long At) _summonWarnedFor;

    private static void WarnSummonFailed(YokaiEntry entry) {
        var territory = IPlayerState.Get().Territory.RowId;
        var now = Environment.TickCount64;
        // Repeats rather than warning once: the grind is parked until this resolves, and a single line
        // that scrolled away an hour ago leaves an idle-looking bot with no stated reason.
        if (_summonWarnedFor.Territory == territory && _summonWarnedFor.Minion == entry.Minion.RowId && now - _summonWarnedFor.At < SummonWarnRepeatMs)
            return;

        _summonWarnedFor = (territory, entry.Minion.RowId, now);
        Svc.Chat.Print($"[AutoFATE] Paused: cannot summon {entry.Minion.Value.Singular}, which this zone's legendary medal requires. "
            + "If it is raining, turn off auto-umbrella under Character > Fashion Accessories; it blocks minion summoning.");
    }

    private static long _noWatchWarnedAt;

    private static void WarnNoWatch() {
        var now = Environment.TickCount64;
        if (_noWatchWarnedAt != 0 && now - _noWatchWarnedAt < SummonWarnRepeatMs)
            return;

        _noWatchWarnedAt = now;
        Svc.Chat.Print("[AutoFATE] Paused: no Yo-kai Watch in your inventory. Plain Yo-kai Medals only drop while it is "
            + "equipped, so the minion phase cannot progress without one — collect it from the event NPC first.");
    }

    /// <summary>Totals for the minion-buying phase, snapshotted together so the UI reads them once.</summary>
    public readonly record struct YokaiTotals(int MinionsOwned, int MinionsTotal, int Medals, int MedalsNeeded) {
        public int MedalsRemaining => Math.Max(0, MedalsNeeded - Medals);
    }

    public static YokaiTotals GetTotals() => new(MinionsOwned, MinionsTotal, RegularMedals, RegularMedalsNeeded);

    /// <summary>One minion's state, flattened for the tracker UI so it needs no sheet access.</summary>
    public readonly record struct YokaiProgress(
        uint CompanionId,
        string MinionName,
        string WeaponName,
        string MedalName,
        uint MedalItemId,
        bool MinionOwned,
        bool WeaponOwned,
        bool IsSummoned,
        int Medals,
        int MedalsRequired,
        IReadOnlyList<string> Zones) {
        public int MedalsRemaining => WeaponOwned ? 0 : Math.Max(0, MedalsRequired - Medals);
    }

    public static IEnumerable<YokaiProgress> GetProgress() => Yokai.Select(ToProgress);

    /// <summary>Cheap membership test — the UI checks this per frame against hover state.</summary>
    public static bool IsYokaiMinion(uint companionId) => Yokai.Any(e => e.Minion.RowId == companionId);

    /// <summary>
    /// Resolve any Yo-kai item back to its minion. The Medallium lists medals rather than summonable
    /// minions, so hovering it surfaces an item id, not a companion id.
    /// </summary>
    public static uint? MinionForItem(uint itemId)
        => Yokai.FirstOrDefault(e => e.MinionItem.RowId == itemId || e.Medal.RowId == itemId || e.Weapon.RowId == itemId)?.Minion.RowId;

    /// <summary>
    /// Manual summon for the book's right-click entry. Unlike the grind path this neither dismounts
    /// nor retries — a deliberate click should behave like clicking the minion in the guide, and
    /// silently yanking the player off their mount would be a surprise.
    /// </summary>
    public static unsafe void SummonNow(uint companionId) {
        if (!IsYokaiMinion(companionId))
            return;

        // UseAction on the summoned minion dismisses it — not what "Summon" should ever do
        if (IPlayerState.Get().Minion.RowId == companionId)
            return;

        if (ICondition.Get()[ConditionFlag.Mounted]) {
            Svc.Chat.Print("[AutoFATE] Dismount first — minions can't be summoned while mounted.");
            return;
        }

        ActionManager.Instance()->UseAction(ActionType.Companion, companionId);
    }

    private static YokaiProgress ToProgress(YokaiEntry e) => new(
        e.Minion.RowId,
        e.Minion.Value.Singular.ToString(),
        e.Weapon.Value.Name.ToString(),
        e.Medal.Value.Name.ToString(),
        e.Medal.RowId,
        e.Learned, // bought-but-unused reads as not owned — that is the actionable state

        e.WeaponObtained,
        IPlayerState.Get().Minion.RowId == e.Minion.RowId,
        GetItemCount(e.Medal.RowId),
        RequiredFor(e),
        [.. e.Zones.Select(z => z.Value.PlaceName.Value.Name.ToString())]);

    private static IEnumerable<YokaiEntry> EntriesNeedingFarm() => Yokai.Where(NeedsFarm);

    private static bool NeedsFarm(YokaiEntry entry)
        => IsUnfinished(entry) && GetItemCount(entry.Medal.RowId) < RequiredFor(entry);

    private static Task NextFrames(int n, CancellationToken ct) => IFramework.Get().DelayTicks(n, ct);

    private static YokaiEntry? GetCurrentMinionEntry()
        => Yokai.FirstOrDefault(e => e.Minion.RowId == IPlayerState.Get().Minion.RowId);

    private static unsafe int GetItemCount(uint itemId) => InventoryManager.Instance()->GetInventoryItemCount(itemId);

    public record YokaiEntry {
        public RowRef<Companion> Minion { get; init; }
        /// <summary>The item the vendor sells; using it is what actually teaches the minion.</summary>
        public RowRef<Item> MinionItem { get; init; }
        public RowRef<Item> Medal { get; init; }
        public RowRef<Item> Weapon { get; init; }
        /// <summary>Achievement granted for the weapon. Authoritative where an item count is not.</summary>
        public int WeaponAchievement { get; init; }
        public List<RowRef<TerritoryType>> Zones { get; init; }

        public YokaiEntry(YKW ykw, uint minionItem, uint weapon, int weaponAchievement) {
            Minion = ykw.Companion;
            Medal = ykw.Item;
            MinionItem = Item.GetRowRef(minionItem);
            Weapon = Item.GetRowRef(weapon);
            WeaponAchievement = weaponAchievement;
            Zones = [.. ykw.Location.Where(z => z.RowId != 0)];
        }

        /// <summary>Learned, or bought and still sitting in the bag — either way the medals are spent,
        /// so the minion phase must not keep charging for it.</summary>
        public bool Unlocked => Learned || GetItemCount(MinionItem.RowId) > 0;

        public unsafe bool Learned => UIState.Instance()->IsCompanionUnlocked(Minion.RowId);

        /// <summary>
        /// Achievement first: it survives the weapon being discarded or desynthesised, and Shogunyan's
        /// reward is a two-piece set where counting one item is already the wrong question. Falls back
        /// to inventory while the achievement list is unloaded, which it is until something opens it.
        /// </summary>
        public unsafe bool WeaponObtained
            => AchievementsLoaded ? UIState.Instance()->Achievement.IsComplete(WeaponAchievement) : GetItemCount(Weapon.RowId) > 0;
    }

    public static unsafe bool AchievementsLoaded => UIState.Instance()->Achievement.IsLoaded();

    /// <summary>
    /// Companion id -> vendor item, weapon, weapon achievement. Only the parts the YKW sheet does not
    /// carry; the medal and the farm zones come from the sheet, which is why the zone lists that used
    /// to sit here (transcribed from a wiki) are gone.
    /// </summary>
    private static readonly (uint Minion, uint MinionItem, uint Weapon, int WeaponAchievement)[] _catalogue = [
        (200, 15195, 15210, 1526), // Jibanyan: Paw of the Crimson Cat
        (201, 15196, 15216, 1527), // Komasan: Cane of the Shrine Guardian
        (202, 15197, 15212, 1528), // Whisper: Bow of the White Wisp
        (203, 15198, 15217, 1529), // Blizzaria: Staff of the Snow Maiden
        (204, 15199, 15213, 1530), // Kyubi: Twintails of the Flame Fox
        (205, 15200, 15219, 1531), // Komajiro: Codex of the Shrine Guardian
        (206, 15201, 15218, 1532), // Manjimutt: Book of the Eerie Mutt
        (207, 15202, 15220, 1533), // Noko: Globe of the Lucky Snake
        (208, 15203, 15211, 1534), // Venoct: Spear of the Spark Serpent
        (209, 15204, 15208, 1535), // Shogunyan: Whisker of the Brave Cat (+ shield 15221)
        (210, 15205, 15214, 1536), // Hovernyan: Fang of the Fearless Cat
        (211, 15206, 15215, 1537), // Robonyan F-type: Musket of the Metal Cat
        (212, 15207, 15209, 1538), // USApyon: Ears of the Moon Rabbit
        (390, 30877, 30809, 2613), // Lord Enma: Gunblade of the Yo-kai King
        (391, 30878, 30808, 2615), // Lord Ananta: Rapier of the Serpent Lord
        (392, 30879, 30807, 2614), // Zazel: Katana of the King's Counsel
        (393, 30880, 30810, 2616), // Damona: Glaives of the Dark Princess
    ];

    private static IReadOnlyList<YokaiEntry>? _yokai;

    /// <summary>Built lazily — the sheet read needs Dalamud's data service, which is not up when
    /// static initialisers for this type would otherwise run.</summary>
    public static IReadOnlyList<YokaiEntry> Yokai => _yokai ??= Build();

    private static IReadOnlyList<YokaiEntry> Build() {
        var rows = YKW.Rows.Where(r => r.Companion.RowId != 0).ToDictionary(r => r.Companion.RowId);
        return [.. _catalogue
            .Where(c => rows.ContainsKey(c.Minion))
            .Select(c => new YokaiEntry(rows[c.Minion], c.MinionItem, c.Weapon, c.WeaponAchievement))];
    }

    public static unsafe bool IsWatchEquipped() => InventoryManager.Instance()->GetInventoryContainer(InventoryType.EquippedItems)->GetInventorySlot(10)->ItemId == 15222;
}
