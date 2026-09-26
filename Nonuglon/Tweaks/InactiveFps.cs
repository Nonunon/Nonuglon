using System;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Config;
using ECommons.DalamudServices;
using static Nonuglon.Support.CommandText;

namespace Nonuglon.Tweaks;

/// <summary>
/// Mini-tweak (see IMiniTweak.cs / Commands.cs) wrapping the game's own System
/// Configuration option
/// (https://dalamud.dev/api/Dalamud.Game.Config/Enums/SystemConfigOption/#fpsinactive)
/// - "Limit frame rate when client is inactive.". Its own Enabled flag is kept
/// separate from Commands' master switch: both need to be true (see
/// EffectivelyEnabled) before the "limit" subcommand or checkbox actually
/// touches the live game setting, so checking this alone (with Commands off) or
/// Commands alone (with this unchecked) is inert by design - the checkbox itself
/// stays togglable either way, only the *effect* is gated.
///
/// Sets via the uint overload (0u/1u) rather than the bool overload: reading
/// Dalamud's own GameConfigSection source, the uint/float/string setters marshal
/// onto the framework thread (RunOnFrameworkThread) but the bool overload
/// doesn't. Both HandleCommand and DrawRow here already run on the framework
/// thread in Dalamud's normal execution model, so this isn't fixing a live bug -
/// it's cheap insurance against that asymmetry ever mattering if this code is
/// ever called from somewhere that isn't.
/// </summary>
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

    // "limit" only shows up (and only works, see HandleCommand below) once
    // EffectivelyEnabled - while off there's nothing beyond the plain
    // on/off/toggle to advertise.
    public string[] UsageLines =>
        EffectivelyEnabled
            ? [$"/Nonuglon {CommandName} <on|off|toggle>", $"/Nonuglon {CommandName} limit <on|off|toggle>"]
            : [$"/Nonuglon {CommandName} <on|off|toggle>"];

    /// <summary>True only when both this mini-tweak AND Commands itself (the
    /// master switch) are on - the actual gate the "limit" subcommand and
    /// checkbox check before touching the live game setting.</summary>
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
        // "limit" is only recognized as a subcommand at all once both this
        // mini-tweak and Commands are on - while disabled it falls straight
        // through to the plain on/off/toggle usage error below, exactly as if
        // "limit" were never a valid word here, rather than hinting that it
        // exists but needs something enabled first.
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
