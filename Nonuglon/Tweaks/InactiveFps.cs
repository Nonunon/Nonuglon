using System;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Config;
using ECommons.DalamudServices;
using static Nonuglon.Support.CommandText;

namespace Nonuglon.Tweaks;

/// <summary>Mini-tweak wrapping the game's System Configuration option
/// (https://dalamud.dev/api/Dalamud.Game.Config/Enums/SystemConfigOption/#fpsinactive)
/// - "Limit frame rate when client is inactive.". Sets via the uint overload
/// (0u/1u): Dalamud's uint/float/string setters marshal onto the framework
/// thread but the bool overload doesn't, so this is cheap insurance in case
/// this is ever called from somewhere that isn't already on it.</summary>
public class InactiveFps : IMiniTweak
{
    public string Name => "Inactive Window FPS Throttle";
    public string Description => "Toggle the game's own \"Limit frame rate when client is inactive.\" System Configuration setting.";

    public bool Enabled
    {
        get => Plugin.Configuration.InactiveFpsEnabled;
        set => Plugin.Configuration.InactiveFpsEnabled = value;
    }

    public string CommandName => "inactivefps";

    // "limit" only shows up once EffectivelyEnabled - see HandleCommand below.
    public string[] UsageLines =>
        EffectivelyEnabled
            ? [$"/Nonuglon {CommandName} <on|off|toggle>", $"/Nonuglon {CommandName} limit <on|off|toggle>"]
            : [$"/Nonuglon {CommandName} <on|off|toggle>"];

    /// <summary>True only when both this and Commands are on - the actual gate
    /// before touching the live game setting.</summary>
    private static bool EffectivelyEnabled => Plugin.Configuration.CommandsEnabled && Plugin.Configuration.InactiveFpsEnabled;

    private static bool TryGetLimit(out bool value) => Svc.GameConfig.TryGet(SystemConfigOption.FPSInActive, out value);

    public void DrawRow()
    {
        var enabled = Enabled;
        if (ImGui.Checkbox($"{Name}##InactiveFpsEnabled", ref enabled))
        {
            Enabled = enabled;
            Plugin.Configuration.Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(Description);

        ImGui.Indent();
        ImGui.BeginDisabled(!EffectivelyEnabled);

        if (!TryGetLimit(out var limitEnabled))
        {
            ImGui.TextDisabled("Could not read the game's current setting.");
        }
        else if (ImGui.Checkbox("Limit frame rate when client is inactive##InactiveFpsLimit", ref limitEnabled))
        {
            Svc.GameConfig.Set(SystemConfigOption.FPSInActive, limitEnabled ? 1u : 0u);
        }

        ImGui.EndDisabled();
        ImGui.Unindent();
    }

    public void HandleCommand(string[] args)
    {
        // Only recognized once EffectivelyEnabled; otherwise falls through.
        if (EffectivelyEnabled && args.Length >= 1 && args[0].Equals("limit", StringComparison.OrdinalIgnoreCase))
        {
            if (!TryGetLimit(out var current) || args.Length < 2 || !ResolveBool(args[1], current, out var enabled))
            {
                Print($"Usage: /Nonuglon {CommandName} limit <on|off|toggle>");
                return;
            }

            Svc.GameConfig.Set(SystemConfigOption.FPSInActive, enabled ? 1u : 0u);
            ReportStateChange($"{Name}: limit frame rate when inactive", current, enabled);
            return;
        }

        if (args.Length == 0 || !ResolveBool(args[0], Enabled, out var enabledSelf))
        {
            Print($"Usage: /Nonuglon {CommandName} <on|off|toggle>");
            return;
        }

        var previous = Enabled;
        Enabled = enabledSelf;
        Plugin.Configuration.Save();
        ReportStateChange(Name, previous, enabledSelf);
    }
}
