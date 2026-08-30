using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Fate;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Lumina.Excel.Sheets;
using Lumina.Extensions;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using TerritoryIntendedUse = FFXIVClientStructs.FFXIV.Client.Enums.TerritoryIntendedUse;

namespace AutoFATE.Core;

internal sealed class FateGrind(FateModule module) : TaskBase {
    // The preset itself (avoidance/steering only — combat belongs to RSR) lives in BossModPreset,
    // which also injects it at install time; the task only self-heals and activates it.
    private const string _presetName = BossModPreset.PresetName;

    private const string AutoTargetModule = "BossMod.Autorotation.MiscAI.AutoTarget";

    /// <summary>True after this task told RSR to fight; transition-guarded so the operating mode
    /// isn't re-sent (and re-toasted by RSR) every single loop tick.</summary>
    private bool _rsrEngaged;

    private int PullSize => IPlayerState.Get().ClassJob.Value switch {
        var cj when cj.IsTank => 0, // unlimited
        var cj when cj.IsDps => 3,
        var cj when cj.IsHealer => 5,
        _ => 1,
    };

    private static IPlayerCharacter? Player => IObjectTable.Get().LocalPlayer;

    protected override async Task Execute() {
        using var stop = new OnDispose(() => Service.TextAdvance.DisableExternalControl(Plugin.PluginName));
        BossModPreset.EnsureInstalled(); // self-heal: BossMod may have (re)loaded without the preset
        try {
            while (!CancelToken.IsCancellationRequested && module.Running) {
                module.StopIfNoRemaining();
                if (module.PendingStopWhenSafe && PublicEvent.CurrentFate is null && !ICondition.Get()[ConditionFlag.InCombat]) {
                    module.PendingStopWhenSafe = false;
                    module.Running = false;
                }
                if (!module.Running)
                    break;

                var state = State;
                module.CurrentState = state.ToString();
                module.CurrentTargetFateId = NextFate?.Id ?? PublicEvent.CurrentFate?.Id;

                // World visits, DC travel, deaths and duty exits all dismiss minions without ever
                // touching the zone-swap path, so reconcile here instead of only after our own
                // teleports. No-op for modes that don't need it; skipped for the states that are
                // either mid-transit (they reconcile at their destination) or unable to act.
                // Runs before HandleIntegrations so a resummon and the preset activation that depends
                // on it land in the same tick.
                if (state is GrindState.BetweenFates or GrindState.Engaging or GrindState.WaitingForFates or GrindState.WaitingForFollowUp)
                    await module.GetCurrentMode().EnsureZoneState(IPlayerState.Get().Territory.RowId, Dismount, CancelToken);

                HandleIntegrations();

                switch (state) {
                    case GrindState.Unconscious:
                        await Revive();
                        break;
                    case GrindState.Engaging:
                        // A fate ring can spawn over a parked, mounted character (HandleNoFates parks
                        // mounted), and HandleIntegrations refuses to engage while mounted — without
                        // this dismount nothing would ever start fighting. Gated on the fate actually
                        // being ours to fight, so idling inside some other fate's ring keeps the mount.
                        if (Player is { Mounted: true } && (NextFate is null || NextFate.Id == PublicEvent.CurrentFate?.Id))
                            await Dismount();
                        // this should only ever happen during hot reloading bossmod during a fate
                        if (PublicEvent.CurrentFate is { IsOnMap: true } current && !Service.BossMod.HasTempMap())
                            await GenerateObstacleMap(current);
                        await NextFrame();
                        break;
                    case GrindState.WaitingForFollowUp:
                        await NextFrame(100);
                        break;
                    case GrindState.PreparingZone:
                        await PrepareZoneState();
                        break;
                    case GrindState.BetweenFates:
                        await MoveToFate();
                        break;
                    case GrindState.WaitingForFates:
                        await HandleNoFates();
                        break;
                    case GrindState.SwapZones:
                        await SwapNewItemTarget();
                        break;
                    case GrindState.SyncToHost:
                        await SyncToHostZone();
                        break;
                    default:
                        await NextFrame();
                        break;
                }
            }
        }
        catch (OperationCanceledException) {
            throw; // expected, don't log
        }
        catch (Exception ex) {
            // Stop (which clears the BossMod preset) BEFORE Error — Error always throws,
            // so anything after it never runs and the character would keep fighting unattended.
            module.Running = false;
            Svc.Chat.Print($"[AutoFATE] Stopped due to an error: {ex.Message} (details in /xllog)");
            Error($"Error: {ex}");
        }
    }

    public PublicEvent? NextFate { get; set; }
    private uint? ReturnToFateId { get; set; } // when we die, if the fate we were in progressed enough to not qualify, we want to return to it anyway
    private uint? LastStuckFateId { get; set; }
    private int ConsecutiveStuckRetries { get; set; }
    private uint? FollowUpFateId { get; set; } // id to store to check if NextFate is a follow up to this
    private long FollowUpWatchUntilMs { get; set; }
    private uint? WaitForExpiryFateId { get; set; } // id for when we leave a collect fate. Stay in zone until fate is null

