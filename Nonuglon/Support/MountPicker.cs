using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game;
using Dalamud.Interface.Textures;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Lumina.Excel.Sheets;

namespace Nonuglon.Support;

/// <summary>Searchable mount dropdown plus mounting helpers. The choice is a
/// plain mount id (0 = Mount Roulette) shared across characters: a character
/// that doesn't own it just falls back to roulette, so nothing gets dropped
/// from config when switching characters.</summary>
public static unsafe class MountPicker
{
    public const uint Roulette = 0;
    private const uint MountRouletteGeneralActionId = 9;
    private const float ListHeight = 260f;

    private readonly record struct MountEntry(uint Id, string Name, uint Icon);

    private static List<MountEntry>? allMounts;
    private static string search = string.Empty;
    private static bool ownedOnly = true;

    /// <summary>Every named mount in the game, sorted by name. Built once.</summary>
    private static List<MountEntry> AllMounts => allMounts ??= Svc.Data.GetExcelSheet<Mount>()
        .Where(m => m.Icon != 0 && !m.Singular.IsEmpty)
        .Select(m => new MountEntry(m.RowId, Prettify(m.Singular.ExtractText()), m.Icon))
        .OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
        .ToList();

    // English mount names are stored lowercase ("company chocobo").
    private static string Prettify(string name) =>
        Svc.ClientState.ClientLanguage == ClientLanguage.English ? CultureInfo.InvariantCulture.TextInfo.ToTitleCase(name) : name;

    public static string GetName(uint id) =>
        id == Roulette ? "Mount Roulette" : AllMounts.FirstOrDefault(m => m.Id == id).Name ?? $"Mount #{id}";

    public static bool IsOwned(uint id) => id == Roulette || PlayerState.Instance()->IsMountUnlocked(id);

    /// <summary>Summons the given mount, or roulette if it's 0 or not owned by
    /// this character. Returns false when it had to fall back.</summary>
    public static bool Use(uint id)
    {
        if (id != Roulette && IsOwned(id))
        {
            ActionManager.Instance()->UseAction(ActionType.Mount, id);
            return true;
        }

        ActionManager.Instance()->UseAction(ActionType.GeneralAction, MountRouletteGeneralActionId);
        return id == Roulette;
    }

    /// <summary>Draws the dropdown; returns true when the selection changed.</summary>
    public static bool Draw(string label, ref uint mountId)
    {
        var changed = false;
        var preview = GetName(mountId) + (IsOwned(mountId) ? "" : " (not owned here)");

        if (!ImGui.BeginCombo(label, preview, ImGuiComboFlags.HeightLarge)) return false;

        if (ImGui.IsWindowAppearing()) ImGui.SetKeyboardFocusHere();
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##MountPickerSearch", "Search mounts...", ref search, 64);
        ImGui.Checkbox("Owned on this character only##MountPicker", ref ownedOnly);
        ImGui.Separator();

        if (ImGui.BeginChild("##MountPickerList", new Vector2(0, ListHeight * ImGui.GetIO().FontGlobalScale)))
        {
            if (ImGui.Selectable("Mount Roulette", mountId == Roulette))
            {
                mountId = Roulette;
                changed = true;
                ImGui.CloseCurrentPopup();
            }

            var iconSize = new Vector2(ImGui.GetTextLineHeight());
            foreach (var mount in AllMounts)
            {
                if (search.Length > 0 && !mount.Name.Contains(search, StringComparison.OrdinalIgnoreCase)) continue;
                var owned = IsOwned(mount.Id);
                if (ownedOnly && !owned) continue;

                // Only load icons for rows actually on screen.
                if (ImGui.IsRectVisible(iconSize))
                    ImGui.Image(Svc.Texture.GetFromGameIcon(new GameIconLookup(mount.Icon)).GetWrapOrEmpty().Handle, iconSize);
                else
                    ImGui.Dummy(iconSize);
                ImGui.SameLine();

                if (!owned) ImGui.PushStyleColor(ImGuiCol.Text, UiColors.Disabled);
                if (ImGui.Selectable($"{mount.Name}##Mount{mount.Id}", mount.Id == mountId))
                {
                    mountId = mount.Id;
                    changed = true;
                    ImGui.CloseCurrentPopup();
                }
                if (!owned) ImGui.PopStyleColor();
            }
        }
        ImGui.EndChild();
        ImGui.EndCombo();

        if (changed) search = string.Empty;
        return changed;
    }
}
