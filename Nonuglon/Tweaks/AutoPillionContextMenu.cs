using System;
using System.Linq;
using Dalamud.Game.Gui.ContextMenu;
using ECommons.DalamudServices;

namespace Nonuglon.Tweaks;

/// <summary>Adds "Add as Auto Pillion favorite" to the right-click context menu on
/// players in the party list, friend list, chat log, and similar windows. Ported
/// from HuntTrainAssistant's ContextMenuManager.cs (NightmareXIV) - same
/// OnMenuOpened/AddMenuItem pattern, trimmed down to just adding a favorite name
/// rather than their cross-world hunt-train conductor assignment (we don't need the
/// homeworld/public-world checks that exist there for cross-world lookups - Auto
/// Pillion only ever cares about someone physically near you).</summary>
public class AutoPillionContextMenu : IDisposable
{
    // Addons where a right-click-a-player context menu makes sense. Trimmed from
    // HuntTrainAssistant's own (larger) list to the ones relevant here. null covers
    // the default nameplate/target context menu.
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
            OnClicked = OnClicked
        };

        Svc.ContextMenu.OnMenuOpened += OnMenuOpened;
    }

    private void OnMenuOpened(IMenuOpenedArgs args)
    {
        if (!ValidAddons.Contains(args.AddonName)) return;
        if (args.Target is not MenuTargetDefault target || string.IsNullOrEmpty(target.TargetName)) return;

        args.AddMenuItem(menuItem);
    }

    private void OnClicked(IMenuItemClickedArgs args)
    {
        if (args.Target is not MenuTargetDefault target || string.IsNullOrEmpty(target.TargetName)) return;

        // 0 means the target isn't a real player-on-a-world (e.g. an NPC that
        // still passed the ValidAddons/name checks above) - nothing sane to save.
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