    public IOrderedEnumerable<PublicEvent> AvailableFates => FateModule.ApplySortOrder(PublicEvent.Fates.Where(module.FateConditions), module.Config.SortOrder);
    private bool HasTwistOfFate => IObjectTable.Get().LocalPlayer?.StatusList.HasTwistOfFate() ?? false;

    // multibox client mode: mirror the host's target instead of picking our own.
    // IsClientRole gates all autonomous behavior (own fate picking, zone roaming) so a stale
    // host feed leaves the client waiting, never grinding unattended on its own; IsFollowing
    // additionally requires a fresh feed and gates actions that consume live host data.
    private bool IsClientRole => module.Multibox.Role == MultiboxRole.Client;
    private bool IsFollowing => module.Multibox.IsFollowingHost;
    private PublicEvent? RemoteFate => module.Multibox.RemoteTargetFateId is { } id ? PublicEvent.GetFateById(id) : null;
    private bool HasCandidateFate => IsClientRole ? RemoteFate is not null : AvailableFates.FirstOrDefault() is not null;

    private GrindState State {
        get {
            // Idle for clients: SwapZones would let a client zone-swap autonomously off mode data
            if (!IObjectTable.Get().LocalPlayer.Available) return IsClientRole ? GrindState.Idle : GrindState.SwapZones;

            if (WaitForExpiryFateId is { } waitId && PublicEvent.GetFateById(waitId) is null)
                WaitForExpiryFateId = null;

            if (ICondition.Get()[ConditionFlag.Unconscious]) {
                // clients don't remember death fates — the host's current target decides where they go
                if (!IsClientRole && PublicEvent.CurrentFate is { Id: var id, Progress: < 100 })
                    ReturnToFateId = id;
                FollowUpFateId = null;
                return GrindState.Unconscious;
            }

            if (PublicEvent.CurrentFate is { } current) {
                if (current.Progress >= 100)
                    StartFollowUpWatch(current);
                else if (FollowUpFateId == current.Id)
                    FollowUpFateId = null;

                // treat completed collect fates as done and wait for out of combat/not busy before trying to move away
                if (current is { Rule: PublicEvent.FateRule.Collect, Progress: >= 100, Id: var id } && !IObjectTable.Get().LocalPlayer.IsBusy) {
                    WaitForExpiryFateId = id;
                    var hasOther = IsClientRole ? RemoteFate is { } remote && remote.Id != id : AvailableFates.FirstOrDefault(f => f.Id != id) is { };
                    if (!hasOther)
                        return GrindState.WaitingForFates;
                    // this branch reaches BetweenFates without falling through to the gate below
                    return module.GetCurrentMode().IsZoneStateSatisfied(IPlayerState.Get().Territory.RowId) ? GrindState.BetweenFates : GrindState.PreparingZone;
                }
                Status = "Engaging";
                return GrindState.Engaging;
            }

            if (ShouldWaitForFollowUp())
                return GrindState.WaitingForFollowUp;

            // don't chase the host into a new zone while collect-fate rewards are still pending here
            if (IsFollowing && WaitForExpiryFateId is null && module.Multibox.HostTerritoryId is { } hostZone && hostZone != IPlayerState.Get().Territory.RowId && !ICondition.Get()[ConditionFlag.InCombat])
                return GrindState.SyncToHost;

            if (!IsClientRole && !HasTwistOfFate && !ICondition.Get()[ConditionFlag.InCombat] && module.IsZoneItemTargetComplete(IPlayerState.Get().Territory.RowId, out _))
                return GrindState.SwapZones;

            // Last gate before committing to a fate: never tag one that cannot pay out. Deliberately
            // below Engaging, so a fate already in progress is fought to the end (the minion may come
            // back before it completes) rather than abandoned.
            if (!module.GetCurrentMode().IsZoneStateSatisfied(IPlayerState.Get().Territory.RowId))
                return GrindState.PreparingZone;

            if (HasCandidateFate)
                return GrindState.BetweenFates;

            return GrindState.WaitingForFates;
        }
    }
    private enum GrindState {
        Idle,
        WaitingForFates,
        WaitingForFollowUp,
        PreparingZone,
        BetweenFates,
        SwapZones,
        SyncToHost,
        Engaging,
        Unconscious,
    }

    private enum MoveStopReason {
        None,
        FateInvalid,
        FatePending,
        HigherPriority,
        NpcLoaded,
        StuckRetry,
        StuckTeleport,
    }

    private sealed class MoveTracker(Vector3 initialPosition, long initialTick) {
        private Vector3 LastProgressPosition { get; set; } = initialPosition;
        private long LastProgressAt { get; set; } = initialTick;
        private long LastPathActivityAt { get; set; } = initialTick;
        private Vector3 RetryPosition { get; set; }
        private bool RetriedOnce { get; set; }
        private bool WasRunning { get; set; }

