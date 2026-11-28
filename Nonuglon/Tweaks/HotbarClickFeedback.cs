using System;
using System.Collections.Generic;
using System.Reflection;
using Dalamud.Bindings.ImGui;
using Dalamud.Hooking;
using Dalamud.Plugin.Ipc;
using ECommons.DalamudServices;
using FFXIVClientStructs.Attributes;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using Nonuglon.Support;
using static FFXIVClientStructs.FFXIV.Client.UI.Misc.RaptureHotbarModule;

namespace Nonuglon.Tweaks;

/// <summary>Plays the hotbar press animation (and click sound) for actions
/// fired by automation such as Wrath Combo's Auto-Rotation. Slot search and
/// pulse follow Rotation Solver Reborn's KeyboardNoise
/// (https://github.com/FFXIV-CombatReborn/RotationSolverReborn, MiscUpdater.cs,
/// HotbarAddonHelper.cs). Real presses go through RaptureHotbarModule's
/// ExecuteSlot(ById), which already animate, so those are skipped.</summary>
public unsafe class HotbarClickFeedback : TweakBase
{
    public override string Name => "Hotbar Click Feedback";
    public override string Description => "Plays the hotbar button press animation and click sound when an action is used by automation (like Wrath Combo's Auto-Rotation) instead of a real hotbar press.";
    public override TweakCategory Category => TweakCategory.DutiesAndCombat;

    public override bool ConfigEnabled
    {
        get => Plugin.Configuration.HotbarClickFeedbackEnabled;
        set => Plugin.Configuration.HotbarClickFeedbackEnabled = value;
    }

    public override string[] CommandNames => ["hotbarclick", "clickfeedback"];

    public override bool HasWarning => Enabled && Plugin.Configuration.HotbarClickFeedbackWrathOnly && !WrathLoaded;

    private const string WrathInternalName = "WrathCombo";
    private const uint ClickSoundEffect = 12;
    private const int StandardHotbarCount = 10;
    // A queued action can make UseAction return true again for the same id.
    private static readonly TimeSpan SameActionCooldown = TimeSpan.FromMilliseconds(600);
    private static readonly TimeSpan WrathStateCacheTime = TimeSpan.FromSeconds(1);

    // Order matters: 0-9 standard bars, 10 the cross bar, 11+ the double
    // cross bars (see RaptureIndex).
    private static readonly string[] AddonNames = GetAddonNames();

    private readonly Random random = new();
    private readonly Dictionary<uint, DateTime> lastPulseByAction = [];
    private ICallGateSubscriber<bool>? wrathAutoRotationState;
    private bool wrathStateCached;
    private DateTime wrathStateCheckedAt;

    private Hook<ActionManager.Delegates.UseAction>? useActionHook;
    private Hook<RaptureHotbarModule.Delegates.ExecuteSlot>? executeSlotHook;
    private Hook<RaptureHotbarModule.Delegates.ExecuteSlotById>? executeSlotByIdHook;
    private int realPressDepth;
    // Bumped on Disable so pulses still scheduled on RunOnTick stop.
    private int generation;

    private static bool WrathLoaded => PluginDetection.IsPluginLoaded(WrathInternalName);

    private readonly record struct SlotRef(int AddonIndex, int SlotIndex);

    protected override void Enable()
    {
        useActionHook ??= Svc.Hook.HookFromAddress<ActionManager.Delegates.UseAction>(
            (nint)ActionManager.MemberFunctionPointers.UseAction, UseActionDetour);
        executeSlotHook ??= Svc.Hook.HookFromAddress<RaptureHotbarModule.Delegates.ExecuteSlot>(
            (nint)RaptureHotbarModule.MemberFunctionPointers.ExecuteSlot, ExecuteSlotDetour);
        executeSlotByIdHook ??= Svc.Hook.HookFromAddress<RaptureHotbarModule.Delegates.ExecuteSlotById>(
            (nint)RaptureHotbarModule.MemberFunctionPointers.ExecuteSlotById, ExecuteSlotByIdDetour);

        realPressDepth = 0;
        executeSlotHook.Enable();
        executeSlotByIdHook.Enable();
        useActionHook.Enable();
    }

    protected override void Disable()
    {
        useActionHook?.Disable();
        executeSlotHook?.Disable();
        executeSlotByIdHook?.Disable();
        generation++;
        lastPulseByAction.Clear();
    }

    public override void Dispose()
    {
        base.Dispose();
        useActionHook?.Dispose();
        executeSlotHook?.Dispose();
        executeSlotByIdHook?.Dispose();
    }

    private byte ExecuteSlotDetour(RaptureHotbarModule* module, HotbarSlot* slot)
    {
        realPressDepth++;
        try { return executeSlotHook!.Original(module, slot); }
        finally { realPressDepth--; }
    }

    private byte ExecuteSlotByIdDetour(RaptureHotbarModule* module, uint hotbarId, uint slotId)
    {
        realPressDepth++;
        try { return executeSlotByIdHook!.Original(module, hotbarId, slotId); }
        finally { realPressDepth--; }
    }

