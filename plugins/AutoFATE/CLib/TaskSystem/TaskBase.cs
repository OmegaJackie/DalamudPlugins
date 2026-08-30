using AutoFATE.CLib.Services;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Network;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using System.Threading.Tasks;

namespace AutoFATE.CLib.TaskSystem;

[Flags]
public enum MovementOptions {
    None = 0,
    Mount = 1 << 0,
    Fly = 1 << 1,
    Dismount = 1 << 2,
}

public enum PathingStrategy {
    Auto = 0,
    Navmesh = 1,
    Direct = 2,
}

public static class MovementOptionsExtensions {
    extension(MovementOptions) {
        public static MovementOptions Current {
            get {
                if (IObjectTable.Get().LocalPlayer.InFlight)
                    return MovementOptions.Mount | MovementOptions.Fly | MovementOptions.Dismount;
                if (IObjectTable.Get().LocalPlayer.Mounted)
                    return MovementOptions.Mount | MovementOptions.Dismount;
                return MovementOptions.None;
            }
        }
    }
}

public readonly record struct MovementConfig(float? Tolerance, MovementOptions Movement, PathingStrategy Pathing) {
    public static MovementConfig Default => new(null, MovementOptions.None, PathingStrategy.Auto);
    public static MovementConfig Everything => new(null, MovementOptions.Mount | MovementOptions.Fly | MovementOptions.Dismount, PathingStrategy.Auto);
    public static MovementConfig GroundMove => new(null, MovementOptions.Mount | MovementOptions.Dismount, PathingStrategy.Auto);
    public static MovementConfig InteractRange => new(3, MovementOptions.None, PathingStrategy.Auto);

    public MovementConfig WithTolerance(float? tolerance) => this with { Tolerance = tolerance };
    public MovementConfig WithTolerance(InteractRange range) => this with { Tolerance = range.MaxDistance };
    public MovementConfig WithOptions(MovementOptions movement) => this with { Movement = movement };
    public MovementConfig WithStrategy(PathingStrategy pathing) => this with { Pathing = pathing };
}

public readonly record struct InteractRange(float MaxDistance, float MaxUpDistance) {
    public static InteractRange Aetheryte => new(8.5f, float.MaxValue);
    public static InteractRange GatheringPoint => new(3, 3);
    public static InteractRange EventObj => new(2.1f, float.MaxValue);
}

[Flags]
public enum UiSkipOptions {
    None = 0,
    Talk = 1 << 0,
    YesNo = 1 << 1,
    Request = 1 << 2,
}

public abstract class TaskBase : AutoTask {
    /// <summary>
    /// Raised with the destination aetheryte id immediately before an aetheryte teleport is requested.
    /// A teleport made inside a party extends a free ride-along offer to every member in the zone, and
    /// the receiving client cannot tell from the prompt who offered — this is how it finds out that the
    /// offer about to arrive is ours. Static because tasks are created and discarded per run.
    /// </summary>
    public static event Action<uint>? Teleporting;

    // Wall-clock, not frames — a tick count shrinks with framerate. Long waits here are legitimate:
    // a cross-zone volume pathfind alone has been observed at 9s, and vnav runs requests serially, so
    // anything queued ahead (obstacle bitmaps, other plugins) adds its runtime. This is purely a
    // hung-task backstop, not a performance guard.
    private const long PathfindTimeoutMs = 60_000;
    private const int NavigationIdleTicks = 10;
    private const int HandoffAttempts = 3;
    private const long NavmeshBuildStartTimeoutMs = 15_000;

    private readonly OverrideMovement movement = new();
    private static IPlayerCharacter? Player => IObjectTable.Get().LocalPlayer;

    protected TaskBase() {
        RegisterCleanup(movement);
    }