        public MoveStopReason CheckStuck(Vector3 currentPosition) {
            var now = Environment.TickCount64;
            var isRunning = Svc.Navmesh.IsRunning();
            var isPathfinding = Svc.Navmesh.PathfindInProgress;

            if (isRunning || isPathfinding)
                LastPathActivityAt = now;

            if (!isRunning) {
                WasRunning = false;
                LastProgressPosition = currentPosition;
                LastProgressAt = now;

                // if vnav hard fails then it'll go back to being idle while MoveTo is waiting for it
                if (!isPathfinding && now - LastPathActivityAt >= 1500) {
                    if (RetriedOnce && Vector3.Distance(currentPosition, RetryPosition) <= 3f)
                        return MoveStopReason.StuckTeleport;

                    RetryPosition = currentPosition;
                    RetriedOnce = true;
                    return MoveStopReason.StuckRetry;
                }

                return MoveStopReason.None;
            }

            if (!WasRunning) {
                WasRunning = true;
                LastProgressPosition = currentPosition;
                LastProgressAt = now;
                return MoveStopReason.None;
            }

            if (Vector3.Distance(currentPosition, LastProgressPosition) > 1.5f) {
                LastProgressPosition = currentPosition;
                LastProgressAt = now;
                return MoveStopReason.None;
            }

            if (now - LastProgressAt < 2000)
                return MoveStopReason.None;

            if (RetriedOnce && Vector3.Distance(currentPosition, RetryPosition) <= 3f)
                return MoveStopReason.StuckTeleport;

            RetryPosition = currentPosition;
            RetriedOnce = true;
            return MoveStopReason.StuckRetry;
        }
    }

    private async Task Revive() {
        using var scope = BeginScope(nameof(Revive));
        if (Player is null) return;

        // RSR turns itself off on death (AutoOffWhenDead, default ON) without telling us. Drop the
        // latch so the next engagement re-sends the operating mode — otherwise an in-place party
        // raise inside the fate ring would leave RSR off while _rsrEngaged still reads true.
        _rsrEngaged = false;

        await WaitUntil(() => Player.IsRevivable, "WaitForRevivable");
        (var lastZone, var lastPos) = (IPlayerState.Get().Territory, Player.Position);
        if (IPartyList.Get().Length is 0) {
            Status = "Reviving";
            GameMain.ExecuteCommand(CommandFlag.Revive.Value, AgentReviveOp.Return.Value);
        }
        else {
            Status = "Waiting For Raise";
            await WaitUntil(() => Player.ReviveState is 2, "WaitingForRaise"); // 1 = return, 2 = raise
            GameMain.ExecuteCommand(CommandFlag.Revive.Value, AgentReviveOp.AcceptRevive.Value); // a1=5 for raises
        }
        await WaitWhile(() => ICondition.Get()[ConditionFlag.Unconscious], "WaitForAlive");

        // if the zone we were in was an instanced zone, we might end up in a different one when tp'ing back
        // if the way back involves taking a city route, we don't be near an aetheryte to swap instances
        if (Player.Territory.RowId != lastZone.RowId) {
            await TeleportTo(lastZone.RowId, lastPos);
            await UseAethernet(lastZone.RowId, lastPos);
        }
    }

