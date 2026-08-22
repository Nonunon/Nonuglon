using System;
using System.Linq;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using ECommons.DalamudServices;
using Nonuglon.Support;

namespace Nonuglon.Tweaks;

/// <summary>
/// Ported from ffxiv-bundleoftweaks' Tweaks/AutoPillion.cs.
/// Automatically hops onto a nearby mount with an open pillion seat.
/// Optionally restricted to one named person via config.
///
/// No TaskManager here on purpose: a single RidePillion call doesn't need a task
/// queue, and ECommons' TaskManager wait-timeout defaults to ~30s, which made a
/// missed attempt (dismount, target out of range mid-ride, etc.) lock the tweak up
/// for way too long. A plain timestamp-based retry throttle is simpler and its
/// timeout is fully in our control via AutoPillionRetryTimeoutMs.
/// </summary>
public unsafe class AutoPillion : TweakBase
{
    public override string Name => "Auto Pillion";
    public override string Description => "Automatically hops onto a nearby mount with an open pillion seat. Optionally restrict to one specific person.";

    /// <summary>0 = idle/not attempting. Otherwise, the Environment.TickCount64 at
    /// which the current attempt should be considered timed out.</summary>
    private long attemptExpiresAt;

    protected override void Enable() => Svc.Framework.Update += OnUpdate;

    protected override void Disable()
    {
        Svc.Framework.Update -= OnUpdate;
        attemptExpiresAt = 0;
    }

    private void OnUpdate(IFramework framework)
    {
        var player = Svc.Objects.LocalPlayer;
        if (player is null || player.IsMounted())
        {
            attemptExpiresAt = 0;
            return;
        }

        // Still within the current attempt's window - don't spam RidePillion every
        // frame while waiting to see if the last attempt lands. Once the window
        // passes without us getting mounted, this falls through and tries again.
        if (attemptExpiresAt != 0 && Environment.TickCount64 < attemptExpiresAt)
            return;
        attemptExpiresAt = 0;

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
        Svc.Log.Debug($"[AutoPillion] Mounting up with {target.Name.TextValue}");
        target.BattleChara()->RidePillion(10);
        attemptExpiresAt = Environment.TickCount64 + Plugin.Configuration.AutoPillionRetryTimeoutMs;
    }

    public override void DrawOptions()
    {
        var config = Plugin.Configuration;

        var restrictToPerson = config.AutoPillionRestrictToPerson;
        if (ImGui.Checkbox("Restrict to one person##AutoPillion", ref restrictToPerson))
        {
            config.AutoPillionRestrictToPerson = restrictToPerson;
            config.Save();
        }

        var targetName = config.AutoPillionTargetName;
        if (ImGui.InputText("Target name##AutoPillion", ref targetName, 64))
        {
            config.AutoPillionTargetName = targetName;
            config.Save();
        }

        var retryTimeout = config.AutoPillionRetryTimeoutMs;
        if (ImGui.SliderInt("Retry timeout (ms)##AutoPillion", ref retryTimeout, 500, 10000))
        {
            config.AutoPillionRetryTimeoutMs = retryTimeout;
            config.Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("How long to wait for a ride attempt to land before giving up and retrying. Lower = faster remount after dismounting, but more spammy if it keeps missing.");
    }
}
