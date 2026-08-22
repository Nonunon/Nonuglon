using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Plugin;
using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using ECommons;
using Nonuglon.Tweaks;
using Nonuglon.Windows;

namespace Nonuglon;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IChatGui ChatGui { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;

    private const string CommandName = "/Nonuglon";
    private const string CommandNameAlias = "/nonuglon";

    public static Configuration Configuration { get; private set; } = null!;

    public readonly WindowSystem WindowSystem = new("Nonuglon");
    private ConfigWindow ConfigWindow { get; init; }
    private MainWindow MainWindow { get; init; }

    // -- Tweaks --
    public readonly List<TweakBase> Tweaks = [];

    public Plugin()
    {
        ECommonsMain.Init(PluginInterface, this, ECommons.Module.All);

        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        ConfigWindow = new ConfigWindow(this);
        MainWindow = new MainWindow(this);

        WindowSystem.AddWindow(ConfigWindow);
        WindowSystem.AddWindow(MainWindow);

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Opens Nonuglon settings. See /Nonuglon help for subcommands."
        });

        // Lowercase alias so /nonuglon works the same as /Nonuglon. Hidden from the
        // command help list so it doesn't show up as a duplicate entry there.
        CommandManager.AddHandler(CommandNameAlias, new CommandInfo(OnCommand)
        {
            HelpMessage = "Opens Nonuglon settings. See /Nonuglon help for subcommands.",
            ShowInHelp = false
        });

        // Tell the UI system that we want our windows to be drawn through the window system
        PluginInterface.UiBuilder.Draw += WindowSystem.Draw;

        // This adds a button to the plugin installer entry of this plugin which allows
        // toggling the display status of the configuration ui
        PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUi;

        // Adds another button doing the same but for the main ui of the plugin
        PluginInterface.UiBuilder.OpenMainUi += ToggleMainUi;

        Tweaks.Add(new InstantReturn());
        Tweaks.Add(new AutoPillion());
        Tweaks.Add(new EntrustChocoboDuplicates());

        if (Configuration.InstantReturnEnabled) Tweaks[0].EnableTweak();
        if (Configuration.AutoPillionEnabled) Tweaks[1].EnableTweak();
        if (Configuration.EntrustChocoboDuplicatesEnabled) Tweaks[2].EnableTweak();

        Log.Information($"===A cool log message from {PluginInterface.Manifest.Name}===");
    }

    public void Dispose()
    {
        foreach (var tweak in Tweaks)
            tweak.Dispose();
        Tweaks.Clear();

        // Unregister all actions to not leak anything during disposal of plugin
        PluginInterface.UiBuilder.Draw -= WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleMainUi;

        WindowSystem.RemoveAllWindows();

        ConfigWindow.Dispose();
        MainWindow.Dispose();

        CommandManager.RemoveHandler(CommandName);
        CommandManager.RemoveHandler(CommandNameAlias);

        ECommonsMain.Dispose();
    }

    /// <summary>Finds the loaded tweak of type T and enables/disables it. Used by
    /// both the config window checkboxes and the slash command handler, so the two
    /// stay in sync with each other and with Configuration.</summary>
    public bool SetTweakEnabled<T>(bool enabled) where T : TweakBase
    {
        var tweak = Tweaks.Find(t => t is T);
        if (tweak is null) return false;

        if (enabled) tweak.EnableTweak();
        else tweak.DisableTweak();

        return true;
    }

    private void OnCommand(string command, string args)
    {
        var parts = args.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            ToggleConfigUi();
            return;
        }

        switch (parts[0].ToLowerInvariant())
        {
            case "instantreturn":
            case "quickreturn":
                HandleInstantReturnCommand(parts);
                break;
            case "autopillion":
            case "pillion":
                HandleAutoPillionCommand(parts);
                break;
            case "entrustchocobo":
            case "entrust":
            case "chocobo":
                HandleEntrustCommand(parts);
                break;
            case "config":
            case "settings":
                ToggleConfigUi();
                break;
            case "help":
            case "?":
                PrintUsage();
                break;
            default:
                Print($"Unknown subcommand \"{parts[0]}\". Try /Nonuglon help.");
                break;
        }
    }

    private void HandleInstantReturnCommand(string[] parts)
    {
        if (parts.Length < 2) { PrintUsage(); return; }

        if (parts[1].Equals("leaveparty", StringComparison.OrdinalIgnoreCase))
        {
            if (parts.Length < 3 || !ResolveBool(parts[2], Configuration.InstantReturnLeaveParty, out var leaveParty))
            {
                Print("Usage: /Nonuglon instantreturn leaveparty <on|off|toggle>");
                return;
            }

            Configuration.InstantReturnLeaveParty = leaveParty;
            Configuration.Save();
            Print($"Quick Return: leave party first {(leaveParty ? "enabled" : "disabled")}.");
            return;
        }

        if (!ResolveBool(parts[1], Configuration.InstantReturnEnabled, out var enabled))
        {
            Print("Usage: /Nonuglon instantreturn <on|off|toggle>");
            return;
        }

        Configuration.InstantReturnEnabled = enabled;
        Configuration.Save();
        SetTweakEnabled<InstantReturn>(enabled);
        Print($"Quick Return {(enabled ? "enabled" : "disabled")}.");
    }

    private void HandleAutoPillionCommand(string[] parts)
    {
        if (parts.Length < 2) { PrintUsage(); return; }

        switch (parts[1].ToLowerInvariant())
        {
            case "restrict":
                if (parts.Length < 3 || !ResolveBool(parts[2], Configuration.AutoPillionRestrictToPerson, out var restrict))
                {
                    Print("Usage: /Nonuglon autopillion restrict <on|off|toggle>");
                    return;
                }
                Configuration.AutoPillionRestrictToPerson = restrict;
                Configuration.Save();
                Print($"Auto Pillion: restrict to one person {(restrict ? "enabled" : "disabled")}.");
                return;

            case "target":
                var name = string.Join(' ', parts.Skip(2));
                if (name.Equals("clear", StringComparison.OrdinalIgnoreCase) || name.Equals("none", StringComparison.OrdinalIgnoreCase))
                    name = string.Empty;

                Configuration.AutoPillionTargetName = name;
                Configuration.Save();
                Print(string.IsNullOrEmpty(name) ? "Auto Pillion: target cleared." : $"Auto Pillion: target set to \"{name}\".");
                return;

            case "timeout":
                if (parts.Length < 3 || !int.TryParse(parts[2], out var ms))
                {
                    Print("Usage: /Nonuglon autopillion timeout <ms, 500-10000>");
                    return;
                }
                ms = Math.Clamp(ms, 500, 10000);
                Configuration.AutoPillionRetryTimeoutMs = ms;
                Configuration.Save();
                Print($"Auto Pillion: retry timeout set to {ms}ms.");
                return;

            default:
                if (!ResolveBool(parts[1], Configuration.AutoPillionEnabled, out var enabled))
                {
                    Print("Usage: /Nonuglon autopillion <on|off|toggle>");
                    return;
                }
                Configuration.AutoPillionEnabled = enabled;
                Configuration.Save();
                SetTweakEnabled<AutoPillion>(enabled);
                Print($"Auto Pillion {(enabled ? "enabled" : "disabled")}.");
                return;
        }
    }

    private void HandleEntrustCommand(string[] parts)
    {
        if (parts.Length < 2 || !ResolveBool(parts[1], Configuration.EntrustChocoboDuplicatesEnabled, out var enabled))
        {
            Print("Usage: /Nonuglon entrustchocobo <on|off|toggle>");
            return;
        }

        Configuration.EntrustChocoboDuplicatesEnabled = enabled;
        Configuration.Save();
        SetTweakEnabled<EntrustChocoboDuplicates>(enabled);
        Print($"Saddlebag Entrust Duplicates {(enabled ? "enabled" : "disabled")}.");
    }

    private static bool TryParseBool(string s, out bool value)
    {
        switch (s.ToLowerInvariant())
        {
            case "true": case "on": case "1": case "yes": case "enable": case "enabled":
                value = true;
                return true;
            case "false": case "off": case "0": case "no": case "disable": case "disabled":
                value = false;
                return true;
            default:
                value = false;
                return false;
        }
    }

    /// <summary>Same as TryParseBool, but also accepts "toggle" to flip whatever the
    /// current value already is - handy for macros/hotkeys where you don't want to
    /// track state yourself.</summary>
    private static bool ResolveBool(string s, bool currentValue, out bool value)
    {
        if (s.Equals("toggle", StringComparison.OrdinalIgnoreCase))
        {
            value = !currentValue;
            return true;
        }

        return TryParseBool(s, out value);
    }

    private static void Print(string message) => ChatGui.Print($"[Nonuglon] {message}");

    private static void PrintUsage()
    {
        Print("Usage:");
        Print("  /Nonuglon - open settings window");
        Print("  /Nonuglon instantreturn <on|off|toggle>");
        Print("  /Nonuglon instantreturn leaveparty <on|off|toggle>");
        Print("  /Nonuglon autopillion <on|off|toggle>");
        Print("  /Nonuglon autopillion restrict <on|off|toggle>");
        Print("  /Nonuglon autopillion target <name|clear>");
        Print("  /Nonuglon autopillion timeout <ms, 500-10000>");
        Print("  /Nonuglon entrustchocobo <on|off|toggle>");
    }

    public void ToggleConfigUi() => ConfigWindow.Toggle();
    public void ToggleMainUi() => MainWindow.Toggle();
}