    private async Task MoveToFate() {
        using var scope = BeginScope(nameof(MoveToFate));
        if (Player is null) return;

        IEnumerable<PublicEvent> GetAvailableFates() {
            // If current is a collect at 100% we're leaving it; pick a different fate.
            if (PublicEvent.CurrentFate is { Rule: PublicEvent.FateRule.Collect, Progress: >= 100, Id: var currentId })
                return AvailableFates.Where(f => f.Id != currentId);
            return AvailableFates;
        }

        bool TrySelectNextFate(out PublicEvent selected) {
            if (IsClientRole) {
                // clients mirror the host's target; local priorities/filters don't apply,
                // and a stale feed means "wait", never "pick my own"
                if (RemoteFate is { } remote) {
                    selected = remote;
                    return true;
                }
                selected = null!;
                return false;
            }

            if (ReturnToFateId is { } returnFateId) {
                if (PublicEvent.GetFateById(returnFateId) is { Progress: < 100 } returnFate) {
                    selected = returnFate;
                    return true;
                }

                ReturnToFateId = null;
            }

            if (FollowUpFateId is { } parentId && Environment.TickCount64 < FollowUpWatchUntilMs) {
                var parent = Fate.GetRow(parentId);
                // allow even if pending
                if (PublicEvent.Fates.Where(f => f.FateType == FateType.Normal && f.Id > parentId && Fate.GetRow(f.Id).Location == parent.Location).OrderBy(f => Player!.DistanceTo(f.Position)).FirstOrDefault() is { } followUp) {
                    selected = followUp;
                    return true;
                }
            }

            if (GetAvailableFates().FirstOrDefault() is { } candidate) {
                selected = candidate;
                return true;
            }

            selected = null!;
            return false;
        }

        if (!TrySelectNextFate(out var nextFate))
            return;

        NextFate = nextFate;
        module.CurrentTargetFateId = nextFate.Id; // publish before the travel leg, not after it

        // Leaving a completed collect fate: HandleIntegrations ran before this selection with
        // NextFate still pointing at the departing fate, so its deactivate-on-leave branch could not
        // fire — and it won't run again until the travel leg ends. Turn combat off here, or BossMod
        // and RSR stay live (tagging mobs, breaking mounts) for the whole trip.
        if (PublicEvent.CurrentFate is { } departing && departing.Id != nextFate.Id && !ICondition.Get()[ConditionFlag.InCombat])
            DeactivateIntegrations(clearNextFate: false);
        if (!NextFate.IsOnMap) {
            Status = "Waiting for fate to appear";
            await Mount();
            await NextFrame(30);
            return;
        }

        var msh = await FateDestination(NextFate);
        Log($"[NextFate={NextFate.Position}] -> [mesh={msh}]");

        var progress = new MoveTracker(Player.Position, Environment.TickCount64);
        var stopReason = MoveStopReason.None;

        bool IsCurrentFateInvalid() {
            if (NextFate is null)
                return true;
            if (PublicEvent.GetFateById(NextFate.Id) is not { } current)
                return true;

            NextFate = current; // keep nextfate fresh in case an unactivated fate disappears while pathing to it
            if (IsClientRole)
                return module.Multibox.RemoteTargetFateId != current.Id; // host switched targets or went stale

            if (!current.IsOnMap)
                return false;

            return ReturnToFateId == current.Id ? current.Progress >= 100 : !module.FateConditions(current);
        }

        bool TrySwitchToHigherPriorityFate() {
            if (IsClientRole)
                return false; // host target changes are handled by IsCurrentFateInvalid

            // don't check if we're returning to a previous fate
            if (ReturnToFateId is not null || NextFate is null)
                return false;

            if (GetAvailableFates().FirstOrDefault() is not { } higherPrio || higherPrio.Id == NextFate.Id)
                return false;

            Log($"Switching target fate {NextFate.Id} -> {higherPrio.Id} (higher priority)");
            NextFate = higherPrio;
            return true;
        }

        bool ShouldSwitchToNpc() => NextFate is { State: FateState.Preparing } fate && TryGetValidMotivationNpc(fate, out _);

        bool ShouldStopMove() {
            // preserve the first reason so it can't be overwritten by a later check.
            if (stopReason != MoveStopReason.None)
                return true;

            stopReason = MoveStopReason.None;

            if (IsCurrentFateInvalid()) {
                stopReason = MoveStopReason.FateInvalid;
                return true;
            }

            if (TrySwitchToHigherPriorityFate()) {
                stopReason = MoveStopReason.HigherPriority;
                return true;
            }

            if (NextFate is { IsOnMap: false }) {
                stopReason = MoveStopReason.FatePending;
                return true;
            }

            if (ShouldSwitchToNpc()) {
                stopReason = MoveStopReason.NpcLoaded;
                return true;
            }

            if (Player is { Position: var pos } && progress.CheckStuck(pos) is not MoveStopReason.None and var reason) {
                if (reason == MoveStopReason.StuckTeleport)
                    Warning("Stuck again; teleporting instead");
                else
                    Warning("Stuck on the way to fate. Retrying from current position");

                stopReason = reason;
                return true;
            }

            return false;
        }

        await GenerateObstacleMap(nextFate);
        await MoveTo(msh, MovementConfig.Everything.WithTolerance(3),
            // in progress = urgent, otherwise teleporting all the time isn't necessary
            // also prohibit when you have the xp buff or when waiting for collect fate rewards
            allowTeleportIfFaster: NextFate is { Progress: > 0 } && !HasTwistOfFate && WaitForExpiryFateId is null,
            stopCondition: ShouldStopMove,
            onStopReached: async () => {
                if (stopReason == MoveStopReason.NpcLoaded)
                    await ActivateFate();
            });

        Log($"{nameof(MoveToFate)} finished with stopReason={stopReason} fate={NextFate?.Id}");

        if (stopReason == MoveStopReason.StuckRetry && NextFate is { Id: var stuckFateId }) {
            if (LastStuckFateId == stuckFateId)
                ConsecutiveStuckRetries++;
            else {
                LastStuckFateId = stuckFateId;
                ConsecutiveStuckRetries = 1;
            }

            if (ConsecutiveStuckRetries >= 2) {
                Warning($"Escalating repeated stuck retries to teleport for fate {stuckFateId}");
                stopReason = MoveStopReason.StuckTeleport;
            }
        }
        else if (stopReason != MoveStopReason.StuckTeleport) {
            LastStuckFateId = null;
            ConsecutiveStuckRetries = 0;
        }

        if (stopReason == MoveStopReason.HigherPriority)
            return;

        if (stopReason == MoveStopReason.FatePending) {
            Status = "Waiting for fate to appear";
            await Mount();
            await NextFrame(30);
            return;
        }

        if (stopReason == MoveStopReason.StuckTeleport && WaitForExpiryFateId is null && NextFate is { Id: var fateId } && PublicEvent.GetFateById(fateId) is { } currentFate) {
            NextFate = currentFate;
            LastStuckFateId = null;
            ConsecutiveStuckRetries = 0;
            Status = "Teleporting to fate";
            var fateTerritoryId = Player.Territory.RowId;
            await TeleportTo(fateTerritoryId, currentFate.Position, allowSameZoneTeleport: true);
            await UseAethernet(fateTerritoryId, currentFate.Position);
            return;
        }

        // only activate after a normal arrival; if we explicitly stopped (e.g. npcloaded), let the loop re-handle
        if (stopReason == MoveStopReason.None && NextFate is { State: FateState.Preparing, MotivationNpcId: not 0xE0000000 } && PublicEvent.Fates.Any(f => f.Id == NextFate.Id))
            await ActivateFate();
    }