    /// <summary>Callers that query the mesh before moving must await this first — every Query.Mesh.*
    /// gate returns null while the navmesh is unloaded, which is indistinguishable from "no such point".</summary>
    protected async Task NavmeshReady() {
        using var scope = BeginScope("WaitingForNavmesh");
        // Without vnavmesh, IsReady stays false and BuildProgress stays -1 forever — abort instead of soft-locking.
        ErrorIf(!Svc.Navmesh.IsAvailable, "vnavmesh is not available");
        if (Svc.Navmesh.IsReady)
            return;

        Status = "Waiting for Navmesh";

        // No mesh and no build running. vnav only kicks a load when its zone key CHANGES, so a load
        // that failed or was cancelled leaves it wedged: mesh null, no task, and a key that already
        // matches this zone — its own Update then does nothing on every tick, forever. Reload builds
        // from that existing key, so asking once is the way out.
        if (Svc.Navmesh.BuildProgress < 0) {
            Warning("vnav has no navmesh and none building; requesting a reload");
            Svc.Navmesh.Reload();
        }

        var deadline = Environment.TickCount64 + NavmeshBuildStartTimeoutMs;
        await WaitUntil(() => Svc.Navmesh.IsReady || Svc.Navmesh.BuildProgress >= 0 || Environment.TickCount64 >= deadline, "WaitForBuildStart");

        // Progress is only unbounded-waited once it exists: a moving value proves vnav is working.
        if (Svc.Navmesh.BuildProgress >= 0)
            await WaitWhile(() => Svc.Navmesh.BuildProgress >= 0, "BuildMesh");

        ErrorIf(!Svc.Navmesh.IsReady,
            $"vnavmesh has no navmesh for this zone {NavmeshBuildStartTimeoutMs / 1000}s after a reload request "
            + $"(autoLoad={Svc.Navmesh.IsAutoLoad}, buildProgress={Svc.Navmesh.BuildProgress:F2}). "
            + "Check /vnav for a build error, and try Rebuild there.");
    }

    protected async Task MoveToFlag(MovementConfig config, bool allowTeleportIfFaster = true, Func<bool>? stopCondition = null, Func<Task>? onStopReached = null) {
        using var scope = BeginScope("MoveToFlag");
        if (FlagMapMarker.Get() is not { } flag) {
            Error($"No flag set!");
            return;
        }
        var destination = flag.Position.ToVector3();
        var teleportTerritoryId = flag.TerritoryId;
        var teleportDestination = destination;
        if (flag.TerritoryId == 886) {
            teleportTerritoryId = 418;
            teleportDestination = Coords.AetherytePosition(70);
        }
        await TeleportTo(teleportTerritoryId, teleportDestination);
        await UseAethernet(flag.TerritoryId, destination);
        ErrorIf(IClientState.Get().TerritoryType != flag.TerritoryId, $"Failed to reach flag territory (exp: {flag.TerritoryId}, act: {IClientState.Get().TerritoryType})");
        await NavmeshReady();
        if (Svc.Navmesh.FlagToPoint() is not { } pof) {
            Error($"Unable to convert flag to point on floor");
            return;
        }
        await MoveTo(pof, config, allowTeleportIfFaster, stopCondition, onStopReached);
    }

    protected async Task MoveTo(uint territoryId, Vector3 dest, MovementConfig config, bool allowTeleportIfFaster = true, Func<bool>? stopCondition = null, Func<Task>? onStopReached = null, bool allowAethernetWithinTerritory = true) {
        using var scope = BeginScope("MoveToCmb");
        var teleportTerritoryId = territoryId;
        var teleportDestination = dest;
        if (territoryId == 886) {
            teleportTerritoryId = 418;
            teleportDestination = Coords.AetherytePosition(70);
        }
        await TeleportTo(teleportTerritoryId, teleportDestination);
        await UseAethernet(territoryId, dest);
        ErrorIf(IClientState.Get().TerritoryType != territoryId, $"Failed to reach territory (exp: {territoryId}, act: {IClientState.Get().TerritoryType})");
        await MoveTo(dest, config, allowTeleportIfFaster, stopCondition, onStopReached, allowAethernet: allowAethernetWithinTerritory);
        await NavmeshReady();
    }

