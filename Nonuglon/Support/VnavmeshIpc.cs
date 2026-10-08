using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Plugin.Ipc;
using ECommons.DalamudServices;

namespace Nonuglon.Support;

/// <summary>Thin wrapper over vnavmesh's IPC (names/signatures from its
/// IPCProvider.cs). Every call is guarded: if vnavmesh is missing, mid-reload
/// or its IPC changed shape, callers get null/false instead of an exception.</summary>
public static class VnavmeshIpc
{
    public static bool IsLoaded => PluginDetection.IsPluginLoaded(PluginDetection.VnavmeshInternalName);

    // Created once instead of per call: this is hit several times a frame during a run.
    private static readonly ICallGateSubscriber<bool> isReady = Sub<bool>("vnavmesh.Nav.IsReady");
    private static readonly ICallGateSubscriber<bool> isPathRunning = Sub<bool>("vnavmesh.Path.IsRunning");
    private static readonly ICallGateSubscriber<bool> pathfindInProgress = Sub<bool>("vnavmesh.SimpleMove.PathfindInProgress");
    private static readonly ICallGateSubscriber<Vector3, bool, bool> pathfindAndMoveTo = Svc.PluginInterface.GetIpcSubscriber<Vector3, bool, bool>("vnavmesh.SimpleMove.PathfindAndMoveTo");
    private static readonly ICallGateSubscriber<Vector3?> flagToPoint = Sub<Vector3?>("vnavmesh.Query.Mesh.FlagToPoint");
    private static readonly ICallGateSubscriber<Vector3, bool, float, Vector3?> pointOnFloor = Svc.PluginInterface.GetIpcSubscriber<Vector3, bool, float, Vector3?>("vnavmesh.Query.Mesh.PointOnFloor");
    private static readonly ICallGateSubscriber<Vector3, float, float, Vector3?> nearestPointReachable = Svc.PluginInterface.GetIpcSubscriber<Vector3, float, float, Vector3?>("vnavmesh.Query.Mesh.NearestPointReachable");
    private static readonly ICallGateSubscriber<Vector3, float, bool, bool> isPointOnMesh = Svc.PluginInterface.GetIpcSubscriber<Vector3, float, bool, bool>("vnavmesh.Query.Mesh.IsPointOnMesh");
    private static readonly ICallGateSubscriber<List<Vector3>> listWaypoints = Sub<List<Vector3>>("vnavmesh.Path.ListWaypoints");
    private static readonly ICallGateSubscriber<object> stop = Sub<object>("vnavmesh.Path.Stop");
    private static readonly ICallGateSubscriber<Vector3, Vector3, bool, CancellationToken, Task<List<Vector3>>> pathfindCancelable =
        Svc.PluginInterface.GetIpcSubscriber<Vector3, Vector3, bool, CancellationToken, Task<List<Vector3>>>("vnavmesh.Nav.PathfindCancelable");
    private static readonly ICallGateSubscriber<List<Vector3>, bool, object> moveTo = Svc.PluginInterface.GetIpcSubscriber<List<Vector3>, bool, object>("vnavmesh.Path.MoveTo");

    private static ICallGateSubscriber<T> Sub<T>(string name) => Svc.PluginInterface.GetIpcSubscriber<T>(name);

    public static bool IsReady() => Guard("Nav.IsReady", () => isReady.InvokeFunc());
    public static bool IsPathRunning() => Guard("Path.IsRunning", () => isPathRunning.InvokeFunc());
    public static bool IsPathfindInProgress() => Guard("SimpleMove.PathfindInProgress", () => pathfindInProgress.InvokeFunc());

    public static bool PathfindAndMoveTo(Vector3 dest, bool fly) => Guard("SimpleMove.PathfindAndMoveTo", () => pathfindAndMoveTo.InvokeFunc(dest, fly));

    /// <summary>vnavmesh's own flag pick: the highest floor within 5y of the flag.</summary>
    public static Vector3? FlagToPoint() => Guard("Query.Mesh.FlagToPoint", () => flagToPoint.InvokeFunc());

    /// <summary>Highest floor below p.Y within halfExtentXZ of p.</summary>
    public static Vector3? PointOnFloor(Vector3 p, bool allowUnlandable, float halfExtentXZ) =>
        Guard("Query.Mesh.PointOnFloor", () => pointOnFloor.InvokeFunc(p, allowUnlandable, halfExtentXZ));

    /// <summary>Nearest reachable mesh point to p (3D distance) inside the given box.</summary>
    public static Vector3? NearestPointReachable(Vector3 p, float halfExtentXZ, float halfExtentY) =>
        Guard("Query.Mesh.NearestPointReachable", () => nearestPointReachable.InvokeFunc(p, halfExtentXZ, halfExtentY));

    /// <summary>True if a mesh polygon lies within halfExtentY under/over p;
    /// allowUnreachable false limits it to vnavmesh's flood-filled reachable set.</summary>
    public static bool IsPointOnMesh(Vector3 p, float halfExtentY, bool allowUnreachable) =>
        Guard("Query.Mesh.IsPointOnMesh", () => isPointOnMesh.InvokeFunc(p, halfExtentY, allowUnreachable));

    public static List<Vector3>? ListWaypoints() => Guard("Path.ListWaypoints", () => listWaypoints.InvokeFunc());

    /// <summary>Computes a path without moving (unlike SimpleMove), cancelable
    /// with our own token, so it doesn't count as SimpleMove.PathfindInProgress.</summary>
    public static Task<List<Vector3>>? Pathfind(Vector3 from, Vector3 to, bool fly, CancellationToken cancel) =>
        Guard("Nav.PathfindCancelable", () => pathfindCancelable.InvokeFunc(from, to, fly, cancel));

    /// <summary>Follows the given waypoints, replacing any current path.</summary>
    public static bool MoveTo(List<Vector3> waypoints, bool fly) =>
        Guard("Path.MoveTo", () => { moveTo.InvokeAction(waypoints, fly); return true; });

    public static void Stop()
    {
        try { stop.InvokeAction(); }
        catch (Exception ex) { Svc.Log.Verbose($"[vnavmesh IPC] Path.Stop failed: {ex.Message}"); }
    }

    private static T? Guard<T>(string name, Func<T> call)
    {
        if (!IsLoaded) return default;
        try { return call(); }
        catch (Exception ex)
        {
            Svc.Log.Verbose($"[vnavmesh IPC] {name} failed: {ex.Message}");
            return default;
        }
    }
}
