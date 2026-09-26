using System;
using System.Linq;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using ECommons.DalamudServices;
using Nonuglon.Support;
using static Nonuglon.Support.CommandText;

namespace Nonuglon.Tweaks;

/// <summary>
/// Ported from ffxiv-bundleoftweaks' (https://github.com/Jaksuhn/ffxiv-bundleoftweaks,
/// BSD-3-Clause-licensed) Tweaks/AutoPillion.cs.
/// Automatically hops onto a nearby mount with an open pillion seat. Optionally
/// restricted to a saved list of favorite people via config, populated either
/// through this tweak's own options UI, the right-click context menu
/// (AutoPillionContextMenu), or Chat 2's context menu integration
/// (AutoPillionChat2Ipc).
///
/// No TaskManager here on purpose: a single RidePillion call doesn't need a task
/// queue, and ECommons' TaskManager wait-timeout defaults to ~30s, which made a
/// missed attempt (dismount, target out of range mid-ride, etc.) lock the tweak up
/// for way too long. A plain timestamp-based retry throttle is simpler and its
/// timeout is fully in our control via AutoPillionRetryTimeoutMs.
/// </summary>
public unsafe class AutoPillion : TweakBase
{
    public override string Name => "Auto Pillion";
    public override string Description => "Automatically hops onto a nearby mount with an open pillion seat. Optionally restrict to a saved list of favorite people.";

    public override bool ConfigEnabled
    {
        get => Plugin.Configuration.AutoPillionEnabled;
        set => Plugin.Configuration.AutoPillionEnabled = value;
    }

    public override string[] CommandNames => ["autopillion", "pillion"];

    // All the subcommands beyond plain on/off/toggle only show up (and only
    // work, see HandleCommand below) once the tweak itself is on.
    public override string[] UsageLines =>
        Enabled
            ?
            [
                ..base.UsageLines,
                $"/Nonuglon {CommandNames[0]} restrict <on|off|toggle>",
                $"/Nonuglon {CommandNames[0]} target <add|remove|enable|disable|list|clear> [name@world]",
                $"/Nonuglon {CommandNames[0]} contextmenu <on|off|toggle>",
                $"/Nonuglon {CommandNames[0]} chat2menu <on|off|toggle>",
                $"/Nonuglon {CommandNames[0]} timeout <ms, 500-10000>",
            ]
            : base.UsageLines;

    /// <summary>0 = idle/not attempting. Otherwise, the Environment.TickCount64 at
    /// which the current attempt should be considered timed out.</summary>
    private long attemptExpiresAt;

    private AutoPillionContextMenu? contextMenu;
    private AutoPillionChat2Ipc? chat2Ipc;

    /// <summary>Backing fields for the "add a favorite" name/world text inputs in
    /// DrawOptions - live on the tweak instance (not static/local) since ImGui
    /// needs somewhere stable to write into across frames.</summary>
    private string newFavoriteNameInput = string.Empty;
    private string newFavoriteWorldInput = string.Empty;
    /// <summary>Set by TryAddFavorite when the last Add click failed validation
    /// (empty field, unknown world, or a duplicate) - drawn as an inline warning
    /// under the input row until the next successful add or edit.</summary>
    private string? newFavoriteError;

    protected override void Enable()
    {
        Svc.Framework.Update += OnUpdate;
        SyncIntegrations(tweakEnabled: true);
    }

    protected override void Disable()
    {
        Svc.Framework.Update -= OnUpdate;
        attemptExpiresAt = 0;
        SyncIntegrations(tweakEnabled: false);
    }

    /// <summary>Creates or tears down the context-menu integrations to match the
    /// current config flags, given whether the tweak itself is (about to be)
    /// enabled. Takes an explicit flag rather than reading the Enabled property
    /// directly: TweakBase.EnableTweak() only flips Enabled to true AFTER Enable()
    /// returns, so reading Enabled from inside Enable() itself would still see
    /// false. Everywhere else (DrawOptions' checkboxes, called well after
    /// EnableTweak() has completed), Enabled is safe to read directly.</summary>
    public void SyncIntegrations(bool tweakEnabled)
    {
        var config = Plugin.Configuration;

        var wantContextMenu = tweakEnabled && config.AutoPillionContextMenuEnabled;
        if (wantContextMenu && contextMenu is null) contextMenu = new AutoPillionContextMenu();
        else if (!wantContextMenu && contextMenu is not null) { contextMenu.Dispose(); contextMenu = null; }

        var wantChat2Ipc = tweakEnabled && config.AutoPillionChat2ContextMenuEnabled;
        if (wantChat2Ipc && chat2Ipc is null) chat2Ipc = new AutoPillionChat2Ipc();
        else if (!wantChat2Ipc && chat2Ipc is not null) { chat2Ipc.Dispose(); chat2Ipc = null; }
    }

