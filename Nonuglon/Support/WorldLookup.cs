using System;
using ECommons.DalamudServices;
using Lumina.Excel.Sheets;

namespace Nonuglon.Support;

/// <summary>Shared world-name resolution, so the config UI and chat command
/// validate names the same way.</summary>
public static class WorldLookup
{
    /// <summary>Case-insensitive, restricted to real selectable worlds
    /// (IsPublic) - filters out test/beta rows that would otherwise "succeed"
    /// unmatchably. Walks manually rather than LINQ FirstOrDefault, since a
    /// not-found result there is a default-constructed row whose properties
    /// (read through ExcelPage/RowOffset) aren't safe to touch.</summary>
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
