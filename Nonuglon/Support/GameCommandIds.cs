namespace Nonuglon.Support;

/// <summary>GameMain.ExecuteCommand IDs, ported from ffxiv-bundleoftweaks' clib
/// (clib.Enums.CommandFlag) rather than vendoring the whole ~800-line enum.</summary>
internal static class GameCommandIds
{
    /// <summary>clib's ReturnIfNotLalafell/InstantReturn (214) - returns to the
    /// nearest safe point directly, no confirmation dialog, no AgentReturn
    /// round-trip. Not a general action, so UseAction is the wrong tool.</summary>
    public const int ReturnIfNotLalafell = 214;
}