    protected async Task MoveTo(Vector3 dest, MovementConfig config, bool allowTeleportIfFaster = true, Func<bool>? stopCondition = null, Func<Task>? onStopReached = null, bool allowAethernet = true) {
        using var scope = BeginScope("MoveTo");
        await WaitUntil(() => Player.Available, "WaitingForPlayer");
        var tolerance = Math.Max(config.Tolerance ?? 0, Svc.Navmesh.GetTolerance());
        if (Player.WithinRange(dest, tolerance))
            return;

        if (allowTeleportIfFaster && Coords.IsTeleportingFaster(dest)) {
            await TeleportTo(IClientState.Get().TerritoryType, dest, allowSameZoneTeleport: true);
            await WaitWhile(() => Player.IsBusy, "WaitForAvailable");
        }

        if (allowAethernet)
            await UseAethernet(IClientState.Get().TerritoryType, dest);

        if (config.Movement.HasFlag(MovementOptions.Mount) || config.Movement.HasFlag(MovementOptions.Fly))
            await Mount();

        if (config.Pathing == PathingStrategy.Direct)
            await MoveToDirectly(dest, tolerance);
        else {
            await NavmeshReady();
            // Both flags. PathfindInProgress is vnav's Nav gate, but the call below goes through
            // SimpleMove, which rejects on its OWN in-flight task (PathfindingInProgress) — waiting
            // only on Nav let a live SimpleMove task through, and the rejection killed the whole run.
            await WaitWhile(() => Svc.Navmesh.PathfindInProgress || Svc.Navmesh.PathfindingInProgress, "WaitingForInProgressCalls");

            // Assert the movement gate. It survives whoever turned it off, so a single earlier
            // disable — vnav's own toggle, another plugin steering movement — freezes every
            // subsequent path here while vnav still reports it computed one.
            if (!Svc.Navmesh.GetMovementAllowed()) {
                Warning("vnav movement was disabled; re-enabling");
                Svc.Navmesh.SetMovementAllowed(true);
            }

            // Mounted matters as much as CanFly: Mount() above returns silently when the zone or the
            // character can't mount, and asking vnav for a fly path on foot leaves its follower with
            // nothing it can execute.
            var fly = Player.InFlight || (config.Movement.HasFlag(MovementOptions.Fly) && Control.CanFly && Player.Mounted);

            // Ground-only areas (currently the U'Ghamaro Mines in Outer La Noscea): the volume mesh
            // there is unpolished, so this leg is forced onto the ground mesh — landing first when
            // the restriction catches us mid-air, because a ground path can't start in the sky.
            if (fly && !FlightRestrictions.AllowFlight(IClientState.Get().TerritoryType, dest)) {
                Log($"Destination or player is in a ground-only area; pathing to {dest} on foot");
                if (Player.InFlight) {
                    // allowReposition: false — a repositioning Dismount re-enters MoveTo, which in a
                    // ground-only area re-enters Dismount, recursing without bound.
                    await Dismount(allowReposition: false);
                    ErrorIf(Player.InFlight, $"Could not land near {Player?.Position} to start a ground-only path to {dest}");
                    if (config.Movement.HasFlag(MovementOptions.Mount))
                        await Mount();
                }
                fly = false;
            }

            // Pathfind and hand the waypoints over ourselves instead of going through SimpleMove.
            // SimpleMove owns a single _pendingTask: it rejects overlapping requests outright, and on
            // completion reads .Result inside a try/catch, swallowing a faulted or cancelled task as
            // one log line — leaving the follower with an empty waypoint list while every state gate
            // we can query says the path succeeded. That is exactly the observed failure: vnav logs
            // "Pathfinding done: N waypoints", then NumWaypoints stays 0 for every following tick.
            //
            // Polled, not awaited: the task completes on a worker thread, and awaiting it would resume
            // the rest of this method — all of which touches game state — off the framework thread.
            async Task<List<Vector3>?> TryPathfind(bool flying) {
                var pathTask = Svc.Navmesh.PathfindWithTolerance(Player!.Position, dest, flying, config.Tolerance ?? 3f);
                ErrorIf(pathTask is null, "vnav pathfind gate unavailable");

                Status = $"Pathfinding to {dest}";
                var deadline = Environment.TickCount64 + PathfindTimeoutMs;
                await WaitUntil(() => pathTask!.IsCompleted || Environment.TickCount64 >= deadline, "Pathfind");
                // A hung pathfinder is vnav itself broken — retrying would just hang another minute.
                ErrorIf(!pathTask!.IsCompleted, $"Pathfind to {dest} still not finished after {PathfindTimeoutMs / 1000}s — vnav pathfinder looks hung");

                if (pathTask.IsFaulted || pathTask.IsCanceled) {
                    Warning($"Pathfind ({(flying ? "fly" : "walk")}) to {dest} failed: {pathTask.Exception?.GetBaseException().Message ?? "cancelled — navmesh reloaded mid-pathfind"}");
                    return null;
                }

                return pathTask.Result is { Count: > 0 } found ? found : null;
            }

            var waypoints = await TryPathfind(fly);

            // Fly paths fail where the volume mesh is missing or ragged; the ground mesh usually
            // still has a route. Land and retry on foot before declaring the trip impossible.
            if (waypoints is null && fly) {
                Warning($"No fly path to {dest}; retrying on the ground");
                if (Player.InFlight) {
                    // allowReposition: false for the same recursion reason as the forced-ground branch
                    await Dismount(allowReposition: false);
                    // a ground pathfind from mid-air faults inside vnav (start polygon search is ~5y
                    // tall), which would read as "no waypoints" — name the real problem instead
                    ErrorIf(Player.InFlight, $"No fly path to {dest} and could not land at {Player?.Position} to walk instead");
                }
                fly = false;
                waypoints = await TryPathfind(fly);
            }

            ErrorIf(waypoints is not { Count: > 0 }, $"Pathfind to {dest} produced no waypoints");

            Status = $"Moving to {dest}";
            using var stop = new OnDispose(Svc.Navmesh.Stop);

            // Hand off and verify the follower kept the path. vnav's "Cancel current path on player
            // movement input" option calls Stop() the moment any movement input reads non-zero —
            // "player (or some other plugin) pressing keys", per its source — which empties the list
            // one frame after a successful handoff. We still hold the waypoints, so retry a few times
            // for input blips; if it's constant (stick drift, another movement plugin), fail loudly
            // with the actual fix instead of silently re-pathing forever.
            var handedOff = false;
            for (var attempt = 1; attempt <= HandoffAttempts && !handedOff; attempt++) {
                ErrorIf(!Svc.Navmesh.MoveTo(waypoints, fly), "vnav path follower gate unavailable");
                await NextFrame(3);
                handedOff = Svc.Navmesh.NumWaypoints() > 0 || Player.WithinRange(dest, tolerance);
                if (!handedOff)
                    Warning($"vnav dropped the path within a frame of handoff ({attempt}/{HandoffAttempts})");
            }
            ErrorIf(!handedOff,
                "vnavmesh immediately discards every path it is given. Open /vnav and untick "
                + "\"Cancel current path on player movement input\" — a drifting controller stick or another "
                + "movement plugin (e.g. BossMod AI, /bmrai off) triggers it every frame.");

            // vnav reports neither pathfinding nor running for a few frames between finishing the path
            // and the follower picking it up. The single tick above only covers the gap BEFORE the
            // pathfind starts; this later gap made the very first idle check read as "arrived", so
            // navigation ended at the starting point and the caller carried on as if it had travelled.
            var idleTicks = 0;
            var peakWaypoints = 0;
            bool NavigationStopped() {
                // Path.IsRunning is just Waypoints.Count > 0, so peak tells apart "vnav never handed
                // the path to its follower" from "something emptied the list after it did" — the
                // difference between a broken request and another consumer calling Path.Stop.
                peakWaypoints = Math.Max(peakWaypoints, Svc.Navmesh.NumWaypoints());
                if (Svc.Navmesh.PathfindingInProgress || Svc.Navmesh.IsRunning()) {
                    idleTicks = 0;
                    return false;
                }
                return ++idleTicks >= NavigationIdleTicks;
            }

            if (stopCondition is null) {
                await WaitWhile(() => !Player.WithinRange(dest, tolerance) && !NavigationStopped(), "Navigate");
            }
            else {
                await WaitWhile(() => !Player.WithinRange(dest, tolerance) && !stopCondition() && !NavigationStopped(), "Navigate");
                if (stopCondition() && onStopReached is not null) {
                    Svc.Navmesh.Stop(); // must be stopped because onStopReached's MoveTo (if present) calls !PathfindingInProgress
                    await onStopReached();
                }
            }

            // Ending here without arriving means vnav gave up (or never started) rather than that we
            // got there — silent otherwise, and the caller would act as though the trip succeeded.
            WarningIf(!Player.WithinRange(dest, tolerance) && (stopCondition is null || !stopCondition()),
                $"Navigation ended {Vector3.Distance(Player?.Position ?? dest, dest):F1}y from {dest} without arriving "
                + $"(fly={fly}, mounted={Player.Mounted}, inFlight={Player.InFlight}, canFly={Control.CanFly}, "
                + $"canMount={Player.CanMount}, running={Svc.Navmesh.IsRunning()}, "
                + $"waypoints={Svc.Navmesh.NumWaypoints()}, peakWaypoints={peakWaypoints}, "
                + $"movementAllowed={Svc.Navmesh.GetMovementAllowed()})");
        }

        if (config.Movement.HasFlag(MovementOptions.Dismount) && Player.WithinRange(dest, tolerance)) // only dismount if we're close
            await Dismount();
    }

