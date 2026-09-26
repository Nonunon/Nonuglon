using Dalamud.Configuration;
using System;
using System.Collections.Generic;

namespace Nonuglon;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 0;

    // -- Tweak toggles --
    // All default false: nothing should activate on a fresh install until the
    // user opts in.
    public bool InstantReturnEnabled { get; set; } = false;
    public bool InstantReturnLeaveParty { get; set; } = true;

    public bool AutoPillionEnabled { get; set; } = false;
    /// <summary>When true, Auto Pillion only offers a ride to people in
    /// AutoPillionFavorites, instead of anyone nearby in the party.</summary>
    public bool AutoPillionRestrictToPerson { get; set; } = false;
    /// <summary>Superseded by AutoPillionFavorites (name+world). Not
    /// auto-migrated - a bare name has no world to recover; Plugin.cs just logs
    /// a one-time reminder to re-add these manually.</summary>
    [Obsolete("Superseded by AutoPillionFavorites. Not auto-migrated - do not read/write this elsewhere.")]
    public List<string> AutoPillionFavoriteTargets { get; set; } = new();
    /// <summary>Players Auto Pillion offers a ride to when
    /// AutoPillionRestrictToPerson is on, identified by name + home world.</summary>
    public List<AutoPillionFavorite> AutoPillionFavorites { get; set; } = new();
    /// <summary>Native right-click "Add to Auto Pillion" entry. Independent of
    /// AutoPillionChat2ContextMenuEnabled below and of the tweak's own on/off.</summary>
    public bool AutoPillionContextMenuEnabled { get; set; } = false;
    /// <summary>Same idea for Chat 2's right-click integration (inert if Chat 2
    /// isn't installed), independent of the native context menu above.</summary>
    public bool AutoPillionChat2ContextMenuEnabled { get; set; } = false;
    /// <summary>Superseded by AutoPillionFavoriteTargets; kept only so an
    /// existing single-target value survives the one-time migration in Plugin.cs.</summary>
    [Obsolete("Superseded by AutoPillionFavoriteTargets. Retained for one-time migration only - do not read/write this elsewhere.")]
    public string AutoPillionTargetName { get; set; } = string.Empty;
    /// <summary>How long (ms) to wait for a mount attempt before retrying -
    /// TaskManager's default ~30s timeout is way too long here.</summary>
    public int AutoPillionRetryTimeoutMs { get; set; } = 2000;

    public bool EntrustChocoboDuplicatesEnabled { get; set; } = false;

    public bool SearchInfoMenuEnabled { get; set; } = false;
    /// <summary>Chat 2's right-click integration for View Search Info, same
    /// independent-toggle relationship as AutoPillionChat2ContextMenuEnabled.</summary>
    public bool SearchInfoMenuChat2ContextMenuEnabled { get; set; } = false;

    public bool EstateTeleportationEnabled { get; set; } = false;

    /// <summary>Master switch for "Commands" (see Tweaks/Commands.cs). Disables
    /// every mini-tweak's effect without touching their own flags below.</summary>
    public bool CommandsEnabled { get; set; } = false;

    /// <summary>Inactive Window FPS Throttle's own on/off, distinct from
    /// CommandsEnabled - both must be true before "limit" can touch game config.</summary>
    public bool InactiveFpsEnabled { get; set; } = false;

    /// <summary>Render Toggle's own on/off, same relationship as
    /// InactiveFpsEnabled. Whether rendering is *currently* disabled isn't
    /// persisted - RenderDisableManager doesn't survive a restart either.</summary>
    public bool RenderToggleEnabled { get; set; } = false;

    // The below exists just to make saving less cumbersome
    public void Save()
    {
        Plugin.PluginInterface.SavePluginConfig(this);
    }
}

/// <summary>A single Auto Pillion favorite. WorldId is what OnUpdate matches
/// against (a plain uint compare, no Excel lookup per frame); WorldName is a
/// cached display copy for "Name@World" output.</summary>
[Serializable]
public class AutoPillionFavorite
{
    public string Name { get; set; } = string.Empty;
    public uint WorldId { get; set; }
    public string WorldName { get; set; } = string.Empty;
    /// <summary>When false, OnUpdate skips this favorite without deleting it -
    /// useful for reordering priority (tried in list order) or ruling someone
    /// out temporarily.</summary>
    public bool Enabled { get; set; } = true;
}
