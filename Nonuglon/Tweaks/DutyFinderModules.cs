using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Plugin;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit.Controllers;
using KamiToolKit.Enums;
using KamiToolKit.Nodes;
using Lumina.Excel.Sheets;
using Lumina.Text.ReadOnly;
using Nonuglon.Support;

namespace Nonuglon.Tweaks;

/// <summary>Row walking and label placement adapted from BossmodReborn's
/// (https://github.com/FFXIV-CombatReborn/BossmodReborn, BSD-3-Clause)
/// Framework/ContentsFinderIndicator.cs, re-hosted on TweakBase, reading
/// vbm's registry from outside and drawing KamiToolKit text nodes.</summary>
public unsafe class DutyFinderModules : TweakBase
{
    public override string Name => "Duty Finder Modules";
    public override string Description => "Tags duties in the Duty Finder that Boss Mod has modules for, colored by how complete they are. Requires Boss Mod to be installed and loaded.";

    public override bool ConfigEnabled
    {
        get => Plugin.Configuration.DutyFinderModulesEnabled;
        set => Plugin.Configuration.DutyFinderModulesEnabled = value;
    }

    public override string[] CommandNames => ["dutyfinder", "vbmtags"];

    private static readonly string[] AddonNames = ["ContentsFinder", "RaidFinder"];
    private const string LabelText = "VBM ✓";
    private const float LabelGap = 6f;
    private const float NameMinGap = 20f;
    private const int MaxTextCacheEntries = 4096;

    private static readonly Vector4 CompleteColor = new(0.55f, 0.9f, 0.55f, 1f);
    private static readonly Vector4 IncompleteColor = new(0.96f, 0.78f, 0.31f, 1f);
    private static readonly Vector4 PackColor = new(0.55f, 0.78f, 1f, 1f);

    // Ordered by priority: a duty shows the highest tag of any of its modules.
    private enum Tag : sbyte { None = -1, Complete, Incomplete, Pack }

    private sealed class RowLabel
    {
        public required TextNode Node;
        public Tag Tag = Tag.None;
        public Vector2 Position = new(float.NaN);
        public float Width;
    }

    private readonly List<AddonController> controllers = [];
    private readonly Dictionary<nint, Dictionary<nint, RowLabel>> labels = []; // addon -> row -> label
    private readonly Dictionary<ulong, Tag> textCache = [];
    private readonly HashSet<nint> visited = [];
    private Dictionary<string, Tag>? tagsByName;
    private volatile bool stale = true;
    private int dutyCount;
    private bool wantEnabled;

    private static bool IsBossModAvailable => PluginDetection.IsPluginLoaded(PluginDetection.BossModInternalName);

    protected override void Enable()
    {
        wantEnabled = true;
        stale = true;
        Svc.PluginInterface.ActivePluginsChanged += OnActivePluginsChanged;
        KamiToolKitHost.Ready.ContinueWith(_ => Svc.Framework.RunOnFrameworkThread(CreateControllers),
            TaskContinuationOptions.OnlyOnRanToCompletion);
    }

    protected override void Disable()
    {
        wantEnabled = false;
        Svc.PluginInterface.ActivePluginsChanged -= OnActivePluginsChanged;
        // Disabling a controller runs OnFinalize for an open window, removing its labels.
        foreach (var controller in controllers)
            controller.Dispose();
        controllers.Clear();
        foreach (var addon in new List<nint>(labels.Keys))
            DisposeLabels(addon);
    }

    private void OnActivePluginsChanged(IActivePluginsChangedEventArgs _) => stale = true;

    private void CreateControllers()
    {
        if (!wantEnabled || controllers.Count > 0) return;
        foreach (var name in AddonNames)
        {
            var controller = new AddonController
            {
                AddonName = name,
                OnSetup = _ => stale = true, // re-read vbm each time the window opens
                OnFinalize = addon => DisposeLabels((nint)addon),
                OnUpdate = OnUpdate,
            };
            controller.Enable();
            controllers.Add(controller);
        }
    }

