using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using Nonuglon.Tweaks;

namespace Nonuglon.Windows;

public class ConfigWindow : Window, IDisposable
{
    private readonly Configuration configuration;
    private readonly Plugin plugin;

    public ConfigWindow(Plugin plugin) : base("Nonuglon Tweaks###NonuglonConfig")
    {
        Flags = ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse;

        Size = new Vector2(360, 220);
        SizeCondition = ImGuiCond.FirstUseEver;

        this.plugin = plugin;
        configuration = Plugin.Configuration;
    }

    public void Dispose() { }

    public override void PreDraw()
    {
        if (configuration.IsConfigWindowMovable)
            Flags &= ~ImGuiWindowFlags.NoMove;
        else
            Flags |= ImGuiWindowFlags.NoMove;
    }

    public override void Draw()
    {
        var movable = configuration.IsConfigWindowMovable;
        if (ImGui.Checkbox("Movable Config Window", ref movable))
        {
            configuration.IsConfigWindowMovable = movable;
            configuration.Save();
        }

        ImGui.Separator();

        DrawTweakToggle<InstantReturn>("Quick Return", enabled =>
        {
            configuration.InstantReturnEnabled = enabled;
        });

        var leaveParty = configuration.InstantReturnLeaveParty;
        ImGui.Indent();
        if (ImGui.Checkbox("Leave party first##InstantReturn", ref leaveParty))
        {
            configuration.InstantReturnLeaveParty = leaveParty;
            configuration.Save();
        }
        ImGui.Unindent();

        ImGui.Spacing();

        DrawTweakToggle<AutoPillion>("Auto Pillion", enabled =>
        {
            configuration.AutoPillionEnabled = enabled;
        });

        var restrictToPerson = configuration.AutoPillionRestrictToPerson;
        ImGui.Indent();
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
        ImGui.Unindent();

        ImGui.Spacing();

        DrawTweakToggle<EntrustChocoboDuplicates>("Saddlebag Entrust Duplicates (AetherBags)", enabled =>
        {
            configuration.EntrustChocoboDuplicatesEnabled = enabled;
        });
    }

    private void DrawTweakToggle<T>(string label, Action<bool> onToggled) where T : TweakBase
    {
        var tweak = plugin.Tweaks.Find(t => t is T);
        if (tweak is null)
        {
            ImGui.TextDisabled($"{label} (not loaded)");
            return;
        }

        var enabled = tweak.Enabled;
        if (ImGui.Checkbox($"{label}##{typeof(T).Name}", ref enabled))
        {
            if (enabled) tweak.EnableTweak();
            else tweak.DisableTweak();

            onToggled(enabled);
            configuration.Save();
        }

        if (ImGui.IsItemHovered() && !string.IsNullOrEmpty(tweak.Description))
            ImGui.SetTooltip(tweak.Description);
    }
}
