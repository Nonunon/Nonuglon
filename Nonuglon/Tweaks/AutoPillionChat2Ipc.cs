using System;
using System.Linq;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using ECommons;
using ECommons.EzIpcManager;
using Nonuglon.Support;

namespace Nonuglon.Tweaks;

/// <summary>Adds "Add as Auto Pillion favorite" to Chat 2's own right-click-a-
/// chat-message context menu, via Chat 2's EzIPC-based integration point. Chat 2
/// renders its own chat log outside the native game addons, so it doesn't fire
/// Dalamud's normal OnMenuOpened/AddMenuItem system that AutoPillionContextMenu
/// uses - it has its own IPC hook instead (Register/Unregister/Invoke, documented
/// in Chat 2's own IPC guide). No-ops harmlessly if Chat 2 isn't installed:
/// SafeWrapper.AnyException means EzIPC.Init just leaves Register/Unregister
/// unresolved instead of throwing, so Available()/Dispose() safely do nothing.
///
/// Ported from HuntTrainAssistant's Services/Chat2IPC.cs (NightmareXIV).</summary>
public class AutoPillionChat2Ipc : IDisposable
{
    private string? currentId;

    [EzIPC] private Func<string> Register = null!;
    [EzIPC] private Action<string> Unregister = null!;

    public AutoPillionChat2Ipc()
    {
        EzIPC.Init(this, "ChatTwo", SafeWrapper.AnyException);
        // Register immediately in case Chat 2 was already loaded before Auto
        // Pillion was enabled; Available() also re-fires this if Chat 2 loads or
        // updates afterward.
        Available();
    }

    [EzIPCEvent]
    private void Available() => currentId = Register();

    [EzIPCEvent]
    private void Invoke(string id, PlayerPayload? sender, ulong contentId, Payload? payload, SeString? senderString, SeString? content)
    {
        if (id != currentId || sender is null) return;

        if (ImGui.Selectable($"[{ContextMenuBranding.PrefixChar}] Add to AutoPillion"))
        {
            var favorites = Plugin.Configuration.AutoPillionFavorites;
            var worldId = sender.World.RowId;
            if (worldId != 0 && !favorites.Any(f => f.Name == sender.PlayerName && f.WorldId == worldId))
            {
                favorites.Add(new AutoPillionFavorite
                {
                    Name = sender.PlayerName,
                    WorldId = worldId,
                    WorldName = sender.World.ValueNullable?.Name.ExtractText() ?? string.Empty
                });
                Plugin.Configuration.Save();
            }
        }
    }

    public void Dispose()
    {
        if (currentId != null) Unregister(currentId);
    }
}
