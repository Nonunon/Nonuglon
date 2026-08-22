using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using Nonuglon.Tweaks;

namespace Nonuglon.Windows;

public class ConfigWindow : Window, IDisposable
{
    private static readonly Vector4 EnabledColor = new(0.4f, 0.9f, 0.4f, 1f);
    private static readonly Vector4 DisabledColor = new(0.6f, 0.6f, 0.6f, 1f);
    private static readonly Vector4 HeaderColor = new(0.85f, 0.7f, 0.3f, 1f);
    private static readonly Vector4 WarningColor = new(0.95f, 0.65f, 0.25f, 1f);

    private readonly Configuration configuration;
    private readonly Plugin plugin;

    public ConfigWindow(Plugin plugin) : base("Nonuglon Tweaks###NonuglonConfig")
    {
        Flags = ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse;

        Size = new Vector2(400, 380);
        SizeCondition = ImGuiCond.FirstUseEver;

        this.plugin = plugin;
        configuration = Plugin.Configuration;
    }

    public void Dispose() { }

    public override void Draw()
    {
        DrawTweakSection<InstantReturn>(
            "Quick Return",
            "Calls the Return function directly and unconditionally - a hack that skips the confirmation dialog and fires regardless of whether Return is actually valid right now.",
            enabled => configuration.InstantReturnEnabled = enabled,
            () =>
            {
                var leaveParty = configuration.InstantReturnLeaveParty;
                if (ImGui.Checkbox("Leave party first##InstantReturn", ref leaveParty))
                {
                    configuration.InstantReturnLeaveParty = leaveParty;
                    configuration.Save();
                }
            });

        DrawTweakSection<AutoPillion>(
            "Auto Pillion",
            "Automatically hops onto a nearby mount with an open pillion seat.",
            enabled => configuration.AutoPillionEnabled = enabled,
            () =>
            {
                var restrictToPerson = configuration.AutoPillionRestrictToPerson;
                if (ImGui.Checkbox("Restrict to one person##AutoPillion", ref restrictToPerson))
                {
                    configuration.AutoPillionRestrictToPerson = restrictToPerson;
                    configuration.Save();
                }

                var targetName = configuration.AutoPillionTargetName;
                if (ImGui.InputText("Target name##AutoPillion", ref targetName, 64))
                {
                    configuration.AutoPillionTargetName = targetName;
                    configuration.Save();
                }

                var retryTimeout = configuration.AutoPillionRetryTimeoutMs;
                if (ImGui.SliderInt("Retry timeout (ms)##AutoPillion", ref retryTimeout, 500, 10000))
                {
                    configuration.AutoPillionRetryTimeoutMs = retryTimeout;
                    configuration.Save();
                }
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("How long to wait for a ride attempt to land before giving up and retrying. Lower = faster remount after dismounting, but more spammy if it keeps missing.");
            });

        DrawTweakSection<EntrustChocoboDuplicates>(
            "Saddlebag Entrust Duplicates",
            "Adds a button to the AetherBags saddlebag window to entrust duplicates. Requires AetherBags to be installed.",
            enabled => configuration.EntrustChocoboDuplicatesEnabled = enabled,
            () =>
            {
                if (!EntrustChocoboDuplicates.IsAetherBagsAvailable)
                {
                    ImGui.PushTextWrapPos(ImGui.GetContentRegionAvail().X + ImGui.GetCursorPosX());
                    ImGui.TextColored(WarningColor, "\u26a0 AetherBags not detected - this tweak has no effect until it's installed and loaded.");
                    ImGui.PopTextWrapPos();
                }
            });
    }

    private void DrawTweakSection<T>(string label, string description, Action<bool> onToggled, Action? extraOptions) where T : TweakBase
    {
        ImGui.TextColored(HeaderColor, label);
        ImGui.Separator();

        var tweak = plugin.Tweaks.Find(t => t is T);
        if (tweak is null)
        {
            ImGui.TextDisabled("Not loaded.");
            ImGui.Spacing();
            return;
        }

        ImGui.PushTextWrapPos(ImGui.GetContentRegionAvail().X + ImGui.GetCursorPosX());
        ImGui.TextDisabled(description);
        ImGui.PopTextWrapPos();

        var enabled = tweak.Enabled;
        if (ImGui.Checkbox($"Enabled##{typeof(T).Name}", ref enabled))
        {
            if (enabled) tweak.EnableTweak();
            else tweak.DisableTweak();

            onToggled(enabled);
            configuration.Save();
        }

        ImGui.SameLine();
        ImGui.TextColored(tweak.Enabled ? EnabledColor : DisabledColor, tweak.Enabled ? "\u25cf On" : "\u25cb Off");

        if (extraOptions != null)
        {
            ImGui.Indent();
            extraOptions();
            ImGui.Unindent();
        }

        ImGui.Spacing();
    }
}
