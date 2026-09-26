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

/// <summary>Ported from ffxiv-bundleoftweaks'
/// (https://github.com/Jaksuhn/ffxiv-bundleoftweaks, BSD-3-Clause)
/// Tweaks/InstantReturn.cs, hooking AgentReturn.Return directly via
/// MemberFunctionPointers instead of vendoring its attribute+generator
/// toolchain. The SelectYesno listener isn't redundant with the direct command
/// fire - it clicks through the real confirmation dialog Original(agent) still
/// opens when the general action isn't instantly ready.</summary>
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

    // The "leaveparty" subcommand only shows up (and only works, see
    // HandleCommand below) once the tweak itself is on - while it's off there's
    // nothing beyond the plain on/off/toggle to advertise.
    public override string[] UsageLines =>
        Enabled
            ? [..base.UsageLines, $"/Nonuglon {CommandNames[0]} leaveparty <on|off|toggle>"]
            : base.UsageLines;

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
        // Only recognized while on; otherwise falls through to the plain toggle.
        if (Enabled && args.Length >= 1 && args[0].Equals("leaveparty", StringComparison.OrdinalIgnoreCase))
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
        // Not an if/else: Original(agent) still fires (opens the confirmation
        // dialog HandleSelectYesno clicks through) when unavailable, but the
        // direct command below always fires regardless.
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

        // Calls disband/leave every tick until it returns true, rather than
        // once + polling membership separately, so a failed first call retries.
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
