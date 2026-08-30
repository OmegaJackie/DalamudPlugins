using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using FFXIVClientStructs.FFXIV.Client.Enums;
using FFXIVClientStructs.FFXIV.Client.Game.Fate;
using FFXIVClientStructs.FFXIV.Client.Game.InstanceContent;
using FFXIVClientStructs.FFXIV.Client.Game.WKS;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.Interop;

namespace AutoFATE.CLib.Utils;

public enum FateType {
    Normal,
    DynamicEvent, // forays
    MechaEvent, // cosmic exploration
}

/// <summary>
/// Wrapper for all public event types (FATEs, Dynamic Events, Mecha Events)
/// </summary>
public unsafe class PublicEvent(nint address, FateType fateType, uint id) {
    public IntPtr Address { get; } = address;
    public FateType FateType { get; } = fateType;
    public uint Id { get; } = id;

    public static implicit operator PublicEvent(FateContext* fate) {
        ArgumentNullException.ThrowIfNull(fate);
        return new((nint)fate, FateType.Normal, fate->FateId);
    }
    public static implicit operator PublicEvent(Pointer<FateContext> fate) {
        ArgumentNullException.ThrowIfNull(fate.Value);
        return new((nint)fate.Value, FateType.Normal, fate.Value->FateId);
    }
    public static implicit operator PublicEvent(DynamicEvent* dynamicEvent) {
        ArgumentNullException.ThrowIfNull(dynamicEvent);
        return new((nint)dynamicEvent, FateType.DynamicEvent, dynamicEvent->DynamicEventId);
    }
    // By-value struct copies live on the stack — storing their address would dangle.
    // Address 0 forces GetDynamicEvent()/GetMechaEvent() to re-resolve by id instead.
    public static implicit operator PublicEvent(DynamicEvent dynamicEvent) => new(0, FateType.DynamicEvent, dynamicEvent.DynamicEventId);
    public static implicit operator PublicEvent(WKSMechaEvent* mechaEvent) {
        ArgumentNullException.ThrowIfNull(mechaEvent);
        return new((nint)mechaEvent, FateType.MechaEvent, mechaEvent->WKSMechaEventDataRowId);
    }
    public static implicit operator PublicEvent(WKSMechaEvent mechaEvent) => new(0, FateType.MechaEvent, mechaEvent.WKSMechaEventDataRowId);

    public static PublicEvent? CurrentFate => IObjectTable.Get().LocalPlayer?.Territory.Value.TerritoryIntendedUse.Value.StructsEnum switch {
        TerritoryIntendedUse.Overworld => GetCurrentFateOverworld(),
        TerritoryIntendedUse.Bozja or TerritoryIntendedUse.OccultCrescent => GetCurrentForayEvent(),
        TerritoryIntendedUse.CosmicExploration => GetCurrentCosmicEvent(),
        _ => null, // zone doesn't support FATEs (instance, city, etc.)
    };

    public static IEnumerable<PublicEvent> Fates => IObjectTable.Get().LocalPlayer?.Territory.Value.TerritoryIntendedUse.Value.StructsEnum switch {
        TerritoryIntendedUse.Overworld => GetOverworldFates(),
        TerritoryIntendedUse.Bozja or TerritoryIntendedUse.OccultCrescent => GetForayFates(),
        TerritoryIntendedUse.CosmicExploration => GetCosmicFates(),
        _ => [],
    };

    public static PublicEvent? GetFateById(uint id) => Fates.FirstOrDefault(f => f.Id == id);

    private static PublicEvent? GetCurrentFateOverworld() {
        if (FateManager.Instance() is not (not null and var fm)) return null; // someone actually saw this syntax and said "LGTM, ship it"
        if (fm->CurrentFate is not (not null and var f)) return null; // just give me ?->
        return (PublicEvent)f;
    }

    private static PublicEvent? GetCurrentForayEvent() {
        var container = DynamicEventContainer.GetInstance();
        if (container != null) {
            var dynamicEvent = container->GetCurrentEvent();
            if (dynamicEvent != null && dynamicEvent->State != DynamicEventState.Inactive)
                return (PublicEvent)dynamicEvent;
        }
        return GetCurrentFateOverworld();
    }

    private static PublicEvent? GetCurrentCosmicEvent() {
        var manager = WKSManager.Instance();
        if (manager == null || manager->MechaEventModule == null)
            return GetCurrentFateOverworld();
        var mechaEvent = manager->MechaEventModule->CurrentEvent;
        if (mechaEvent != null && mechaEvent->Flags.HasFlag(WKSMechaEventFlag.IsEventActive))
            return (PublicEvent)mechaEvent;
        return GetCurrentFateOverworld();
    }

