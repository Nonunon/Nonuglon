using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using Nonuglon.Support;
using Nonuglon.Tweaks;

namespace Nonuglon.Windows;

public class ConfigWindow : Window, IDisposable
{
    private const float SplitterThickness = 6f;
    private const float MinSidebarWidth = 90f;
    private const float MaxSidebarWidth = 260f;

    // selectedIndex value for the pinned General row above the tweaks.
    private const int GeneralIndex = -1;

    private readonly Plugin plugin;
    private int selectedIndex;
    // Unscaled units, times ImGuiHelpers.GlobalScale where drawn, so the
    // sidebar and its drag limits grow with the UI scale.
    private float sidebarWidth = 150f;

    public ConfigWindow(Plugin plugin) : base("Nonuglon Tweaks###NonuglonConfig")
    {
        Flags = ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse;

        Size = new Vector2(480, 360);
        SizeCondition = ImGuiCond.FirstUseEver;

        this.plugin = plugin;
    }

    public void Dispose() { }

    public override void Draw()
    {
        if (plugin.Tweaks.Count == 0)
        {
            ImGui.TextDisabled("No tweaks loaded.");
            return;
        }

        selectedIndex = Math.Clamp(selectedIndex, GeneralIndex, plugin.Tweaks.Count - 1);

        // Same row, same explicit height, so all three panes line up evenly.
        var paneHeight = ImGui.GetContentRegionAvail().Y;

        DrawSidebar(paneHeight);
        ImGui.SameLine(0, 0);
        DrawSplitter(paneHeight);
        ImGui.SameLine(0, 0);
        if (selectedIndex == GeneralIndex) DrawGeneral(paneHeight);
        else DrawSelectedTweak(plugin.Tweaks[selectedIndex], paneHeight);
    }

    /// <summary>Left pane: one row per loaded tweak, driven off plugin.Tweaks -
    /// no changes needed here when a tweak is added.</summary>
    private void DrawSidebar(float height)
    {
        ImGui.BeginChild("##NonuglonSidebar", new Vector2(sidebarWidth * ImGuiHelpers.GlobalScale, height), true);

        // Pinned General row: a label-less Selectable for the hit area, with the
        // icon (icon font) and text drawn over it, since one label can't mix fonts.
        var rowStart = ImGui.GetCursorPos();
        if (ImGui.Selectable("##sidebarGeneral", selectedIndex == GeneralIndex))
            selectedIndex = GeneralIndex;
        ImGui.SetCursorPos(rowStart);
        using (Plugin.PluginInterface.UiBuilder.IconFontHandle.Push())
            ImGui.TextUnformatted(FontAwesomeIcon.Cog.ToIconString());
        ImGui.SameLine();
        ImGui.TextUnformatted(GeneralSettings.Name);
        ImGui.Separator();

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

        ImGui.Button("##NonuglonSplitter", new Vector2(SplitterThickness * ImGuiHelpers.GlobalScale, height));

        ImGui.PopStyleColor(3);

        if (ImGui.IsItemActive())
            sidebarWidth = Math.Clamp(sidebarWidth + (ImGui.GetIO().MouseDelta.X / ImGuiHelpers.GlobalScale), MinSidebarWidth, MaxSidebarWidth);

        if (ImGui.IsItemHovered() || ImGui.IsItemActive())
            ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeEw);
    }

    /// <summary>Right pane for the General row, same header layout as a tweak.</summary>
    private static void DrawGeneral(float height)
    {
        ImGui.BeginChild("##NonuglonTweakDetails", new Vector2(0, height), true);

        ImGui.TextColored(UiColors.Header, GeneralSettings.Name);
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.PushTextWrapPos(ImGui.GetContentRegionAvail().X + ImGui.GetCursorPosX());
        ImGui.TextDisabled(GeneralSettings.Description);
        ImGui.PopTextWrapPos();
        ImGui.Spacing();

        GeneralSettings.Draw();

        ImGui.EndChild();
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
            tweak.SetEnabled(enabled);
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
