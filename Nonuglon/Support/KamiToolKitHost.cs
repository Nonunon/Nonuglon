using System;
using System.Threading.Tasks;
using Dalamud.Plugin;
using ECommons.DalamudServices;
using KamiToolKit;

namespace Nonuglon.Support;

/// <summary>Owns KamiToolKit's init/teardown for the whole plugin. Plugin is a
/// plain IDalamudPlugin (sync ctor), but KamiToolKitLibrary.InitializeAsync
/// hops onto the framework thread, so it's kicked off from a pool thread and
/// tweaks that need native nodes wait on <see cref="Ready"/>.</summary>
internal static class KamiToolKitHost
{
    public static Task Ready { get; private set; } = Task.CompletedTask;

    private static bool started;

    public static void Initialize(IDalamudPluginInterface pluginInterface)
    {
        started = true;
        Ready = Task.Run(() => KamiToolKitLibrary.InitializeAsync(pluginInterface));
        // Otherwise a failed init only surfaces at teardown.
        Ready.ContinueWith(t => Svc.Log.Error(t.Exception!, "KamiToolKit failed to initialize; native UI nodes are unavailable."),
            TaskContinuationOptions.OnlyOnFaulted);
    }

    /// <summary>True once init has finished and failed (not while still running).</summary>
    public static bool Failed => Ready.IsFaulted || Ready.IsCanceled;

    /// <summary>Call after every tweak using KamiToolKit has been disposed.</summary>
    public static void Dispose()
    {
        if (!started) return;
        started = false;

        try
        {
            if (Svc.Framework.IsInFrameworkUpdateThread)
            {
                // Init can't be waited on from here without deadlocking the thread it needs.
                if (Ready.IsCompleted) KamiToolKitLibrary.Dispose();
            }
            else
            {
                Ready.GetAwaiter().GetResult();
                Task.Run(KamiToolKitLibrary.DisposeAsync).GetAwaiter().GetResult();
            }
        }
        catch (Exception ex)
        {
            Svc.Log.Error(ex, "KamiToolKit teardown failed.");
        }
    }
}
