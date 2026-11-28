using System;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin;
using ECommons.Automation.NeoTaskManager;
using ECommons.DalamudServices;
using ECommons.Throttlers;
using ECommons.UIHelpers.AddonMasterImplementations;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit.Controllers;
using KamiToolKit.Nodes;
using Nonuglon.Support;

namespace Nonuglon.Tweaks;

/// <summary>Ported from PandorasBox's
/// (https://github.com/PunishXIV/PandorasBox, BSD-3-Clause)
/// Features/UI/ExtractAll.cs, re-hosted on TweakBase, with the button as a native
/// KamiToolKit node instead of PandorasBox's ImGui overlay. Same extraction flow:
/// per Materialize tab, one switch/extract/confirm round per fully bonded item,
/// with YesAlready paused. While on, it can switch Pandora's own copy off over
/// IPC (runtime only, and back on when this turns off) so the two don't stack.</summary>
public unsafe class ExtractAllMateria : TweakBase
{
    public override string Name => "Extract All Materia";
    public override string Description => "Adds an \"Extract All\" button to the Materia Extraction window that extracts materia from every fully spiritbonded item, tab by tab.";
    public override TweakCategory Category => TweakCategory.Items;

    public override bool ConfigEnabled
    {
        get => Plugin.Configuration.ExtractAllMateriaEnabled;
        set => Plugin.Configuration.ExtractAllMateriaEnabled = value;
    }

    public override string[] CommandNames => ["extractall", "materia"];

    private const string AddonName = "Materialize";
    private const string DialogName = "MaterializeDialog";
    private const string PandoraFeature = "ExtractAll";
    private const int ReplacedNodeIndex = 2;
    private const ushort FullSpiritbond = 10000;
    private const string IdleText = "Extract All";
    private const string BusyText = "Extracting. Click to abort.";

    // Materialize's tabs in order, as upstream walks them (no waist slot any more).
    private static readonly InventoryType[][] Tabs =
    [
        [InventoryType.EquippedItems],
        [InventoryType.ArmoryOffHand, InventoryType.ArmoryMainHand],
        [InventoryType.ArmoryHead, InventoryType.ArmoryBody, InventoryType.ArmoryHands],
        [InventoryType.ArmoryLegs, InventoryType.ArmoryFeets],
        [InventoryType.ArmoryEar, InventoryType.ArmoryNeck],
        [InventoryType.ArmoryWrist, InventoryType.ArmoryRings],
        [InventoryType.Inventory1, InventoryType.Inventory2, InventoryType.Inventory3, InventoryType.Inventory4],
    ];

    private readonly TaskManager taskManager = new();
    private AddonController? controller;
    private TextButtonNode? button;
    private bool wantEnabled;
    private bool extracting;

    // Set when we switched Pandora's copy off, so turning this off gives it back.
    private bool pandoraSuppressed;
    private bool pandoraStillOn;
    private long nextPandoraCheck;

    protected override void Enable()
    {
        wantEnabled = true;
        Svc.PluginInterface.ActivePluginsChanged += OnActivePluginsChanged;
        CheckPandora();
        KamiToolKitHost.Ready.ContinueWith(_ => Svc.Framework.RunOnFrameworkThread(CreateController),
            TaskContinuationOptions.OnlyOnRanToCompletion);
    }

    protected override void Disable()
    {
        wantEnabled = false;
        Svc.PluginInterface.ActivePluginsChanged -= OnActivePluginsChanged;
        StopExtracting();
        controller?.Dispose();
        controller = null;
        DisposeButton();
        RestoreReplacedNode();
        RestorePandora();
    }

    public override void Dispose()
    {
        base.Dispose();
        YesAlreadyLock.Dispose();
    }

    public override bool HasWarning => Enabled && (KamiToolKitHost.Failed || pandoraStillOn);

    // -- Native button --

    private void CreateController()
    {
        if (!wantEnabled || controller != null) return;
        controller = new AddonController
        {
            AddonName = AddonName,
            OnSetup = OnSetup,
            OnFinalize = _ =>
            {
                StopExtracting();
                DisposeButton();
            },
            OnUpdate = OnUpdate,
        };
        controller.Enable();
    }

