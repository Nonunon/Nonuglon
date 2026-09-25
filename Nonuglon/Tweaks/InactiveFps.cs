using System;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Config;
using ECommons.DalamudServices;
using static Nonuglon.Support.CommandText;

namespace Nonuglon.Tweaks;

/// <summary>
/// Chat-command toggle over the game's own System Configuration option
/// (https://dalamud.dev/api/Dalamud.Game.Config/Enums/SystemConfigOption/#fpsinactive)
/// - "Limit frame rate when client is inactive.". No hook to install: ConfigEnabled
/// here means "is this tweak allowed to touch that setting at all", backed by its
/// own Configuration field like every other tweak, deliberately kept separate from
/// the live game value itself - so enabling the tweak never implicitly flips the
/// game's setting, and the game's setting can't be changed through Nonuglon while
/// the tweak is off. The actual read/write happens only via the "limit" subcommand,
/// gated on Enabled.
/// </summary>
public class InactiveFps : TweakBase
{
    public override string Name => "Inactive Window FPS Throttle";
    public override string Description => "Toggle the game's own \"Limit frame rate when client is inactive.\" System Configuration setting.";

    public override bool ConfigEnabled
    {
        get => Plugin.Configuration.InactiveFpsEnabled;
        set => Plugin.Configuration.InactiveFpsEnabled = value;
    }

    public override string[] CommandNames => ["inactivefps"];

    public override string[] UsageLines =>
    [
        ..base.UsageLines,
        $"/Nonuglon {CommandNames[0]} limit <on|off|toggle>",
    ];

    // Nothing to hook or clean up - this tweak's "on" state only gates whether the
    // live game setting is reachable through Nonuglon, it doesn't install anything.
    protected override void Enable() { }
    protected override void Disable() { }

    private static bool TryGetLimit(out bool value) => Svc.GameConfig.TryGet(SystemConfigOption.FPSInActive, out value);

    public override void DrawOptions()
    {
        ImGui.BeginDisabled(!Enabled);

        if (!TryGetLimit(out var limitEnabled))
        {
            ImGui.TextDisabled("Could not read the game's current setting.");
        }
        else if (ImGui.Checkbox("Limit frame rate when client is inactive##InactiveFps", ref limitEnabled))
        {
            Svc.GameConfig.Set(SystemConfigOption.FPSInActive, limitEnabled);
        }

        ImGui.EndDisabled();
    }

    public override void HandleCommand(string[] args)
    {
        if (args.Length >= 1 && args[0].Equals("limit", StringComparison.OrdinalIgnoreCase))
        {
            if (!Enabled)
            {
                Print($"{Name} is off - enable it first with /Nonuglon {CommandNames[0]} on.");
                return;
            }

            if (!TryGetLimit(out var current) || args.Length < 2 || !ResolveBool(args[1], current, out var enabled))
            {
                Print($"Usage: /Nonuglon {CommandNames[0]} limit <on|off|toggle>");
                return;
            }

            Svc.GameConfig.Set(SystemConfigOption.FPSInActive, enabled);
            ReportStateChange($"{Name}: limit frame rate when inactive", current, enabled);
            return;
        }

        base.HandleCommand(args);
    }
}
