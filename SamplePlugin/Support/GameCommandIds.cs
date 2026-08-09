namespace Nonuglon.Support;

/// <summary>
/// A couple of GameMain.ExecuteCommand IDs, ported from ffxiv-bundleoftweaks' clib
/// submodule (clib.Enums.CommandFlag) - pulling in just what we need instead of the
/// whole ~800-line enum.
/// </summary>
internal static class GameCommandIds
{
    /// <summary>
    /// clib.Enums.CommandFlag.ReturnIfNotLalafell / .InstantReturn (id 214).
    /// Returns to the nearest safe point on the current map directly - no confirmation
    /// dialog, no AgentReturn round-trip. This is what actually performs the teleport;
    /// it is NOT a general action, so ActionManager.UseAction is the wrong tool for it.
    /// </summary>
    public const int ReturnIfNotLalafell = 214;
}
