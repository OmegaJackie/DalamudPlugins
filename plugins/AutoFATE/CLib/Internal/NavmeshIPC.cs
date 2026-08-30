using AutoFATE.CLib.Services;
using Dalamud.Plugin.Ipc;
using System.Threading.Tasks;

namespace AutoFATE.CLib.Internal;

internal class NavmeshIPC {
    private readonly ICallGateSubscriber<bool> _navIsReady;
    private readonly ICallGateSubscriber<float> _navBuildProgress;
    private readonly ICallGateSubscriber<bool> _navPathfindInProgress;
    private readonly ICallGateSubscriber<bool> _navReload;
    private readonly ICallGateSubscriber<bool> _navIsAutoLoad;
    private readonly ICallGateSubscriber<Vector3, Vector3, bool, Task<List<Vector3>>?> _pathfind;
    private readonly ICallGateSubscriber<Vector3, Vector3, bool, float, Task<List<Vector3>>?> _pathfindWithTolerance;

    private readonly ICallGateSubscriber<Vector3, float, float, Vector3?> _nearestPoint;
    private readonly ICallGateSubscriber<Vector3, float, float, Vector3?> _nearestPointReachable;
    private readonly ICallGateSubscriber<Vector3, bool, float, Vector3?> _pointOnFloor;
    private readonly ICallGateSubscriber<Vector3?> _flagToPoint;

    private readonly ICallGateSubscriber<object> _pathStop;
    private readonly ICallGateSubscriber<List<Vector3>, bool, object> _pathMoveTo;
    private readonly ICallGateSubscriber<bool> _pathIsRunning;
    private readonly ICallGateSubscriber<int> _pathNumWaypoints;
    private readonly ICallGateSubscriber<bool> _pathGetMovementAllowed;
    private readonly ICallGateSubscriber<bool, object> _pathSetMovementAllowed;
    private readonly ICallGateSubscriber<float> _pathGetTolerance;

    private readonly ICallGateSubscriber<Vector3, bool, bool> _pathfindAndMoveTo;
    private readonly ICallGateSubscriber<Vector3, bool, float, bool> _pathfindAndMoveCloseTo;
    private readonly ICallGateSubscriber<bool> _pathfindInProgress;

    public NavmeshIPC() {
        _navIsReady = Svc.Interface.GetIpcSubscriber<bool>("vnavmesh.Nav.IsReady");
        _navBuildProgress = Svc.Interface.GetIpcSubscriber<float>("vnavmesh.Nav.BuildProgress");
        _navPathfindInProgress = Svc.Interface.GetIpcSubscriber<bool>("vnavmesh.Nav.PathfindInProgress");
        _navReload = Svc.Interface.GetIpcSubscriber<bool>("vnavmesh.Nav.Reload");
        _navIsAutoLoad = Svc.Interface.GetIpcSubscriber<bool>("vnavmesh.Nav.IsAutoLoad");
        _pathfind = Svc.Interface.GetIpcSubscriber<Vector3, Vector3, bool, Task<List<Vector3>>?>("vnavmesh.Nav.Pathfind");
        _pathfindWithTolerance = Svc.Interface.GetIpcSubscriber<Vector3, Vector3, bool, float, Task<List<Vector3>>?>("vnavmesh.Nav.PathfindWithTolerance");

        _nearestPoint = Svc.Interface.GetIpcSubscriber<Vector3, float, float, Vector3?>("vnavmesh.Query.Mesh.NearestPoint");
        _nearestPointReachable = Svc.Interface.GetIpcSubscriber<Vector3, float, float, Vector3?>("vnavmesh.Query.Mesh.NearestPointReachable");
        _pointOnFloor = Svc.Interface.GetIpcSubscriber<Vector3, bool, float, Vector3?>("vnavmesh.Query.Mesh.PointOnFloor");
        _flagToPoint = Svc.Interface.GetIpcSubscriber<Vector3?>("vnavmesh.Query.Mesh.FlagToPoint");

        _pathStop = Svc.Interface.GetIpcSubscriber<object>("vnavmesh.Path.Stop");
        _pathMoveTo = Svc.Interface.GetIpcSubscriber<List<Vector3>, bool, object>("vnavmesh.Path.MoveTo");
        _pathIsRunning = Svc.Interface.GetIpcSubscriber<bool>("vnavmesh.Path.IsRunning");
        _pathNumWaypoints = Svc.Interface.GetIpcSubscriber<int>("vnavmesh.Path.NumWaypoints");
        _pathGetMovementAllowed = Svc.Interface.GetIpcSubscriber<bool>("vnavmesh.Path.GetMovementAllowed");
        _pathSetMovementAllowed = Svc.Interface.GetIpcSubscriber<bool, object>("vnavmesh.Path.SetMovementAllowed");
        _pathGetTolerance = Svc.Interface.GetIpcSubscriber<float>("vnavmesh.Path.GetTolerance");

        _pathfindAndMoveTo = Svc.Interface.GetIpcSubscriber<Vector3, bool, bool>("vnavmesh.SimpleMove.PathfindAndMoveTo");
        _pathfindAndMoveCloseTo = Svc.Interface.GetIpcSubscriber<Vector3, bool, float, bool>("vnavmesh.SimpleMove.PathfindAndMoveCloseTo");
        _pathfindInProgress = Svc.Interface.GetIpcSubscriber<bool>("vnavmesh.SimpleMove.PathfindInProgress");

    }