    // on the frame a fate spawns, some of its values are default (at least position), so don't add them until that's loaded
    private static IEnumerable<PublicEvent> GetOverworldFates()
        => FateManager.Instance()->Fates.Select(evt => (PublicEvent)evt).Where(evt => evt.Position != Vector3.Zero);

    private static IEnumerable<PublicEvent> GetForayFates() {
        var overworldFates = GetOverworldFates();

        var container = DynamicEventContainer.GetInstance();
        if (container == null)
            return overworldFates;

        var dynamicEvents = container->Events.ToArray().Where(evt => evt.State != DynamicEventState.Inactive).Select(evt => (PublicEvent)evt);
        return overworldFates.Concat(dynamicEvents);
    }

    private static IEnumerable<PublicEvent> GetCosmicFates() {
        var overworldFates = GetOverworldFates();
        var manager = WKSManager.Instance();
        if (manager == null || manager->MechaEventModule == null)
            return overworldFates;
        var mechaEvents = manager->MechaEventModule->Events.ToArray().Where(evt => evt.Flags.HasFlag(WKSMechaEventFlag.IsEventActive)).Select(evt => (PublicEvent)evt);
        return overworldFates.Concat(mechaEvents);
    }

    // Always re-resolve by id: the pointer captured at construction can dangle once the
    // event ends, and even reading its id field to validate would touch freed memory.
    private FateContext* GetFate() {
        var manager = FateManager.Instance();
        return manager == null ? null : manager->GetFateById((ushort)Id);
    }

    private DynamicEvent? GetDynamicEvent() {
        var container = DynamicEventContainer.GetInstance();
        if (container == null)
            return null;

        foreach (var evt in container->Events)
            if (evt.DynamicEventId == Id)
                return evt;
        return null;
    }

    private WKSMechaEvent? GetMechaEvent() {
        var manager = WKSManager.Instance();
        if (manager == null || manager->MechaEventModule == null)
            return null;

        foreach (var evt in manager->MechaEventModule->Events)
            if (evt.WKSMechaEventDataRowId == Id)
                return evt;
        return null;
    }

    // Events can vanish between enumeration and property access (expired fate, zone change);
    // resolve failures degrade to defaultValue instead of dereferencing null/dangling memory.
    private T GetValue<T>(Func<nint, T> getFate, Func<DynamicEvent, T> getDynamicEvent, Func<WKSMechaEvent, T> getMechaEvent, T defaultValue = default!) {
        switch (FateType) {
            case FateType.Normal:
                var fate = GetFate();
                return fate == null ? defaultValue : getFate((nint)fate);
            case FateType.DynamicEvent:
                return GetDynamicEvent() is { } dynamicEvent ? getDynamicEvent(dynamicEvent) : defaultValue;
            case FateType.MechaEvent:
                return GetMechaEvent() is { } mechaEvent ? getMechaEvent(mechaEvent) : defaultValue;
            default:
                return defaultValue;
        }
    }

    public Vector3 Position => GetValue(
        fate => fate.Cast<FateContext>()->Location,
        dynamicEvent => dynamicEvent.MapMarker.Position,
        mechaEvent => mechaEvent.MapMarkers[0].MapMarkerData.Position,
        Vector3.Zero
    );

    public float Radius => GetValue(
        fate => fate.Cast<FateContext>()->Radius,
        dynamicEvent => dynamicEvent.MapMarker.Radius,
        mechaEvent => mechaEvent.MapMarkers[0].MapMarkerData.Radius,
        0f
    );

    public int Progress => GetValue(
        fate => fate.Cast<FateContext>()->Progress,
        dynamicEvent => dynamicEvent.Progress,
        mechaEvent => mechaEvent.EventProgress,
        0
    );

    public int Duration => GetValue(
        fate => fate.Cast<FateContext>()->Duration,
        dynamicEvent => (int)dynamicEvent.SecondsDuration,
        mechaEvent => mechaEvent.EventEndTimestamp - mechaEvent.EventStartTimestamp,
        0
    );

    public float TimeRemaining => GetValue(
        fate => fate.Cast<FateContext>()->StartTimeEpoch + fate.Cast<FateContext>()->Duration - DateTimeOffset.Now.ToUnixTimeSeconds(),
        dynamicEvent => dynamicEvent.SecondsLeft,
        mechaEvent => mechaEvent.EventStartTimestamp + (mechaEvent.EventEndTimestamp - mechaEvent.EventStartTimestamp) - DateTimeOffset.Now.ToUnixTimeSeconds(),
        -1f
    );

    public int StartTimeEpoch => GetValue(
        fate => fate.Cast<FateContext>()->StartTimeEpoch,
        dynamicEvent => dynamicEvent.StartTimestamp,
        mechaEvent => mechaEvent.EventStartTimestamp,
        0
    );

