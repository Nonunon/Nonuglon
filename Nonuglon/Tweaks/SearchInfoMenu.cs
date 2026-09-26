using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.Gui.ContextMenu;
using Dalamud.Plugin.Services;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using Nonuglon.Support;

namespace Nonuglon.Tweaks;

/// <summary>Ported from a friend's decompile of the standalone "SearchInfoMenu"
/// plugin (not the original source), reworked onto TweakBase/Svc. Adds "View
/// Search Info" to the right-click menu on other players, opening the game's
/// Search Info window directly without targeting/Examine first.
///
/// AgentDetail.OpenForCharacterData wants an InfoProxyCommonList.CharacterData*,
/// not a GameObject/Character pointer, so one is synthesized from the target's
/// public fields plus their native Character* (for ContentId/AccountId/Sex,
/// not exposed on IPlayerCharacter). It keeps reading from that pointer for a
/// few frames after the call returns, hence RetainedCharacterData below rather
/// than freeing immediately.</summary>
public sealed unsafe class SearchInfoMenu : TweakBase
{
    public override string Name => "Search Info Menu";
    public override string Description => "Adds \"View Search Info\" to the right-click context menu on other players, opening the game's Search Info window for them directly.";

    public override bool ConfigEnabled
    {
        get => Plugin.Configuration.SearchInfoMenuEnabled;
        set => Plugin.Configuration.SearchInfoMenuEnabled = value;
    }

    public override string[] CommandNames => ["searchinfo", "searchinfomenu"];

    private struct RetainedCharacterData(nint address, int framesRemaining)
    {
        public nint Address = address;
        public int FramesRemaining = framesRemaining;
        public readonly unsafe InfoProxyCommonList.CharacterData* Pointer => (InfoProxyCommonList.CharacterData*)Address;
    }

    // How long to keep a native CharacterData buffer alive after handing it to
    // AgentDetail before freeing it - comfortably past the window in which
    // AgentDetail is still reading from it (~0.5s at 60fps).
    private const int RetainNativeBufferFrames = 30;

    private readonly List<RetainedCharacterData> retainedCharacterData = [];

    private readonly MenuItem menuItem = new()
    {
        Name = "View Search Info",
        PrefixChar = ContextMenuBranding.PrefixChar,
        PrefixColor = ContextMenuBranding.PrefixColor,
    };

    private SearchInfoChat2Ipc? chat2Ipc;

    protected override void Enable()
    {
        menuItem.OnClicked = OpenSearchInfo;
        Svc.ContextMenu.OnMenuOpened += OnContextMenuOpened;
        Svc.Framework.Update += OnFrameworkUpdate;
        SyncChat2Integration(tweakEnabled: true);
    }

    protected override void Disable()
    {
        Svc.ContextMenu.OnMenuOpened -= OnContextMenuOpened;
        Svc.Framework.Update -= OnFrameworkUpdate;
        FreeRetainedCharacterData();
        SyncChat2Integration(tweakEnabled: false);
    }

    /// <summary>Creates/tears down the Chat 2 IPC integration. Takes an explicit
    /// flag (same reasoning as AutoPillion.SyncIntegrations) since Enabled isn't
    /// true yet when Enable()/Disable() call this.</summary>
    public void SyncChat2Integration(bool tweakEnabled)
    {
        var want = tweakEnabled && Plugin.Configuration.SearchInfoMenuChat2ContextMenuEnabled;
        if (want && chat2Ipc is null) chat2Ipc = new SearchInfoChat2Ipc(this);
        else if (!want && chat2Ipc is not null) { chat2Ipc.Dispose(); chat2Ipc = null; }
    }