    private const int FateDestinationAttempts = 5;

    /// <summary>
    /// A point inside the fate ring that vnav can actually reach. One random offset used to be taken
    /// on trust: when it failed to snap, the raw point was used anyway and the character pathed at
    /// something off-mesh. Rings sit on terrain that climbs well past the default 5y search box, so
    /// this searches taller and tries several offsets before settling for the centre.
    /// </summary>
    private async Task<Vector3> FateDestination(PublicEvent fate) {
        // The mesh queries below return null while the navmesh is unloaded — which it is for a moment
        // after every teleport, exactly when this runs. Without this wait the fallback fires for a
        // reason that has nothing to do with the terrain and we path at an unsnapped point.
        await NavmeshReady();

        for (var attempt = 0; attempt < FateDestinationAttempts; attempt++) {
            if (Svc.Navmesh.NearestPointReachable(fate.Position.RandomPoint(fate.Radius * 0.5f), 5, 20) is { } onMesh)
                return onMesh;
        }

        if (Svc.Navmesh.NearestPointReachable(fate.Position, 10, 30) is { } centre) {
            Log($"No random point on mesh for fate {fate.Id}; using its centre");
            return centre;
        }

        Warning($"No reachable mesh point in fate {fate.Id}; heading for its centre unsnapped");
        return fate.Position;
    }