    private bool UseActionDetour(ActionManager* manager, ActionType actionType, uint actionId, ulong targetId,
        uint extraParam, ActionManager.UseActionMode mode, uint comboRouteId, bool* outOptAreaTargeted)
    {
        var result = useActionHook!.Original(manager, actionType, actionId, targetId, extraParam, mode, comboRouteId, outOptAreaTargeted);

        if (result && actionType == ActionType.Action && realPressDepth == 0)
        {
            try { OnAutomatedAction(manager, actionId); }
            catch (Exception ex) { Svc.Log.Warning(ex, $"[{Name}] Pulse failed."); }
        }

        return result;
    }

    private void OnAutomatedAction(ActionManager* manager, uint actionId)
    {
        var config = Plugin.Configuration;
        if (config.HotbarClickFeedbackWrathOnly && !WrathAutoRotationOn()) return;

        var adjusted = manager->GetAdjustedActionId(actionId);
        var now = DateTime.UtcNow;
        if (lastPulseByAction.TryGetValue(adjusted, out var last) && now - last < SameActionCooldown) return;

        var slots = FindSlots(manager, adjusted);
        if (slots.Count == 0) return;

        lastPulseByAction[adjusted] = now;
        var minPulses = Math.Max(1, config.HotbarClickFeedbackPulsesMin);
        var maxPulses = Math.Max(minPulses, config.HotbarClickFeedbackPulsesMax);
        Pulse(slots, random.Next(minPulses, maxPulses + 1), generation);
    }

    private void Pulse(List<SlotRef> slots, int remaining, int gen)
    {
        if (remaining <= 0 || gen != generation || !Enabled) return;

        var config = Plugin.Configuration;
        foreach (var (addonIndex, slotIndex) in slots)
        {
            // Re-fetched each pulse: the addon may have closed or been rebuilt.
            var bar = GetAddon(addonIndex);
            if (bar == null) continue;

            var barSlots = bar->ActionBarSlotVector.AsSpan();
            if (slotIndex >= barSlots.Length) continue;

            var icon = barSlots[slotIndex].Icon;
            if (icon == null || !icon->AtkResNode.IsVisible()) continue;

            bar->PulseActionBarSlot(slotIndex);
            if (config.HotbarClickFeedbackSound)
                UIGlobals.PlaySoundEffect(ClickSoundEffect);
        }

        if (remaining <= 1) return;

        var minDelay = Math.Max(0, config.HotbarClickFeedbackDelayMinMs);
        var maxDelay = Math.Max(minDelay, config.HotbarClickFeedbackDelayMaxMs);
        var delay = TimeSpan.FromMilliseconds(random.Next(minDelay, maxDelay + 1));
        _ = Svc.Framework.RunOnTick(() => Pulse(slots, remaining - 1, gen), delay);
    }

    private static List<SlotRef> FindSlots(ActionManager* manager, uint adjustedActionId)
    {
        var found = new List<SlotRef>();
        var hotbarModule = UIModule.Instance() == null ? null : UIModule.Instance()->GetRaptureHotbarModule();
        if (hotbarModule == null) return found;

        for (var addonIndex = 0; addonIndex < AddonNames.Length; addonIndex++)
        {
            var bar = GetAddon(addonIndex);
            if (bar == null || !bar->IsVisible) continue;

            var raptureIndex = RaptureIndex(bar, addonIndex);
            var hotSlots = raptureIndex >= 0 && raptureIndex < hotbarModule->Hotbars.Length
                ? hotbarModule->Hotbars[raptureIndex].Slots
                : default;

            var barSlots = bar->ActionBarSlotVector.AsSpan();
            for (var slotIndex = 0; slotIndex < barSlots.Length; slotIndex++)
            {
                var slot = barSlots[slotIndex];
                if (slot.Icon == null || !slot.Icon->AtkResNode.IsVisible()) continue;

                HotbarSlot? hot = slotIndex < hotSlots.Length ? hotSlots[slotIndex] : null;
                if (IsMatch(manager, slot, hot, adjustedActionId))
                    found.Add(new SlotRef(addonIndex, slotIndex));
            }
        }

        return found;
    }

    private static bool IsMatch(ActionManager* manager, ActionBarSlot slot, HotbarSlot? hot, uint adjustedActionId)
    {
        if (hot is { } h)
        {
            if (h.CommandType == HotbarSlotType.PvPCombo)
                return h.ApparentSlotType == HotbarSlotType.Action && h.ApparentActionId == adjustedActionId;

            if (!IsActionType(h.OriginalApparentSlotType) || !IsActionType(h.ApparentSlotType))
                return false;
        }

        return manager->GetAdjustedActionId((uint)slot.ActionId) == adjustedActionId;
    }

    private static bool IsActionType(HotbarSlotType type) => type is HotbarSlotType.Action or HotbarSlotType.CraftAction;

