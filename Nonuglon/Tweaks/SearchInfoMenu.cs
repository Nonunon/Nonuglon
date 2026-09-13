using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.Gui.ContextMenu;
using Dalamud.Plugin.Services;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using Nonuglon.Support;

namespace Nonuglon.Tweaks;

/// <summary>
/// Ported from a friend's decompile of the standalone "SearchInfoMenu" plugin (not
/// the original source - reworked here onto TweakBase/Svc instead of its own
/// constructor-injected services). Adds "View Search Info" to the right-click
/// context menu on other players, opening the game's Search Info / Character
/// Look-up window (the one the Party Finder and Friend List use) for them directly,
/// without needing to target or Examine them first.
///
/// AgentDetail.OpenForCharacterData takes a pointer to an
/// InfoProxyCommonList.CharacterData - the struct the game's own social lists use
/// internally - rather than a GameObject/Character pointer, so one is synthesized
/// here from the target IPlayerCharacter's public fields plus their native
/// Character* (for ContentId, AccountId and Sex, which aren't exposed on
/// IPlayerCharacter). AgentDetail keeps reading from that pointer for a bit after
/// OpenForCharacterData returns rather than copying it in immediately, so the
/// native buffer is retained for a few frames (see RetainedCharacterData) instead
/// of being freed right away.
/// </summary>
public sealed unsafe class SearchInfoMenu : TweakBase
{
    public override string Name => "Search Info Menu";
    public override string Description => "Adds \"View Search Info\" to the right-click context menu on other players, opening the game's Search Info window for them directly.";

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

    protected override void Enable()
    {
        menuItem.OnClicked = OpenSearchInfo;
        Svc.ContextMenu.OnMenuOpened += OnContextMenuOpened;
        Svc.Framework.Update += OnFrameworkUpdate;
    }

    protected override void Disable()
    {
        Svc.ContextMenu.OnMenuOpened -= OnContextMenuOpened;
        Svc.Framework.Update -= OnFrameworkUpdate;
        FreeRetainedCharacterData();
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

    private static bool IsLocalPlayer(IPlayerCharacter player)
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