    // some are just so bad it's not worth having them. No better solution than this yet.
    private readonly List<uint> _obstacleMapBlacklist = [1831, 1832, 1914, 1915];
    private async Task GenerateObstacleMap(PublicEvent evt) {
        if (_obstacleMapBlacklist.Contains(evt.Id)) {
            return;
        }

        if (!Service.BossMod.SupportsObstacleMaps) {
            return; // BossMod build without ObstacleMap IPC; it can still fight, just without the nav bitmap
        }

        if (!Svc.Navmesh.IsAvailable) {
            return; // vnavmesh gone mid-run; waiting on BuildProgress would never complete
        }

        using var scope = BeginScope(nameof(GenerateObstacleMap));

        // bitmap is built via vnav and doesn't await the mesh still being built
        if (!Svc.Navmesh.IsReady) {
            Status = "Waiting for Navmesh";
            // No mesh and no build running: vnav is wedged (or auto-load is off) and will never
            // start one on its own — same recovery as NavmeshReady, but skip instead of erroring.
            // Bounded, because vnav unloading after the IsAvailable check above would otherwise
            // leave this spinning on gates that permanently answer false/-1.
            if (Svc.Navmesh.BuildProgress < 0)
                Svc.Navmesh.Reload();
            var deadline = Environment.TickCount64 + 15_000;
            await WaitUntil(() => Svc.Navmesh.IsReady || Svc.Navmesh.BuildProgress >= 0 || Environment.TickCount64 >= deadline, "WaitForBuildStart");
            if (Svc.Navmesh.BuildProgress >= 0)
                await WaitWhile(() => Svc.Navmesh.BuildProgress >= 0, "BuildMesh");
            if (!Svc.Navmesh.IsReady) {
                Warning($"Navmesh not ready; skipping obstacle map for fate {evt.Id}");
                return;
            }
        }

        // sometimes the center of a fate is unreachable (tower fate in amh araeng), so generate from a reachable point then compensate for being off center.
        // Search box matches FateDestination's: fate rings sit on terrain that climbs well past the default 5y,
        // and a null here used to shrink the bitmap to a 10y disc (see below), which is worse than not snapping.
        var safe = Svc.Navmesh.NearestPointReachable(evt.Position, 10, 30);
        float? margin = safe is { } ? Vector3.Distance(evt.Position, safe.Value) : null;
        try {
            // `+` binds tighter than `??`, so `evt.Radius + margin ?? 10` parsed as `(evt.Radius + margin) ?? 10`:
            // whenever the snap failed the whole expression collapsed to a *10 yard* bitmap around a 40-60y ring.
            // BossMod then had obstacle data for a tiny disc and empty space everywhere else, which is exactly
            // the state where it walks into terrain. The 10 was always meant to be the fallback margin.
            if (!Service.BossMod.Generate(safe ?? evt.Position, evt.Radius + (margin ?? 10), false)) {
                Warning($"Obstacle map generation failed to start for fate {evt.Id}");
                _obstacleMapBlacklist.Add(evt.Id);
                return;
            }
        }
        catch (Exception ex) {
            // a previously faulted generation rethrows through Generate; don't let it kill the task
            Warning($"Obstacle map generation failed to start for fate {evt.Id}: {ex.Message}");
            _obstacleMapBlacklist.Add(evt.Id);
            return;
        }

        var generationFailed = false;
        await WaitUntil(() => {
            var status = Service.BossMod.GetGenerationStatus();
            if (status is TaskStatus.RanToCompletion) {
                Log($"Obstacle map generated for fate {evt.Id}");
                return true;
            }
            if (status is TaskStatus.Faulted) {
                Warning($"Obstacle map generation failed for fate {evt.Id}");
                generationFailed = true;
                return true; // allow moving without the map rather than getting stuck in an infinite wait
            }
            return false;
        }, "WaitForObstacleMap");

        if (generationFailed) {
            _obstacleMapBlacklist.Add(evt.Id);
            return;
        }

        if (Service.BossMod.EvaluateTempMapQuality() is { } quality) {
            Log($"Generated obstacle map quality for fate {evt.Id}: {quality}");
            if (quality.IsBad) {
                // Warning, not Log: this is the one line that explains "the character runs into walls".
                // Clearing the map does not make BossMod cautious — it makes BossMod pathfind over an
                // empty arena, so it beelines through cliffs and never corrects. Only maps that are
                // structurally unusable get here now (see BitmapQuality.IsBad).
                Warning($"Obstacle map for fate {evt.Id} is unusable ({quality}); clearing it. BossMod will navigate as if the terrain were empty for this fate.");
                _obstacleMapBlacklist.Add(evt.Id);
                Service.BossMod.ClearTempMap();
            }
        }
        else
            // Log, not Warning: a BossMod build without the EvaluateTempMapQuality gate would warn on every fate forever.
            Log($"BossMod returned no quality metrics for fate {evt.Id}'s obstacle map; keeping it anyway");
    }

    private async Task ActivateFate() {
        using var scope = BeginScope(nameof(ActivateFate));
        if (Player is null) return;

        if (NextFate is not { } fate)
            return;
        if (!fate.IsOnMap)
            return;

        // sometimes fates are in prep for a very long time before they're on the map. Wait until the npc is actually ready before returning/attempting anything
        await WaitUntil(() => TryGetValidMotivationNpc(fate, out _) || fate.State is FateState.Running, "WaitForNpcSpawn");

        if (fate.State is FateState.Running) return; // someone beat us to activating

        if (TryGetValidMotivationNpc(fate, out var npc)) {
            Log($"ActivateFate start: fate={NextFate.Id} npc={npc.EntityId} npcPos={npc.Position} playerPos={Player.Position} dist={Player.DistanceTo(npc.Position):F2} inRange={npc.IsInInteractRange()}");
            await MoveTo(npc.Position, MovementConfig.InteractRange.WithOptions(MovementOptions.Current));
            Log($"ActivateFate after MoveTo: npc={npc.EntityId} playerPos={Player.Position} dist={Player.DistanceTo(npc.Position):F2} inRange={npc.IsInInteractRange()}");
            try {
                await InteractWith(npc, () => NextFate?.State == FateState.Running || !TryGetValidMotivationNpc(fate, out _), skip: UiSkipOptions.Talk | UiSkipOptions.YesNo);
            }
            catch (Exception ex) {
                // will crash if we don't catch and it's fine if interact fails because the npc/fate disappeared before we could start
                if (NextFate is null || !TryGetValidMotivationNpc(NextFate, out _) || NextFate.State != FateState.Preparing) {
                    Warning($"Skipping fate activation: npc/fate vanished before interact ({ex.Message})");
                    return;
                }
                throw;
            }
        }
        else
            Error($"Something weird happened with the npc activation [{fate}]");
    }

    /// <summary>
    /// Parks the grind until the mode's zone requirements hold. Retries indefinitely by design — a fate
    /// completed without the zone's Yo-kai minion out pays nothing, so waiting beats grinding blind —
    /// but yields each pass so the run stays cancellable and "/autofate stop" still responds.
    /// </summary>
    private async Task PrepareZoneState() {
        using var scope = BeginScope(nameof(PrepareZoneState));
        Status = "Waiting for the zone's minion";
        await module.GetCurrentMode().EnsureZoneState(IPlayerState.Get().Territory.RowId, Dismount, CancelToken);
        if (!module.GetCurrentMode().IsZoneStateSatisfied(IPlayerState.Get().Territory.RowId))
            await NextFrame(30); // summon was rejected (combat/rain/occupied); back off before retrying
    }

