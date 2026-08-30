using System;
using System.Numerics;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;

namespace BozjaBuddyReborn.External;

/// <summary>
/// Which of this plugin's subsystems last successfully issued a path.
///
/// vnavmesh has exactly ONE AsyncMoveRequest and ONE FollowPath for the whole process, and its
/// PathfindInProgress / Path.IsRunning gates carry no request identity - so "is a path running"
/// can never answer "is MY path running". Both of our movement sources asked it the latter
/// question and got the former's answer, which is how travel's leftover path convinced the
/// combat approach it was already closing on the target while the character ran the other way.
/// </summary>
public enum NavClient : byte
{
    None = 0,

    /// <summary>Long-distance travel to an objective (Automation/Movement).</summary>
    Travel = 1,

    /// <summary>Closing the last few yalms onto a target (Automation/CombatApproach).</summary>
    Approach = 2,
}

/// <summary>
/// vnavmesh (awgil/ffxiv_navmesh) movement + pathfinding.
///
/// Every gate string below is transcribed from vnavmesh's own IPCProvider.cs; the last
/// generic parameter is the return type, and void gates are registered as
/// <c>...,object</c> and invoked with InvokeAction.
///
/// Discipline (all enforced here): resolve subscribers once, treat "vnavmesh not
/// installed" as a normal state, gate every path request on Nav.IsReady, and never let an
/// IPC exception escape - a missing dependency must degrade the feature, not crash the tick.
/// </summary>
public sealed class NavmeshIpc
{
    private readonly ICallGateSubscriber<bool>? _isReady;
    private readonly ICallGateSubscriber<float>? _buildProgress;
    private readonly ICallGateSubscriber<Vector3, bool, bool>? _pathfindAndMoveTo;
    private readonly ICallGateSubscriber<Vector3, bool, float, bool>? _pathfindAndMoveCloseTo;
    private readonly ICallGateSubscriber<bool>? _pathfindInProgress;
    private readonly ICallGateSubscriber<object>? _stop;
    private readonly ICallGateSubscriber<bool>? _pathIsRunning;
    private readonly ICallGateSubscriber<Vector3, float, float, Vector3?>? _nearestPointReachable;
    private readonly ICallGateSubscriber<Vector3, bool, float, Vector3?>? _pointOnFloor;
    private readonly ICallGateSubscriber<Vector3, float, bool, bool>? _isPointOnMesh;
    private readonly ICallGateSubscriber<float, object>? _setTolerance;

    public NavmeshIpc(IDalamudPluginInterface pi)
    {
        _isReady = Bind<ICallGateSubscriber<bool>>(() => pi.GetIpcSubscriber<bool>("vnavmesh.Nav.IsReady"));
        _buildProgress = Bind<ICallGateSubscriber<float>>(() => pi.GetIpcSubscriber<float>("vnavmesh.Nav.BuildProgress"));
        _pathfindAndMoveTo = Bind<ICallGateSubscriber<Vector3, bool, bool>>(
            () => pi.GetIpcSubscriber<Vector3, bool, bool>("vnavmesh.SimpleMove.PathfindAndMoveTo"));
        _pathfindAndMoveCloseTo = Bind<ICallGateSubscriber<Vector3, bool, float, bool>>(
            () => pi.GetIpcSubscriber<Vector3, bool, float, bool>("vnavmesh.SimpleMove.PathfindAndMoveCloseTo"));
        _pathfindInProgress = Bind<ICallGateSubscriber<bool>>(
            () => pi.GetIpcSubscriber<bool>("vnavmesh.SimpleMove.PathfindInProgress"));
        _stop = Bind<ICallGateSubscriber<object>>(() => pi.GetIpcSubscriber<object>("vnavmesh.Path.Stop"));
        _pathIsRunning = Bind<ICallGateSubscriber<bool>>(() => pi.GetIpcSubscriber<bool>("vnavmesh.Path.IsRunning"));
        _nearestPointReachable = Bind<ICallGateSubscriber<Vector3, float, float, Vector3?>>(
            () => pi.GetIpcSubscriber<Vector3, float, float, Vector3?>("vnavmesh.Query.Mesh.NearestPointReachable"));
        _pointOnFloor = Bind<ICallGateSubscriber<Vector3, bool, float, Vector3?>>(
            () => pi.GetIpcSubscriber<Vector3, bool, float, Vector3?>("vnavmesh.Query.Mesh.PointOnFloor"));
        _isPointOnMesh = Bind<ICallGateSubscriber<Vector3, float, bool, bool>>(
            () => pi.GetIpcSubscriber<Vector3, float, bool, bool>("vnavmesh.Query.Mesh.IsPointOnMesh"));
        _setTolerance = Bind<ICallGateSubscriber<float, object>>(
            () => pi.GetIpcSubscriber<float, object>("vnavmesh.Path.SetTolerance"));
    }

    private static T? Bind<T>(Func<T> resolve) where T : class
    {
        try { return resolve(); }
        catch { return null; }
    }

    /// <summary>True when vnavmesh is loaded and exposing its gates.</summary>
    public bool Available
    {
        get { try { return _isReady?.HasFunction ?? false; } catch { return false; } }
    }

    /// <summary>True when the navmesh for the current zone has finished building.</summary>
    public bool MeshReady
    {
        get { try { return _isReady?.InvokeFunc() ?? false; } catch { return false; } }
    }

    /// <summary>Mesh build progress 0..1, or -1 when not building.</summary>
    public float BuildProgress
    {
        get { try { return _buildProgress?.InvokeFunc() ?? -1f; } catch { return -1f; } }
    }

    /// <summary>True while the character is following a path.</summary>
    public bool PathRunning
    {
        get { try { return _pathIsRunning?.InvokeFunc() ?? false; } catch { return false; } }
    }

