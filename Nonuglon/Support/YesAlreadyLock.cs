using System.Collections.Generic;
using ECommons.DalamudServices;

namespace Nonuglon.Support;

/// <summary>Pauses YesAlready while we drive a dialog ourselves, by adding our
/// name to its "YesAlready.StopRequests" shared set. Ported from PandorasBox's
/// Helpers/YesAlready.cs, new-version path only (the reflection path is for
/// YesAlready before 1.4).</summary>
public static class YesAlreadyLock
{
    private static HashSet<string>? stopRequests;
    private static bool locked;

    private static HashSet<string>? StopRequests
    {
        get
        {
            if (stopRequests == null && Svc.PluginInterface.TryGetData<HashSet<string>>("YesAlready.StopRequests", out var data))
                stopRequests = data;
            return stopRequests;
        }
    }

    public static void Lock()
    {
        if (locked || !PluginDetection.IsPluginLoaded(PluginDetection.YesAlreadyInternalName) || StopRequests is not { } set) return;
        set.Add(Svc.PluginInterface.InternalName);
        locked = true;
        Svc.Log.Debug("[YesAlready] paused");
    }

    public static void Unlock()
    {
        if (!locked) return;
        locked = false;
        stopRequests?.Remove(Svc.PluginInterface.InternalName);
        Svc.Log.Debug("[YesAlready] resumed");
    }

    /// <summary>Unlocks and drops the shared-data reference, on plugin unload.</summary>
    public static void Dispose()
    {
        Unlock();
        if (stopRequests != null) Svc.PluginInterface.RelinquishData("YesAlready.StopRequests");
        stopRequests = null;
    }
}