    /// <summary>Multibox client: follow the host into its current zone.</summary>
    private async Task SyncToHostZone() {
        if (Player is null || module.Multibox.HostTerritoryId is not { } destination || destination == Player.Territory.RowId)
            return;

        using var scope = BeginScope(nameof(SyncToHostZone));
        Status = "Teleporting to host's zone";
        await Mount();
        await TeleportTo(destination, Vector3.Zero);
        // the minion requirement is per-character, so clients fix their own rather than inheriting the host's
        await module.GetCurrentMode().EnsureZoneState(destination, Dismount, CancelToken);
    }

    private async Task HandleNoFates() {
        if (WaitForExpiryFateId is not null) {
            using var scope = BeginScope("WaitForFateRewards");
            Status = "Waiting for fate rewards";
            await Mount();
            await NextFrame(60);
            return;
        }

        if (IsClientRole) {
            // clients never roam or swap zones on their own; the host decides where to go next
            using var scope = BeginScope("WaitForHost");
            Status = IsFollowing ? "Waiting for host's next fate" : "Waiting for host connection";
            await Mount();
            await NextFrame(60);
            return;
        }

        var hasEffectiveZones = module.GetEffectiveSwapZones() is { Count: > 0 } || module.HasSelectedSwapZones;
        if (!HasTwistOfFate && (hasEffectiveZones || module.Config.SwapZones)) {
            using var scope = BeginScope("SwapZones");
            // The achievement/random fallbacks are for free-roam grinding. When the mode defines its
            // own zones they'd send us somewhere the mode earns nothing at all, so stay put instead
            // and wait for a fate here.
            var destination = module.GetNextPreferredSwapZone(Player.Territory.RowId)
                ?? (module.ModeSuppliesSwapZones ? Player.Territory.RowId : GetNextAchievementZone() ?? GetRandomSameExpacZone());
            if (destination == Player.Territory.RowId) {
                Status = "Waiting for fates in selected zones";
                await Mount();
                await NextFrame(60);
                return;
            }

            await Mount();
            await TeleportTo(destination, Vector3.Zero);
            await module.GetCurrentMode().EnsureZoneState(destination, Dismount, CancelToken);
        }
        else {
            using var scope = BeginScope("WaitForFates");
            Status = HasTwistOfFate ? "Waiting for fates (preserving Twist of Fate)" : "Waiting for fates to spawn";
            await Mount();
            await NextFrame(60);
        }
    }

    private async Task SwapNewItemTarget() {
        if (!module.IsZoneItemTargetComplete(Player.Territory.RowId, out var destination))
            return;
        using var scope = BeginScope("SwapNewItemTarget");
        if (destination != Player.Territory.RowId) {
            Status = "ZoneItemTarget complete. Swapping zones.";
            await Mount();
            await TeleportTo(destination, Vector3.Zero);
        }
        else
            Status = "ZoneItemTarget complete. Switching target.";
        await module.GetCurrentMode().EnsureZoneState(destination, Dismount, CancelToken);
    }

    private void HandleIntegrations() {
        if (PublicEvent.CurrentFate is { } fate) {
            // when we leave collect fates early, it's still CurrentFate, so we need to ignore that and deactivate anyway
            if (fate is { Rule: PublicEvent.FateRule.Collect, Progress: >= 100 } && (NextFate is null || NextFate.Id != fate.Id)) {
                // don't deactivate before we're out of combat
                if (ICondition.Get()[ConditionFlag.InCombat])
                    return;
                DeactivateIntegrations(clearNextFate: false);
                return;
            }

            // only activate for the fate we're pathfinding to (or any if NextFate is null)
            if (NextFate is { } next && fate.Id != next.Id
                && !(fate is { Rule: PublicEvent.FateRule.Collect, Progress: >= 100 } && ICondition.Get()[ConditionFlag.InCombat])) {
                DeactivateIntegrations(clearNextFate: false);
                return;
            }

            if (Player.Mounted) {
                DeactivateIntegrations(clearNextFate: false);
                return;
            }

            // Don't let BossMod start swinging before the zone's requirements hold. This is the last
            // chokepoint: MoveTo can teleport within the zone when that is faster, and a same-zone
            // teleport reloads the area and dismisses the minion, so arriving "prepared" is not enough.
            // Gated on being out of combat — refusing to fight while already engaged would strand us
            // taking damage with no way to summon (the summon is itself blocked in combat).
            if (!ICondition.Get()[ConditionFlag.InCombat] && !module.GetCurrentMode().IsZoneStateSatisfied(IPlayerState.Get().Territory.RowId)) {
                DeactivateIntegrations(clearNextFate: false);
                return;
            }

            if (Service.BossMod.GetActive() != _presetName) {
                if (Service.BossMod.Get(_presetName) is null)
                    Service.BossMod.Create(BossModPreset.Json, true);
                else
                    Service.BossMod.SetActive(_presetName);
            }
            Service.BossMod.AddTransientStrategy(_presetName, AutoTargetModule, "MaxTargets", PullSize.ToString());

            // Combat actions come from RSR in Manual mode: it fights whatever AutoTarget selects and
            // never picks targets itself, so the two plugins can't disagree about the pull.
            if (!_rsrEngaged)
                _rsrEngaged = Service.RotationSolver.Engage();

            if (PublicEvent.CurrentFate is { Rule: PublicEvent.FateRule.Collect } && !Service.TextAdvance.IsInExternalControl())
                Service.TextAdvance.EnableExternalControl(Plugin.PluginName, new() { EnableTalkSkip = true, EnableRequestFill = true, EnableRequestHandin = true });
        }
        else {
            // Fate ended; clear NextFate so routing is correct. Only turn off combat preset once out of combat,
            // so we don't get stuck if a non-fate mob is still aggroed when the fate completes.
            NextFate = null;
            if (!ICondition.Get()[ConditionFlag.InCombat])
                DeactivateIntegrations(clearNextFate: false);
        }
    }

