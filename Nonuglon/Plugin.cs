using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Plugin;
using System;
using System.Collections.Generic;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using ECommons;
using Nonuglon.Tweaks;
using Nonuglon.Windows;
using static Nonuglon.Support.CommandText;

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

    /// <summary>Command name -> HandleCommand, for both tweaks and mini-tweaks'
    /// top-level aliases. Lets OnCommand route without a hand-maintained case
    /// per tweak.</summary>
    private readonly Dictionary<string, Action<string[]>> tweakCommands = new(StringComparer.OrdinalIgnoreCase);

    private readonly InactiveFps inactiveFps = new();
    private readonly RenderToggle renderToggle = new();

    public Plugin()
    {
        // Only the ECommons modules our tweaks actually touch (DalamudReflector,
        // ObjectFunctions for EntrustChocoboDuplicates) - others would just add
        // startup/shutdown noise to /xllog.
        ECommonsMain.Init(PluginInterface, this, ECommons.Module.DalamudReflector, ECommons.Module.ObjectFunctions);

        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

#pragma warning disable CS0618 // AutoPillionTargetName is Obsolete - this is the one sanctioned read/write of it, migrating it into the new list on first load after upgrading.
        if (!string.IsNullOrEmpty(Configuration.AutoPillionTargetName) && Configuration.AutoPillionFavoriteTargets.Count == 0)
        {
            Configuration.AutoPillionFavoriteTargets.Add(Configuration.AutoPillionTargetName);
            Configuration.AutoPillionTargetName = string.Empty;
            Configuration.Save();
        }
#pragma warning restore CS0618

#pragma warning disable CS0618 // AutoPillionFavoriteTargets is Obsolete - read-only, one-time informational check. There's no world to migrate a bare name into, so this just surfaces the old list in the log instead of silently losing it.
        if (Configuration.AutoPillionFavoriteTargets.Count > 0 && Configuration.AutoPillionFavorites.Count == 0)
        {
            Log.Information($"Auto Pillion: {Configuration.AutoPillionFavoriteTargets.Count} favorite(s) from before world-aware matching are still on file " +
                $"({string.Join(", ", Configuration.AutoPillionFavoriteTargets)}) but can't be auto-migrated without knowing their world. " +
                "Re-add them via the config UI or \"/Nonuglon autopillion target add <name>@<world>\".");
        }
#pragma warning restore CS0618

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

        // Standalone raw alias, quicker than typing the full /Nonuglon prefix.
        CommandManager.AddHandler($"/{inactiveFps.CommandName}", new CommandInfo(
            (_, args) => inactiveFps.HandleCommand(args.Split(' ', StringSplitOptions.RemoveEmptyEntries)))
        {
            HelpMessage = $"Alias for /Nonuglon {inactiveFps.CommandName}. See /Nonuglon help."
        });

        // Same idea, but a bare "/rendertoggle" (no args) is a special case that
        // flips the state directly rather than hitting the usual usage error.
        CommandManager.AddHandler($"/{renderToggle.CommandName}", new CommandInfo((_, args) =>
        {
            var parts = args.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) renderToggle.ToggleAction();
            else renderToggle.HandleCommand(parts);
        })
        {
            HelpMessage = $"Alias for /Nonuglon {renderToggle.CommandName}. With no arguments, flips 3D rendering directly (mini-tweak must be on)."
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
        Tweaks.Add(new SearchInfoMenu());
        Tweaks.Add(new EstateTeleportation());
        Tweaks.Add(new Commands(inactiveFps, renderToggle));

        foreach (var tweak in Tweaks)
        {
            foreach (var name in tweak.CommandNames)
                tweakCommands[name] = tweak.HandleCommand;

            if (tweak.ConfigEnabled) tweak.EnableTweak();
        }

        // Mini-tweaks also get a top-level alias, alongside Commands' own
        // nested routing and the raw commands registered above.
        tweakCommands[inactiveFps.CommandName] = inactiveFps.HandleCommand;
        tweakCommands[renderToggle.CommandName] = renderToggle.HandleCommand;

        Log.Information($"{PluginInterface.Manifest.Name} v{PluginInterface.Manifest.AssemblyVersion} loaded. " +
            string.Join(", ", Tweaks.ConvertAll(tweak => $"{tweak.Name}={tweak.ConfigEnabled}")));
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
        CommandManager.RemoveHandler($"/{inactiveFps.CommandName}");
        CommandManager.RemoveHandler($"/{renderToggle.CommandName}");

        ECommonsMain.Dispose();
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
            case "config":
            case "settings":
                ToggleConfigUi();
                break;
            case "help":
            case "?":
                PrintUsage();
                break;
            default:
                if (tweakCommands.TryGetValue(parts[0], out var handler))
                    handler(parts[1..]);
                else
                    Print($"Unknown subcommand \"{parts[0]}\". Try /Nonuglon help.");
                break;
        }
    }

    private void PrintUsage()
    {
        Print("Usage:");
        Print("  /Nonuglon - open settings window");
        Print("  /Nonuglon config | settings - open settings window");
        Print("  /Nonuglon help | ? - show this list");
        foreach (var tweak in Tweaks)
            foreach (var line in tweak.UsageLines)
                Print($"  {line}");
    }

    public void ToggleConfigUi() => ConfigWindow.Toggle();
    public void ToggleMainUi() => MainWindow.Toggle();
}
