using System.Collections.Generic;
using System.Linq;
using Dalamud.Plugin;
using ECommons.DalamudServices;

namespace Nonuglon.Support;

/// <summary>Shared "is X plugin available" check via Dalamud's InstalledPlugins.
/// That list is copied and wrapped on every read, and this is asked several
/// times a frame, so the loaded set is cached until ActivePluginsChanged.</summary>
public static class PluginDetection
{
    public const string Chat2InternalName = "ChatTwo";
    public const string VnavmeshInternalName = "vnavmesh";

    private static HashSet<string>? loaded;
    private static bool subscribed;

    /// <summary>Internal name may differ from the plugin installer's display
    /// name - check the manifest if unsure.</summary>
    public static bool IsPluginLoaded(string internalName) => (loaded ??= Snapshot()).Contains(internalName);

    private static HashSet<string> Snapshot()
    {
        if (!subscribed)
        {
            Svc.PluginInterface.ActivePluginsChanged += OnActivePluginsChanged;
            subscribed = true;
        }
        return Svc.PluginInterface.InstalledPlugins.Where(x => x.IsLoaded).Select(x => x.InternalName).ToHashSet();
    }

    private static void OnActivePluginsChanged(IActivePluginsChangedEventArgs _) => loaded = null;

    /// <summary>Call on plugin unload, before ECommons is disposed.</summary>
    public static void Dispose()
    {
        if (subscribed) Svc.PluginInterface.ActivePluginsChanged -= OnActivePluginsChanged;
        subscribed = false;
        loaded = null;
    }
}
