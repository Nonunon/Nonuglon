using System;
using System.Linq;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using ECommons;
using ECommons.EzIpcManager;
using Nonuglon.Support;

namespace Nonuglon.Tweaks;

/// <summary>Adds "Add to Auto Pillion" (or "Remove from") to Chat 2's own right-click menu,
/// via Chat 2's EzIPC hook (Register/Unregister/Invoke) rather than Dalamud's
/// normal OnMenuOpened, since Chat 2 renders its own chat log outside the native
/// addons. SafeWrapper.AnyException means EzIPC.Init leaves Register/Unregister
/// unresolved (not throwing) when Chat 2 isn't installed, so this no-ops
/// harmlessly. Ported from HuntTrainAssistant's
/// (https://github.com/NightmareXIV/HuntTrainAssistant - a Dalamud plugin, so
/// bound by Dalamud's own AGPL-3.0 regardless of its lack of a LICENSE file)
/// Services/Chat2IPC.cs.</summary>
public class AutoPillionChat2Ipc : IDisposable
{
    private string? currentId;
    private readonly EzIPCDisposalToken[] ipcTokens;

    [EzIPC] private Func<string> Register = null!;
    [EzIPC] private Action<string> Unregister = null!;

    public AutoPillionChat2Ipc()
    {
        ipcTokens = EzIPC.Init(this, PluginDetection.Chat2InternalName, SafeWrapper.AnyException);
        // In case Chat 2 was already loaded; Available() re-fires this too.
        Available();
    }

    [EzIPCEvent]
    private void Available() => currentId = Register();

    [EzIPCEvent]
    private void Invoke(string id, PlayerPayload? sender, ulong contentId, Payload? payload, SeString? senderString, SeString? content)
    {
        if (id != currentId || sender is null) return;

        var worldId = sender.World.RowId;
        if (worldId == 0) return;

        // Same Add/Remove switch as the native menu.
        var saved = AutoPillion.IsFavorite(sender.PlayerName, worldId);
        if (!ImGui.Selectable($"[{ContextMenuBranding.PrefixChar}] {(saved ? "Remove from" : "Add to")} Auto Pillion")) return;
        if (saved) AutoPillion.RemoveFavoriteAndReport(sender.PlayerName, worldId);
        else AutoPillion.AddFavoriteAndReport(sender.PlayerName, worldId);
    }

    public void Dispose()
    {
        if (currentId != null) Unregister(currentId);
        foreach (var token in ipcTokens)
            token.Dispose();
    }
}
