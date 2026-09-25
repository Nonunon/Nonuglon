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

    /// <summary>Maps each tweak's CommandNames (canonical name + aliases) to the
    /// tweak instance itself, built once from Tweaks below. Lets OnCommand route a
    /// chat subcommand to the right tweak's HandleCommand without a hand-maintained
    /// case per tweak - adding a tweak to the list above is enough to also wire up
    /// its chat command(s), as long as it declares CommandNames.</summary>
    private readonly Dictionary<string, TweakBase> tweakCommands = new(StringComparer.OrdinalIgnoreCase);

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
        Tweaks.Add(new InactiveFps());

        foreach (var tweak in Tweaks)
        {
            foreach (var name in tweak.CommandNames)
                tweakCommands[name] = tweak;

            if (tweak.ConfigEnabled) tweak.EnableTweak();
        }

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
                if (tweakCommands.TryGetValue(parts[0], out var tweak))
                    tweak.HandleCommand(parts[1..]);
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