    private void OnUpdate(AtkUnitBase* addon)
    {
        if (addon == null || !addon->IsVisible) return;

        if (stale || tagsByName == null)
        {
            stale = false;
            tagsByName = BuildTags();
            textCache.Clear();
        }

        if (!labels.TryGetValue((nint)addon, out var rows))
            labels[(nint)addon] = rows = [];
        visited.Clear();
        VisitManager(rows, &addon->UldManager, 0);
    }

    private Dictionary<string, Tag> BuildTags()
    {
        var names = new Dictionary<string, Tag>(StringComparer.OrdinalIgnoreCase);
        var coverage = IsBossModAvailable ? BossModModules.Read() : null;
        dutyCount = coverage?.Count ?? 0;
        if (coverage == null) return names;

        var sheet = Svc.Data.GetExcelSheet<ContentFinderCondition>();
        foreach (var (cfc, c) in coverage)
        {
            if (sheet.GetRowOrDefault(cfc) is not { } row) continue;
            var name = Normalize(row.Name.ExtractText());
            if (name.Length == 0) continue;
            var tag = c.FromPack > 0 ? Tag.Pack : c.Incomplete > 0 ? Tag.Incomplete : Tag.Complete;
            names[name] = names.TryGetValue(name, out var existing) && existing > tag ? existing : tag;
        }
        return names;
    }

    // Rows are ListItemRenderer components somewhere under the addon; walk until we find them.
    private void VisitManager(Dictionary<nint, RowLabel> rows, AtkUldManager* manager, int depth)
    {
        if (depth > 4) return;
        if (manager->NodeList != null)
            for (var i = 0; i < manager->NodeListCount; ++i)
                VisitNode(rows, manager->NodeList[i], depth);
        VisitChain(rows, manager->RootNode, depth, 0);
    }

    private void VisitChain(Dictionary<nint, RowLabel> rows, AtkResNode* first, int depth, int treeDepth)
    {
        if (treeDepth > 8) return;
        for (var node = first; node != null; node = node->PrevSiblingNode)
        {
            VisitNode(rows, node, depth);
            if ((int)node->Type < 1000 && node->ChildNode != null)
                VisitChain(rows, node->ChildNode, depth, treeDepth + 1);
        }
    }

    private void VisitNode(Dictionary<nint, RowLabel> rows, AtkResNode* node, int depth)
    {
        if (node == null || (int)node->Type < 1000) return;
        var componentNode = (AtkComponentNode*)node;
        var component = componentNode->Component;
        if (component == null || !visited.Add((nint)componentNode)) return;
        var info = (AtkUldComponentInfo*)component->UldManager.Objects;
        if (info == null) return;

        if (info->ComponentType == ComponentType.ListItemRenderer)
            UpdateRow(rows, componentNode);
        else
            VisitManager(rows, &component->UldManager, depth + 1);
    }

    private void UpdateRow(Dictionary<nint, RowLabel> rows, AtkComponentNode* rowNode)
    {
        var rowRes = (AtkResNode*)rowNode;
        var first = rowNode->Component->UldManager.RootNode;
        rows.TryGetValue((nint)rowNode, out var label);
        AtkResNode* own = label != null ? (AtkResNode*)label.Node : null;

        if (first == null || !rowRes->IsVisible())
        {
            Hide(label);
            return;
        }

        AtkTextNode* nameNode = null;
        var tag = Tag.None;
        FindName(first, own, ref nameNode, ref tag, 0);
        if (nameNode == null)
        {
            Hide(label);
            return;
        }

        // The label sits just left of whatever comes first after the name
        // (duty support icon, level text), so it moves with those icons.
        var nameX = RowRelativeX((AtkResNode*)nameNode, rowNode);
        var anchor = float.MaxValue;
        AtkTextNode* style = null;
        var styleX = float.MaxValue;
        for (var child = first; child != null; child = child->PrevSiblingNode)
            AccumulateAnchor(child, rowNode, own, nameX + NameMinGap, rowRes->Width * 0.5f, ref anchor, ref style, ref styleX, 0);
        if (anchor == float.MaxValue || style == null)
        {
            Hide(label);
            return;
        }

        if (label == null)
        {
            label = rows[(nint)rowNode] = new RowLabel { Node = CreateLabel(style) };
            label.Node.AttachNode(style, NodePosition.AfterTarget);
            rowNode->Component->UldManager.UpdateDrawNodeList();
            label.Width = label.Node.GetTextDrawSize(false).X;
            if (label.Width <= 0) label.Width = 48f;
        }

        if (label.Tag != tag)
        {
            label.Tag = tag;
            label.Node.TextColor = tag switch
            {
                Tag.Pack => PackColor,
                Tag.Incomplete => IncompleteColor,
                _ => CompleteColor,
            };
        }

        var parent = ((AtkResNode*)label.Node)->ParentNode;
        var parentIsRow = parent == null || parent == rowRes;
        var position = new Vector2(
            anchor - LabelGap - label.Width - (parentIsRow ? 0f : RowRelativeX(parent, rowNode)),
            RowRelativeY((AtkResNode*)style, rowNode) - (parentIsRow ? 0f : RowRelativeY(parent, rowNode)));
        if (position != label.Position)
        {
            label.Position = position;
            label.Node.Size = new Vector2(label.Width, style->AtkResNode.Height);
            label.Node.Position = position;
        }
        if (!label.Node.IsVisible) label.Node.IsVisible = true;
    }

