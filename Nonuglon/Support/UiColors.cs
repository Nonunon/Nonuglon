using System.Numerics;

namespace Nonuglon.Support;

/// <summary>Shared ImGui color constants for status indicators (enabled/disabled/
/// header/warning), used by both ConfigWindow and each tweak's own DrawOptions() -
/// centralized so a tweak rendering its own status doesn't drift out of sync with
/// the window's colors.</summary>
public static class UiColors
{
    public static readonly Vector4 Enabled = new(0.4f, 0.9f, 0.4f, 1f);
    public static readonly Vector4 Disabled = new(0.6f, 0.6f, 0.6f, 1f);
    public static readonly Vector4 Header = new(0.85f, 0.7f, 0.3f, 1f);
    public static readonly Vector4 Warning = new(0.95f, 0.65f, 0.25f, 1f);
}