    public int EndTimeEpoch => GetValue(
        fate => fate.Cast<FateContext>()->StartTimeEpoch + fate.Cast<FateContext>()->Duration,
        dynamicEvent => (int)(dynamicEvent.StartTimestamp + dynamicEvent.SecondsDuration),
        mechaEvent => mechaEvent.EventEndTimestamp,
        0
    );

    public bool HasBonus => GetValue(
        fate => fate.Cast<FateContext>()->IsBonus,
        _ => false,
        _ => false,
        false
    );

    public byte Level => GetValue(
        fate => fate.Cast<FateContext>()->Level,
        dynamicEvent => (byte)dynamicEvent.MapMarker.RecommendedLevel,
        mechaEvent => (byte)mechaEvent.MapMarkers[0].MapMarkerData.RecommendedLevel,
        (byte)0
    );

    public string Name => FateType switch {
        FateType.Normal => IDataManager.Get().GetRef<Sheets.Fate>(Id).Value.Name.ToString() ?? $"FATE {Id}",
        FateType.DynamicEvent => IDataManager.Get().GetRef<Sheets.DynamicEvent>(Id).Value.Name.ToString() ?? $"DynamicEvent {Id}",
        FateType.MechaEvent => IDataManager.Get().GetRef<Sheets.WKSMechaEventData>(Id).Value.Name.ToString() ?? $"MechaEvent {Id}",
        _ => $"Unknown Type: {Id}",
    };

    public uint MotivationNpcId => GetValue(
        fate => fate.Cast<FateContext>()->MotivationNpc,
        _ => 0u,
        _ => 0u,
        0u
    );

    public IGameObject? MotivationNpc => GetValue(
        // sometimes when they're initially loaded, the gameobject is garbage with an entity id of 200000001 and object kind MountType
        fate => IObjectTable.Get().FirstOrDefault(o => o.EntityId == fate.Cast<FateContext>()->MotivationNpc && o.ObjectKind == ObjectKind.BattleNpc),
        _ => null,
        _ => null,
        null
    );

    public IGameObject? ObjectiveNpc => GetValue(
        fate => IObjectTable.Get().FirstOrDefault(o => o.EntityId == fate.Cast<FateContext>()->ObjectiveNpc),
        _ => null,
        _ => null,
        null
    );

    public ItemHandle? EventItem => GetValue(
        fate => (ItemHandle)fate.Cast<FateContext>()->TurnInEventItem,
        _ => null,
        _ => null,
        null
    );

    public FateState State => GetValue(
        fate => fate.Cast<FateContext>()->State,
        dynamicEvent => ToFateState(dynamicEvent.State),
        mechaEvent => ToFateState(mechaEvent.Flags),
        (FateState)0
    );

    /// <summary>When a fate hasn't appeared on the map yet</summary>
    public bool IsPending => State == FateState.Running && TimeRemaining <= 0 || !IsOnMap;

    public bool IsOnMap {
        get {
            // some other plugins like mappy fuck with detecting on map presence, so early return in cases where it has to be on the map
            if (State is FateState.Running && TimeRemaining > 0 || Progress > 0) return true;
            // markers go stale when the map isn't open, so force an update
            var agent = AgentMap.Instance();
            agent->UpdateEventMapMarkers(&agent->EventMarkersPtrs);
            return agent->EventMarkers.Any(m => m is { IconId: not 0, DataId: not 0 and var id } && id == Id);
        }
    }

    public FateRule Rule => GetValue(
        fate => (FateRule)fate.Cast<FateContext>()->Rule,
        _ => FateRule.Normal,
        _ => FateRule.Normal,
        FateRule.None
    );

    private FateState ToFateState(DynamicEventState state) => state switch {
        DynamicEventState.Register => FateState.Preparing,
        DynamicEventState.Warmup => FateState.Preparing,
        DynamicEventState.Battle => FateState.Running,
        _ => FateState.Ended,
    };

    private FateState ToFateState(WKSMechaEventFlag flag) => flag switch {
        WKSMechaEventFlag.IsEventActive => FateState.Running,
        _ => FateState.Ended,
    };

    public enum FateRule : byte {
        None = 0,
        Normal = 1, // trash fates or boss fates
        Collect = 2, // pick up EventObjects or get them from killing mobs
        Escort = 3, // guide some npc to the finish line
        Defend = 4, // defend objectives like crates from being destroyed
        EventFate = 5, // used for seasonal event fates, like Little Ladies Day, Hatching Tide
        Chase = 6, // that one special fate in The Peaks
        ConcertedWorks = 7, // rebuilding the firmament fates
        Fete = 8, // firmament fates
    }
}