    // RSR uses the addon index for standard bars, but RaptureHotbarId follows
    // bar cycling; double cross bars ignore it and use BarTarget instead.
    private static int RaptureIndex(AddonActionBarBase* bar, int addonIndex) =>
        addonIndex <= StandardHotbarCount ? bar->RaptureHotbarId : ((AddonActionDoubleCrossBase*)bar)->BarTarget;

    private static AddonActionBarBase* GetAddon(int addonIndex) =>
        (AddonActionBarBase*)(nint)Svc.GameGui.GetAddonByName(AddonNames[addonIndex], 1);

    private static string[] GetAddonNames()
    {
        List<string> names = [];
        Add<AddonActionBar>();
        Add<AddonActionBarX>();
        Add<AddonActionCross>();
        Add<AddonActionDoubleCrossBase>();
        return [.. names];

        void Add<T>() where T : struct
        {
            if (typeof(T).GetCustomAttribute<AddonAttribute>() is { } attr)
                names.AddRange(attr.AddonIdentifiers);
        }
    }

    /// <summary>Wrath's Auto-Rotation state over IPC, cached briefly. A missing
    /// or changed Wrath reads as off, so nothing pulses.</summary>
    private bool WrathAutoRotationOn()
    {
        var now = DateTime.UtcNow;
        if (now - wrathStateCheckedAt < WrathStateCacheTime) return wrathStateCached;
        wrathStateCheckedAt = now;

        if (!WrathLoaded) return wrathStateCached = false;
        try
        {
            wrathAutoRotationState ??= Svc.PluginInterface.GetIpcSubscriber<bool>("WrathCombo.GetAutoRotationState");
            wrathStateCached = wrathAutoRotationState.InvokeFunc();
        }
        catch (Exception ex)
        {
            Svc.Log.Verbose($"[{Name}] Wrath IPC failed: {ex.Message}");
            wrathStateCached = false;
        }
        return wrathStateCached;
    }

    public override void DrawOptions()
    {
        var config = Plugin.Configuration;

        var wrathOnly = config.HotbarClickFeedbackWrathOnly;
        if (ImGui.Checkbox("Only while Wrath Auto-Rotation is on##HotbarClickFeedback", ref wrathOnly))
        {
            config.HotbarClickFeedbackWrathOnly = wrathOnly;
            config.Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Off: any action not used from a hotbar press gets the animation (other automation plugins, macros).");
        if (wrathOnly && !WrathLoaded)
            ImGui.TextColored(UiColors.Warning, "Wrath Combo isn't loaded, so nothing will animate.");

        var sound = config.HotbarClickFeedbackSound;
        if (ImGui.Checkbox("Click sound##HotbarClickFeedback", ref sound))
        {
            config.HotbarClickFeedbackSound = sound;
            config.Save();
        }

        var pulsesMin = config.HotbarClickFeedbackPulsesMin;
        if (ImGui.SliderInt("Presses (min)##HotbarClickFeedback", ref pulsesMin, 1, 5))
        {
            config.HotbarClickFeedbackPulsesMin = Math.Clamp(pulsesMin, 1, 5);
            config.HotbarClickFeedbackPulsesMax = Math.Max(config.HotbarClickFeedbackPulsesMax, config.HotbarClickFeedbackPulsesMin);
            config.Save();
        }
        var pulsesMax = config.HotbarClickFeedbackPulsesMax;
        if (ImGui.SliderInt("Presses (max)##HotbarClickFeedback", ref pulsesMax, 1, 5))
        {
            config.HotbarClickFeedbackPulsesMax = Math.Clamp(pulsesMax, 1, 5);
            config.HotbarClickFeedbackPulsesMin = Math.Min(config.HotbarClickFeedbackPulsesMin, config.HotbarClickFeedbackPulsesMax);
            config.Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Each automated action plays a random number of presses in this range, like mashing the key.");

        var delayMin = config.HotbarClickFeedbackDelayMinMs;
        if (ImGui.SliderInt("Delay between presses (min, ms)##HotbarClickFeedback", ref delayMin, 0, 1000))
        {
            config.HotbarClickFeedbackDelayMinMs = Math.Clamp(delayMin, 0, 1000);
            config.HotbarClickFeedbackDelayMaxMs = Math.Max(config.HotbarClickFeedbackDelayMaxMs, config.HotbarClickFeedbackDelayMinMs);
            config.Save();
        }
        var delayMax = config.HotbarClickFeedbackDelayMaxMs;
        if (ImGui.SliderInt("Delay between presses (max, ms)##HotbarClickFeedback", ref delayMax, 0, 1000))
        {
            config.HotbarClickFeedbackDelayMaxMs = Math.Clamp(delayMax, 0, 1000);
            config.HotbarClickFeedbackDelayMinMs = Math.Min(config.HotbarClickFeedbackDelayMinMs, config.HotbarClickFeedbackDelayMaxMs);
            config.Save();
        }
    }
}
