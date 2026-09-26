namespace Nonuglon.Support;

/// <summary>Shared boxed-letter prefix for this plugin's context menu entries,
/// instead of Dalamud's default red "D". PrefixColor is a Lumina UIColor row id
/// (verified visually in-game), not an RGB value.</summary>
public static class ContextMenuBranding
{
    public const char PrefixChar = 'N';
    public const ushort PrefixColor = 504;
}
