using System.Linq;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using ECommons.Automation.NeoTaskManager;
using ECommons.DalamudServices;
using Nonuglon.Support;

namespace Nonuglon.Tweaks;

/// <summary>
/// Ported from ffxiv-bundleoftweaks' Tweaks/AutoPillion.cs.
/// Automatically hops onto a nearby mount with an open pillion seat.
/// Optionally restricted to one named person via config.
/// </summary>
public unsafe class AutoPillion : TweakBase
{
    public override string Name => "Auto Pillion";
    public override string Description => "Automatically hops onto a nearby mount with an open pillion seat. Optionally restrict to one specific person.";

    private readonly TaskManager taskManager = new();

    protected override void Enable() => Svc.Framework.Update += OnUpdate;

    protected override void Disable()
    {
        Svc.Framework.Update -= OnUpdate;
        if (taskManager.NumQueuedTasks > 0)
            taskManager.Abort();
    }

    private void OnUpdate(IFramework framework)
    {
        var player = Svc.Objects.LocalPlayer;
        if (player is null || Svc.Condition[ConditionFlag.Mounted])
        {
            if (taskManager.NumQueuedTasks > 0)
                taskManager.Abort();
            return;
        }

        // Already mid-attempt: a ride command was just sent and we're waiting on
        // the Mounted condition to flip. Without this guard, OnUpdate re-fires every
        // single frame while still in range and not yet mounted, stacking a fresh
        // ride+wait attempt on top of the one still in flight - that's the "fires
        // once, errors, fires again" loop.
        if (taskManager.NumQueuedTasks > 0) return;

        var config = Plugin.Configuration;

        if (config.AutoPillionRestrictToPerson)
        {
            if (string.IsNullOrEmpty(config.AutoPillionTargetName)) return;

            var target = GameObjectPillionExtensions.FindPlayerByName(config.AutoPillionTargetName);
            if (target is null || target.EntityId == player.EntityId) return;
            if (target.CurrentDistance >= 3) return;
            if (!target.CanRidePillion()) return;

            MountUpWith(target);
            return;
        }

        var member = Svc.Party.FirstOrDefault(o =>
            o != null && o.EntityId != player.EntityId &&
            o.GameObject is { } go && go.CurrentDistance < 3 && go.CanRidePillion());

        if (member?.GameObject is { } partyTarget)
            MountUpWith(partyTarget);
    }

    private void MountUpWith(IGameObject target)
    {
        var name = target.Name.TextValue;
        taskManager.Enqueue(() => Svc.Log.Debug($"[AutoPillion] Mounting up with {name}"), "AutoPillion: log");
        taskManager.Enqueue(() => target.BattleChara()->RidePillion(10), "AutoPillion: ride");
        taskManager.Enqueue(() => Svc.Condition[ConditionFlag.Mounted], "AutoPillion: wait for mount");
    }
}
