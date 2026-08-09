using Dalamud.Hooking;
using ECommons.Automation.NeoTaskManager;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using Nonuglon.Support;

namespace Nonuglon.Tweaks;

/// <summary>
/// Ported from ffxiv-bundleoftweaks' Tweaks/InstantReturn.cs.
///
/// The original used a custom [AddressHook&lt;T&gt;] attribute + source generator
/// (from the InteropSourceGenerators submodule) to wire up the hook. We're not
/// vendoring that toolchain, so this hooks AgentReturn.Return directly via its
/// already-resolved MemberFunctionPointers address instead - same net effect,
/// no source generator required.
///
/// The actual teleport is fired via GameMain.ExecuteCommand(214) - see
/// Support/GameCommandIds.cs - NOT via the Return general action. UseAction on the
/// general action just re-triggers the same AgentReturn flow that opens the
/// confirmation dialog (which is what we're hooking to bypass in the first place),
/// so it can't be used as the trigger here.
/// </summary>
public unsafe class InstantReturn : TweakBase
{
    public override string Name => "Quick Return";
    public override string Description => "Calls Return directly instead of clicking through the confirmation dialog. Optionally leaves/disbands your party first.";

    private const int ReturnGeneralActionId = 8;

    private readonly TaskManager taskManager = new();
    private Hook<AgentReturn.Delegates.Return>? returnHook;

    protected override void Enable()
    {
        returnHook ??= Svc.Hook.HookFromAddress<AgentReturn.Delegates.Return>(
            (nint)AgentReturn.MemberFunctionPointers.Return, ReturnDetour);
        returnHook.Enable();
    }

    protected override void Disable()
    {
        returnHook?.Disable();
        if (taskManager.NumQueuedTasks > 0)
            taskManager.Abort();
    }

    public override void Dispose()
    {
        base.Dispose();
        returnHook?.Dispose();
    }

    private void ReturnDetour(AgentReturn* agent)
    {
        // Deliberately NOT an if/else, and no early return: the original tweak calls
        // Original(agent) as a side effect when the general action reads as unavailable
        // (letting the base UI/animation state settle), but ALWAYS fires the direct
        // command below regardless of that check. That's the whole "hack" - it wins
        // the race against the confirmation dialog instead of waiting to see if the
        // dialog was needed.
        if (ActionManager.Instance()->GetActionStatus(ActionType.GeneralAction, ReturnGeneralActionId) != 0)
            returnHook!.Original(agent);

        if (Plugin.Configuration.InstantReturnLeaveParty)
            StartLeaveThenReturn();
        else
            GameMain.ExecuteCommand(GameCommandIds.ReturnIfNotLalafell, 0, 0, 0, 0);
    }

    private void StartLeaveThenReturn()
    {
        taskManager.Enqueue(() =>
        {
            if (!InfoProxyCrossRealm.IsLocalPlayerInParty()) return true;
            if (InfoProxyCrossRealm.IsLocalPlayerPartyLeader())
                InfoProxyPartyMember.Instance()->DisbandParty();
            else
                InfoProxyPartyMember.Instance()->LeaveParty();
            return true;
        }, "InstantReturn: leave/disband party");

        taskManager.Enqueue(() => !InfoProxyCrossRealm.IsLocalPlayerInParty(), "InstantReturn: wait for party to clear");
        taskManager.Enqueue(() => GameMain.ExecuteCommand(GameCommandIds.ReturnIfNotLalafell, 0, 0, 0, 0), "InstantReturn: fire Return");
    }
}
