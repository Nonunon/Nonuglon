using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using ECommons.DalamudServices;
using ECommons.Reflection;

namespace Nonuglon.Support;

/// <summary>Boss Mod (vbm) module coverage of one duty.</summary>
public readonly record struct DutyCoverage(int Modules, int Incomplete, int FromPack);

/// <summary>Reads vbm's loaded module registry by reflection, grouped by
/// ContentFinderCondition row. vbm's IPC only answers "is there a module for
/// this boss OID", which can't be mapped to a duty. Read-only; any missing
/// member just yields null.</summary>
public static class BossModModules
{
    // Module assemblies vbm itself ships; any other is a module pack.
    private static readonly HashSet<string> StockAssemblies = ["BossMod", "BossMod.Modules", "BossMod.Ultimate"];

    /// <summary>Why the last Read returned null, for the config window.</summary>
    public static string? LastError { get; private set; }

    public static Dictionary<uint, DutyCoverage>? Read()
    {
        try
        {
            var result = ReadInternal();
            LastError = null;
            return result;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            Svc.Log.Warning(ex, "[DutyFinderModules] Couldn't read Boss Mod's module registry.");
            return null;
        }
    }

    private static Dictionary<uint, DutyCoverage> ReadInternal()
    {
        if (!DalamudReflector.TryGetDalamudPlugin(PluginDetection.BossModInternalName, out var instance, out _, true, true) || instance == null)
            throw new InvalidOperationException("Boss Mod isn't loaded.");

        const BindingFlags Static = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
        var registry = instance.GetType().Assembly.GetType("BossMod.BossModuleRegistry")
            ?? throw new MissingMemberException("BossMod.BossModuleRegistry");
        var infoType = registry.GetNestedType("Info") ?? throw new MissingMemberException("BossModuleRegistry.Info");
        var modules = registry.GetProperty("RegisteredModules", Static)?.GetValue(null) as IDictionary
            ?? throw new MissingMemberException("BossModuleRegistry.RegisteredModules");

        var groupType = Field(infoType, "GroupType");
        var groupId = Field(infoType, "GroupID");
        var incomplete = Field(infoType, "Incomplete");
        var moduleType = Field(infoType, "ModuleType");
        var category = Field(infoType, "Category");
        var (disabledModules, disabledCategories) = ReadDisabled(registry.GetField("_config", Static)?.GetValue(null));

        var byCfc = new Dictionary<uint, DutyCoverage>();
        foreach (var info in modules.Values)
        {
            if (info == null || groupType.GetValue(info)?.ToString() != "CFC") continue;
            var cfc = Convert.ToUInt32(groupId.GetValue(info));
            if (cfc == 0) continue;

            // Modules the user switched off in vbm never run, so they don't count.
            var type = moduleType.GetValue(info) as Type;
            if (type != null && disabledModules.Contains(type.ToString())) continue;
            if (disabledCategories.Contains(category.GetValue(info)?.ToString() ?? "")) continue;

            var fromPack = type != null && !StockAssemblies.Contains(type.Assembly.GetName().Name ?? "");
            byCfc.TryGetValue(cfc, out var c);
            byCfc[cfc] = new(c.Modules + 1, c.Incomplete + ((bool)incomplete.GetValue(info)! ? 1 : 0), c.FromPack + (fromPack ? 1 : 0));
        }
        return byCfc;
    }

    private static FieldInfo Field(Type t, string name) =>
        t.GetField(name, BindingFlags.Public | BindingFlags.Instance) ?? throw new MissingMemberException(t.Name, name);

    // Best effort: an unreadable config just means nothing is treated as disabled.
    private static (HashSet<string> Modules, HashSet<string> Categories) ReadDisabled(object? config)
    {
        HashSet<string> modules = [], categories = [];
        if (config == null) return (modules, categories);
        var t = config.GetType();
        if (t.GetField("DisabledModules")?.GetValue(config) is IEnumerable m)
            foreach (var x in m) modules.Add(x?.ToString() ?? "");
        if (t.GetField("DisabledCategories")?.GetValue(config) is IEnumerable c)
            foreach (var x in c) categories.Add(x?.ToString() ?? "");
        return (modules, categories);
    }
}