    protected async Task MoveToDirectly(Vector3 dest, Func<bool> stopCondition) {
        using var scope = BeginScope("MoveDirectly");
        if (stopCondition())
            return;

        Status = $"Moving to {dest}";
        movement.DesiredPosition = dest;
        movement.Enabled = true;
        using var stop = new OnDispose(() => movement.Enabled = false);
        await WaitUntil(stopCondition, "WaitForCondition");
    }

    protected async Task MoveToDirectly(Vector3 dest, float tolerance) {
        using var scope = BeginScope("MoveDirectlyWithTolerance");
        await MoveToDirectly(dest, () => Player.WithinRange(dest, tolerance));
    }

    protected async Task TeleportTo(uint territoryId, FlagMapMarker flag, bool allowSameZoneTeleport = false)
        => await TeleportTo(territoryId, new Vector3(flag.XFloat, 0, flag.YFloat), allowSameZoneTeleport);

    protected async Task TeleportTo(uint territoryId, Vector3 destination, bool allowSameZoneTeleport = false) {
        using var scope = BeginScope("Teleport");
        if (!allowSameZoneTeleport && IClientState.Get().TerritoryType == territoryId)
            return; // already in correct zone

        // must wait for ui or else a world travel (that fades ui) will conflict because teleport is called before it fades back in
        await WaitWhile(() => Player.IsUiFading, "WaitForUiUnfade");

        var closestAetheryteId = Coords.FindClosestAetheryte(territoryId, destination, includeAethernet: true) ?? 0;
        var teleportAetheryteId = Coords.FindPrimaryAetheryte(closestAetheryteId);
        ErrorIf(teleportAetheryteId == 0, $"Failed to find aetheryte in [{territoryId}] {Sheets.TerritoryType.GetRowRef(territoryId).Value.PlaceName.Value.Name}");
        if (Sheets.Aetheryte.GetRowRef(teleportAetheryteId) is { Value.Territory.RowId: var destinationId, Value.PlaceName.Value.Name: var destinationName } &&
            (IClientState.Get().TerritoryType != destinationId || allowSameZoneTeleport)) {
            Status = $"Teleporting to {destinationName}";

            while (true) { // infinite loops are my passion
                // Teleport is combat-locked, and Mount() only waits combat out for its own short
                // grace — a caller can still reach here mid-fight (e.g. a leftover mob aggroed after
                // a fate). This wait terminates because integrations stay engaged on whatever is
                // still attacking.
                if (ICondition.Get()[ConditionFlag.InCombat]) {
                    Status = "Waiting for combat to end before teleporting";
                    await WaitWhile(() => ICondition.Get()[ConditionFlag.InCombat], "WaitCombatEnd");
                }

                var sawCast = false;
                var sawUiFade = false;
                // Announce before the request, not after: the offer reaches the other clients as soon
                // as the server accepts this, and they have to already know it was ours.
                Teleporting?.Invoke(teleportAetheryteId);
                ErrorIf(!ActionManager.Teleport(teleportAetheryteId), $"Failed to teleport to {teleportAetheryteId}");

                while (true) {
                    var isUiFading = Player.IsUiFading;
                    var isCasting = Player?.IsCasting ?? false;

                    if (isCasting)
                        sawCast = true;
                    if (isUiFading)
                        sawUiFade = true;

                    if (sawUiFade && !isUiFading) {
                        await WaitUntil(() => GameMain.IsTerritoryLoaded && Player.Interactable, "WaitTransportFinish");
                        break;
                    }

                    // cast ended, ui didn't fade, and cast didn't complete
                    // id resets after cast but elapsed doesn't until a new cast occurs. I'm assuming that it cannot be 5 and the teleport still gets cancelled
                    if (sawCast && !isCasting && !sawUiFade && ActionManager.GetCastAction() is not { Elapsed: 5 })
                        break;

                    await NextFrame();
                }

                if (sawUiFade)
                    break;
            }
        }

        // still gotta use aethernet if target has a layover
        if (IClientState.Get().TerritoryType != territoryId)
            await UseAethernet(territoryId, destination);

        ErrorIf(IClientState.Get().TerritoryType != territoryId, $"Failed to reach territory (exp: {territoryId}, act: {IClientState.Get().TerritoryType})");
    }

