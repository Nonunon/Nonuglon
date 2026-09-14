using System;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Hooking;
using ECommons.Automation.NeoTaskManager;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Nonuglon.Support;
using static Nonuglon.Support.CommandText;

namespace Nonuglon.Tweaks;

/// <summary>
/// Ported from ffxiv-bundleoftweaks' Tweaks/InstantReturn.cs.
///
/// The original used a custom [AddressHook&lt;T&gt;] attribute + source generator to
/// wire up the hook. We're not vendoring that toolchain, so this hooks
/// AgentReturn.Return directly via its already-resolved MemberFunctionPointers
/// address instead - confirmed to generate identical code to what the real
/// generator produces.
///
/// The direct teleport is fired via GameMain.ExecuteCommand(214) - see
/// Support/GameCommandIds.cs. The SelectYesno auto-click listener is NOT
/// redundant with that - it's what handles the case where Original(agent) still
/// opens the real confirmation dialog (general action not instantly ready), which
/// the original design expects and clicks through.
/// </summary>
public unsafe class InstantReturn : TweakBase
{
    public override string Name => "Quick Return";
    public override string Description => "Calls the Return function directly and unconditionally - a hack that skips the confirmation dialog and fires regardless of whether Return is actually valid right now. Optionally leaves/disbands your party first.";

    public override bool ConfigEnabled
    {
        get => Plugin.Configuration.InstantReturnEnabled;
        set => Plugin.Configuration.InstantReturnEnabled = value;
    }

    public override string[] CommandNames => ["instantreturn", "quickreturn"];

    public override string[] UsageLines =>
    [
        ..base.UsageLines,
        $"/Nonuglon {CommandNames[0]} leaveparty <on|off|toggle>",
    ];

    private const int ReturnGeneralActionId = 8;

    private readonly TaskManager taskManager = new();
    private Hook<AgentReturn.Delegates.Return>? returnHook;

    protected override void Enable()
    {
        returnHook ??= Svc.Hook.HookFromAddress<AgentReturn.Delegates.Return>(
            (nint)AgentReturn.MemberFunctionPointers.Return, ReturnDetour);
        returnHook.Enable();

        Svc.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, "SelectYesno", HandleSelectYesno);
    }

    protected override void Disable()
    {
        returnHook?.Disable();
        Svc.AddonLifecycle.UnregisterListener(AddonEvent.PostSetup, "SelectYesno", HandleSelectYesno);
        if (taskManager.NumQueuedTasks > 0)
            taskManager.Abort();
    }

    public override void Dispose()
    {
        base.Dispose();
        returnHook?.Dispose();
    }

    public override void DrawOptions()
    {
        var leaveParty = Plugin.Configuration.InstantReturnLeaveParty;
        if (ImGui.Checkbox("Leave party first##InstantReturn", ref leaveParty))
        {
            Plugin.Configuration.InstantReturnLeaveParty = leaveParty;
            Plugin.Configuration.Save();
        }
    }

    public override void HandleCommand(string[] args)
    {
        if (args.Length >= 1 && args[0].Equals("leaveparty", StringComparison.OrdinalIgnoreCase))
        {
            if (args.Length < 2 || !ResolveBool(args[1], Plugin.Configuration.InstantReturnLeaveParty, out var leaveParty))
            {
                Print($"Usage: /Nonuglon {CommandNames[0]} leaveparty <on|off|toggle>");
                return;
            }

            var previous = Plugin.Configuration.InstantReturnLeaveParty;
            Plugin.Configuration.InstantReturnLeaveParty = leaveParty;
            Plugin.Configuration.Save();
            ReportStateChange("Quick Return: leave party first", previous, leaveParty);
            return;
        }

        base.HandleCommand(args);
    }

    private void ReturnDetour(AgentReturn* agent)
    {
        // Deliberately NOT an if/else, and no early return: the original tweak calls
        // Original(agent) as a side effect when the general action reads as unavailable
        // (which opens the real confirmation dialog - HandleSelectYesno below clicks
        // it through), but ALWAYS fires the direct command regardless of that check.
        if (ActionManager.Instance()->GetActionStatus(ActionType.GeneralAction, ReturnGeneralActionId) != 0)
            returnHook!.Original(agent);

        if (Plugin.Configuration.InstantReturnLeaveParty)
            StartLeaveThenReturn();
        else
            FireReturnCommand();
    }

    private void StartLeaveThenReturn()
    {
        if (!InfoProxyCrossRealm.IsLocalPlayerInParty())
        {
            FireReturnCommand();
            return;
        }

        // Matches the original's WaitUntil(Disband/Leave) exactly: call the
        // disband/leave function itself every tick until IT returns true, rather
        // than calling it once and separately polling party membership. If the
        // first call fails (not ready, rate limited, whatever), this keeps retrying
        // instead of silently giving up.
        if (InfoProxyCrossRealm.IsLocalPlayerPartyLeader())
            taskManager.Enqueue(() => InfoProxyPartyMember.Instance()->DisbandParty(), "InstantReturn: wait for disband");
        else
            taskManager.Enqueue(() => InfoProxyPartyMember.Instance()->LeaveParty(), "InstantReturn: wait for leave");

        taskManager.Enqueue(() =>
        {
            FireReturnCommand();
            return true;
        }, "InstantReturn: fire Return");
    }

    private static void FireReturnCommand() =>
        GameMain.ExecuteCommand(GameCommandIds.ReturnIfNotLalafell, 0, 0, 0, 0);

    private void HandleSelectYesno(AddonEvent type, AddonArgs args)
    {
        var agent = AgentModule.Instance()->GetAgentByInternalId(AgentId.Return);
        if (agent is null || agent->AddonId != args.Addon.Id) return;

        args.ReceiveEvent(AtkEventType.ButtonClick, 0);
    }
}
