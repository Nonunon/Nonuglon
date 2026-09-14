using System;
using ECommons.DalamudServices;
using static Nonuglon.Support.CommandText;

namespace Nonuglon.Tweaks;

/// <summary>
/// Minimal base class for a self-contained tweak: something with its own on/off switch
/// that hooks itself into the game and cleans up after itself.
///
/// Deliberately much lighter than ffxiv-bundleoftweaks' Tweak&lt;T&gt; system - no
/// reflection-driven config UI, no IPC requirement graph, no attribute-based hook
/// generation. Just enough plumbing to host a handful of hand-ported tweaks.
/// </summary>
public abstract class TweakBase : IDisposable
{
    public abstract string Name { get; }
    public abstract string Description { get; }
    public bool Enabled { get; private set; }

    /// <summary>The persisted on/off flag for this tweak, backed by its own
    /// Configuration property. Owning this here - instead of Plugin.cs and
    /// ConfigWindow.cs each separately knowing which Configuration field belongs to
    /// which tweak - is what lets EnableTweaksFromConfig, ConfigWindow's checkbox,
    /// and HandleCommand below all share one code path instead of three
    /// hand-synced ones.</summary>
    public abstract bool ConfigEnabled { get; set; }

    /// <summary>Chat command names (e.g. "searchinfo") that route "/Nonuglon &lt;name&gt; ..."
    /// to this tweak's HandleCommand, checked case-insensitively. The first entry is
    /// the canonical name used in generated usage text; any others are aliases.
    /// Empty by default - a tweak with no entries here can still be toggled from the
    /// config window, just not from chat.</summary>
    public virtual string[] CommandNames => [];

    /// <summary>Usage line(s) for this tweak shown by "/Nonuglon help", generated
    /// from CommandNames by default. Override alongside HandleCommand to document
    /// any extra subcommands - see AutoPillion/InstantReturn.</summary>
    public virtual string[] UsageLines => CommandNames.Length == 0 ? [] : [$"/Nonuglon {CommandNames[0]} <on|off|toggle>"];

    /// <summary>Handles "/Nonuglon &lt;CommandNames[n]&gt; ..." for this tweak - args
    /// excludes the command name itself, so args[0] is whatever came right after it
    /// (if anything). Default implementation is a plain on/off/toggle switch over
    /// ConfigEnabled; override to add extra subcommands, falling back to
    /// base.HandleCommand(args) for the plain toggle case.</summary>
    public virtual void HandleCommand(string[] args)
    {
        if (args.Length == 0 || !ResolveBool(args[0], ConfigEnabled, out var enabled))
        {
            Print($"Usage: /Nonuglon {CommandNames[0]} <on|off|toggle>");
            return;
        }

        var previous = ConfigEnabled;
        ConfigEnabled = enabled;
        Plugin.Configuration.Save();
        if (enabled) EnableTweak();
        else DisableTweak();
        ReportStateChange(Name, previous, enabled);
    }

    public void EnableTweak()
    {
        if (Enabled) return;

        try
        {
            Enable();
            Enabled = true;
        }
        catch (Exception ex)
        {
            // A single tweak failing to hook (e.g. Reloaded.Hooks can't find a nearby
            // trampoline slot) shouldn't take the whole plugin down with it.
            Svc.Log.Error(ex, $"[{Name}] Failed to enable, leaving it off.");
        }
    }

    public void DisableTweak()
    {
        if (!Enabled) return;

        try
        {
            Disable();
        }
        catch (Exception ex)
        {
            Svc.Log.Error(ex, $"[{Name}] Failed to disable cleanly.");
        }
        finally
        {
            Enabled = false;
        }
    }

    protected abstract void Enable();
    protected abstract void Disable();

    /// <summary>Draws this tweak's extra options (if any) in the config window,
    /// indented under its enable checkbox. Default no-op - tweaks with nothing
    /// beyond the on/off switch don't need to override this. Colocating each
    /// tweak's own options UI here (instead of hardcoding it in ConfigWindow) means
    /// adding a new tweak never requires touching ConfigWindow.cs.</summary>
    public virtual void DrawOptions() { }

    /// <summary>True when this tweak is enabled but not actually functioning as
    /// expected right now - e.g. a required companion plugin isn't loaded. Drives a
    /// distinct warning color on the status dot, separate from plain on/off.
    /// Default false; only override this if the tweak has some external
    /// dependency that can silently make "enabled" not mean "working".</summary>
    public virtual bool HasWarning => false;

    public virtual void Dispose() => DisableTweak();
}
