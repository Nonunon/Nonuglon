using System;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace Nonuglon.Windows;

public class MainWindow : Window, IDisposable
{
    private readonly Plugin plugin;

    public MainWindow(Plugin plugin) : base("Nonuglon##NonuglonMain", ImGuiWindowFlags.AlwaysAutoResize)
    {
        this.plugin = plugin;
    }

    public void Dispose() { }

    public override void Draw()
    {
        var enabledCount = plugin.Tweaks.FindAll(t => t.Enabled).Count;
        ImGui.Text($"{enabledCount}/{plugin.Tweaks.Count} tweaks enabled");

        ImGui.Spacing();

        foreach (var tweak in plugin.Tweaks)
        {
            var (glyph, color) = ConfigWindow.StatusDot(tweak);
            ImGui.TextColored(color, glyph);
            ImGui.SameLine();
            ImGui.Text(tweak.Name);
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        if (ImGui.Button("Open Settings"))
            plugin.ToggleConfigUi();

        ImGui.SameLine();
        ImGui.TextDisabled("(honk)");
    }
}
