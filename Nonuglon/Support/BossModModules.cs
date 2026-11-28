using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using ECommons.DalamudServices;
using ECommons.Reflection;

namespace Nonuglon.Support;

/// <summary>Boss Mod (vbm) module coverage of one duty. Packs holds the
/// assembly names of the module packs (not stock) its modules come from.</summary>
public readonly record struct DutyCoverage(int Modules, int Incomplete, int FromPack, string[] Packs);

/// <summary>One vbm module, as the Testing check needs it.</summary>
public readonly record struct ModuleDetails(string TypeName, uint NameId, string Contributors, bool Incomplete);

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

    /// <summary>Assembly names of every module pack vbm had loaded at the last
    /// Read (enabled or not), sorted; null until the first successful Read.</summary>
    public static IReadOnlyList<string>? LoadedPacks { get; private set; }

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
        var packsByCfc = new Dictionary<uint, SortedSet<string>>();
        var loadedPacks = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var info in modules.Values)
        {
            if (info == null) continue;
            var pack = PackName(moduleType.GetValue(info) as Type);
            if (pack != null) loadedPacks.Add(pack);
            if (groupType.GetValue(info)?.ToString() != "CFC") continue;
            var cfc = Convert.ToUInt32(groupId.GetValue(info));
            if (cfc == 0) continue;

            // Modules the user switched off in vbm never run, so they don't count.
            var type = moduleType.GetValue(info) as Type;
            if (type != null && disabledModules.Contains(type.ToString())) continue;
            if (disabledCategories.Contains(category.GetValue(info)?.ToString() ?? "")) continue;

            var fromPack = PackName(type);
            if (fromPack != null)
            {
                if (!packsByCfc.TryGetValue(cfc, out var set)) packsByCfc[cfc] = set = new(StringComparer.Ordinal);
                set.Add(fromPack);
            }
            byCfc.TryGetValue(cfc, out var c);
            byCfc[cfc] = new(c.Modules + 1, c.Incomplete + ((bool)incomplete.GetValue(info)! ? 1 : 0), c.FromPack + (fromPack != null ? 1 : 0), []);
        }
        foreach (var (cfc, set) in packsByCfc)
            byCfc[cfc] = byCfc[cfc] with { Packs = [.. set] };
        LoadedPacks = [.. loadedPacks];
        return byCfc;
    }

    // The module's assembly name when it comes from a pack, null for vbm's own modules.
    private static string? PackName(Type? type)
    {
        var name = type?.Assembly.GetName().Name;
        return name == null || StockAssemblies.Contains(name) ? null : name;
    }

    /// <summary>The enabled CFC modules of one duty, by primary actor OID (null
    /// if vbm can't be read; the reason is in LastError).</summary>
    public static Dictionary<uint, ModuleDetails>? ReadDuty(uint cfc)
    {
        try
        {
            var result = ReadDutyInternal(cfc);
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

    private static Dictionary<uint, ModuleDetails> ReadDutyInternal(uint cfc)
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
        var oid = Field(infoType, "PrimaryActorOID");
        var nameId = Field(infoType, "NameID");
        var contributors = Field(infoType, "Contributors");
        var (disabledModules, disabledCategories) = ReadDisabled(registry.GetField("_config", Static)?.GetValue(null));

        var byOid = new Dictionary<uint, ModuleDetails>();
        foreach (var info in modules.Values)
        {
            if (info == null || groupType.GetValue(info)?.ToString() != "CFC" || Convert.ToUInt32(groupId.GetValue(info)) != cfc) continue;
            var type = moduleType.GetValue(info) as Type;
            if (type != null && disabledModules.Contains(type.ToString())) continue;
            if (disabledCategories.Contains(category.GetValue(info)?.ToString() ?? "")) continue;
            byOid[Convert.ToUInt32(oid.GetValue(info))] = new(type?.Name ?? "?", Convert.ToUInt32(nameId.GetValue(info)),
                contributors.GetValue(info) as string ?? "", (bool)incomplete.GetValue(info)!);
        }
        return byOid;
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
