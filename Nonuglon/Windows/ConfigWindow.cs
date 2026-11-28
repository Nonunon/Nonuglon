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
    private const float CategoryIndent = 10f;

    // selectedIndex value for the pinned General row above the tweaks.
    private const int GeneralIndex = -1;

    private readonly Plugin plugin;
    private int selectedIndex;
    // Unscaled units, times ImGuiHelpers.GlobalScale where drawn, so the
    // sidebar and its drag limits grow with the UI scale.
    private float sidebarWidth = 150f;
    // Sized to the longest name on the first draw; dragging overrides it after.
    private bool sidebarFitted;

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
        if (!sidebarFitted)
        {
            sidebarWidth = FitSidebarWidth();
            sidebarFitted = true;
        }

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

        // Grouped by category (enum order), tweaks in Plugin.Tweaks order within
        // each. Folded groups are saved, so the sidebar opens the way it was left.
        var config = Plugin.Configuration;
        ImGui.PushStyleVar(ImGuiStyleVar.IndentSpacing, CategoryIndent * ImGuiHelpers.GlobalScale);
        foreach (var category in Enum.GetValues<TweakCategory>())
        {
            var any = false;
            for (var i = 0; i < plugin.Tweaks.Count && !any; i++)
                any = plugin.Tweaks[i].Category == category;
            if (!any) continue;

            var open = !config.CollapsedTweakCategories.Contains(category);
            ImGui.SetNextItemOpen(open);
            ImGui.PushStyleColor(ImGuiCol.Text, UiColors.Disabled);
            var nowOpen = ImGui.TreeNodeEx($"{category.DisplayName()}##sidebarCategory{category}", ImGuiTreeNodeFlags.SpanAvailWidth);
            ImGui.PopStyleColor();
            if (nowOpen != open)
            {
                if (nowOpen) config.CollapsedTweakCategories.Remove(category);
                else config.CollapsedTweakCategories.Add(category);
                config.Save();
            }
            if (!nowOpen) continue;

            for (var i = 0; i < plugin.Tweaks.Count; i++)
                if (plugin.Tweaks[i].Category == category)
                    DrawSidebarRow(plugin.Tweaks[i], i);
            ImGui.TreePop();
        }
        ImGui.PopStyleVar();

        ImGui.EndChild();
    }

    /// <summary>Label-less Selectable for the hit area, dot and name drawn over it
    /// (like the General row), so a name too long for the sidebar ends in "..."
    /// with the full name on hover instead of being cut mid-letter.</summary>
    private void DrawSidebarRow(TweakBase tweak, int index)
    {
        var (dotGlyph, dotColor) = StatusDot(tweak);
        var rowStart = ImGui.GetCursorPos();
        var rowWidth = ImGui.GetContentRegionAvail().X;

        if (ImGui.Selectable($"##sidebar{index}", index == selectedIndex))
            selectedIndex = index;
        var hovered = ImGui.IsItemHovered();

        // Only the dot carries status color; the name is plain text, greyed when off.
        ImGui.SetCursorPos(rowStart);
        ImGui.TextColored(dotColor, dotGlyph);
        ImGui.SameLine();
        var shown = FitText(tweak.Name, rowStart.X + rowWidth - ImGui.GetCursorPosX(), out var cut);
        if (tweak.Enabled) ImGui.TextUnformatted(shown);
        else ImGui.TextColored(UiColors.Disabled, shown);

        if (hovered && cut) ImGui.SetTooltip(tweak.Name);
    }

    private static string FitText(string text, float room, out bool cut)
    {
        cut = ImGui.CalcTextSize(text).X > room;
        if (!cut) return text;
        for (var n = text.Length - 1; n > 0; n--)
        {
            var candidate = text[..n].TrimEnd() + "...";
            if (ImGui.CalcTextSize(candidate).X <= room) return candidate;
        }
        return "...";
    }

    /// <summary>Widest sidebar row (tweak or category header), so the first
    /// open shows every name whole. Unscaled units, like sidebarWidth.</summary>
    private float FitSidebarWidth()
    {
        var style = ImGui.GetStyle();
        var scale = ImGuiHelpers.GlobalScale;
        var widest = ImGui.CalcTextSize("● " + GeneralSettings.Name).X + ImGui.GetFontSize();
        foreach (var tweak in plugin.Tweaks)
        {
            widest = Math.Max(widest, CategoryIndent * scale + ImGui.CalcTextSize($"● {tweak.Name}").X);
            widest = Math.Max(widest, ImGui.GetFontSize() + style.FramePadding.X * 2 + ImGui.CalcTextSize(tweak.Category.DisplayName()).X);
        }
        var total = widest + style.WindowPadding.X * 2 + style.ScrollbarSize;
        return Math.Clamp(total / scale, MinSidebarWidth, MaxSidebarWidth);
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
