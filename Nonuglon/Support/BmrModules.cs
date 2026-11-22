using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ECommons.DalamudServices;

namespace Nonuglon.Support;

/// <summary>BossModReborn's module coverage by ContentFinderCondition row, read
/// from its installed DLL (it can't run alongside vbm, so it's usually off).
/// Cached until the DLL changes.</summary>
public static class BmrModules
{
    public const string InternalName = "BossModReborn";

    // Same group types BMR's own Duty Finder indicator treats as duties.
    private static readonly HashSet<string> DutyGroups = ["CFC", "MaskedCarnivale", "CrucibleOfTheUnbroken"];

    private static string? cachedPath;
    private static DateTime cachedWrite;
    private static Dictionary<uint, string>? cached;

    public static string? LastError { get; private set; }

    /// <summary>CFC row -> lowest maturity name among its modules (Dummy skipped).</summary>
    public static Dictionary<uint, string>? Read()
    {
        try
        {
            var path = FindDll() ?? throw new FileNotFoundException("BossModReborn isn't installed.");
            var write = File.GetLastWriteTimeUtc(path);
            if (cached == null || path != cachedPath || write != cachedWrite)
            {
                cached = Build(ModuleInfoScanner.Scan(path));
                cachedPath = path;
                cachedWrite = write;
            }
            LastError = null;
            return cached;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            Svc.Log.Warning(ex, "[DutyFinderModules] Couldn't read BossModReborn's modules.");
            return null;
        }
    }

    private static Dictionary<uint, string> Build(List<ModuleInfoScanner.Entry> entries)
    {
        var worst = new Dictionary<uint, ModuleInfoScanner.Entry>();
        foreach (var e in entries)
        {
            if (e.GroupId == 0 || e.MaturityName == "Dummy" || !DutyGroups.Contains(e.GroupType)) continue;
            if (!worst.TryGetValue(e.GroupId, out var w) || e.Maturity < w.Maturity)
                worst[e.GroupId] = e;
        }
        return worst.ToDictionary(kv => kv.Key, kv => kv.Value.MaturityName);
    }

    // installedPlugins\BossModReborn\<version>\, highest version wins like Dalamud.
    private static string? FindDll()
    {
        var root = Svc.PluginInterface.ConfigDirectory.Parent?.Parent;
        var dir = root == null ? null : new DirectoryInfo(Path.Combine(root.FullName, "installedPlugins", InternalName));
        if (dir == null || !dir.Exists) return null;
        return dir.GetDirectories()
            .Select(d => (Dir: d, Version: Version.TryParse(d.Name, out var v) ? v : null))
            .Where(x => x.Version != null && File.Exists(Path.Combine(x.Dir.FullName, InternalName + ".dll")))
            .OrderByDescending(x => x.Version)
            .Select(x => Path.Combine(x.Dir.FullName, InternalName + ".dll"))
            .FirstOrDefault();
    }
}
