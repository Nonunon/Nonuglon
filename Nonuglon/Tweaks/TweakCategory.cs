namespace Nonuglon.Tweaks;

/// <summary>Sidebar group a tweak is listed under, drawn in this order.</summary>
public enum TweakCategory
{
    Travel,
    Items,
    RightClickMenus,
    DutiesAndCombat,
    Commands,
}

public static class TweakCategoryExtensions
{
    public static string DisplayName(this TweakCategory category) => category switch
    {
        TweakCategory.Travel => "Travel",
        TweakCategory.Items => "Items",
        TweakCategory.RightClickMenus => "Right-click menus",
        TweakCategory.DutiesAndCombat => "Duties & combat",
        TweakCategory.Commands => "Commands",
        _ => category.ToString(),
    };
}