    private void OnUpdate(IFramework framework)
    {
        var player = Svc.Objects.LocalPlayer;
        if (player is null || player.IsMounted())
        {
            attemptExpiresAt = 0;
            return;
        }

        // Still within the current attempt's window - don't spam RidePillion every
        // frame while waiting to see if the last attempt lands. Once the window
        // passes without us getting mounted, this falls through and tries again.
        if (attemptExpiresAt != 0 && Environment.TickCount64 < attemptExpiresAt)
            return;
        attemptExpiresAt = 0;

        var config = Plugin.Configuration;

        if (config.AutoPillionRestrictToPerson)
        {
            foreach (var favorite in config.AutoPillionFavorites)
            {
                if (!favorite.Enabled) continue;

                var target = GameObjectPillionExtensions.FindPlayerByNameAndWorld(favorite.Name, favorite.WorldId);
                if (target is null || target.EntityId == player.EntityId) continue;
                if (target.CurrentDistance >= 3) continue;
                if (!target.CanRidePillion()) continue;

                MountUpWith(target);
                return;
            }
            return;
        }

        var member = Svc.Party.FirstOrDefault(o =>
            o != null && o.EntityId != player.EntityId &&
            o.GameObject is { } go && go.CurrentDistance < 3 && go.CanRidePillion());

        if (member?.GameObject is { } partyTarget)
            MountUpWith(partyTarget);
    }

    private void MountUpWith(IGameObject target)
    {
        Svc.Log.Debug($"[AutoPillion] Mounting up with {target.Name.TextValue}");
        target.BattleChara()->RidePillion(10);
        attemptExpiresAt = Environment.TickCount64 + Plugin.Configuration.AutoPillionRetryTimeoutMs;
    }

    /// <summary>Small muted "(?)" next to a checkbox that shows a tooltip on
    /// hover - standard ImGui idiom for explaining a setting without permanently
    /// occupying space with a full sentence. warning: true recolors it amber
    /// instead of the default muted gray, for cases where the setting is on but
    /// something about it isn't actually working right now (e.g. Chat 2 not being
    /// loaded while its integration is enabled).</summary>
    private static void HelpMarker(string tooltip, bool warning = false)
    {
        ImGui.SameLine();
        ImGui.TextColored(warning ? UiColors.Warning : UiColors.Disabled, "(?)");
        if (ImGui.IsItemHovered())
        {
            ImGui.BeginTooltip();
            ImGui.PushTextWrapPos(ImGui.GetFontSize() * 25f);
            ImGui.TextUnformatted(tooltip);
            ImGui.PopTextWrapPos();
            ImGui.EndTooltip();
        }
    }

