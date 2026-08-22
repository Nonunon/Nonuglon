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
        // Only initializing the modules our tweaks actually touch. ObjectLife (VFX +
        // GameObject ctor hooks) and SplatoonAPI are unused by anything in this plugin -
        // dropping them removes that startup/shutdown noise from /xllog and skips
        // installing hooks we never needed. DalamudReflector and ObjectFunctions are kept
        // since ECommons.UIHelpers.AddonMasterImplementations (used by
        // EntrustChocoboDuplicates) may depend on them internally - left in until
        // confirmed otherwise.
        ECommonsMain.Init(PluginInterface, this, ECommons.Module.DalamudReflector, ECommons.Module.ObjectFunctions);

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

        Log.Information($"{PluginInterface.Manifest.Name} v{PluginInterface.Manifest.AssemblyVersion} loaded. " +
            $"InstantReturn={Configuration.InstantReturnEnabled}, AutoPillion={Configuration.AutoPillionEnabled}, " +
            $"EntrustChocoboDuplicates={Configuration.EntrustChocoboDuplicatesEnabled}");
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

            var previousLeaveParty = Configuration.InstantReturnLeaveParty;
            Configuration.InstantReturnLeaveParty = leaveParty;
            Configuration.Save();
            ReportStateChange("Quick Return: leave party first", previousLeaveParty, leaveParty);
            return;
        }

        if (!ResolveBool(parts[1], Configuration.InstantReturnEnabled, out var enabled))
        {
            Print("Usage: /Nonuglon instantreturn <on|off|toggle>");
            return;
        }

        var previousEnabled = Configuration.InstantReturnEnabled;
        Configuration.InstantReturnEnabled = enabled;
        Configuration.Save();
        SetTweakEnabled<InstantReturn>(enabled);
        ReportStateChange("Quick Return", previousEnabled, enabled);
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
                var previousRestrict = Configuration.AutoPillionRestrictToPerson;
                Configuration.AutoPillionRestrictToPerson = restrict;
                Configuration.Save();
                ReportStateChange("Auto Pillion: restrict to one person", previousRestrict, restrict);
                return;

            case "target":
                var name = string.Join(' ', parts.Skip(2));
                if (name.Equals("clear", StringComparison.OrdinalIgnoreCase) || name.Equals("none", StringComparison.OrdinalIgnoreCase))
                    name = string.Empty;

                var previousTarget = Configuration.AutoPillionTargetName;
                Configuration.AutoPillionTargetName = name;
                Configuration.Save();
                ReportStateChange(previousTarget, name,
                    string.IsNullOrEmpty(name) ? "Auto Pillion: target cleared." : $"Auto Pillion: target set to \"{name}\".");
                return;

            case "timeout":
                if (parts.Length < 3 || !int.TryParse(parts[2], out var ms))
                {
                    Print("Usage: /Nonuglon autopillion timeout <ms, 500-10000>");
                    return;
                }
                ms = Math.Clamp(ms, 500, 10000);
                var previousMs = Configuration.AutoPillionRetryTimeoutMs;
                Configuration.AutoPillionRetryTimeoutMs = ms;
                Configuration.Save();
                ReportStateChange(previousMs, ms, $"Auto Pillion: retry timeout set to {ms}ms.");
                return;

            default:
                if (!ResolveBool(parts[1], Configuration.AutoPillionEnabled, out var enabled))
                {
                    Print("Usage: /Nonuglon autopillion <on|off|toggle>");
                    return;
                }
                var previousEnabled = Configuration.AutoPillionEnabled;
                Configuration.AutoPillionEnabled = enabled;
                Configuration.Save();
                SetTweakEnabled<AutoPillion>(enabled);
                ReportStateChange("Auto Pillion", previousEnabled, enabled);
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

        var previousEnabled = Configuration.EntrustChocoboDuplicatesEnabled;
        Configuration.EntrustChocoboDuplicatesEnabled = enabled;
        Configuration.Save();
        SetTweakEnabled<EntrustChocoboDuplicates>(enabled);
        ReportStateChange("Saddlebag Entrust Duplicates", previousEnabled, enabled);
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

    /// <summary>Prints to chat AND mirrors the same message to the plugin log at
    /// Verbose level, so every chat message has a corresponding /xllog entry even
    /// after it scrolls out of the chat window. Verbose (not Debug) on purpose -
    /// Debug is reserved for the "no-op, suppressed from chat" messages in
    /// ReportStateChange, so the two log levels stay distinct: Verbose = full
    /// cookie trail of everything sent to chat, Debug = the extra stuff that
    /// didn't make it to chat.</summary>
    private static void Print(string message)
    {
        ChatGui.Print($"[Nonuglon] {message}");
        Log.Verbose(message);
    }

    /// <summary>Prints a state-change message to chat only if the value actually
    /// changed. If the command was a no-op (e.g. "instantreturn on" while it was
    /// already on), the message is routed to the plugin log instead, so it's still
    /// visible via /xllog without cluttering chat. Generic so it covers any
    /// comparable config value (bool toggles, the target name, the timeout ms),
    /// not just booleans.</summary>
    private static void ReportStateChange<T>(T previousValue, T newValue, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(previousValue, newValue))
            Print(message);
        else
            Log.Debug($"{message} (already was, no change)");
    }

    /// <summary>Convenience overload for the common "X enabled/disabled." shape used
    /// by every boolean tweak toggle.</summary>
    private static void ReportStateChange(string label, bool previousValue, bool newValue) =>
        ReportStateChange(previousValue, newValue, $"{label} {(newValue ? "enabled" : "disabled")}.");

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
