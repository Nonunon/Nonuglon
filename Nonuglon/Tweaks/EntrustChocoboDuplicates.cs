using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using ECommons.Automation.NeoTaskManager;
using ECommons.DalamudServices;
using ECommons.ImGuiMethods;
using ECommons.UIHelpers.AddonMasterImplementations;
using FFXIVClientStructs.FFXIV.Client.Game;
using Dalamud.Interface.Utility;
using FFXIVClientStructs.FFXIV.Client.UI;
using KamiToolKit.Controllers;
using KamiToolKit.Nodes;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.Sheets;
using Nonuglon.Support;
using static ECommons.UIHelpers.AddonMasterImplementations.AddonMaster;

namespace Nonuglon.Tweaks;

/// <summary>Ported from PandorasBox's
/// (https://github.com/PunishXIV/PandorasBox, BSD-3-Clause)
/// Features/UI/EntrustChocoboDuplicates.cs, re-hosted on TweakBase, with the button
/// as a native KamiToolKit node instead of PandorasBox's ImGui overlay.</summary>
public unsafe class EntrustChocoboDuplicates : TweakBase
{
    public override string Name => "Saddlebag Duplicates";
    public override string Description => "Adds a button to the bottom of the AetherBags saddlebag window to entrust duplicates. Requires the AetherBags plugin to be installed and loaded.";

    public override bool ConfigEnabled
    {
        get => Plugin.Configuration.EntrustChocoboDuplicatesEnabled;
        set => Plugin.Configuration.EntrustChocoboDuplicatesEnabled = value;
    }

    public override string[] CommandNames => ["entrustchocobo", "entrust", "chocobo"];

    private const string AetherBagsPluginInternalName = "AetherBags";
    private const string AetherBagsSaddlebagAddonName = "AetherBags_SaddleBag";
    private const uint WindowNodeId = 2;
    private const float ReservedFooterStripHeight = 40f;
    private const float ButtonLeftInset = 10f;
    private const float ButtonLift = 10f;
    private static readonly Vector2 MinPopupSize = new(1f, 1f);
    private static readonly Vector2 ButtonSize = new(140f, 28f);

    private static readonly InventoryType[] PlayerInventory =
        [InventoryType.Inventory1, InventoryType.Inventory2, InventoryType.Inventory3, InventoryType.Inventory4];
    private static readonly InventoryType[] Saddlebag =
        [InventoryType.SaddleBag1, InventoryType.SaddleBag2, InventoryType.PremiumSaddleBag1, InventoryType.PremiumSaddleBag2];

    private readonly TaskManager taskManager = new();

    /// <summary>Public so ConfigWindow can show a hint when AetherBags isn't
    /// detected, instead of the tweak's button just silently never appearing.</summary>
    public static bool IsAetherBagsAvailable => PluginDetection.IsPluginLoaded(AetherBagsPluginInternalName);

    private AddonController? controller;
    private TextButtonNode? button;
    private bool wantEnabled;

    protected override void Enable()
    {
        wantEnabled = true;
        Svc.PluginInterface.UiBuilder.Draw += Draw;
        ApplyMode();
    }

    private static bool UseNativeButton => Plugin.Configuration.EntrustChocoboNativeButton;

    // Native (KamiToolKit) or ImGui button, per config; the other one is torn down.
    private void ApplyMode()
    {
        if (!UseNativeButton)
        {
            DisposeNative();
            return;
        }

        // KamiToolKit finishes initializing asynchronously after plugin load.
        KamiToolKitHost.Ready.ContinueWith(_ => Svc.Framework.RunOnFrameworkThread(CreateController),
            TaskContinuationOptions.OnlyOnRanToCompletion);
    }

    private void DisposeNative()
    {
        controller?.Dispose();
        controller = null;
        DisposeButton();
    }

    protected override void Disable()
    {
        wantEnabled = false;
        Svc.PluginInterface.UiBuilder.Draw -= Draw;
        DisposeNative();
        if (taskManager.NumQueuedTasks > 0)
            taskManager.Abort();
    }