    protected async Task UseAethernet(uint territoryId, Vector3 destination) {
        using var scope = BeginScope("UseAethernet");
        if (territoryId == 886) {
            // firmament special case
            Status = $"Interacting with aetheryte to get to the Firmament";
            var (firmamentObjId, firmamentObjPos) = Coords.FindAetheryte(70);
            if (firmamentObjId is 0)
                return;
            if (!Player.WithinRange(firmamentObjPos, InteractRange.Aetheryte.MaxDistance))
                await MoveTo(firmamentObjPos, MovementConfig.Default.WithTolerance(InteractRange.Aetheryte), allowTeleportIfFaster: false, allowAethernet: false);
            if (Player.Mounted)
                await Dismount();
            ErrorIf(!TargetSystem.InteractWith(firmamentObjId), "Failed to interact with aetheryte");
            await WaitUntilSkipping(() => AtkUnitBase.IsAddonReady("SelectString"), "WaitSelectFirmament", UiSkipOptions.Talk);
            PacketDispatcher.TeleportToFirmament(70);
            await WaitUntilTerritory(territoryId);
            return;
        }

        var sourceAetheryteId = Coords.FindClosestAetheryte(IClientState.Get().TerritoryType, Player!.Position, includeAethernet: true) ?? 0;
        var destinationAetheryteId = Coords.FindClosestAetheryte(territoryId, destination, includeAethernet: true) ?? 0;
        if (sourceAetheryteId == 0 || destinationAetheryteId == 0 || sourceAetheryteId == destinationAetheryteId)
            return;

        var sourcePrimary = Coords.FindPrimaryAetheryte(sourceAetheryteId);
        var destinationPrimary = Coords.FindPrimaryAetheryte(destinationAetheryteId);
        if (sourcePrimary == 0 || sourcePrimary != destinationPrimary)
            return;

        var (aetheryteId, aetherytePos) = Coords.FindAetheryte(sourceAetheryteId) is var sourceObj && sourceObj.id != 0 ? sourceObj : Coords.FindAetheryte(sourcePrimary);
        if (aetheryteId == 0)
            return;

        Status = $"Interacting with aethernet to get to [{territoryId}]";
        if (!Player.WithinRange(aetherytePos, InteractRange.Aetheryte.MaxDistance))
            await MoveTo(aetherytePos, MovementConfig.Default.WithTolerance(InteractRange.Aetheryte), allowTeleportIfFaster: false, allowAethernet: false);
        if (Player.Mounted)
            await Dismount();
        ErrorIf(!TargetSystem.InteractWith(aetheryteId), "Failed to interact with aetheryte");

        if (Sheets.Aetheryte.GetRow(sourceAetheryteId).IsAetheryte)
            await WaitUntilSkipping(() => AtkUnitBase.IsAddonReady("SelectString"), "WaitSelectAethernet", UiSkipOptions.Talk);
        else
            await WaitUntil(() => AtkUnitBase.IsAddonReady("TelepotTown"), "WaitForAddon");
        PacketDispatcher.TeleportToAethernet(sourceAetheryteId, destinationAetheryteId);
        await WaitUntilThenFalse(() => ICondition.Get()[Dalamud.Game.ClientState.Conditions.ConditionFlag.BetweenAreas], "TeleportStart");

        if (IClientState.Get().TerritoryType != territoryId)
            await WaitUntil(() => IClientState.Get().TerritoryType == territoryId && GameMain.IsTerritoryLoaded && Player.Interactable, "TeleportFinish");
        else
            await WaitUntil(() => GameMain.IsTerritoryLoaded && Player.Interactable, "TeleportFinishSameTerritory");
    }