    private static TextNode CreateLabel(AtkTextNode* style) => new()
    {
        String = LabelText,
        FontType = style->FontType,
        FontSize = style->FontSize,
        TextFlags = style->TextFlags,
        AlignmentType = AlignmentType.Right,
        TextOutlineColor = new Vector4(style->EdgeColor.R, style->EdgeColor.G, style->EdgeColor.B, style->EdgeColor.A) / 255f,
    };

    private void FindName(AtkResNode* first, AtkResNode* own, ref AtkTextNode* result, ref Tag tag, int depth)
    {
        if (depth > 6) return;
        for (var node = first; node != null && result == null; node = node->PrevSiblingNode)
        {
            if (node == own || !node->IsVisible()) continue;

            if (node->Type == NodeType.Text)
            {
                var t = LookupText((AtkTextNode*)node);
                if (t != Tag.None)
                {
                    result = (AtkTextNode*)node;
                    tag = t;
                }
            }
            else if ((int)node->Type >= 1000)
            {
                var component = ((AtkComponentNode*)node)->Component;
                if (component != null && component->UldManager.RootNode != null)
                    FindName(component->UldManager.RootNode, own, ref result, ref tag, depth + 1);
            }
            else if (node->ChildNode != null)
            {
                FindName(node->ChildNode, own, ref result, ref tag, depth + 1);
            }
        }
    }

    // Runs for every text node of every row each frame, so results are cached by a hash of the raw bytes.
    private Tag LookupText(AtkTextNode* text)
    {
        var bytes = text->NodeText.AsSpan();
        if (bytes.IsEmpty) return Tag.None;

        var hash = 14695981039346656037ul;
        foreach (var b in bytes)
            hash = (hash ^ b) * 1099511628211ul;
        if (textCache.TryGetValue(hash, out var cached)) return cached;

        if (textCache.Count >= MaxTextCacheEntries) textCache.Clear();
        var name = Normalize(new ReadOnlySeStringSpan(bytes).ExtractText());
        var result = tagsByName!.TryGetValue(name, out var t) ? t : Tag.None;
        textCache[hash] = result;
        return result;
    }

    private static void AccumulateAnchor(AtkResNode* node, AtkComponentNode* rowNode, AtkResNode* own, float minX, float maxLeafWidth,
        ref float anchor, ref AtkTextNode* style, ref float styleX, int depth)
    {
        if (node == own || !node->IsVisible() || node->Type is NodeType.Collision or NodeType.NineGrid || (int)node->Type >= 1000)
            return;

        if (node->Type == NodeType.Res)
        {
            if (depth < 4)
                for (var child = node->ChildNode; child != null; child = child->PrevSiblingNode)
                    AccumulateAnchor(child, rowNode, own, minX, maxLeafWidth, ref anchor, ref style, ref styleX, depth + 1);
            return;
        }

        if (node->Width == 0 || node->Width > maxLeafWidth) return;
        var x = RowRelativeX(node, rowNode);
        if (x <= minX) return;

        var left = x;
        if (node->Type == NodeType.Text)
        {
            var text = (AtkTextNode*)node;
            if (text->NodeText.Length == 0) return;
            left = x + VisualLeftOffset(text);
            if (x < styleX)
            {
                style = text;
                styleX = x;
            }
        }
        anchor = Math.Min(anchor, left);
    }

