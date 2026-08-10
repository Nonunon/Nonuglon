using System;
using System.Numerics;
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
            var color = tweak.Enabled ? new Vector4(0.4f, 0.9f, 0.4f, 1f) : new Vector4(0.6f, 0.6f, 0.6f, 1f);
            ImGui.TextColored(color, tweak.Enabled ? "\u25cf" : "\u25cb");
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