    public override void DrawOptions()
    {
        var config = Plugin.Configuration;

        var restrictToPerson = config.AutoPillionRestrictToPerson;
        if (ImGui.Checkbox("Restrict to favorites##AutoPillion", ref restrictToPerson))
        {
            config.AutoPillionRestrictToPerson = restrictToPerson;
            config.Save();
        }

        ImGui.Spacing();
        ImGui.TextDisabled("Context menu integrations:");

        var contextMenuEnabled = config.AutoPillionContextMenuEnabled;
        if (ImGui.Checkbox("Context Menu##AutoPillionContextMenu", ref contextMenuEnabled))
        {
            config.AutoPillionContextMenuEnabled = contextMenuEnabled;
            config.Save();
            SyncIntegrations(tweakEnabled: Enabled);
        }
        HelpMarker("Adds \"Add to Auto Pillion\" to the right-click menu on the party list, friend list, and chat log (among other places you can right-click a player).");

        var chat2Enabled = config.AutoPillionChat2ContextMenuEnabled;
        if (ImGui.Checkbox("Chat 2 Context Menu##AutoPillionChat2ContextMenu", ref chat2Enabled))
        {
            config.AutoPillionChat2ContextMenuEnabled = chat2Enabled;
            config.Save();
            SyncIntegrations(tweakEnabled: Enabled);
        }
        HelpMarker(
            "Right-click a name in Chat 2's own chat log, then look under Integrations for \"Add to Auto Pillion\". Requires the Chat 2 plugin.",
            warning: chat2Enabled && !PluginDetection.IsPluginLoaded("ChatTwo"));

        if (chat2Enabled && !PluginDetection.IsPluginLoaded("ChatTwo"))
        {
            ImGui.PushTextWrapPos(ImGui.GetContentRegionAvail().X + ImGui.GetCursorPosX());
            ImGui.TextColored(UiColors.Warning, "Chat 2 not detected - this integration has no effect until it's installed and loaded.");
            ImGui.PopTextWrapPos();
        }

        ImGui.Spacing();
        ImGui.TextDisabled("Favorites:");

        const float addButtonWidth = 50f;
        var fieldWidth = (ImGui.GetContentRegionAvail().X - addButtonWidth - (ImGui.GetStyle().ItemSpacing.X * 2)) / 2f;

        ImGui.SetNextItemWidth(fieldWidth);
        ImGui.InputTextWithHint("##AutoPillionNewFavoriteName", "Character name", ref newFavoriteNameInput, 64);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(fieldWidth);
        ImGui.InputTextWithHint("##AutoPillionNewFavoriteWorld", "World", ref newFavoriteWorldInput, 32);
        ImGui.SameLine();
        if (ImGui.Button("Add##AutoPillionFavorite"))
            TryAddFavorite(config);

        if (newFavoriteError is { } error)
        {
            ImGui.PushTextWrapPos(ImGui.GetContentRegionAvail().X + ImGui.GetCursorPosX());
            ImGui.TextColored(UiColors.Warning, error);
            ImGui.PopTextWrapPos();
        }

        if (config.AutoPillionFavorites.Count == 0)
        {
            ImGui.TextDisabled("(none saved yet - add one above)");
        }
        else
        {
            // Shown in list order, same as OnUpdate tries them (top = tried
            // first) - reordering priority means removing and re-adding in the
            // order you want. Removal is deferred to after the loop instead of
            // RemoveAt-ing mid-iteration, so indices don't shift out from under
            // the rows still to be drawn.
            var removeIndex = -1;
            for (var i = 0; i < config.AutoPillionFavorites.Count; i++)
            {
                var favorite = config.AutoPillionFavorites[i];

                var favoriteEnabled = favorite.Enabled;
                if (ImGui.Checkbox($"##AutoPillionFavoriteEnabled{i}", ref favoriteEnabled))
                {
                    favorite.Enabled = favoriteEnabled;
                    config.Save();
                }
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Unchecked: kept in the list, but skipped by matching - lets you rule someone out (or prefer another favorite ahead of them) without deleting them.");
                ImGui.SameLine();

                if (favorite.Enabled)
                    ImGui.Text($"{favorite.Name}@{favorite.WorldName}");
                else
                    ImGui.TextDisabled($"{favorite.Name}@{favorite.WorldName}");

                ImGui.SameLine();
                if (ImGui.Button($"x##AutoPillionFavorite{i}"))
                    removeIndex = i;
            }

            if (removeIndex >= 0)
            {
                config.AutoPillionFavorites.RemoveAt(removeIndex);
                config.Save();
            }
        }

        ImGui.Spacing();
        var retryTimeout = config.AutoPillionRetryTimeoutMs;
        if (ImGui.SliderInt("Retry timeout (ms)##AutoPillion", ref retryTimeout, 500, 10000))
        {
            config.AutoPillionRetryTimeoutMs = retryTimeout;
            config.Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("How long to wait for a ride attempt to land before giving up and retrying. Lower = faster remount after dismounting, but more spammy if it keeps missing.");
    }

    /// <summary>Validates and adds a favorite from the two input fields above -
    /// both must be non-empty, the world must resolve against the real World Excel
    /// sheet (via WorldLookup, so a typo doesn't silently save a favorite that can
    /// never match anyone), and the resulting name+world pair must not already be
    /// saved. Sets newFavoriteError on failure instead of throwing/logging, since
    /// this is a plain user-input mistake, not an exceptional condition.</summary>
    private void TryAddFavorite(Configuration config)
    {
        var name = newFavoriteNameInput.Trim();
        var worldInput = newFavoriteWorldInput.Trim();

        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(worldInput))
        {
            newFavoriteError = "Enter both a character name and a world.";
            return;
        }

        if (!WorldLookup.TryFindWorld(worldInput, out var world))
        {
            newFavoriteError = $"Unknown world \"{worldInput}\".";
            return;
        }

        if (config.AutoPillionFavorites.Any(f => f.Name == name && f.WorldId == world.RowId))
        {
            newFavoriteError = $"\"{name}@{world.Name.ExtractText()}\" is already a favorite.";
            return;
        }

        config.AutoPillionFavorites.Add(new AutoPillionFavorite { Name = name, WorldId = world.RowId, WorldName = world.Name.ExtractText() });
        config.Save();

        newFavoriteNameInput = string.Empty;
        newFavoriteWorldInput = string.Empty;
        newFavoriteError = null;
    }

