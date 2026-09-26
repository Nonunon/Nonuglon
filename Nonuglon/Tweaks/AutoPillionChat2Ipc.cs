using System;
using System.Linq;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using ECommons;
using ECommons.EzIpcManager;
using Nonuglon.Support;

namespace Nonuglon.Tweaks;

/// <summary>Adds "Add as Auto Pillion favorite" to Chat 2's own right-click menu,
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

    [EzIPC] private Func<string> Register = null!;
    [EzIPC] private Action<string> Unregister = null!;

    public AutoPillionChat2Ipc()
    {
        EzIPC.Init(this, "ChatTwo", SafeWrapper.AnyException);
        // In case Chat 2 was already loaded; Available() re-fires this too.
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
