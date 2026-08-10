using System;
using ECommons.DalamudServices;

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

    public virtual void Dispose() => DisableTweak();
}
