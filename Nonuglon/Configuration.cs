using Dalamud.Configuration;
using System;
using System.Collections.Generic;

namespace Nonuglon;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 0;

    // -- Tweak toggles --
    public bool InstantReturnEnabled { get; set; } = true;
    public bool InstantReturnLeaveParty { get; set; } = true;

    public bool AutoPillionEnabled { get; set; } = true;
    /// <summary>When true, Auto Pillion only offers a ride to people in
    /// AutoPillionFavoriteTargets, instead of anyone nearby in the party.</summary>
    public bool AutoPillionRestrictToPerson { get; set; } = false;
    /// <summary>Names of players Auto Pillion will offer a ride to when
    /// AutoPillionRestrictToPerson is enabled. Populated via the Auto Pillion
    /// options UI, the right-click context menu, or the Chat 2 context menu
    /// integration.</summary>
    public List<string> AutoPillionFavoriteTargets { get; set; } = new();
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

    public bool EntrustChocoboDuplicatesEnabled { get; set; } = true;

    // The below exists just to make saving less cumbersome
    public void Save()
    {
        Plugin.PluginInterface.SavePluginConfig(this);
    }
}