    private const long MountTimeoutMs = 10_000;
    private const long DismountTimeoutMs = 12_000;
    private const long MountCombatGraceMs = 5_000;
    private const long FlightSettleMs = 2_000;

    protected async Task Mount() {
        using var scope = BeginScope(nameof(Mount));
        if (!Player.CanMount) return; // early return if not in mounting territories

        Status = "Mounting";
        // Bounded: mounting is a nicety, not a requirement — an unmountable state (combat pull,
        // status, event) used to spin this loop forever and hang the whole task.
        var deadline = Environment.TickCount64 + MountTimeoutMs;
        // A finished fate holds the combat flag up for a second or two after its last mob dies, and
        // mounting is refused for exactly that long. Abandoning the mount on the flag alone walked
        // the entire next leg — and grounded it too, because MoveTo only asks vnav for a fly path if
        // we come back mounted. Wait the flag out, but only briefly: still in combat after the grace
        // means a real fight, and travel shouldn't stall on it.
        var combatDeadline = Environment.TickCount64 + MountCombatGraceMs;
        while (!Player.Mounted) {
            if (ICondition.Get()[ConditionFlag.InCombat]) {
                if (Environment.TickCount64 >= combatDeadline) {
                    Log($"Still in combat after {MountCombatGraceMs / 1000}s; continuing without a mount");
                    return;
                }
                Status = "Waiting for combat to drop before mounting";
                deadline = Environment.TickCount64 + MountTimeoutMs; // the mount's own budget starts once combat clears
                await NextFrame();
                continue;
            }
            if (Environment.TickCount64 >= deadline) {
                Warning($"Could not mount within {MountTimeoutMs / 1000}s; continuing on foot");
                return;
            }
            if (!Player.IsBusy && !ActionManager.IsActionInUse(ActionType.GeneralAction, 24))
                ActionManager.UseAction(ActionType.GeneralAction, 24);
            await NextFrame();
        }

        await SettleFlightAvailability();
    }

