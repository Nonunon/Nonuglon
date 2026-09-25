using Dalamud.Configuration;
using System;
using System.Collections.Generic;

namespace Nonuglon;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 0;

    // -- Tweak toggles --
    // Every tweak defaults to off: nothing should start doing anything to a fresh
    // install until the user explicitly opts in, even the "handful of things I
    // actually wanted" ones from the README.
    public bool InstantReturnEnabled { get; set; } = false;
    public bool InstantReturnLeaveParty { get; set; } = true;

    public bool AutoPillionEnabled { get; set; } = false;
    /// <summary>When true, Auto Pillion only offers a ride to people in
    /// AutoPillionFavorites, instead of anyone nearby in the party.</summary>
    public bool AutoPillionRestrictToPerson { get; set; } = false;
    /// <summary>Superseded by AutoPillionFavorites, which also tracks each
    /// favorite's home world so two players who happen to share a name on
    /// different worlds can't be confused for each other. Not auto-migrated - a
    /// bare name has no world to recover. Plugin.cs logs a one-time reminder on
    /// load if this is non-empty and AutoPillionFavorites is not, so old entries
    /// stay visible (in the log) to re-add manually, then this is left alone.</summary>
    [Obsolete("Superseded by AutoPillionFavorites. Not auto-migrated - do not read/write this elsewhere.")]
    public List<string> AutoPillionFavoriteTargets { get; set; } = new();
    /// <summary>Players Auto Pillion will offer a ride to when
    /// AutoPillionRestrictToPerson is enabled, identified by name AND home world.
    /// Populated via the Auto Pillion options UI (which requires typing a world),
    /// the right-click context menu, or the Chat 2 context menu integration (both
    /// of which capture the target's home world automatically).</summary>
    public List<AutoPillionFavorite> AutoPillionFavorites { get; set; } = new();
    /// <summary>Whether the native right-click "Add to Auto Pillion" context menu
    /// entry (party list, friend list, chat log, etc.) is active. Off by default
    /// and independent of AutoPillionChat2ContextMenuEnabled below - seeing the
    /// menu entry implies favorites are actually being curated, so it shouldn't
    /// appear just because Auto Pillion itself is on.</summary>
    public bool AutoPillionContextMenuEnabled { get; set; } = false;
    /// <summary>Same idea as AutoPillionContextMenuEnabled, but for Chat 2's own
    /// right-click-a-message context menu integration (requires Chat 2 installed;
    /// harmlessly inert otherwise). Independently toggleable from the native one.</summary>
    public bool AutoPillionChat2ContextMenuEnabled { get; set; } = false;
    /// <summary>Superseded by AutoPillionFavoriteTargets (a list, supporting more
    /// than one saved person). Kept only so an existing single-target config value
    /// survives the upgrade - Plugin.cs migrates it into AutoPillionFavoriteTargets
    /// once on load, then leaves this empty going forward.</summary>
    [Obsolete("Superseded by AutoPillionFavoriteTargets. Retained for one-time migration only - do not read/write this elsewhere.")]
    public string AutoPillionTargetName { get; set; } = string.Empty;
    /// <summary>How long (ms) to wait for a ride/mount attempt to land before giving
    /// up and letting OnUpdate retry. TaskManager's default wait timeout is ~30s,
    /// which is way too long for a "just try again" tweak like this.</summary>
    public int AutoPillionRetryTimeoutMs { get; set; } = 2000;

    public bool EntrustChocoboDuplicatesEnabled { get; set; } = false;

    public bool SearchInfoMenuEnabled { get; set; } = false;
    /// <summary>Chat 2's own right-click-a-message context menu integration for
    /// View Search Info (requires Chat 2 installed; harmlessly inert otherwise).
    /// Off by default, same as AutoPillionChat2ContextMenuEnabled - independently
    /// toggleable from the tweak's native right-click menu, which is always active
    /// whenever the tweak itself is enabled.</summary>
    public bool SearchInfoMenuChat2ContextMenuEnabled { get; set; } = false;

    public bool EstateTeleportationEnabled { get; set; } = false;

    /// <summary>Whether the Inactive Window FPS Throttle tweak itself is on -
    /// distinct from the live game setting it controls. Off by default like every
    /// other tweak; the game's own "Limit frame rate when client is inactive."
    /// value is only readable/settable via "/Nonuglon inactivefps limit ..." once
    /// this is true, so the tweak never touches game config the user hasn't
    /// explicitly opted into managing through Nonuglon.</summary>
    public bool InactiveFpsEnabled { get; set; } = false;

    // The below exists just to make saving less cumbersome
    public void Save()
    {
        Plugin.PluginInterface.SavePluginConfig(this);
    }
}

/// <summary>A single Auto Pillion favorite, identified by name and home world.
/// WorldId is what OnUpdate actually matches against (a plain uint compare against
/// IPlayerCharacter.HomeWorld.RowId, no Excel sheet lookup needed per candidate
/// per frame); WorldName is a cached copy of the world's display name from the
/// moment this favorite was added, used only for showing "Name@World" in the UI
/// and chat output.</summary>
[Serializable]
public class AutoPillionFavorite
{
    public string Name { get; set; } = string.Empty;
    public uint WorldId { get; set; }
    public string WorldName { get; set; } = string.Empty;
    /// <summary>When false, OnUpdate skips this favorite entirely (as if it
    /// weren't in the list) while leaving it saved - a way to temporarily rule
    /// someone out (or prefer another favorite ahead of them, since OnUpdate tries
    /// favorites in list order and stops at the first match) without losing the
    /// saved name+world. Defaults true so existing/newly-added favorites behave
    /// exactly as before until deliberately turned off.</summary>
    public bool Enabled { get; set; } = true;
}