    /// <summary>True while a pathfind computation is still running (movement has not begun).</summary>
    public bool PathfindInProgress
    {
        get { try { return _pathfindInProgress?.InvokeFunc() ?? false; } catch { return false; } }
    }

    /// <summary>Either computing a path or walking one - by ANYONE, including other plugins.</summary>
    public bool Busy => PathfindInProgress || PathRunning;

    private NavClient _owner = NavClient.None;

    /// <summary>
    /// Which of our subsystems owns the path vnavmesh is currently running, or None when
    /// nothing of ours is. Recorded here because it is the single point every request of ours
    /// passes through, and because vnavmesh discards the information entirely.
    /// </summary>
    public NavClient Owner => Busy ? _owner : NavClient.None;

    /// <summary>True when the running path is one this subsystem issued.</summary>
    public bool OwnedBy(NavClient client) => Owner == client;

    /// <summary>
    /// Walk/fly to a destination, stopping within <paramref name="range"/> units of it.
    ///
    /// RETURNS FALSE AND DOES NOTHING when vnavmesh already has a pathfind in flight - its
    /// AsyncMoveRequest.MoveTo refuses outright rather than queueing. Callers must not commit
    /// any "we are now heading there" state unless this returned true.
    /// </summary>
    public bool MoveCloseTo(Vector3 destination, float range, bool fly, NavClient owner)
    {
        try
        {
            var ok = _pathfindAndMoveCloseTo?.InvokeFunc(destination, fly, range) ?? false;
            if (ok)
                _owner = owner;
            return ok;
        }
        catch { return false; }
    }

    /// <summary>Walk/fly to an exact destination. Same refusal semantics as MoveCloseTo.</summary>
    public bool MoveTo(Vector3 destination, bool fly, NavClient owner)
    {
        try
        {
            var ok = _pathfindAndMoveTo?.InvokeFunc(destination, fly) ?? false;
            if (ok)
                _owner = owner;
            return ok;
        }
        catch { return false; }
    }

    /// <summary>
    /// Stop moving and clear the current path.
    ///
    /// NOT a guarantee: this clears vnavmesh's waypoint list but cannot cancel a pathfind that
    /// is still computing, and that pathfind hands its result straight to the follower when it
    /// lands. Callers that need movement to actually stay stopped must re-issue until
    /// <see cref="Busy"/> goes false - see Movement.PumpStop.
    /// </summary>
    public void Stop(NavClient owner = NavClient.None)
    {
        try { _stop?.InvokeAction(); }
        catch { /* vnavmesh absent - nothing to stop */ }

        if (owner == NavClient.None || _owner == owner)
            _owner = NavClient.None;
    }

    /// <summary>Per-waypoint arrival tolerance.</summary>
    public void SetTolerance(float tolerance)
    {
        try { _setTolerance?.InvokeAction(tolerance); }
        catch { /* optional tuning */ }
    }

    /// <summary>
    /// Is this exact point standable, walkable mesh we can actually reach?
    ///
    /// The complement of <see cref="SnapToMesh"/>: snapping always returns SOMETHING, so it can
    /// silently hand back a point on the far side of a wall. This is the question to ask when a
    /// candidate has to be rejected rather than approximated.
    ///
    /// Returns true when the gate is missing - "unknown" must never block movement.
    /// </summary>
    public bool IsPointOnMesh(Vector3 point, float halfExtentY = 5f)
    {
        try { return _isPointOnMesh?.InvokeFunc(point, halfExtentY, false) ?? true; }
        catch { return true; }
    }

    /// <summary>
    /// Snap a raw world position onto a reachable point of the navmesh.
    ///
    /// This matters more in Bozja than almost anywhere else: Zadnor is stacked, multi-level
    /// terrain, and a Critical Engagement's published map-marker centre is frequently a
    /// point in the air above the arena or inside geometry. Pathing straight at it strands
    /// the character. Try the reachable-point query first, then drop to the floor beneath,
    /// and only fall back to the raw point if both fail.
    /// </summary>
    public Vector3 SnapToMesh(Vector3 raw, float halfExtentXZ = 20f, float halfExtentY = 20f)
    {
        try
        {
            var reachable = _nearestPointReachable?.InvokeFunc(raw, halfExtentXZ, halfExtentY);
            if (reachable is { } r)
                return r;
        }
        catch { /* fall through */ }

        try
        {
            var floor = _pointOnFloor?.InvokeFunc(raw, false, halfExtentXZ);
            if (floor is { } f)
                return f;
        }
        catch { /* fall through */ }

        return raw;
    }

    /// <summary>
    /// Resolve a horizontal-only point (from map coordinates, which carry no altitude) onto the
    /// ground.
    ///
    /// Order matters here. A map point has no Y, and seeding Y at 0 is the classic trap: it sits
    /// below the terrain in most zones, so a nearest-point query rejects every floor above sea
    /// level and resolves nothing. Instead the point is seeded high and dropped straight down -
    /// PointOnFloor finds the floor BELOW a point - with a wide reachable-point query as the
    /// fallback for a seed that started inside geometry.
    /// </summary>
    public Vector3 ResolveGroundPoint(float worldX, float worldZ, float seedY = 1024f)
    {
        var seed = new Vector3(worldX, seedY, worldZ);

        try
        {
            var floor = _pointOnFloor?.InvokeFunc(seed, false, 20f);
            if (floor is { } f)
                return f;
        }
        catch { /* fall through */ }

        try
        {
            // Generous vertical extent: we genuinely do not know the altitude here.
            var reachable = _nearestPointReachable?.InvokeFunc(seed, 50f, 500f);
            if (reachable is { } r)
                return r;
        }
        catch { /* fall through */ }

        return seed;
    }
}
