using System;
using Dalamud.Plugin.Ipc;
using ECommons.DalamudServices;

namespace Nonuglon.Support;

/// <summary>Pandora's Box feature on/off over its IPC (PandoraIPC.cs), by feature
/// class name ("Internal" calls). Its Disable() doesn't touch Pandora's saved
/// config, so a feature switched off here comes back when Pandora reloads.
/// Guarded like VnavmeshIpc: a missing or changed Pandora gives null/false.</summary>
public static class PandorasBoxIpc
{
    public static bool IsLoaded => PluginDetection.IsPluginLoaded(PluginDetection.PandorasBoxInternalName);

    private static readonly ICallGateSubscriber<string, bool?> getEnabled =
        Svc.PluginInterface.GetIpcSubscriber<string, bool?>("PandorasBox.GetFeatureEnabledInternal");
    private static readonly ICallGateSubscriber<string, bool, object> setEnabled =
        Svc.PluginInterface.GetIpcSubscriber<string, bool, object>("PandorasBox.SetFeatureEnabledInternal");

    /// <summary>Null when Pandora isn't loaded or doesn't know the feature.</summary>
    public static bool? GetFeatureEnabled(string featureClass)
    {
        if (!IsLoaded) return null;
        try { return getEnabled.InvokeFunc(featureClass); }
        catch (Exception ex)
        {
            Svc.Log.Verbose($"[PandorasBox IPC] GetFeatureEnabledInternal failed: {ex.Message}");
            return null;
        }
    }

    public static bool SetFeatureEnabled(string featureClass, bool enabled)
    {
        if (!IsLoaded) return false;
        try
        {
            setEnabled.InvokeAction(featureClass, enabled);
            return true;
        }
        catch (Exception ex)
        {
            Svc.Log.Verbose($"[PandorasBox IPC] SetFeatureEnabledInternal failed: {ex.Message}");
            return false;
        }
    }
}
