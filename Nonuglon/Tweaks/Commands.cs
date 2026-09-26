using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Bindings.ImGui;

namespace Nonuglon.Tweaks;

/// <summary>
/// Master switch for a grab-bag of tiny, single-setting "mini-tweaks" too small
/// to justify their own sidebar entry (see IMiniTweak.cs) - currently just
/// InactiveFps. Turning Commands off disables every mini-tweak's actual effect
/// at once without losing their individual on/off state (each keeps its own
/// Configuration flag, checkable/uncheckable regardless of Commands' state);
/// turning Commands back on picks up exactly whichever ones were individually
/// checked. Each mini-tweak is also reachable as a standalone
/// "/Nonuglon &lt;name&gt; ..." alias (wired up in Plugin.cs) in addition to
/// "/Nonuglon commands &lt;name&gt; ...".
/// </summary>
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

    // Nothing to hook - Commands itself has no behavior beyond gating the
    // mini-tweaks it hosts, which each check Plugin.Configuration.CommandsEnabled
    // directly rather than holding a reference back to this instance.
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
        // Nested "/Nonuglon commands <name> ..." routing only exists while
        // Commands itself is on - while off, this falls straight through to the
        // plain on/off/toggle usage error below, exactly as if no mini-tweak
        // name were ever valid here. Each mini-tweak's own standalone alias
        // (e.g. "/Nonuglon inactivefps ...", wired up separately in Plugin.cs)
        // is unaffected by this - it's that mini-tweak's own direct head command.
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
}
