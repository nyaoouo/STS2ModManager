using System.Drawing;

namespace STS2ModManager.App;

/// <summary>
/// Centralized color palette for the unified link-style button system.
/// Both themes have idle/press/border/highlight pairs.
/// </summary>
internal static class AppPalette
{
    // Light theme.
    public static readonly Color LightIdleText  = Color.FromArgb(33, 33, 33);    // #212121
    public static readonly Color LightPressText = Color.Black;                    // #000000
    public static readonly Color LightBorder    = Color.FromArgb(33, 33, 33);    // #212121
    public static readonly Color LightHighlight = Color.FromArgb(25, 118, 210);  // #1976D2 Material Blue 700
    public static readonly Color LightDisabledText = Color.FromArgb(160, 160, 160); // #A0A0A0

    // Dark theme.
    public static readonly Color DarkIdleText   = Color.FromArgb(230, 230, 230); // #E6E6E6
    public static readonly Color DarkPressText  = Color.White;                    // #FFFFFF
    public static readonly Color DarkBorder     = Color.FromArgb(230, 230, 230); // #E6E6E6
    public static readonly Color DarkHighlight  = Color.FromArgb(100, 181, 246); // #64B5F6 Material Blue 300
    public static readonly Color DarkDisabledText  = Color.FromArgb(120, 120, 120); // #787878
}
