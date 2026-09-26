using System;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.Gui.ContextMenu;
using Dalamud.Game.Text.SeStringHandling;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using Nonuglon.Support;
using static Nonuglon.Support.CommandText;

namespace Nonuglon.Tweaks;

/// <summary>Adapted from XA-Slave's
/// Services/EstateTeleportationContextMenuService.cs
/// (https://github.com/xa-io/XA-Slave, AGPL-3.0) - the generation-guarded
/// menu/click lifecycle and friend-list resolution are carried over as-is
/// (that's what makes a delayed native call against a stale menu target safe);
/// the surrounding object model is rebuilt on TweakBase/Svc.
///
/// Opens the game's own friend estate-teleport window
/// (AgentFriendlist.OpenFriendEstateTeleportation), reachable from more places
/// than just the Friend List. Only adds the menu item when
/// MenuTargetDefault.TargetObject is null - the opposite gap from Search Info
/// Menu - since the native menu already shows this itself for any live,
/// rendered character.</summary>
public sealed unsafe class EstateTeleportation : TweakBase
{
    public override string Name => "Estate Teleportation";
    public override string Description => "Adds \"Estate Teleportation\" to the right-click context menu on a friend in your party list (and similar list-style menus) sharing your current world, opening the game's own friend estate-teleport window for them directly.";

    public override bool ConfigEnabled
    {
        get => Plugin.Configuration.EstateTeleportationEnabled;
        set => Plugin.Configuration.EstateTeleportationEnabled = value;
    }

    public override string[] CommandNames => ["estateteleport", "estate"];

    /// <summary>Bumped on logout/territory change, captured per menu click - a
    /// stale generation means the world changed since the menu opened, so
    /// OpenEstate drops it instead of teleporting into a stale context.</summary>
    private uint generation;

    /// <summary>Last failure shown in chat, or null after a success - see
    /// ReportFailure. Avoids reprinting the same failure on repeat clicks.</summary>
    private string? lastReportedFailure;

    protected override void Enable()
    {
        // Doesn't throw when the native function is missing - see HasWarning's
        // doc comment in TweakBase.cs for why that matters here.
        generation++;
        Svc.ContextMenu.OnMenuOpened += OnMenuOpened;
        Svc.ClientState.Logout += OnLogout;
        Svc.ClientState.TerritoryChanged += OnTerritoryChanged;
    }

    protected override void Disable()
    {
        Svc.ContextMenu.OnMenuOpened -= OnMenuOpened;
        Svc.ClientState.Logout -= OnLogout;
        Svc.ClientState.TerritoryChanged -= OnTerritoryChanged;
        lastReportedFailure = null;
    }

    public override bool HasWarning => Enabled && AgentFriendlist.MemberFunctionPointers.OpenFriendEstateTeleportation == null;

    public override void DrawOptions()
    {
        if (!HasWarning) return;

        ImGui.PushTextWrapPos(ImGui.GetContentRegionAvail().X + ImGui.GetCursorPosX());
        ImGui.TextColored(UiColors.Warning, "The game's estate teleportation function could not be resolved - this tweak has no effect right now.");
        ImGui.PopTextWrapPos();
    }

    private void OnLogout(int type, int code) => generation++;

    private void OnTerritoryChanged(uint territoryType) => generation++;

    private static bool IsReady() =>
        Svc.ClientState.IsLoggedIn &&
        Svc.PlayerState.IsLoaded &&
        Svc.Objects.LocalPlayer != null &&
        !Svc.Condition[ConditionFlag.BetweenAreas] &&
        !Svc.Condition[ConditionFlag.BetweenAreas51];

    private void OnMenuOpened(IMenuOpenedArgs args)
    {
        // TargetObject non-null means a live rendered character, where the game
        // already shows this natively - skip to avoid a duplicate. Friend List
        // is excluded outright since it always carries the native entry.
        if (!IsReady() || args.MenuType != ContextMenuType.Default || args.AddonName == "FriendList"
            || args.Target is not MenuTargetDefault { TargetObject: null } target)
            return;

        try
        {
            // Don't retain MenuTargetDefault itself - it reads mutable native state.
            var name = target.TargetName;
            var world = target.TargetHomeWorld.RowId;
            var contentId = ResolveFriend(target.TargetContentId, name, world);
            if (contentId == 0) return;

            var owner = Svc.PlayerState.ContentId;
            var menuGeneration = generation;
            args.AddMenuItem(new MenuItem
            {
                Name = new SeStringBuilder().AddText("Estate Teleportation").Build(),
                PrefixChar = ContextMenuBranding.PrefixChar,
                PrefixColor = ContextMenuBranding.PrefixColor,
                OnClicked = _ => OpenEstate(contentId, name, world, owner, menuGeneration),
            });
        }
        catch (Exception ex)
        {
            Svc.Log.Warning(ex, $"[{Name}] Could not resolve the context-menu friend.");
        }
    }

    /// <summary>Matches against the live friend list by name+world (only
    /// same-world friends are eligible), falling back to content ID if the menu
    /// supplied one. Returns 0 rather than throwing - a name collision with a
    /// stranger is expected here, not exceptional.</summary>
    private static ulong ResolveFriend(ulong contentId, string name, uint world)
    {
        var local = Svc.Objects.LocalPlayer;
        if (local == null || string.IsNullOrWhiteSpace(name) || world == 0 || world >= ushort.MaxValue
            || world != local.CurrentWorld.RowId || contentId == ulong.MaxValue)
            return 0;

        var friends = InfoProxyFriendList.Instance();
        if (friends == null || friends->CharData == null || friends->EntryCount > 200)
            return 0;

        ulong match = 0;
        foreach (ref readonly var friend in friends->CharDataSpan)
        {
            if (friend.ContentId == 0 || friend.ContentId == ulong.MaxValue
                || friend.ContentId == Svc.PlayerState.ContentId
                || friend.HomeWorld != world
                || !string.Equals(friend.NameString, name, StringComparison.Ordinal)
                || (contentId != 0 && friend.ContentId != contentId))
                continue;

            // Name+world alone must match exactly one friend - refuse rather than guess.
            if (match != 0) return 0;
            match = friend.ContentId;
        }

        return match;
    }

    private void OpenEstate(ulong contentId, string name, uint world, ulong owner, uint menuGeneration)
    {
        if (!Enabled || generation != menuGeneration || !IsReady() || owner == 0 || Svc.PlayerState.ContentId != owner)
            return;

        try
        {
            if (ResolveFriend(contentId, name, world) != contentId)
            {
                ReportFailure("The friend or current world changed since the menu was opened - reopen it and try again.");
                return;
            }

            var agent = AgentFriendlist.Instance();
            if (agent == null || AgentFriendlist.MemberFunctionPointers.OpenFriendEstateTeleportation == null)
            {
                ReportFailure("The game's friend estate selector isn't ready.");
                return;
            }

            agent->OpenFriendEstateTeleportation(contentId);
            lastReportedFailure = null;
        }
        catch (Exception ex)
        {
            Svc.Log.Warning(ex, $"[{Name}] Could not open the friend estate selector.");
            ReportFailure("Could not open the friend estate selector - see the plugin log for details.");
        }
    }

    /// <summary>Prints a failure to chat only if it differs from the last one
    /// already shown (see lastReportedFailure) - always logged, but chat only
    /// gets a fresh line once per distinct failure state.</summary>
    private void ReportFailure(string message)
    {
        Svc.Log.Warning($"[{Name}] {message}");
        if (lastReportedFailure == message) return;
        lastReportedFailure = message;
        Print($"{Name}: {message}");
    }
}
