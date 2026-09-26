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

        // Same row, same explicit height, so all three panes line up evenly.
        var paneHeight = ImGui.GetContentRegionAvail().Y;

        DrawSidebar(paneHeight);
        ImGui.SameLine(0, 0);
        DrawSplitter(paneHeight);
        ImGui.SameLine(0, 0);
        DrawSelectedTweak(plugin.Tweaks[selectedIndex], paneHeight);
    }

    /// <summary>Left pane: one row per loaded tweak, driven off plugin.Tweaks -
    /// no changes needed here when a tweak is added.</summary>
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

    /// <summary>Invisible-button divider; dragging adjusts sidebarWidth via
    /// mouse delta, clamped to a sane range. Cursor swaps to resize-arrow on
    /// hover.</summary>
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

    /// <summary>Right pane: header, description, enable checkbox, then the
    /// tweak's own DrawOptions().</summary>
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

    /// <summary>Shared status-dot glyph+color so the sidebar and detail pane
    /// never disagree. Always the same circle glyph (FFXIV's font lacks a
    /// warning-triangle) - warning is conveyed by color alone.</summary>
    private static (string Glyph, Vector4 Color) StatusDot(TweakBase tweak)
    {
        if (!tweak.Enabled) return ("\u25cb", UiColors.Disabled);
        return ("\u25cf", tweak.HasWarning ? UiColors.Warning : UiColors.Enabled);
    }
}
