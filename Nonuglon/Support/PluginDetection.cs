using System.Linq;
using ECommons.DalamudServices;

namespace Nonuglon.Support;

/// <summary>Small shared helper for checking whether another plugin is currently
/// installed and loaded, via Dalamud's own live InstalledPlugins list. Centralized
/// here so any tweak or window that needs a "is X plugin available" check can reuse
/// the same lookup instead of duplicating the LINQ each time.</summary>
public static class PluginDetection
{
    /// <summary>True if a plugin with this internal name is installed and
    /// currently loaded. Internal name is usually the plugin's assembly/manifest
    /// name, which may differ from what's shown in the plugin installer UI - check
    /// the actual manifest if unsure. Re-evaluates live each call (no caching), so
    /// it correctly reflects the other plugin being enabled/disabled mid-session.</summary>
    public static bool IsPluginLoaded(string internalName) =>
        Svc.PluginInterface.InstalledPlugins.Any(x => x.IsLoaded && x.InternalName == internalName);
}
