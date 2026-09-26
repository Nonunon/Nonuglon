using System;
using ECommons.DalamudServices;
using static Nonuglon.Support.CommandText;

namespace Nonuglon.Tweaks;

/// <summary>Minimal base for a self-contained tweak: its own on/off switch,
/// hooks itself into the game, cleans up after itself. Deliberately lighter
/// than ffxiv-bundleoftweaks' Tweak&lt;T&gt; system - no reflection-driven
/// config UI, no attribute-based hook generation.</summary>
public abstract class TweakBase : IDisposable
{
    public abstract string Name { get; }
    public abstract string Description { get; }
    public bool Enabled { get; private set; }

    /// <summary>Persisted on/off flag, backed by its own Configuration property.
    /// Owning it here (not in Plugin.cs/ConfigWindow.cs) is what lets
    /// EnableTweak, the config checkbox, and HandleCommand share one code path.</summary>
    public abstract bool ConfigEnabled { get; set; }

    /// <summary>Chat command names routing "/Nonuglon &lt;name&gt; ..." to
    /// HandleCommand; first entry is canonical, rest are aliases. Empty by
    /// default - still toggleable from the config window, just not chat.</summary>
    public virtual string[] CommandNames => [];

    /// <summary>Usage line(s) shown by "/Nonuglon help". Override alongside
    /// HandleCommand to document extra subcommands - see AutoPillion.</summary>
    public virtual string[] UsageLines => CommandNames.Length == 0 ? [] : [$"/Nonuglon {CommandNames[0]} <on|off|toggle>"];

    /// <summary>Handles "/Nonuglon &lt;name&gt; ..." - args excludes the command
    /// name itself. Default is a plain on/off/toggle over ConfigEnabled;
    /// override for extra subcommands, falling back to base.HandleCommand.</summary>
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

    /// <summary>Draws this tweak's extra options in the config window, indented
    /// under its checkbox. Default no-op; keeps ConfigWindow.cs tweak-agnostic.</summary>
    public virtual void DrawOptions() { }

    /// <summary>True when enabled but not actually working (e.g. a required
    /// companion plugin isn't loaded) - drives a warning-colored status dot.
    /// Note: Enable() must not throw on the condition this checks, or Enabled
    /// stays false and this becomes unreachable (see EstateTeleportation.cs).</summary>
    public virtual bool HasWarning => false;

    public virtual void Dispose() => DisableTweak();
}