    public override void HandleCommand(string[] args)
    {
        // Every subcommand below only exists once the tweak itself is on - while
        // it's off, this falls straight through to the plain on/off/toggle usage
        // error at the bottom, exactly as if none of these words were ever valid
        // here.
        if (!Enabled || args.Length == 0) { base.HandleCommand(args); return; }

        switch (args[0].ToLowerInvariant())
        {
            case "restrict":
            {
                if (args.Length < 2 || !ResolveBool(args[1], Plugin.Configuration.AutoPillionRestrictToPerson, out var restrict))
                {
                    Print($"Usage: /Nonuglon {CommandNames[0]} restrict <on|off|toggle>");
                    return;
                }
                var previousRestrict = Plugin.Configuration.AutoPillionRestrictToPerson;
                Plugin.Configuration.AutoPillionRestrictToPerson = restrict;
                Plugin.Configuration.Save();
                ReportStateChange("Auto Pillion: restrict to one person", previousRestrict, restrict);
                return;
            }

            case "target":
                HandleTargetCommand(args);
                return;

            case "contextmenu":
            {
                if (args.Length < 2 || !ResolveBool(args[1], Plugin.Configuration.AutoPillionContextMenuEnabled, out var contextMenuEnabled))
                {
                    Print($"Usage: /Nonuglon {CommandNames[0]} contextmenu <on|off|toggle>");
                    return;
                }
                var previousContextMenu = Plugin.Configuration.AutoPillionContextMenuEnabled;
                Plugin.Configuration.AutoPillionContextMenuEnabled = contextMenuEnabled;
                Plugin.Configuration.Save();
                SyncIntegrations(tweakEnabled: Enabled);
                ReportStateChange("Auto Pillion: right-click menu", previousContextMenu, contextMenuEnabled);
                return;
            }

            case "chat2menu":
            {
                if (args.Length < 2 || !ResolveBool(args[1], Plugin.Configuration.AutoPillionChat2ContextMenuEnabled, out var chat2MenuEnabled))
                {
                    Print($"Usage: /Nonuglon {CommandNames[0]} chat2menu <on|off|toggle>");
                    return;
                }
                var previousChat2Menu = Plugin.Configuration.AutoPillionChat2ContextMenuEnabled;
                Plugin.Configuration.AutoPillionChat2ContextMenuEnabled = chat2MenuEnabled;
                Plugin.Configuration.Save();
                SyncIntegrations(tweakEnabled: Enabled);
                ReportStateChange("Auto Pillion: Chat 2 menu", previousChat2Menu, chat2MenuEnabled);
                return;
            }

            case "timeout":
            {
                if (args.Length < 2 || !int.TryParse(args[1], out var ms))
                {
                    Print($"Usage: /Nonuglon {CommandNames[0]} timeout <ms, 500-10000>");
                    return;
                }
                ms = Math.Clamp(ms, 500, 10000);
                var previousMs = Plugin.Configuration.AutoPillionRetryTimeoutMs;
                Plugin.Configuration.AutoPillionRetryTimeoutMs = ms;
                Plugin.Configuration.Save();
                ReportStateChange(previousMs, ms, $"Auto Pillion: retry timeout set to {ms}ms.");
                return;
            }

            default:
                base.HandleCommand(args);
                return;
        }
    }

