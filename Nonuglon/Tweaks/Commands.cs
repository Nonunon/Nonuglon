using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Bindings.ImGui;

namespace Nonuglon.Tweaks;

/// <summary>Master switch for tiny single-setting "mini-tweaks" (IMiniTweak.cs).
/// Turning this off disables every mini-tweak's effect without touching their
/// own Configuration flags, so re-enabling picks up what was checked before.
/// Each mini-tweak is also reachable as a standalone "/Nonuglon &lt;name&gt;
/// ..." alias (Plugin.cs), not just nested under "commands".</summary>
public class Commands : TweakBase
{
    public override string Name => "Commands";
    public override string Description => "A grab-bag of tiny one-setting toggles, too small to need their own tweak. Turning this off disables all of them at once without losing their individual on/off state.";

    public override bool ConfigEnabled
    {
        get => Plugin.Configuration.CommandsEnabled;
        set => Plugin.Configuration.CommandsEnabled = value;
    }

    public override string[] CommandNames => ["commands"];

    private readonly List<IMiniTweak> miniTweaks;

    public Commands(params IMiniTweak[] miniTweaks) => this.miniTweaks = miniTweaks.ToList();

    // Nothing to hook; mini-tweaks check Plugin.Configuration.CommandsEnabled directly.
    protected override void Enable() { }
    protected override void Disable() { }

    public override string[] UsageLines =>
    [
        ..base.UsageLines,
        ..miniTweaks.SelectMany(m => m.UsageLines),
    ];

    public override void DrawOptions()
    {
        ImGui.TextDisabled("Mini-tweaks:");
        foreach (var mini in miniTweaks)
            mini.DrawRow();
    }

    public override void HandleCommand(string[] args)
    {
        // Nested routing only exists while Commands is on; each mini-tweak's
        // own standalone alias (Plugin.cs) is unaffected by this.
        if (Enabled && args.Length >= 1)
        {
            var mini = miniTweaks.FirstOrDefault(m => m.CommandName.Equals(args[0], StringComparison.OrdinalIgnoreCase));
            if (mini != null)
            {
                mini.HandleCommand(args[1..]);
                return;
            }
        }

        base.HandleCommand(args);
    }

    public override void Dispose()
    {
        foreach (var mini in miniTweaks)
            mini.Dispose();
        base.Dispose();
    }
}
