using System;
using System.Linq;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.Gui.ContextMenu;
using ECommons.DalamudServices;
using Nonuglon.Support;

namespace Nonuglon.Tweaks;

/// <summary>Adds "Add as Auto Pillion favorite" to the right-click menu on the
/// party list, friend list, chat log, etc. Ported from HuntTrainAssistant's
/// (https://github.com/NightmareXIV/HuntTrainAssistant - a Dalamud plugin, so
/// bound by Dalamud's own AGPL-3.0 regardless of its lack of a LICENSE file)
/// ContextMenuManager.cs, trimmed to a favorite name instead of their cross-world
/// conductor assignment - Auto Pillion only cares about someone physically
/// near you.</summary>
public class AutoPillionContextMenu : IDisposable
{
    // Addons where a right-click-player menu makes sense; null = default nameplate/target menu.
    private static readonly string?[] ValidAddons =
    [
        null,
        "PartyMemberList",
        "_PartyList",
        "FriendList",
        "FreeCompany",
        "LinkShell",
        "CrossWorldLinkshell",
        "ChatLog",
        "SocialList",
        "ContactList",
    ];

    private readonly MenuItem menuItem;

    public AutoPillionContextMenu()
    {
        menuItem = new MenuItem
        {
            Name = "Add to Auto Pillion",
            OnClicked = OnClicked,
            PrefixChar = ContextMenuBranding.PrefixChar,
            PrefixColor = ContextMenuBranding.PrefixColor,
        };

        Svc.ContextMenu.OnMenuOpened += OnMenuOpened;
    }

    private void OnMenuOpened(IMenuOpenedArgs args)
    {
        if (!ValidAddons.Contains(args.AddonName)) return;
        if (args.Target is not MenuTargetDefault target || string.IsNullOrEmpty(target.TargetName)) return;

        // TargetObject is only populated for a live rendered object (nameplate
        // menus) - excludes NPCs/mobs when present, filtered out entirely.
        if (target.TargetObject is { } obj && obj.ObjectKind != ObjectKind.Pc) return;

        args.AddMenuItem(menuItem);
    }

    private void OnClicked(IMenuItemClickedArgs args)
    {
        if (args.Target is not MenuTargetDefault target || string.IsNullOrEmpty(target.TargetName)) return;

        // 0 means not a real player-on-a-world (e.g. an NPC past the checks above).
        var worldId = target.TargetHomeWorld.RowId;
        if (worldId == 0) return;

        var favorites = Plugin.Configuration.AutoPillionFavorites;
        if (favorites.Any(f => f.Name == target.TargetName && f.WorldId == worldId)) return;

        favorites.Add(new AutoPillionFavorite
        {
            Name = target.TargetName,
            WorldId = worldId,
            WorldName = target.TargetHomeWorld.ValueNullable?.Name.ExtractText() ?? string.Empty
        });
        Plugin.Configuration.Save();
    }

    public void Dispose() => Svc.ContextMenu.OnMenuOpened -= OnMenuOpened;
}
