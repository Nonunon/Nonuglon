using System;
using System.Collections.Generic;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game;
using Lumina.Excel.Sheets;
using Nonuglon.Support;

namespace Nonuglon.Tweaks;

/// <summary>Duty Finder Modules' Testing check: when a boss whose vbm module is
/// marked incomplete is pulled, says so in chat with the module's contributors,
/// once per module per duty visit. Watches the object table for the module's
/// primary actor in combat, since vbm has no event for this outside itself.</summary>
public sealed unsafe class DutyFinderTestingCheck : IDisposable
{
    private const double PollSeconds = 1;

    private readonly HashSet<string> announced = [];
    private Dictionary<uint, ModuleDetails>? modules;
    private uint cfc;
    private DateTime nextPoll;
    private bool running;

    public void SetRunning(bool run)
    {
        if (run == running) return;
        running = run;
        if (run)
            Svc.Framework.Update += OnUpdate;
        else
            Svc.Framework.Update -= OnUpdate;
        Reset(0);
    }

    public void Dispose() => SetRunning(false);

    private void Reset(uint newCfc)
    {
        cfc = newCfc;
        modules = null;
        announced.Clear();
    }

    private void OnUpdate(IFramework framework)
    {
        var now = DateTime.UtcNow;
        if (now < nextPoll) return;
        nextPoll = now.AddSeconds(PollSeconds);

        var gm = GameMain.Instance();
        var current = gm == null ? 0u : gm->CurrentContentFinderConditionId;
        if (current != cfc) Reset(current);
        if (cfc == 0 || !PluginDetection.IsPluginLoaded(PluginDetection.BossModInternalName)) return;

        // Read once per visit; a failed read retries on the next poll.
        modules ??= BossModModules.ReadDuty(cfc);
        if (modules == null || modules.Count == 0) return;

        foreach (var obj in Svc.Objects)
        {
            if (obj is not IBattleNpc npc || (npc.StatusFlags & StatusFlags.InCombat) == 0) continue;
            if (!modules.TryGetValue(npc.BaseId, out var module) || !module.Incomplete || !announced.Add(module.TypeName)) continue;

            var who = module.Contributors.Length > 0
                ? $"the contributors are {module.Contributors}, please contact them with any issues"
                : "no contributors are listed";
            CommandText.Print($"Testing check: the {BossName(module, npc)} module is marked incomplete in Boss Mod; {who}.", MessageKind.Notice);
        }
    }

    private static string BossName(ModuleDetails module, IBattleNpc npc)
    {
        if (module.NameId != 0 && Svc.Data.GetExcelSheet<BNpcName>().TryGetRow(module.NameId, out var row))
        {
            var name = row.Singular.ExtractText();
            if (name.Length > 0) return name;
        }
        var shown = npc.Name.TextValue;
        return shown.Length > 0 ? shown : module.TypeName;
    }
}
