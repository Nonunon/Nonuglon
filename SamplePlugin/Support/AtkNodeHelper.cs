using System.Numerics;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace Nonuglon.Support;

/// <summary>Copied verbatim from PandorasBox's Helpers/AtkResNodeHelper.cs.</summary>
internal static unsafe class AtkNodeHelper
{
    public static Vector2 GetNodePosition(AtkResNode* node)
    {
        var pos = new Vector2(node->X, node->Y);
        var par = node->ParentNode;
        while (par != null)
        {
            pos *= new Vector2(par->ScaleX, par->ScaleY);
            pos += new Vector2(par->X, par->Y);
            par = par->ParentNode;
        }

        return pos;
    }

    public static Vector2 GetNodeScale(AtkResNode* node)
    {
        if (node == null) return new Vector2(1, 1);
        var scale = new Vector2(node->ScaleX, node->ScaleY);
        while (node->ParentNode != null)
        {
            node = node->ParentNode;
            scale *= new Vector2(node->ScaleX, node->ScaleY);
        }

        return scale;
    }
}
