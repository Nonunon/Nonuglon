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

/// <summary>
/// Ported from PandorasBox's Features/UI/EntrustChocoboDuplicatesAB.cs.
/// Re-hosted on TweakBase instead of PandorasBox's Feature/Overlays window system -
/// draws directly off UiBuilder.Draw instead of going through P.Ws.
/// </summary>
public unsafe class EntrustChocoboDuplicates : TweakBase
{
    public override string Name => "Saddlebag Entrust Duplicates (AetherBags)";
    public override string Description => "Adds a button to the bottom of the AetherBags saddlebag window to entrust duplicates. Requires the AetherBags plugin to be installed and loaded.";

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

    private static bool IsAetherBagsLoaded => PluginDetection.IsPluginLoaded(AetherBagsPluginInternalName);

    /// <summary>Public so ConfigWindow can show a hint when AetherBags isn't
    /// detected, instead of the tweak's button just silently never appearing.</summary>
    public static bool IsAetherBagsAvailable => IsAetherBagsLoaded;

    protected override void Enable() => Svc.PluginInterface.UiBuilder.Draw += Draw;

    protected override void Disable()
    {
        Svc.PluginInterface.UiBuilder.Draw -= Draw;
        if (taskManager.NumQueuedTasks > 0)
            taskManager.Abort();
    }

    private void Draw()
    {
        if (!IsAetherBagsLoaded) return;

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
        var oldFontScale = ImGui.GetFont().Scale;
        ImGui.GetFont().Scale *= scale.X;
        ImGui.PushFont(ImGui.GetFont());
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 0f.Scale());
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(3f.Scale(), 3f.Scale()));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(0f.Scale(), 0f.Scale()));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0f.Scale());
        ImGui.PushStyleVar(ImGuiStyleVar.WindowMinSize, MinPopupSize);
        ImGui.Begin($"###EntrustDuplicatesAB{node->NodeId}", ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoNavFocus
            | ImGuiWindowFlags.AlwaysUseWindowPadding | ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoSavedSettings);

        if (ImGui.Button("Entrust Duplicates"))
            EnqueueEntrustDuplicates();

        ImGui.End();
        ImGui.PopStyleVar(5);
        ImGui.GetFont().Scale = oldFontScale;
        ImGui.PopFont();
        ImGui.PopStyleColor();
    }

    private void EnqueueEntrustDuplicates()
    {
        var inv = InventoryManager.Instance();
        foreach (var inventory in PlayerInventory)
        {
            var container = inv->GetInventoryContainer(inventory);
            for (var i = 1; i <= container->Size; i++)
            {
                var item = container->GetInventorySlot(i - 1);
                if (item->ItemId == 0) continue;

                foreach (var saddlebagType in Saddlebag)
                {
                    var saddleContainer = inv->GetInventoryContainer(saddlebagType);
                    for (var p = 1; p <= saddleContainer->Size; p++)
                    {
                        var saddleItem = saddleContainer->GetInventorySlot(p - 1);
                        if (saddleItem->ItemId == 0) continue;

                        var saddleItemData = Svc.Data.GetExcelSheet<Item>().GetRow(saddleItem->ItemId);
                        if (saddleItemData.IsUnique) continue;

                        if (saddleItem->ItemId == item->ItemId)
                        {
                            taskManager.EnqueueDelay(200);
                            taskManager.Enqueue(() => FireInventoryMenu(inventory, item, 56));
                        }
                    }
                }
            }
        }
    }

    private static void FireInventoryMenu(InventoryType inventory, InventoryItem* item, int eventId)
    {
        var ag = AgentInventoryContext.Instance();
        ag->OpenForItemSlot(inventory, item->Slot, 0, AgentModule.Instance()->GetAgentByInternalId(AgentId.Inventory)->GetAddonId());
        var contextMenuPtr = Svc.GameGui.GetAddonByName("ContextMenu", 1).Address;
        if (contextMenuPtr == nint.Zero) return;
        var contextMenu = (AtkUnitBase*)contextMenuPtr;

        for (var e = 0; e < contextMenu->AtkValuesCount; e++)
        {
            if (ag->EventIds[e] == eventId)
            {
                new AddonMaster.ContextMenu(contextMenu).Entries.First().Select();
                return;
            }
        }
    }
}