    private void OnSetup(AtkUnitBase* addon)
    {
        DisposeButton();
        button = new TextButtonNode
        {
            String = IdleText,
            OnClick = OnClick,
        };
        Place(addon);
        button.AttachNode(addon);
    }

    private void OnUpdate(AtkUnitBase* addon)
    {
        Place(addon);

        // A task that timed out aborts the queue before its final Stop task runs.
        if (extracting && !taskManager.IsBusy) StopExtracting();

        // Pandora may load (or be re-enabled in its window) after us.
        if (Environment.TickCount64 >= nextPandoraCheck)
        {
            nextPandoraCheck = Environment.TickCount64 + 1000;
            CheckPandora();
        }
    }

    // Takes the place of the node upstream hides, at its size.
    private void Place(AtkUnitBase* addon)
    {
        if (button == null || addon->UldManager.NodeListCount <= ReplacedNodeIndex) return;
        var node = addon->UldManager.NodeList[ReplacedNodeIndex];
        if (node == null) return;
        if (node->IsVisible()) node->ToggleVisibility(false);

        var scale = addon->Scale > 0f ? addon->Scale : 1f;
        button.Position = new Vector2(node->ScreenX - addon->X, node->ScreenY - addon->Y) / scale;
        button.Size = new Vector2(node->Width, node->Height);
    }

    private void RestoreReplacedNode()
    {
        var addon = (AtkUnitBase*)Svc.GameGui.GetAddonByName(AddonName).Address;
        if (addon == null || addon->UldManager.NodeListCount <= ReplacedNodeIndex) return;
        var node = addon->UldManager.NodeList[ReplacedNodeIndex];
        if (node != null) node->ToggleVisibility(true);
    }

    private void DisposeButton()
    {
        button?.Dispose();
        button = null;
    }

    private void OnClick()
    {
        if (extracting) StopExtracting();
        else StartExtracting();
    }

    private void SetBusy(bool busy)
    {
        extracting = busy;
        if (button != null) button.String = busy ? BusyText : IdleText;
    }

    // -- Extraction (upstream's task flow) --

    private void StartExtracting()
    {
        SetBusy(true);
        var inv = InventoryManager.Instance();
        taskManager.Enqueue(YesAlreadyLock.Lock, "ExtractAll: pause YesAlready");

        for (var tab = 0; tab < Tabs.Length; tab++)
        {
            var count = 0;
            foreach (var type in Tabs[tab])
            {
                var container = inv->GetInventoryContainer(type);
                if (container == null) continue;
                for (var i = 0; i < container->Size; i++)
                    if (container->GetInventorySlot(i)->SpiritbondOrCollectability == FullSpiritbond) count++;
            }

            var tabIndex = tab;
            for (var i = 0; i < count; i++)
            {
                taskManager.Enqueue(() => SwitchTab(tabIndex), "ExtractAll: switch tab");
                taskManager.Enqueue(ExtractFirst, "ExtractAll: extract");
                taskManager.Enqueue(ConfirmDialog, "ExtractAll: confirm");
            }
        }

        taskManager.Enqueue(StopExtracting, "ExtractAll: done");
    }

    private void StopExtracting()
    {
        if (taskManager.IsBusy) taskManager.Abort();
        YesAlreadyLock.Unlock();
        SetBusy(false);
    }

    private static bool Busy => Svc.Condition[ConditionFlag.Occupied39];

    private static AtkUnitBase* GetAddon(string name)
    {
        var addon = (AtkUnitBase*)Svc.GameGui.GetAddonByName(name).Address;
        return addon != null && addon->IsVisible ? addon : null;
    }

    private bool? SwitchTab(int tabIndex)
    {
        if (Busy) return false;
        taskManager.InsertMulti([new(() => EzThrottler.Throttle("Nonuglon.ExtractAll.Switch", 300)), new(() => EzThrottler.Check("Nonuglon.ExtractAll.Switch"))]);

        var addon = GetAddon(AddonName);
        if (addon == null) return false;
        var values = stackalloc AtkValue[2];
        values[0] = new AtkValue { Type = AtkValueType.Int, Int = 1 };
        values[1] = new AtkValue { Type = AtkValueType.Int, Int = tabIndex };
        addon->FireCallback(2, values);
        return true;
    }

