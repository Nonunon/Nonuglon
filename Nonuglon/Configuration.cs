using Dalamud.Configuration;
using System;

namespace Nonuglon;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 0;

    // -- Tweak toggles --
    public bool InstantReturnEnabled { get; set; } = true;
    public bool InstantReturnLeaveParty { get; set; } = true;

    public bool AutoPillionEnabled { get; set; } = true;
    public bool AutoPillionRestrictToPerson { get; set; } = false;
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
