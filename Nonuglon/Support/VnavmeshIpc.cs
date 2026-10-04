using System;
using System.Collections.Generic;
using System.Numerics;
using ECommons.DalamudServices;

namespace Nonuglon.Support;

/// <summary>Thin wrapper over vnavmesh's IPC (names/signatures from its
/// IPCProvider.cs). Every call is guarded: if vnavmesh is missing, mid-reload
/// or its IPC changed shape, callers get null/false instead of an exception.</summary>
public static class VnavmeshIpc
{
    public static bool IsLoaded => PluginDetection.IsPluginLoaded(PluginDetection.VnavmeshInternalName);

    public static bool IsReady() => Call<bool>("vnavmesh.Nav.IsReady");
    public static bool IsPathRunning() => Call<bool>("vnavmesh.Path.IsRunning");
    public static bool IsPathfindInProgress() => Call<bool>("vnavmesh.SimpleMove.PathfindInProgress");

    public static bool PathfindAndMoveTo(Vector3 dest, bool fly) => Call<Vector3, bool, bool>("vnavmesh.SimpleMove.PathfindAndMoveTo", dest, fly);

    /// <summary>vnavmesh's own flag pick: the highest floor within 5y of the flag.</summary>
    public static Vector3? FlagToPoint() => Call<Vector3?>("vnavmesh.Query.Mesh.FlagToPoint");

    /// <summary>Highest floor below p.Y within halfExtentXZ of p.</summary>
    public static Vector3? PointOnFloor(Vector3 p, bool allowUnlandable, float halfExtentXZ) =>
        Call<Vector3, bool, float, Vector3?>("vnavmesh.Query.Mesh.PointOnFloor", p, allowUnlandable, halfExtentXZ);

    /// <summary>Nearest reachable mesh point to p (3D distance) inside the given box.</summary>
    public static Vector3? NearestPointReachable(Vector3 p, float halfExtentXZ, float halfExtentY) =>
        Call<Vector3, float, float, Vector3?>("vnavmesh.Query.Mesh.NearestPointReachable", p, halfExtentXZ, halfExtentY);

    /// <summary>True if a mesh polygon lies within halfExtentY under/over p;
    /// allowUnreachable false limits it to vnavmesh's flood-filled reachable set.</summary>
    public static bool IsPointOnMesh(Vector3 p, float halfExtentY, bool allowUnreachable) =>
        Call<Vector3, float, bool, bool>("vnavmesh.Query.Mesh.IsPointOnMesh", p, halfExtentY, allowUnreachable);

    public static List<Vector3>? ListWaypoints() => Call<List<Vector3>>("vnavmesh.Path.ListWaypoints");

    public static void Stop()
    {
        try { Svc.PluginInterface.GetIpcSubscriber<object>("vnavmesh.Path.Stop").InvokeAction(); }
        catch (Exception ex) { Svc.Log.Verbose($"[vnavmesh IPC] Path.Stop failed: {ex.Message}"); }
    }

    private static T? Call<T>(string name) =>
        Guard(name, () => Svc.PluginInterface.GetIpcSubscriber<T>(name).InvokeFunc());
    private static T? Call<T1, T2, T>(string name, T1 a, T2 b) =>
        Guard(name, () => Svc.PluginInterface.GetIpcSubscriber<T1, T2, T>(name).InvokeFunc(a, b));
    private static T? Call<T1, T2, T3, T>(string name, T1 a, T2 b, T3 c) =>
        Guard(name, () => Svc.PluginInterface.GetIpcSubscriber<T1, T2, T3, T>(name).InvokeFunc(a, b, c));

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
