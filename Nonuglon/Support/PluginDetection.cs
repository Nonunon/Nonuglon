using System.Linq;
using ECommons.DalamudServices;

namespace Nonuglon.Support;

/// <summary>Shared "is X plugin available" check via Dalamud's InstalledPlugins.</summary>
public static class PluginDetection
{
    /// <summary>Internal name may differ from the plugin installer's display
    /// name - check the manifest if unsure. Re-evaluates live, no caching.</summary>
    public static bool IsPluginLoaded(string internalName) =>
        Svc.PluginInterface.InstalledPlugins.Any(x => x.IsLoaded && x.InternalName == internalName);
}
