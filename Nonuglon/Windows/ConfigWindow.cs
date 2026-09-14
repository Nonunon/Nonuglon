using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using Nonuglon.Support;
using Nonuglon.Tweaks;

namespace Nonuglon.Windows;

public class ConfigWindow : Window, IDisposable
{
    private const float SplitterThickness = 6f;
    private const float MinSidebarWidth = 90f;
    private const float MaxSidebarWidth = 260f;

    private readonly Configuration configuration;
    private readonly Plugin plugin;
    private int selectedIndex;
    private float sidebarWidth = 150f;

    public ConfigWindow(Plugin plugin) : base("Nonuglon Tweaks###NonuglonConfig")
    {
        Flags = ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse;

        Size = new Vector2(480, 360);
        SizeCondition = ImGuiCond.FirstUseEver;

        this.plugin = plugin;
        configuration = Plugin.Configuration;
    }

    public void Dispose() { }

    public override void Draw()
    {
        if (plugin.Tweaks.Count == 0)
        {
            ImGui.TextDisabled("No tweaks loaded.");
            return;
        }

        selectedIndex = Math.Clamp(selectedIndex, 0, plugin.Tweaks.Count - 1);

        // All three panes drawn on the same row (SameLine(0, 0) - no default
        // spacing) at the same explicit height, so the sidebar/splitter/details
        // line up evenly regardless of how much content the selected tweak draws.
        var paneHeight = ImGui.GetContentRegionAvail().Y;

        DrawSidebar(paneHeight);
        ImGui.SameLine(0, 0);
        DrawSplitter(paneHeight);
        ImGui.SameLine(0, 0);
        DrawSelectedTweak(plugin.Tweaks[selectedIndex], paneHeight);
    }

    /// <summary>Left pane: one selectable row per loaded tweak, driven entirely off
    /// plugin.Tweaks - adding a tweak elsewhere in the plugin makes it show up here
    /// automatically, no changes needed in this file.</summary>
    private void DrawSidebar(float height)
    {
        ImGui.BeginChild("##NonuglonSidebar", new Vector2(sidebarWidth, height), true);

        for (var i = 0; i < plugin.Tweaks.Count; i++)
        {
            var tweak = plugin.Tweaks[i];
            var (dotGlyph, dotColor) = StatusDot(tweak);

            ImGui.PushStyleColor(ImGuiCol.Text, dotColor);
            if (ImGui.Selectable($"{dotGlyph} {tweak.Name}##sidebar{i}", i == selectedIndex))
                selectedIndex = i;
            ImGui.PopStyleColor();
        }

        ImGui.EndChild();
    }

    /// <summary>Thin invisible-button divider between the sidebar and detail pane.
    /// Dragging it adjusts sidebarWidth directly via the mouse's per-frame delta,
    /// clamped to a sane range so it can't be dragged down to nothing or out past
    /// the window. Cursor swaps to a resize arrow on hover so it reads as
    /// draggable before the user commits to clicking it.</summary>
    private void DrawSplitter(float height)
    {
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0f, 0f, 0f, 0f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(1f, 1f, 1f, 0.15f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(1f, 1f, 1f, 0.25f));

        ImGui.Button("##NonuglonSplitter", new Vector2(SplitterThickness, height));

        ImGui.PopStyleColor(3);

        if (ImGui.IsItemActive())
            sidebarWidth = Math.Clamp(sidebarWidth + ImGui.GetIO().MouseDelta.X, MinSidebarWidth, MaxSidebarWidth);

        if (ImGui.IsItemHovered() || ImGui.IsItemActive())
            ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeEw);
    }

    /// <summary>Right pane: header, description, the enable checkbox (persisted via
    /// GetToggleAction below), then whatever the tweak itself wants to draw via
    /// DrawOptions().</summary>
    private void DrawSelectedTweak(TweakBase tweak, float height)
    {
        ImGui.BeginChild("##NonuglonTweakDetails", new Vector2(0, height), true);

        ImGui.TextColored(UiColors.Header, tweak.Name);
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.PushTextWrapPos(ImGui.GetContentRegionAvail().X + ImGui.GetCursorPosX());
        ImGui.TextDisabled(tweak.Description);
        ImGui.PopTextWrapPos();
        ImGui.Spacing();

        var enabled = tweak.Enabled;
        if (ImGui.Checkbox($"Enabled##{tweak.GetType().Name}", ref enabled))
        {
            if (enabled) tweak.EnableTweak();
            else tweak.DisableTweak();

            tweak.ConfigEnabled = enabled;
            configuration.Save();
        }

        ImGui.SameLine();
        var (_, statusColor) = StatusDot(tweak);
        ImGui.TextColored(statusColor, tweak.Enabled ? (tweak.HasWarning ? "\u25cf On (see below)" : "\u25cf On") : "\u25cb Off");

        ImGui.Spacing();
        ImGui.Indent();
        tweak.DrawOptions();
        ImGui.Unindent();

        ImGui.EndChild();
    }

    /// <summary>Shared status-dot glyph + color for a tweak, used by both the
    /// sidebar row and the detail pane's On/Off label so the two never disagree.
    /// Always the same filled/hollow circle glyphs (FFXIV's font doesn't carry the
    /// Unicode warning-triangle glyph, so it silently fails to render) - the
    /// warning state is conveyed by color alone: gray/hollow when off, amber-filled
    /// when on but HasWarning is true (e.g. a required companion plugin is
    /// missing), green-filled when on and functioning normally.</summary>
    private static (string Glyph, Vector4 Color) StatusDot(TweakBase tweak)
    {
        if (!tweak.Enabled) return ("\u25cb", UiColors.Disabled);
        return ("\u25cf", tweak.HasWarning ? UiColors.Warning : UiColors.Enabled);
    }
}
