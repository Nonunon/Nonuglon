using System;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using ECommons;
using ECommons.EzIpcManager;
using Nonuglon.Support;

namespace Nonuglon.Tweaks;

/// <summary>Adds "View Search Info" to Chat 2's right-click menu, via the same
/// EzIPC point AutoPillionChat2Ipc uses. Only shows up for a rendered/nearby
/// player: opening Search Info needs their live Character* for Sex/AccountId,
/// which aren't in Chat 2's IPC payload.</summary>
public class SearchInfoChat2Ipc : IDisposable
{
    private readonly SearchInfoMenu owner;
    private string? currentId;

    [EzIPC] private Func<string> Register = null!;
    [EzIPC] private Action<string> Unregister = null!;

    public SearchInfoChat2Ipc(SearchInfoMenu owner)
    {
        this.owner = owner;
        EzIPC.Init(this, "ChatTwo", SafeWrapper.AnyException);
        Available();
    }

    [EzIPCEvent]
    private void Available() => currentId = Register();

    [EzIPCEvent]
    private void Invoke(string id, PlayerPayload? sender, ulong contentId, Payload? payload, SeString? senderString, SeString? content)
    {
        if (id != currentId || sender is null) return;

        if (GameObjectPillionExtensions.FindPlayerByNameAndWorld(sender.PlayerName, sender.World.RowId) is not IPlayerCharacter player
            || SearchInfoMenu.IsLocalPlayer(player))
            return;

        if (ImGui.Selectable($"[{ContextMenuBranding.PrefixChar}] View Search Info"))
            owner.OpenSearchInfoFor(player);
    }

    public void Dispose()
    {
        if (currentId != null) Unregister(currentId);
    }
}