    private void CreateController()
    {
        if (!wantEnabled || !UseNativeButton || controller != null) return;

        controller = new AddonController
        {
            AddonName = AetherBagsSaddlebagAddonName,
            OnSetup = OnSetup,
            OnFinalize = _ => DisposeButton(),
            OnUpdate = OnUpdate,
        };
        controller.Enable();
    }

    private void OnSetup(AtkUnitBase* addon)
    {
        DisposeButton();
        button = new TextButtonNode
        {
            String = "Entrust Duplicates",
            Size = ButtonSize,
            OnClick = EnqueueEntrustDuplicates,
        };
        Place(addon);
        button.AttachNode(addon);
    }

    // The window can be resized, so keep the button on its bottom edge.
    private void OnUpdate(AtkUnitBase* addon) => Place(addon);

    private void Place(AtkUnitBase* addon)
    {
        if (button == null) return;
        var node = addon->GetNodeById(WindowNodeId);
        if (node == null) return;

        var scale = addon->Scale > 0f ? addon->Scale : 1f;
        var origin = new Vector2(node->ScreenX - addon->X, node->ScreenY - addon->Y) / scale;
        var y = origin.Y + node->Height - ReservedFooterStripHeight + (ReservedFooterStripHeight - ButtonSize.Y) / 2f - ButtonLift;
        button.Position = new Vector2(origin.X + ButtonLeftInset, y);
    }

    private void DisposeButton()
    {
        button?.Dispose();
        button = null;
    }

    public override void DrawOptions()
    {
        var native = UseNativeButton;
        if (ImGui.Checkbox("Native button (KamiToolKit)##EntrustNative", ref native))
        {
            Plugin.Configuration.EntrustChocoboNativeButton = native;
            Plugin.Configuration.Save();
            if (Enabled) ApplyMode();
        }
        ImGui.TextDisabled("Off draws the button with ImGui instead.");

        if (UseNativeButton && KamiToolKitHost.Failed)
        {
            ImGui.PushTextWrapPos(ImGui.GetContentRegionAvail().X + ImGui.GetCursorPosX());
            ImGui.TextColored(UiColors.Warning, "KamiToolKit failed to initialize (see /xllog), so the native button can't be shown. Turn this off to use the ImGui button.");
            ImGui.PopTextWrapPos();
        }

        if (IsAetherBagsAvailable) return;

        ImGui.PushTextWrapPos(ImGui.GetContentRegionAvail().X + ImGui.GetCursorPosX());
        ImGui.TextColored(UiColors.Warning, "AetherBags not detected - this tweak has no effect until it's installed and loaded.");
        ImGui.PopTextWrapPos();
    }

    public override bool HasWarning => Enabled && (!IsAetherBagsAvailable || (UseNativeButton && KamiToolKitHost.Failed));

