namespace Nonuglon.Support;

/// <summary>Shared boxed-letter prefix for every native right-click context menu
/// entry this plugin adds (Auto Pillion, Search Info Menu, ...), so they all read as
/// coming from the same plugin instead of each defaulting to Dalamud's own red
/// boxed "D". PrefixColor is a Lumina UIColor row id, not an RGB value - verified
/// visually in-game rather than from raw sheet data, so nudge it here if the shade
/// ever needs to change.</summary>
public static class ContextMenuBranding
{
    public const char PrefixChar = 'N';
    public const ushort PrefixColor = 504;
}