    /// <summary>
    /// The Mounted condition flips before the mount actor is live, and flight reports as unavailable
    /// in that gap — <see cref="Control.CanFly"/> is a status enum underneath, and it answers
    /// NotMounted/PlayerOrMountNull for those few frames. MoveTo reads it the moment
    /// <see cref="Mount"/> returns and commits the whole leg to a ground path on a false read, so let
    /// the answer resolve first. Only the unresolved states wait: a zone that genuinely cannot be
    /// flown answers MountedButCannotFly straight away and must not cost a stall on every leg.
    /// </summary>
    private async Task SettleFlightAvailability() {
        var deadline = Environment.TickCount64 + FlightSettleMs;
        while (Control.GetFlightAllowedStatus() is Control.FlightAllowedStatus.NotMounted or Control.FlightAllowedStatus.PlayerOrMountNull) {
            if (Environment.TickCount64 >= deadline) {
                Log($"Flight availability still unresolved {FlightSettleMs / 1000}s after mounting; pathing on whatever it reports");
                return;
            }
            await NextFrame();
        }
    }

    protected Task Dismount() => Dismount(allowReposition: true);

    /// <param name="allowReposition">False when called from inside <see cref="MoveTo"/> itself
    /// (forced-ground and fly-retry branches): the repositioning legs below re-enter MoveTo, and in a
    /// ground-only area MoveTo re-enters Dismount — an unbounded mutual recursion. Without
    /// repositioning this only descends and dismounts in place, which cannot recurse.</param>
    private async Task Dismount(bool allowReposition) {
        using var scope = BeginScope("Dismount");
        if (Player is null || !Player.Mounted) return;

        if (Player.InFlight && allowReposition) {
            if (Svc.Navmesh.NearestPointReachable(Player.Position) is { } nearestPoint)
                await MoveTo(nearestPoint, MovementConfig.Everything);
            else
                Warning($"No nearest landable point found from {Player.Position}. Dismounting may fail");
        }

        Status = "Dismounting";
        var deadline = Environment.TickCount64 + DismountTimeoutMs;
        var movedToLandable = false;
        while (Player.Mounted) {
            // Unable to land here — descending over unlandable terrain (water, props, mesh seams)
            // repeats forever without this. Hop to a spot the mesh itself calls landable and retry
            // once; if even that fails, fail open and stay mounted rather than hang the task.
            if (Environment.TickCount64 >= deadline) {
                if (allowReposition && !movedToLandable && Svc.Navmesh.NearestPointReachable(Player.Position, 30, 50) is { } landable) {
                    movedToLandable = true;
                    Warning($"Unable to land at {Player.Position}; retrying from {landable}");
                    // Mount|Fly without Dismount: the retry below owns the dismount, no recursion.
                    await MoveTo(landable, new MovementConfig(2, MovementOptions.Mount | MovementOptions.Fly, PathingStrategy.Auto));
                    deadline = Environment.TickCount64 + DismountTimeoutMs;
                    continue;
                }
                Warning($"Unable to dismount within {DismountTimeoutMs / 1000}s; continuing mounted");
                return;
            }

            if (Player.InFlight && !Player.IsAirDismountable) {
                Log($"Descending");
                ActionManager.UseAction(ActionType.GeneralAction, 23); // TODO: find a force ground function
            }
            else if (Player.InFlight && Player.IsAirDismountable) {
                Log($"Air Dismount");
                GameMain.ExecuteLocationCommand(LocationCommandFlag.Dismount, Player.Position, (int)Player.PackedRotation);
            }
            else if (Player.Mounted && !Player.InFlight) {
                Log($"Ground Dismount");
                GameMain.ExecuteCommand(CommandFlag.Dismount, 1);
            }
            await NextFrame();
        }
    }