    public override void DrawOptions()
    {
        var config = Plugin.Configuration;

        ImGui.TextDisabled("Context menu integrations:");

        var chat2Enabled = config.SearchInfoMenuChat2ContextMenuEnabled;
        if (ImGui.Checkbox("Chat 2 Context Menu##SearchInfoMenuChat2ContextMenu", ref chat2Enabled))
        {
            config.SearchInfoMenuChat2ContextMenuEnabled = chat2Enabled;
            config.Save();
            SyncChat2Integration(tweakEnabled: Enabled);
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Right-click a name in Chat 2's own chat log, then look under Integrations for \"View Search Info\". Requires the Chat 2 plugin, and only works while the sender is actually nearby/rendered (same as the native right-click version).");

        if (chat2Enabled && !PluginDetection.IsPluginLoaded("ChatTwo"))
        {
            ImGui.PushTextWrapPos(ImGui.GetContentRegionAvail().X + ImGui.GetCursorPosX());
            ImGui.TextColored(UiColors.Warning, "Chat 2 not detected - this integration has no effect until it's installed and loaded.");
            ImGui.PopTextWrapPos();
        }
    }

    private void OnContextMenuOpened(IMenuOpenedArgs args)
    {
        if (args.MenuType == ContextMenuType.Default && args.Target is MenuTargetDefault { TargetObject: IPlayerCharacter player } && !IsLocalPlayer(player))
        {
            args.AddMenuItem(menuItem);
        }
    }

    private void OpenSearchInfo(IMenuItemClickedArgs args)
    {
        if (args.Target is not MenuTargetDefault { TargetObject: IPlayerCharacter player } || IsLocalPlayer(player))
            return;

        OpenSearchInfoFor(player);
    }

    /// <summary>Shared by the native right-click menu and SearchInfoChat2Ipc -
    /// both just need a live IPlayerCharacter.</summary>
    internal void OpenSearchInfoFor(IPlayerCharacter player)
    {
        var agentDetail = AgentDetail.Instance();
        var nativeCharacter = (Character*)player.Address;
        if (agentDetail == null || nativeCharacter == null)
        {
            Svc.Log.Warning($"[{Name}] Could not open Search Info for {player.Name}: agent=0x{(nint)agentDetail:X}, character=0x{(nint)nativeCharacter:X}.");
            return;
        }

        try
        {
            agentDetail->OpenForCharacterData(RetainCharacterData(BuildCharacterData(player, nativeCharacter)).Pointer, null);
        }
        catch (Exception exception)
        {
            Svc.Log.Error(exception, $"[{Name}] Failed to open Search Info for {player.Name}.");
        }
    }

    internal static bool IsLocalPlayer(IPlayerCharacter player)
    {
        var localPlayer = Svc.Objects.LocalPlayer;
        return localPlayer != null && player.GameObjectId == localPlayer.GameObjectId;
    }

    private static InfoProxyCommonList.CharacterData BuildCharacterData(IPlayerCharacter player, Character* nativeCharacter)
    {
        var data = new InfoProxyCommonList.CharacterData
        {
            ContentId = nativeCharacter->ContentId,
            AccountId = nativeCharacter->AccountId,
            State = InfoProxyCommonList.CharacterData.OnlineStatus.Online,
            CurrentWorld = (ushort)player.CurrentWorld.RowId,
            HomeWorld = (ushort)player.HomeWorld.RowId,
            ClientLanguage = InfoProxyCommonList.CharacterData.Language.None,
            Languages = InfoProxyCommonList.CharacterData.LanguageMask.None,
            Sex = nativeCharacter->Sex,
            Job = (byte)(player.ClassJob.RowId <= 255 ? (byte)player.ClassJob.RowId : 0),
        };
        WriteUtf8(player.Name.ToString(), data.Name);
        WriteUtf8(player.CompanyTag.ToString(), data.FCTag);
        return data;
    }

    private static void WriteUtf8(string value, Span<byte> destination)
    {
        destination.Clear();
        if (destination.Length != 0 && !string.IsNullOrWhiteSpace(value) && Encoding.UTF8.GetBytes(value.AsSpan(), destination) >= destination.Length)
            destination[destination.Length - 1] = 0;
    }

    private RetainedCharacterData RetainCharacterData(InfoProxyCommonList.CharacterData data)
    {
        var address = Marshal.AllocHGlobal(sizeof(InfoProxyCommonList.CharacterData));
        *(InfoProxyCommonList.CharacterData*)address = data;
        var retained = new RetainedCharacterData(address, RetainNativeBufferFrames);
        retainedCharacterData.Add(retained);
        return retained;
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        for (var i = retainedCharacterData.Count - 1; i >= 0; i--)
        {
            var retained = retainedCharacterData[i];
            retained.FramesRemaining--;
            if (retained.FramesRemaining > 0)
            {
                retainedCharacterData[i] = retained;
            }
            else
            {
                Marshal.FreeHGlobal(retained.Address);
                retainedCharacterData.RemoveAt(i);
            }
        }
    }

    private void FreeRetainedCharacterData()
    {
        foreach (var retained in retainedCharacterData)
            Marshal.FreeHGlobal(retained.Address);
        retainedCharacterData.Clear();
    }
}
