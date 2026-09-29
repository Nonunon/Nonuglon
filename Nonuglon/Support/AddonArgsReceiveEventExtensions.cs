using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace Nonuglon.Support;

/// <summary>Ported from ffxiv-bundleoftweaks' clib submodule (clib.Extensions.AddonArgsExtensions) -
/// Dalamud v13+ wraps AddonArgs.Addon in AtkUnitBasePtr instead of exposing a raw event-receive helper.</summary>
internal static unsafe class AddonArgsReceiveEventExtensions
{
    public static T* GetAddon<T>(this AddonArgs args) where T : unmanaged => (T*)args.Addon.Address;

    /// <summary>The event and its data are locals of this method on purpose: they must
    /// stay alive on this stack frame for the duration of the native ReceiveEvent call.</summary>
    public static void ReceiveEvent(this AddonArgs args, AtkEventType eventType, int eventParam)
    {
        var addon = args.GetAddon<AtkUnitBase>();
        var evt = new AtkEvent { Listener = &addon->AtkEventListener, Target = &AtkStage.Instance()->AtkEventTarget };
        var evtData = new AtkEventData();
        addon->ReceiveEvent(eventType, eventParam, &evt, &evtData);
    }
}