    private void Draw()
    {
        if (UseNativeButton) return;

        var addonPtr = Svc.GameGui.GetAddonByName(AetherBagsSaddlebagAddonName).Address;
        if (addonPtr == nint.Zero) return;

        var addon = (AtkUnitBase*)addonPtr;
        if (addon == null || !addon->IsVisible || !addon->IsFullyLoaded())
            return;

        var node = addon->GetNodeById(WindowNodeId);
        if (node == null) return;

        var position = AtkNodeHelper.GetNodePosition(node);
        var scale = AtkNodeHelper.GetNodeScale(node);
        var windowSize = new Vector2(node->Width, node->Height) * scale;

        ImGuiHelpers.ForceNextWindowMainViewport();
        var pos = position;
        pos.Y += windowSize.Y - (ReservedFooterStripHeight * scale.Y);
        pos.X += 10f;
        ImGuiHelpers.SetNextWindowPosRelativeMainViewport(pos);

        ImGui.PushStyleColor(ImGuiCol.WindowBg, 0);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 0f.Scale());
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(3f.Scale(), 3f.Scale()));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(0f.Scale(), 0f.Scale()));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0f.Scale());
        ImGui.PushStyleVar(ImGuiStyleVar.WindowMinSize, MinPopupSize);
        ImGui.Begin($"###EntrustDuplicatesAB{node->NodeId}", ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoNavFocus
            | ImGuiWindowFlags.AlwaysUseWindowPadding | ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoSavedSettings);
        ImGui.SetWindowFontScale(scale.X);

        if (ImGui.Button("Entrust Duplicates"))
            EnqueueEntrustDuplicates();

        ImGui.End();
        ImGui.PopStyleVar(5);
        ImGui.PopStyleColor();
    }

    private void EnqueueEntrustDuplicates()
    {
        // A second click mid-run would queue every slot again behind the first.
        if (taskManager.IsBusy) return;

        var inv = InventoryManager.Instance();
        var saddlebagIds = CollectSaddlebagItemIds(inv);
        foreach (var inventory in PlayerInventory)
        {
            var container = inv->GetInventoryContainer(inventory);
            for (var i = 0; i < container->Size; i++)
            {
                var item = container->GetInventorySlot(i);
                if (item->ItemId == 0) continue;
                if (!saddlebagIds.Contains(item->ItemId)) continue;

                // One task per inventory slot, however many
                // matching stacks the saddlebag holds: the slot is empty after
                // the first entrust.
                var slot = item->Slot;
                taskManager.EnqueueDelay(200);
                taskManager.Enqueue(() => FireInventoryMenu(inventory, slot, 56));
            }
        }
    }

    /// <summary>Non-unique item ids in the saddlebag, gathered once per click
    /// rather than rescanning it for every inventory slot.</summary>
    private static HashSet<uint> CollectSaddlebagItemIds(InventoryManager* inv)
    {
        var itemSheet = Svc.Data.GetExcelSheet<Item>();
        var ids = new HashSet<uint>();
        foreach (var saddlebagType in Saddlebag)
        {
            var saddleContainer = inv->GetInventoryContainer(saddlebagType);
            for (var p = 0; p < saddleContainer->Size; p++)
            {
                var itemId = saddleContainer->GetInventorySlot(p)->ItemId;
                if (itemId == 0 || ids.Contains(itemId)) continue;
                if (itemSheet.GetRow(itemId).IsUnique) continue;
                ids.Add(itemId);
            }
        }
        return ids;
    }

    // Open and select in one task, same as PandorasBox: the ContextMenu addon
    // is already there right after OpenForItemSlot, and splitting this into a
    // retrying second task just left the TaskManager stuck when it never fired.
    private static void FireInventoryMenu(InventoryType inventory, int slot, int eventId)
    {
        var ag = AgentInventoryContext.Instance();
        ag->OpenForItemSlot(inventory, slot, 0, AgentModule.Instance()->GetAgentByInternalId(AgentId.Inventory)->GetAddonId());
        var contextMenuPtr = Svc.GameGui.GetAddonByName("ContextMenu", 1).Address;
        if (contextMenuPtr == nint.Zero)
        {
            Svc.Log.Verbose($"[Entrust] {inventory} slot {slot}: ContextMenu addon not found right after OpenForItemSlot.");
            return;
        }
        var contextMenu = (AtkUnitBase*)contextMenuPtr;

        var entries = new AddonMaster.ContextMenu(contextMenu).Entries;
        Svc.Log.Verbose($"[Entrust] {inventory} slot {slot}: atkValues={contextMenu->AtkValuesCount}, entries=[{string.Join(" | ", entries.Select((x, n) => $"{n}:{x.Text}"))}]");

        for (var e = 0; e < contextMenu->AtkValuesCount; e++)
        {
            if (ag->EventIds[e] == eventId)
            {
                var first = entries.First();
                Svc.Log.Verbose($"[Entrust] {inventory} slot {slot}: event {eventId} found at atk value {e} (would be entry {e - 7}); selecting entry 0 \"{first.Text}\".");
                first.Select();
                return;
            }
        }

        Svc.Log.Verbose($"[Entrust] {inventory} slot {slot}: event {eventId} not found in EventIds[0..{contextMenu->AtkValuesCount}); nothing selected.");
    }
}