    /// <summary>Handles "/Nonuglon autopillion target add|remove|list|clear". A
    /// small dispatcher rather than folding this into HandleCommand's switch, since
    /// managing a list needs more sub-verbs than the plain on/off toggles
    /// elsewhere in that switch. add/remove take "name@world" now that favorites
    /// are world-aware - see TryParseNameAtWorld below.</summary>
    private void HandleTargetCommand(string[] args)
    {
        var favorites = Plugin.Configuration.AutoPillionFavorites;

        if (args.Length < 2)
        {
            Print($"Usage: /Nonuglon {CommandNames[0]} target <add|remove|enable|disable|list|clear> [name@world]");
            return;
        }

        switch (args[1].ToLowerInvariant())
        {
            case "add":
            {
                var raw = string.Join(' ', args.Skip(2));
                if (!TryParseNameAtWorld(raw, out var name, out var worldInput))
                {
                    Print($"Usage: /Nonuglon {CommandNames[0]} target add <name>@<world>");
                    return;
                }
                if (!WorldLookup.TryFindWorld(worldInput, out var world))
                {
                    Print($"Auto Pillion: unknown world \"{worldInput}\".");
                    return;
                }
                var worldName = world.Name.ExtractText();
                if (favorites.Any(f => f.Name == name && f.WorldId == world.RowId))
                {
                    Svc.Log.Debug($"Auto Pillion: \"{name}@{worldName}\" is already a favorite. (already was, no change)");
                    return;
                }
                favorites.Add(new AutoPillionFavorite { Name = name, WorldId = world.RowId, WorldName = worldName });
                Plugin.Configuration.Save();
                Print($"Auto Pillion: added \"{name}@{worldName}\" as a favorite.");
                return;
            }

            case "remove":
            {
                var raw = string.Join(' ', args.Skip(2));
                if (!TryParseNameAtWorld(raw, out var name, out var worldInput))
                {
                    Print($"Usage: /Nonuglon {CommandNames[0]} target remove <name>@<world>");
                    return;
                }
                var removed = favorites.RemoveAll(f =>
                    f.Name.Equals(name, StringComparison.Ordinal) &&
                    f.WorldName.Equals(worldInput, StringComparison.OrdinalIgnoreCase));
                if (removed > 0)
                {
                    Plugin.Configuration.Save();
                    Print($"Auto Pillion: removed \"{name}@{worldInput}\" from favorites.");
                }
                else
                {
                    Svc.Log.Debug($"Auto Pillion: \"{name}@{worldInput}\" wasn't a favorite. (already was, no change)");
                }
                return;
            }

            case "enable":
            case "disable":
            {
                var wantEnabled = args[1].Equals("enable", StringComparison.OrdinalIgnoreCase);
                var raw = string.Join(' ', args.Skip(2));
                if (!TryParseNameAtWorld(raw, out var name, out var worldInput))
                {
                    Print($"Usage: /Nonuglon {CommandNames[0]} target {args[1].ToLowerInvariant()} <name>@<world>");
                    return;
                }
                var favorite = favorites.FirstOrDefault(f =>
                    f.Name.Equals(name, StringComparison.Ordinal) &&
                    f.WorldName.Equals(worldInput, StringComparison.OrdinalIgnoreCase));
                if (favorite is null)
                {
                    Print($"Auto Pillion: \"{name}@{worldInput}\" isn't a saved favorite.");
                    return;
                }
                var previousEnabled = favorite.Enabled;
                favorite.Enabled = wantEnabled;
                Plugin.Configuration.Save();
                ReportStateChange($"Auto Pillion favorite \"{favorite.Name}@{favorite.WorldName}\"", previousEnabled, wantEnabled);
                return;
            }

            case "list":
                Print(favorites.Count == 0
                    ? "Auto Pillion: no favorites saved."
                    : $"Auto Pillion favorites: {string.Join(", ", favorites.Select(f => f.Enabled ? $"{f.Name}@{f.WorldName}" : $"{f.Name}@{f.WorldName} (disabled)"))}");
                return;

            case "clear":
                if (favorites.Count == 0)
                {
                    Svc.Log.Debug("Auto Pillion: favorites already empty. (already was, no change)");
                    return;
                }
                favorites.Clear();
                Plugin.Configuration.Save();
                Print("Auto Pillion: favorites cleared.");
                return;

            default:
                Print($"Usage: /Nonuglon {CommandNames[0]} target <add|remove|enable|disable|list|clear> [name@world]");
                return;
        }
    }

    /// <summary>Splits "Firstname Lastname@World" on the LAST '@' - character
    /// names never contain '@' and world names never contain spaces, so this is
    /// unambiguous even though the name half can have a space in it.</summary>
    private static bool TryParseNameAtWorld(string raw, out string name, out string world)
    {
        var at = raw.LastIndexOf('@');
        if (at <= 0 || at == raw.Length - 1)
        {
            name = string.Empty;
            world = string.Empty;
            return false;
        }

        name = raw[..at].Trim();
        world = raw[(at + 1)..].Trim();
        return !string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(world);
    }
}
