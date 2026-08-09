using Dalamud.Configuration;
using System;

namespace Nonuglon;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 0;

    public bool IsConfigWindowMovable { get; set; } = true;

    // -- Tweak toggles --
    public bool InstantReturnEnabled { get; set; } = true;
    public bool InstantReturnLeaveParty { get; set; } = true;

    public bool AutoPillionEnabled { get; set; } = true;
    public bool AutoPillionRestrictToPerson { get; set; } = false;
    public string AutoPillionTargetName { get; set; } = string.Empty;

    public bool EntrustChocoboDuplicatesEnabled { get; set; } = true;

    // The below exists just to make saving less cumbersome
    public void Save()
    {
        Plugin.PluginInterface.SavePluginConfig(this);
    }
}
