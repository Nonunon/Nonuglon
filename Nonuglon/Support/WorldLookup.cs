using System;
using ECommons.DalamudServices;
using Lumina.Excel.Sheets;

namespace Nonuglon.Support;

/// <summary>Shared helper for resolving a typed world name into its Excel sheet
/// row. Centralized here so the config UI's favorite-add form and the
/// "/Nonuglon autopillion target add" chat command validate world names the same
/// way instead of duplicating the lookup.</summary>
public static class WorldLookup
{
    /// <summary>Case-insensitive lookup restricted to real, currently selectable
    /// worlds (IsPublic) - filters out the test/beta rows the sheet also carries,
    /// which would otherwise "succeed" as a favorite that can never match anyone.
    /// Walks the sheet manually rather than LINQ FirstOrDefault: a not-found result
    /// there is a default-constructed row struct, and touching its properties
    /// (they read through ExcelPage/RowOffset) isn't something to rely on being
    /// safe - returning early instead means a not-found row's properties are never
    /// touched at all.</summary>
    public static bool TryFindWorld(string worldName, out World world)
    {
        foreach (var candidate in Svc.Data.GetExcelSheet<World>())
        {
            if (candidate.IsPublic && string.Equals(candidate.Name.ExtractText(), worldName, StringComparison.OrdinalIgnoreCase))
            {
                world = candidate;
                return true;
            }
        }

        world = default;
        return false;
    }
}
