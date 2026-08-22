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
/// Automatically hops onto a nearby mount with an open pillion seat. Optionally
/// restricted to a saved list of favorite people via config, populated either
/// through this tweak's own options UI, the right-click context menu
/// (AutoPillionContextMenu), or Chat 2's context menu integration
/// (AutoPillionChat2Ipc).
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
    public override string Description => "Automatically hops onto a nearby mount with an open pillion seat. Optionally restrict to a saved list of favorite people.";

    /// <summary>0 = idle/not attempting. Otherwise, the Environment.TickCount64 at
    /// which the current attempt should be considered timed out.</summary>
    private long attemptExpiresAt;

    private AutoPillionContextMenu? contextMenu;
    private AutoPillionChat2Ipc? chat2Ipc;

    /// <summary>Backing field for the "add a favorite" text input in DrawOptions -
    /// lives on the tweak instance (not static/local) since ImGui needs somewhere
    /// stable to write into across frames.</summary>
    private string newFavoriteInput = string.Empty;

    protected override void Enable()
    {
        Svc.Framework.Update += OnUpdate;
        SyncIntegrations(tweakEnabled: true);
    }

    protected override void Disable()
    {
        Svc.Framework.Update -= OnUpdate;
        attemptExpiresAt = 0;
        SyncIntegrations(tweakEnabled: false);
    }

    /// <summary>Creates or tears down the context-menu integrations to match the
    /// current config flags, given whether the tweak itself is (about to be)
    /// enabled. Takes an explicit flag rather than reading the Enabled property
    /// directly: TweakBase.EnableTweak() only flips Enabled to true AFTER Enable()
    /// returns, so reading Enabled from inside Enable() itself would still see
    /// false. Everywhere else (DrawOptions' checkboxes, called well after
    /// EnableTweak() has completed), Enabled is safe to read directly.</summary>
    public void SyncIntegrations(bool tweakEnabled)
    {
        var config = Plugin.Configuration;

        var wantContextMenu = tweakEnabled && config.AutoPillionContextMenuEnabled;
        if (wantContextMenu && contextMenu is null) contextMenu = new AutoPillionContextMenu();
        else if (!wantContextMenu && contextMenu is not null) { contextMenu.Dispose(); contextMenu = null; }

        var wantChat2Ipc = tweakEnabled && config.AutoPillionChat2ContextMenuEnabled;
        if (wantChat2Ipc && chat2Ipc is null) chat2Ipc = new AutoPillionChat2Ipc();
        else if (!wantChat2Ipc && chat2Ipc is not null) { chat2Ipc.Dispose(); chat2Ipc = null; }
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
            foreach (var favoriteName in config.AutoPillionFavoriteTargets)
            {
                var target = GameObjectPillionExtensions.FindPlayerByName(favoriteName);
                if (target is null || target.EntityId == player.EntityId) continue;
                if (target.CurrentDistance >= 3) continue;
                if (!target.CanRidePillion()) continue;

                MountUpWith(target);
                return;
            }
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

    /// <summary>Small muted "(?)" next to a checkbox that shows a tooltip on
    /// hover - standard ImGui idiom for explaining a setting without permanently
    /// occupying space with a full sentence. warning: true recolors it amber
    /// instead of the default muted gray, for cases where the setting is on but
    /// something about it isn't actually working right now (e.g. Chat 2 not being
    /// loaded while its integration is enabled).</summary>
    private static void HelpMarker(string tooltip, bool warning = false)
    {
        ImGui.SameLine();
        ImGui.TextColored(warning ? UiColors.Warning : UiColors.Disabled, "(?)");
        if (ImGui.IsItemHovered())
        {
            ImGui.BeginTooltip();
            ImGui.PushTextWrapPos(ImGui.GetFontSize() * 25f);
            ImGui.TextUnformatted(tooltip);
            ImGui.PopTextWrapPos();
            ImGui.EndTooltip();
        }
    }

    public override void DrawOptions()
    {
        var config = Plugin.Configuration;

        var restrictToPerson = config.AutoPillionRestrictToPerson;
        if (ImGui.Checkbox("Restrict to favorites##AutoPillion", ref restrictToPerson))
        {
            config.AutoPillionRestrictToPerson = restrictToPerson;
            config.Save();
        }

        ImGui.Spacing();
        ImGui.TextDisabled("Context menu integrations:");

        var contextMenuEnabled = config.AutoPillionContextMenuEnabled;
        if (ImGui.Checkbox("Context Menu##AutoPillionContextMenu", ref contextMenuEnabled))
        {
            config.AutoPillionContextMenuEnabled = contextMenuEnabled;
            config.Save();
            SyncIntegrations(tweakEnabled: Enabled);
        }
        HelpMarker("Adds \"Add to Auto Pillion\" to the right-click menu on the party list, friend list, and chat log (among other places you can right-click a player).");

        var chat2Enabled = config.AutoPillionChat2ContextMenuEnabled;
        if (ImGui.Checkbox("Chat 2 Context Menu##AutoPillionChat2ContextMenu", ref chat2Enabled))
        {
            config.AutoPillionChat2ContextMenuEnabled = chat2Enabled;
            config.Save();
            SyncIntegrations(tweakEnabled: Enabled);
        }
        HelpMarker(
            "Right-click a name in Chat 2's own chat log, then look under Integrations for \"Add to Auto Pillion\". Requires the Chat 2 plugin.",
            warning: chat2Enabled && !PluginDetection.IsPluginLoaded("ChatTwo"));

        if (chat2Enabled && !PluginDetection.IsPluginLoaded("ChatTwo"))
        {
            ImGui.PushTextWrapPos(ImGui.GetContentRegionAvail().X + ImGui.GetCursorPosX());
            ImGui.TextColored(UiColors.Warning, "Chat 2 not detected - this integration has no effect until it's installed and loaded.");
            ImGui.PopTextWrapPos();
        }

        ImGui.Spacing();
        ImGui.TextDisabled("Favorites:");

        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - 60f);
        ImGui.InputTextWithHint("##AutoPillionNewFavorite", "Character name", ref newFavoriteInput, 64);
        ImGui.SameLine();
        if (ImGui.Button("Add##AutoPillionFavorite") && !string.IsNullOrWhiteSpace(newFavoriteInput))
        {
            if (!config.AutoPillionFavoriteTargets.Contains(newFavoriteInput))
                config.AutoPillionFavoriteTargets.Add(newFavoriteInput);
            config.Save();
            newFavoriteInput = string.Empty;
        }

        if (config.AutoPillionFavoriteTargets.Count == 0)
        {
            ImGui.TextDisabled("(none saved yet - add one above)");
        }
        else
        {
            for (var i = config.AutoPillionFavoriteTargets.Count - 1; i >= 0; i--)
            {
                ImGui.Text(config.AutoPillionFavoriteTargets[i]);
                ImGui.SameLine();
                if (ImGui.Button($"x##AutoPillionFavorite{i}"))
                {
                    config.AutoPillionFavoriteTargets.RemoveAt(i);
                    config.Save();
                }
            }
        }

        ImGui.Spacing();
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