    // Right/center-aligned text nodes are wider than the text they draw.
    private static float VisualLeftOffset(AtkTextNode* text)
    {
        var align = text->AlignmentType;
        if (align is AlignmentType.TopLeft or AlignmentType.Left or AlignmentType.BottomLeft) return 0;
        ushort width = 0, height = 0;
        text->GetTextDrawSize(&width, &height);
        var nodeWidth = text->AtkResNode.Width;
        return align is AlignmentType.TopRight or AlignmentType.Right or AlignmentType.BottomRight ? nodeWidth - width : (nodeWidth - width) * 0.5f;
    }

    private static float RowRelativeX(AtkResNode* node, AtkComponentNode* rowNode)
    {
        var x = node->X;
        for (var p = node->ParentNode; p != null && p != (AtkResNode*)rowNode; p = p->ParentNode)
            x += p->X;
        return x;
    }

    private static float RowRelativeY(AtkResNode* node, AtkComponentNode* rowNode)
    {
        var y = node->Y;
        for (var p = node->ParentNode; p != null && p != (AtkResNode*)rowNode; p = p->ParentNode)
            y += p->Y;
        return y;
    }

    private static void Hide(RowLabel? label)
    {
        if (label != null && label.Node.IsVisible) label.Node.IsVisible = false;
    }

    // Duty names can carry soft hyphens, zero-width spaces and wrapped whitespace.
    private static string Normalize(string s)
    {
        var sb = new StringBuilder(s.Length);
        var pendingSpace = false;
        foreach (var c in s)
        {
            if (c is '­' or '​') continue;
            if (char.IsWhiteSpace(c))
            {
                pendingSpace = sb.Length > 0;
                continue;
            }
            if (pendingSpace)
            {
                sb.Append(' ');
                pendingSpace = false;
            }
            sb.Append(c);
        }
        return sb.ToString();
    }

    private void DisposeLabels(nint addon)
    {
        if (!labels.Remove(addon, out var rows)) return;
        foreach (var (rowPtr, label) in rows)
        {
            label.Node.Dispose();
            // KamiToolKit only refreshes the row's draw list when its object list is allocated.
            var component = ((AtkComponentNode*)rowPtr)->Component;
            if (component != null) component->UldManager.UpdateDrawNodeList();
        }
    }

    public override void DrawOptions()
    {
        ImGui.TextColored(CompleteColor, "VBM");
        ImGui.SameLine();
        ImGui.TextDisabled("has modules");
        ImGui.TextColored(IncompleteColor, "VBM");
        ImGui.SameLine();
        ImGui.TextDisabled("at least one module is marked incomplete");
        ImGui.TextColored(PackColor, "VBM");
        ImGui.SameLine();
        ImGui.TextDisabled("at least one module comes from a module pack");

        ImGui.PushTextWrapPos(ImGui.GetContentRegionAvail().X + ImGui.GetCursorPosX());
        if (!IsBossModAvailable)
            ImGui.TextColored(UiColors.Warning, "Boss Mod not detected - this tweak has no effect until it's installed and loaded.");
        else if (BossModModules.LastError is { } error)
            ImGui.TextColored(UiColors.Warning, $"Couldn't read Boss Mod's modules: {error}");
        else if (tagsByName != null)
            ImGui.TextDisabled($"Boss Mod has modules for {dutyCount} duties (read when the Duty Finder last opened).");
        ImGui.PopTextWrapPos();
    }

    public override bool HasWarning => Enabled && (!IsBossModAvailable || BossModModules.LastError != null);
}
