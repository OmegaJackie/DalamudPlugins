using AutoFATE.Core;
using Dalamud.Configuration;
using Newtonsoft.Json;
using System.IO;

namespace AutoFATE;

/// <summary>Per-character multibox settings. The config file is shared by every game client
/// on this machine, so anything role-like must be keyed by character, not stored globally.</summary>
public class MultiboxCharacterConfig {
    public MultiboxRole Role = MultiboxRole.Off;
    public bool SyncSettingsFromHost = true;
}

public enum FateSortCriteria {
    HasBonusWithTwist,
    Progress,
    HasBonus,
    TimeRemainingUrgent,
    Distance,
    TimeRemaining,
    Level,
    Name,
}

public class FateSortOrder {
    public FateSortCriteria Criteria { get; set; }
    public bool Descending { get; set; }
}

public class Configuration : IPluginConfiguration, IPluginService {
    public int Version { get; set; } = 1;
    public int InitOrder => 0;

    public int MaxDuration = 900;
    public int MinTimeRemaining = 120;
    public int MaxProgress = 90;
    public bool SwapZones = true;

    public string DisplayNameFormat = "[{Level}] {Name}";
    public Vector4 BarColour = new(0.404f, 0.259f, 0.541f, 1f);
    public Dictionary<FateType, HashSet<uint>> Blacklist = [];
    public List<FateSortOrder> SortOrder = DefaultSortOrder();

    public string SelectedModeId = "None";
    public HashSet<uint> SelectedSwapZones = [];

    /// <summary>
    /// Take the free teleport offer a party member's teleport extends to everyone in the zone only
    /// when it came from the multibox host, for as long as a grind is running. Deliberately local
    /// rather than synced from the host: every client decides for itself, and the host has no offers
    /// to answer. See <see cref="Core.TeleportOfferGuard"/>.
    /// </summary>
    public bool DeclinePartyTeleportOffers = true;

    /// <summary>Version of the BossMod preset last injected — see <see cref="Core.BossModPreset"/>.
    /// Lets an updated preset overwrite exactly once without clobbering user edits every load.</summary>
    public int InstalledPresetVersion;

    /// <summary>Which half of the Yo-kai grind to run. Auto buys minions first, then farms weapons.</summary>
    public YokaiPhase YokaiPhase = YokaiPhase.Auto;

    /// <summary>
    /// Equip the Yo-kai Watch during the weapons phase. Off by default: the watch gates plain Yo-kai
    /// Medals only, while Legendary Medals key off the summoned minion, so forcing it on spends your
    /// wrist slot for nothing. The minion phase ignores this and always equips — plain medals do not
    /// drop without it.
    /// </summary>
    public bool YokaiWearWatchInWeaponsPhase = false;

    // Multibox settings are stored per character because multibox setups usually share
    // this config file across every game client on the machine.
    public Dictionary<ulong, MultiboxCharacterConfig> Multibox = [];

    public MultiboxCharacterConfig GetMultibox(ulong contentId) {
        if (!Multibox.TryGetValue(contentId, out var mb))
            Multibox[contentId] = mb = new MultiboxCharacterConfig();
        return mb;
    }

    public static List<FateSortOrder> DefaultSortOrder() =>
    [
        new() { Criteria = FateSortCriteria.HasBonusWithTwist, Descending = true },
        new() { Criteria = FateSortCriteria.Progress, Descending = true },
        new() { Criteria = FateSortCriteria.HasBonus, Descending = true },
        new() { Criteria = FateSortCriteria.TimeRemainingUrgent, Descending = true },
        new() { Criteria = FateSortCriteria.TimeRemaining, Descending = false },
        new() { Criteria = FateSortCriteria.Distance, Descending = false },
    ];

    public void Save() {
        // Multiboxing shares this file between game processes; a plain save would last-writer-wins
        // away the OTHER clients' per-character Multibox entries. Merge unknown entries from disk
        // first (same-character conflicts keep our in-memory value).
        try {
            if (Svc.Interface.ConfigFile is { Exists: true } file
                && JsonConvert.DeserializeObject<Configuration>(File.ReadAllText(file.FullName)) is { } onDisk) {
                foreach (var (cid, mb) in onDisk.Multibox)
                    Multibox.TryAdd(cid, mb);
            }
            Svc.Interface.SavePluginConfig(this);
        }
        catch (Exception ex) {
            // unreadable copy or a cross-process sharing violation — don't throw into the caller
            Svc.Log.Warning($"[AutoFATE] Config save failed: {ex.Message}");
        }
    }
}
