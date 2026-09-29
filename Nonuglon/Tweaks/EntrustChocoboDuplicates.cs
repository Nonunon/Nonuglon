using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using ECommons.Automation.NeoTaskManager;
using ECommons.DalamudServices;
using ECommons.ImGuiMethods;
using ECommons.UIHelpers.AddonMasterImplementations;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.Sheets;
using Nonuglon.Support;
using static ECommons.UIHelpers.AddonMasterImplementations.AddonMaster;

namespace Nonuglon.Tweaks;

/// <summary>Ported from PandorasBox's
/// (https://github.com/PunishXIV/PandorasBox, BSD-3-Clause)
/// Features/UI/EntrustChocoboDuplicates.cs, re-hosted on TweakBase (draws off
/// UiBuilder.Draw directly) instead of PandorasBox's own window system.</summary>
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
    private static readonly Vector2 MinPopupSize = new(1f, 1f);

    private static readonly InventoryType[] PlayerInventory =
        [InventoryType.Inventory1, InventoryType.Inventory2, InventoryType.Inventory3, InventoryType.Inventory4];
    private static readonly InventoryType[] Saddlebag =
        [InventoryType.SaddleBag1, InventoryType.SaddleBag2, InventoryType.PremiumSaddleBag1, InventoryType.PremiumSaddleBag2];

    private readonly TaskManager taskManager = new();

    /// <summary>Public so ConfigWindow can show a hint when AetherBags isn't
    /// detected, instead of the tweak's button just silently never appearing.</summary>
    public static bool IsAetherBagsAvailable => PluginDetection.IsPluginLoaded(AetherBagsPluginInternalName);

    protected override void Enable() => Svc.PluginInterface.UiBuilder.Draw += Draw;

    protected override void Disable()
    {
        Svc.PluginInterface.UiBuilder.Draw -= Draw;
        if (taskManager.NumQueuedTasks > 0)
            taskManager.Abort();
    }

    public override void DrawOptions()
    {
        if (IsAetherBagsAvailable) return;

        ImGui.PushTextWrapPos(ImGui.GetContentRegionAvail().X + ImGui.GetCursorPosX());
        ImGui.TextColored(UiColors.Warning, "AetherBags not detected - this tweak has no effect until it's installed and loaded.");
        ImGui.PopTextWrapPos();
    }

    public override bool HasWarning => Enabled && !IsAetherBagsAvailable;

    private void Draw()
    {
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
        var inv = InventoryManager.Instance();
        var itemSheet = Svc.Data.GetExcelSheet<Item>();
        foreach (var inventory in PlayerInventory)
        {
            var container = inv->GetInventoryContainer(inventory);
            for (var i = 0; i < container->Size; i++)
            {
                var item = container->GetInventorySlot(i);
                if (item->ItemId == 0) continue;
                if (!HasSaddlebagMatch(inv, itemSheet, item->ItemId)) continue;

                // One task per inventory slot, however many
                // matching stacks the saddlebag holds: the slot is empty after
                // the first entrust.
                var slot = item->Slot;
                taskManager.EnqueueDelay(200);
                taskManager.Enqueue(() => FireInventoryMenu(inventory, slot, 56));
            }
        }
    }

    private static bool HasSaddlebagMatch(InventoryManager* inv, Lumina.Excel.ExcelSheet<Item> itemSheet, uint itemId)
    {
        foreach (var saddlebagType in Saddlebag)
        {
            var saddleContainer = inv->GetInventoryContainer(saddlebagType);
            for (var p = 0; p < saddleContainer->Size; p++)
            {
                var saddleItem = saddleContainer->GetInventorySlot(p);
                if (saddleItem->ItemId != itemId) continue;
                if (itemSheet.GetRow(saddleItem->ItemId).IsUnique) continue;
                return true;
            }
        }
        return false;
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
