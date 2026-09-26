using System;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Gui.Dtr;
using Dalamud.Game.Text.SeStringHandling;
using ECommons.DalamudServices;
using ECommons.GameFunctions;
using static Nonuglon.Support.CommandText;

namespace Nonuglon.Tweaks;

/// <summary>
/// Wraps ECommons' RenderDisableManager (a cross-plugin "disable 3D rendering"
/// request; the UI stays visible). PlaceRequest/RemoveRequest must run on the
/// framework thread per its own docs, so both go through RunOnFrameworkThread.
/// No query API and no persistence across sessions, so "disabled" is tracked
/// locally and never saved to Configuration. The Dtr bar entry exists so a
/// blacked-out screen doesn't get mistaken for a hang. Bare "/rendertoggle"
/// (no args) is a deliberate exception to Nonuglon's usual on/off/toggle
/// convention - it just flips the state directly.
/// </summary>
public class RenderToggle : IMiniTweak
{
    public string Name => "Render Toggle";
    public string Description => "Toggle ECommons' cross-plugin \"disable 3D world rendering\" request - cuts GPU/CPU load while AFK or tabbed away without closing the game. Only the 3D scene is affected; the UI (including this window) stays visible.";

    public bool Enabled
    {
        get => Plugin.Configuration.RenderToggleEnabled;
        set
        {
            Plugin.Configuration.RenderToggleEnabled = value;
            // Force rendering back on when the mini-tweak itself turns off.
            if (!value) SetRenderDisabled(false, announce: false);
        }
    }

    public string CommandName => "rendertoggle";

    public string[] UsageLines =>
        Enabled
            ? [$"/Nonuglon {CommandName} <on|off|toggle>", $"/Nonuglon {CommandName} now <on|off|toggle>"]
            : [$"/Nonuglon {CommandName} <on|off|toggle>"];

    private bool disabled;
    private IDtrBarEntry? dtrEntry;

    private void SetRenderDisabled(bool value, bool announce = true)
    {
        if (disabled == value) return;
        disabled = value;

        Svc.Framework.RunOnFrameworkThread(() =>
        {
            if (disabled) RenderDisableManager.PlaceRequest();
            else RenderDisableManager.RemoveRequest();
        });

        UpdateDtrEntry();
        if (announce) Print($"{Name}: 3D rendering {(disabled ? "disabled" : "enabled")}.");
    }

    private void UpdateDtrEntry()
    {
        if (disabled)
        {
            if (dtrEntry == null)
            {
                dtrEntry = Svc.DtrBar.Get("Nonuglon Render Toggle", new SeStringBuilder().AddText("Render Off").Build());
                dtrEntry.Tooltip = new SeStringBuilder().AddText("Nonuglon: 3D rendering is disabled - click to re-enable.").Build();
                dtrEntry.OnClick = _ => SetRenderDisabled(false);
            }
            dtrEntry.Shown = true;
        }
        else if (dtrEntry != null)
        {
            dtrEntry.Shown = false;
        }
    }

    /// <summary>Bare "/rendertoggle"; swallowed while the mini-tweak is off.</summary>
    public void ToggleAction()
    {
        if (!Enabled) return;
        SetRenderDisabled(!disabled);
    }

    public void DrawRow()
    {
        var enabled = Enabled;
        if (ImGui.Checkbox($"{Name}##RenderToggleEnabled", ref enabled))
        {
            Enabled = enabled;
            Plugin.Configuration.Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(Description);

        ImGui.Indent();
        ImGui.BeginDisabled(!Enabled);

        var disabledLocal = disabled;
        if (ImGui.Checkbox("3D rendering disabled##RenderToggleActive", ref disabledLocal))
            SetRenderDisabled(disabledLocal);

        ImGui.EndDisabled();
        ImGui.Unindent();
    }

    public void HandleCommand(string[] args)
    {
        // "now" only exists once the mini-tweak is on; otherwise falls through.
        if (Enabled && args.Length >= 1 && args[0].Equals("now", StringComparison.OrdinalIgnoreCase))
        {
            if (args.Length < 2 || !ResolveBool(args[1], disabled, out var value))
            {
                Print($"Usage: /Nonuglon {CommandName} now <on|off|toggle>");
                return;
            }

            SetRenderDisabled(value);
            return;
        }

        if (args.Length == 0 || !ResolveBool(args[0], Enabled, out var enabledSelf))
        {
            Print($"Usage: /Nonuglon {CommandName} <on|off|toggle>");
            return;
        }

        var previous = Enabled;
        Enabled = enabledSelf;
        Plugin.Configuration.Save();
        ReportStateChange(Name, previous, enabledSelf);
    }

    public void Dispose()
    {
        SetRenderDisabled(false, announce: false);
        dtrEntry?.Remove();
        dtrEntry = null;
    }
}