    private void DeactivateIntegrations(bool clearNextFate) {
        if (clearNextFate)
            NextFate = null;

        Service.BossMod.ClearActive();
        if (_rsrEngaged) {
            Service.RotationSolver.Off();
            _rsrEngaged = false;
        }
        ITargetManager.Get().Target = null; // avoid preset trying to go to the mob and interfering with casts
        if (Service.TextAdvance.IsInExternalControl())
            Service.TextAdvance.DisableExternalControl(Plugin.PluginName);
    }

    private bool TryGetValidMotivationNpc(PublicEvent fate, [NotNullWhen(true)] out IGameObject? npc) {
        npc = null;
        if (Player?.DistanceTo(fate.Position) > 50) // half the object table range
            return false;

        if (fate.MotivationNpc is not { IsTargetable: true } target)
            return false;

        npc = target;
        return true;
    }

    private const int FollowUpWaitLimit = 15_000;
    private void StartFollowUpWatch(PublicEvent completed) {
        // Follow-up chains only exist for normal fates; DynamicEvent/MechaEvent ids are not Fate sheet rows.
        if (completed.FateType != FateType.Normal)
            return;

        var completedFateId = completed.Id;
        if (!Fate.GetRow(completedFateId).HasFollowUp)
            return;

        if (FollowUpFateId != completedFateId)
            Log($"Watching for follow-up fate after {completedFateId} for {FollowUpWaitLimit / 1000}s");

        FollowUpFateId = completedFateId;
        FollowUpWatchUntilMs = Environment.TickCount64 + FollowUpWaitLimit;
    }

    private bool ShouldWaitForFollowUp() {
        if (IsClientRole)
            return false; // the host does the follow-up watching; clients just mirror its target

        if (FollowUpFateId is not { } fateId)
            return false;

        var row = Fate.GetRow(fateId);
        if (PublicEvent.Fates.Any(f => f.FateType == FateType.Normal && f.Id > fateId && Fate.GetRow(f.Id).Location == row.Location)) {
            Log($"Detected follow-up fate for {fateId}, resuming routing");
            FollowUpFateId = null;
            return false;
        }

        if (Environment.TickCount64 >= FollowUpWatchUntilMs) {
            FollowUpFateId = null;
            return false;
        }

        Status = $"Waiting for follow-up fate ({(FollowUpWatchUntilMs - Environment.TickCount64) / 1000 + 1}s)";
        return true;
    }

    private unsafe uint? GetNextAchievementZone() {
        var agent = AgentFateProgress.Instance();
        if (agent == null) return null;

        // prioritise zones in the same expac as current area
        var currentTabIndex = Array.FindIndex(agent->Tabs.ToArray(), tab => tab.Zones.ToArray().Any(zone => Player.Territory.RowId == zone.TerritoryTypeId));
        var zones = (currentTabIndex != -1 && currentTabIndex < agent->Tabs.Length - 1)
            ? agent->Tabs[currentTabIndex].Zones.ToArray()
            : agent->Tabs.ToArray().SelectMany(tab => tab.Zones.ToArray());

        return zones.FirstOrNull(zone => zone.NeededFates - zone.FateProgress > 0)?.TerritoryTypeId;
    }

    private uint GetRandomSameExpacZone() {
        var rows = TerritoryType.Where(x => x.IsInUse && x.TerritoryIntendedUse.Value.StructsEnum is TerritoryIntendedUse.Overworld && x.ExVersion.RowId == Player.Territory.Value.ExVersion.RowId && !x.IsPvpZone);
        return rows[new Random().Next(rows.Length)].RowId;
    }
}