    private bool? ExtractFirst()
    {
        if (Busy) return false;
        taskManager.InsertMulti([new(() => EzThrottler.Throttle("Nonuglon.ExtractAll.Extract", 100)), new(() => EzThrottler.Check("Nonuglon.ExtractAll.Extract"))]);

        var addon = GetAddon(AddonName);
        if (addon == null) return true;
        var values = stackalloc AtkValue[2];
        values[0] = new AtkValue { Type = AtkValueType.Int, Int = 2 };
        values[1] = new AtkValue { Type = AtkValueType.UInt, UInt = 0 };
        addon->FireCallback(2, values);
        return true;
    }

    private bool? ConfirmDialog()
    {
        if (Busy) return false;
        if (GetAddon(AddonName) == null) return true;
        var dialog = GetAddon(DialogName);
        if (dialog == null) return true;

        new AddonMaster.MaterializeDialog((nint)dialog).Materialize();
        taskManager.InsertMulti([new(() => EzThrottler.Throttle("Nonuglon.ExtractAll.Confirm", 100)), new(() => EzThrottler.Check("Nonuglon.ExtractAll.Confirm"))]);
        return true;
    }

    // -- Pandora's Box handoff --

    // Only Pandora's own load/unload matters; any other plugin changing is noise.
    private void OnActivePluginsChanged(IActivePluginsChangedEventArgs args)
    {
        if (args.AffectedInternalNames.Contains(PluginDetection.PandorasBoxInternalName)) CheckPandora();
    }

    private void CheckPandora()
    {
        if (!Plugin.Configuration.ExtractAllMateriaDisablePandora)
        {
            pandoraStillOn = false;
            return;
        }
        if (PandorasBoxIpc.GetFeatureEnabled(PandoraFeature) != true)
        {
            pandoraStillOn = false;
            return;
        }

        pandoraStillOn = !PandorasBoxIpc.SetFeatureEnabled(PandoraFeature, false);
        if (pandoraStillOn)
        {
            Svc.Log.Warning($"[{Name}] Couldn't switch off Pandora's Box's Extract All Materia.");
            return;
        }
        pandoraSuppressed = true;
        Svc.Log.Information($"[{Name}] Switched off Pandora's Box's Extract All Materia while this is on.");
    }

    private void RestorePandora()
    {
        pandoraStillOn = false;
        if (!pandoraSuppressed) return;
        pandoraSuppressed = false;
        if (PandorasBoxIpc.SetFeatureEnabled(PandoraFeature, true))
            Svc.Log.Information($"[{Name}] Switched Pandora's Box's Extract All Materia back on.");
    }

    // -- Config UI --

    public override void DrawOptions()
    {
        var disablePandora = Plugin.Configuration.ExtractAllMateriaDisablePandora;
        if (ImGui.Checkbox("Turn off Pandora's Box's Extract All Materia while this is on##ExtractAllMateria", ref disablePandora))
        {
            Plugin.Configuration.ExtractAllMateriaDisablePandora = disablePandora;
            Plugin.Configuration.Save();
            if (Enabled)
            {
                if (disablePandora) CheckPandora();
                else RestorePandora();
            }
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Only for this session: Pandora's own setting isn't changed, and it's switched back on when this tweak turns off.");

        ImGui.PushTextWrapPos(ImGui.GetContentRegionAvail().X + ImGui.GetCursorPosX());
        if (KamiToolKitHost.Failed)
            ImGui.TextColored(UiColors.Warning, "KamiToolKit failed to initialize (see /xllog), so the button can't be added.");
        if (pandoraStillOn)
            ImGui.TextColored(UiColors.Warning, "Pandora's Box's Extract All Materia is on and couldn't be switched off, so both buttons may show.");
        else if (pandoraSuppressed)
            ImGui.TextDisabled("Pandora's Box's Extract All Materia is switched off for now.");
        ImGui.PopTextWrapPos();
    }
}