    protected async Task WaitUntilSkipping(Func<bool> condition, string scopeName, UiSkipOptions skip, int? selectStringIndex = null) {
        using var scope = BeginScope(scopeName);
        while (!condition()) {
            if (selectStringIndex is { } index && AtkUnitBase.IsAddonReady("SelectString")) {
                Log("selecting string...");
                AddonSelectString.Select(index);
            }
            if (skip.HasFlag(UiSkipOptions.Talk) && AtkUnitBase.IsAddonReady("Talk")) {
                Log("progressing talk...");
                AddonTalk.Progress();
            }
            if (skip.HasFlag(UiSkipOptions.YesNo) && AtkUnitBase.IsAddonReady("SelectYesno")) {
                Log("progressing yes/no...");
                AddonSelectYesno.Yes();
            }
            if (skip.HasFlag(UiSkipOptions.Request) && AtkUnitBase.IsAddonReady("Request")) {
                Log("progressing request...");
                AgentNpcTrade.TurnInRequests();
            }
            Log("waiting...");
            await NextFrame();
        }
    }

    protected async Task WaitUntilTerritory(uint territoryId) {
        using var scope = BeginScope("WaitUntilTerritory");
        await WaitUntil(() => IClientState.Get().TerritoryType == territoryId && GameMain.IsTerritoryLoaded && Player.Interactable, "WaitingForTerritory");
    }

    protected async Task WaitWhileBusy() {
        using var scope = BeginScope("WaitWhileBusy");
        await WaitWhile(() => Player.IsBusy, "WaitingForNotBusy");
    }

    protected async Task InteractWith(IGameObject obj, Func<bool>? waitUntil = null, int? selectStringIndex = null, UiSkipOptions skip = UiSkipOptions.None) {
        using var scope = BeginScope("InteractWith");

        ErrorIf(waitUntil is null && (selectStringIndex != null || skip != UiSkipOptions.None), "Skip arguments provided but no wait condition");

        if (!obj.IsInInteractRange()) {
            Log("Not in interact range, moving closer");
            await MoveToDirectly(obj.Position, obj.IsInInteractRange);
        }

        Status = $"Interacting with {obj.GameObjectId}";
        await WaitWhile(() => Player.IsJumping, "WaitForAbleToInteract");
        const int maxAttempts = 5;
        for (var attempt = 0; attempt < maxAttempts; attempt++) {
            if (TargetSystem.InteractWith(obj.GameObjectId)) {
                if (waitUntil is { } condition) {
                    await WaitUntilSkipping(condition, "WaitingForNpcInteractionToFinish", skip, selectStringIndex);
                    return;
                }
                return;
            }
            await NextFrame();
        }
        ErrorIf(true, $"Failed to interact with object after {maxAttempts} tries");
    }
}
