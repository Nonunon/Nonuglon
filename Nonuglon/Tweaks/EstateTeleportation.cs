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

/// <summary>
/// Adapted from XA-Slave's Services/EstateTeleportationContextMenuService.cs
/// (https://github.com/xa-io/XA-Slave, AGPL-3.0-licensed) - the generation-guarded
/// menu/click lifecycle and the friend-list resolution logic are carried over
/// essentially as-is, since that's what makes it safe to fire a delayed native call
/// against a menu target that's no longer valid; the surrounding object model is
/// rebuilt on TweakBase/Svc rather than XA-Slave's own constructor-injected services.
///
/// Adds "Estate Teleportation" to the right-click context menu on a friend who
/// shares your current world, opening the game's own friend estate-teleport
/// selector for them directly (AgentFriendlist.OpenFriendEstateTeleportation) - the
/// same window the Friend List's own entry uses, just reachable from more places
/// (party list, chat log, etc).
///
/// The native menu already offers this itself whenever the target is a live,
/// rendered character (right-clicking their nameplate/model out in the world,
/// or in the Friend List where the entry is always present) - the opposite gap
/// from Search Info Menu, which is native on menu-only targets but missing in
/// world space. So this only adds the item when MenuTargetDefault.TargetObject is
/// null - i.e. exactly the cases (party list, chat log, etc.) where the game
/// doesn't already show it - instead of duplicating a menu entry the game already
/// draws itself.
/// </summary>
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

    /// <summary>Bumped on logout or territory change, and captured alongside each
    /// menu click. A click whose generation no longer matches the current one means
    /// the world (or character) changed out from under it since the menu was
    /// opened, so OpenEstate below drops it instead of teleporting into a stale
    /// context.</summary>
    private uint generation;

    /// <summary>Last failure message shown in chat via OpenEstate, or null if the
    /// most recent attempt succeeded (or none has happened yet). Suppresses
    /// repeat chat spam for the *same* failure - clicking a stale menu item
    /// again and again shouldn't reprint the same line every time - while a
    /// genuinely different failure, or a subsequent success, is free to show
    /// again immediately.</summary>
    private string? lastReportedFailure;

    protected override void Enable()
    {
        // Deliberately does NOT throw when the native function can't be
        // resolved: OnMenuOpened/OpenEstate below already independently guard
        // against that (the menu item still gets added, but clicking it just
        // logs/reports a failure instead of crashing). Enabled needs to stay
        // true in that case for HasWarning below to actually be reachable -
        // EnableTweak() catches a throw here by leaving Enabled false, which
        // would make "Enabled && (native fn missing)" permanently false.
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
        // TargetObject is populated whenever the target is a live, rendered
        // character - nameplate/model right-clicks out in the world - and the game
        // already shows its own Estate Teleportation entry there, so skip adding
        // ours to avoid a duplicate. Friend List is excluded outright for the same
        // reason: it always carries the native entry regardless of whether the
        // friend happens to be rendered nearby right now.
        if (!IsReady() || args.MenuType != ContextMenuType.Default || args.AddonName == "FriendList"
            || args.Target is not MenuTargetDefault { TargetObject: null } target)
            return;

        try
        {
            // Never retain MenuTargetDefault itself - its properties read mutable
            // native menu state, so only the plain values below survive past this
            // callback.
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

    /// <summary>Matches a context-menu target against the live friend list by name
    /// and home world (only friends sharing your current world are eligible - that's
    /// what OpenFriendEstateTeleportation expects), falling back to an exact
    /// content-ID match when the menu already supplied one. Returns 0 for "not a
    /// usable friend" rather than throwing, since a stranger simply having the same
    /// name as a friend is an expected, non-exceptional case here.</summary>
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

            // A menu with no content ID (name+world only) must match exactly one
            // friend - two same-named friends on the same world is a coin flip we'd
            // rather refuse than guess wrong on.
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