    public bool IsAvailable => _navIsReady.HasFunction;

    public bool IsReady => _navIsReady.HasFunction && _navIsReady.InvokeFunc();
    public float BuildProgress => _navBuildProgress.HasFunction ? _navBuildProgress.InvokeFunc() : -1f;
    public Task<List<Vector3>>? Pathfind(Vector3 start, Vector3 end, bool fly)
        => _pathfind.HasFunction ? _pathfind.InvokeFunc(start, end, fly) : null;

    public Task<List<Vector3>>? PathfindWithTolerance(Vector3 start, Vector3 end, bool fly, float range)
        => _pathfindWithTolerance.HasFunction ? _pathfindWithTolerance.InvokeFunc(start, end, fly, range) : null;

    public bool PathfindInProgress => _navPathfindInProgress.HasFunction && _navPathfindInProgress.InvokeFunc();

    /// <summary>Force a mesh load. No-op inside vnav unless it already has a zone key, which it only
    /// acquires through its own auto-load path — so this recovers a stale mesh, not an absent one.</summary>
    public bool Reload() => _navReload.HasFunction && _navReload.InvokeFunc();

    /// <summary>vnav's "auto-load navmesh" setting. With it off and nothing loaded, vnav never starts
    /// a build, so every wait for readiness blocks forever.</summary>
    public bool IsAutoLoad => !_navIsAutoLoad.HasFunction || _navIsAutoLoad.InvokeFunc();

    public bool MoveTo(List<Vector3> waypoints, bool fly) {
        if (!_pathMoveTo.HasAction)
            return false;
        _pathMoveTo.InvokeAction(waypoints, fly);
        return true;
    }

    public Vector3? NearestPoint(Vector3 position, float halfExtentXZ = 5, float halfExtentY = 5) => _nearestPoint.HasFunction ? _nearestPoint.InvokeFunc(position, halfExtentXZ, halfExtentY) : null;
    public Vector3? NearestPointReachable(Vector3 position, float halfExtentXZ = 5, float halfExtentY = 5) => _nearestPointReachable.HasFunction ? _nearestPointReachable.InvokeFunc(position, halfExtentXZ, halfExtentY) : null;
    // unlandable isn't (currently) used in any way so it doesn't matter
    public Vector3? PointOnFloor(Vector3 position, bool allowUnlandable = false, float halfExtentXZ = 5) => _pointOnFloor.HasFunction ? _pointOnFloor.InvokeFunc(position, allowUnlandable, halfExtentXZ) : null;
    public Vector3? FlagToPoint() => _flagToPoint.HasFunction ? _flagToPoint.InvokeFunc() : null;

    public void Stop() {
        if (!_pathStop.HasAction)
            return;
        _pathStop.InvokeAction();
    }
    public bool IsRunning() => _pathIsRunning.HasFunction && _pathIsRunning.InvokeFunc();
    public int NumWaypoints() => _pathNumWaypoints.HasFunction ? _pathNumWaypoints.InvokeFunc() : -1;

    /// <summary>
    /// vnav's movement gate. False leaves the path intact but freezes the character, and nothing here
    /// ever set it — so whatever last turned it off (BossMod's movement track, vnav's own toggle)
    /// silently outlives that plugin and every later MoveTo just stands still.
    /// </summary>
    public bool GetMovementAllowed() => !_pathGetMovementAllowed.HasFunction || _pathGetMovementAllowed.InvokeFunc();

    // Registered on vnav's side as an Action (`RegisterAction("Path.SetMovementAllowed", ...)`), so
    // HasFunction is always false here — gating on it made every re-enable a silent no-op, which is
    // why "vnav movement was disabled; re-enabling" could be logged on a loop while nothing moved.
    public void SetMovementAllowed(bool allowed) {
        if (_pathSetMovementAllowed.HasAction)
            _pathSetMovementAllowed.InvokeAction(allowed);
    }
    public float GetTolerance() => _pathGetTolerance.HasFunction ? _pathGetTolerance.InvokeFunc() : 0f;

    public bool PathfindAndMoveTo(Vector3 destination, bool allowFlying = false) => _pathfindAndMoveTo.HasFunction && _pathfindAndMoveTo.InvokeFunc(destination, allowFlying);
    public bool PathfindAndMoveCloseTo(Vector3 destination, bool allowFlying, float range) => _pathfindAndMoveCloseTo.HasFunction && _pathfindAndMoveCloseTo.InvokeFunc(destination, allowFlying, range);
    public bool PathfindingInProgress => _pathfindInProgress.HasFunction && _pathfindInProgress.InvokeFunc();
}
