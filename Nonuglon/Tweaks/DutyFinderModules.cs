using System;
using System.Collections.Generic;
using System.Linq;
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
    public override string Description => "Tags duties in the Duty Finder that Boss Mod has modules for, colored by how complete they are. Can also tag BossModReborn's, read from its installed files, so it only needs to be installed, not enabled. VBM tags need Boss Mod loaded.";

    public override bool ConfigEnabled
    {
        get => Plugin.Configuration.DutyFinderModulesEnabled;
        set => Plugin.Configuration.DutyFinderModulesEnabled = value;
    }

    public override string[] CommandNames => ["dutyfinder", "vbmtags"];

    private static readonly string[] AddonNames = ["ContentsFinder", "RaidFinder"];
    private const string VbmText = "VBM ✓";
    private const string BmrText = "BMR ✓";
    private const string CompactText = "✓";
    private const float LabelGap = 6f;
    private const float NameMinGap = 20f;
    private const int MaxTextCacheEntries = 4096;

    private static readonly Vector4 CompleteColor = new(0.55f, 0.9f, 0.55f, 1f);
    private static readonly Vector4 IncompleteColor = new(0.96f, 0.78f, 0.31f, 1f);
    private static readonly Vector4 PackColor = new(0.55f, 0.78f, 1f, 1f);

    // Ordered by priority: a duty shows the highest tag of any of its modules.
    private enum VbmTag : sbyte { None = -1, Complete, Incomplete, Pack }

    // BMR's own maturity names; a duty shows its least mature module, like BMR does.
    private enum BmrTag : sbyte { None = -1, WIP, Contributed, Verified, AISupport }

    private static readonly (BmrTag Tag, Vector4 Color, string Meaning)[] BmrColors =
    [
        (BmrTag.WIP, new(0.94f, 0.44f, 0.44f, 1f), "work in progress"),
        (BmrTag.Contributed, new(0.55f, 0.78f, 1f, 1f), "contributed, not verified"),
        (BmrTag.Verified, new(0.55f, 0.9f, 0.55f, 1f), "verified"),
        (BmrTag.AISupport, new(0.96f, 0.78f, 0.31f, 1f), "verified with AI"),
    ];

    private static readonly (BmrTagMode Mode, string Label, string Tip)[] BmrModes =
    [
        (BmrTagMode.Off, "Off", "Only Boss Mod's tags."),
        (BmrTagMode.Alongside, "Alongside", "A BMR tag next to the VBM one on every duty BossModReborn covers."),
        (BmrTagMode.OnlyMissing, "Only where VBM has none", "A BMR tag only on duties Boss Mod has no modules for."),
    ];

    // PackRank indexes packColors: the highest-priority pack the duty's modules come from.
    private readonly record struct DutyTags(VbmTag Vbm, BmrTag Bmr, int PackRank = int.MaxValue)
    {
        public static readonly DutyTags None = new(VbmTag.None, BmrTag.None);
        public bool Any => Vbm != VbmTag.None || Bmr != BmrTag.None;
    }

    private sealed class Label
    {
        public required TextNode Node;
        public required string Text;
        public float Width, FullWidth, CompactWidth;
        public bool Compact;
        public Vector4 Color = new(float.NaN);
        public Vector2 Position = new(float.NaN);
    }

    private sealed class RowLabels
    {
        public required Label Vbm;
        public required Label Bmr;
    }

    private readonly List<AddonController> controllers = [];
    private readonly Dictionary<nint, Dictionary<nint, RowLabels>> labels = []; // addon -> row -> labels
    private readonly Dictionary<ulong, DutyTags> textCache = [];
    private readonly HashSet<nint> visited = [];
    private Dictionary<string, DutyTags>? tagsByName;
    private volatile bool stale = true;
    private int vbmDutyCount, bmrDutyCount;
    private List<Vector4> packColors = []; // by PackRank, from the last BuildTags
    private DateTime packsReadAt;
    private bool packSavePending;
    private bool wantEnabled;
    private readonly DutyFinderTestingCheck testingCheck = new();

    private static bool IsBossModAvailable => PluginDetection.IsPluginLoaded(PluginDetection.BossModInternalName);
    private static bool IsBmrInstalled => PluginDetection.IsPluginInstalled(BmrModules.InternalName);
    private static bool IsBmrRunning => PluginDetection.IsPluginLoaded(BmrModules.InternalName);
    // Forced off while BMR isn't installed; the saved choice comes back when it is.
    private static BmrTagMode BmrMode => IsBmrInstalled ? Plugin.Configuration.DutyFinderModulesBmr : BmrTagMode.Off;

    protected override void Enable()
    {
        wantEnabled = true;
        stale = true;
        Svc.PluginInterface.ActivePluginsChanged += OnActivePluginsChanged;
        testingCheck.SetRunning(Plugin.Configuration.DutyFinderModulesTestingCheck);
        KamiToolKitHost.Ready.ContinueWith(_ => Svc.Framework.RunOnFrameworkThread(CreateControllers),
            TaskContinuationOptions.OnlyOnRanToCompletion);
    }

    protected override void Disable()
    {
        wantEnabled = false;
        Svc.PluginInterface.ActivePluginsChanged -= OnActivePluginsChanged;
        testingCheck.SetRunning(false);
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
                OnSetup = _ => stale = true, // re-read the modules each time the window opens
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

    private Dictionary<string, DutyTags> BuildTags()
    {
        var mode = BmrMode;
        var vbm = IsBossModAvailable ? BossModModules.Read() : null;
        var bmr = mode != BmrTagMode.Off ? BmrModules.Read() : null;
        vbmDutyCount = vbm?.Count ?? 0;
        bmrDutyCount = bmr?.Count ?? 0;

        var order = PackOrder();
        var rankOf = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < order.Count; ++i)
            rankOf.TryAdd(order[i].Pack, i);
        packColors = order.ConvertAll(p => p.Color);

        var byCfc = new Dictionary<uint, DutyTags>();
        if (vbm != null)
            foreach (var (cfc, c) in vbm)
            {
                var rank = int.MaxValue;
                foreach (var pack in c.Packs)
                    if (rankOf.TryGetValue(pack, out var r)) rank = Math.Min(rank, r);
                byCfc[cfc] = new(c.FromPack > 0 ? VbmTag.Pack : c.Incomplete > 0 ? VbmTag.Incomplete : VbmTag.Complete, BmrTag.None, rank);
            }
        if (bmr != null)
        {
            foreach (var (cfc, maturity) in bmr)
            {
                if (!byCfc.TryGetValue(cfc, out var t)) t = DutyTags.None;
                else if (mode == BmrTagMode.OnlyMissing) continue;
                byCfc[cfc] = t with { Bmr = Enum.TryParse<BmrTag>(maturity, out var m) ? m : BmrTag.Contributed };
            }
        }

        var names = new Dictionary<string, DutyTags>(StringComparer.OrdinalIgnoreCase);
        var sheet = Svc.Data.GetExcelSheet<ContentFinderCondition>();
        foreach (var (cfc, tags) in byCfc)
        {
            if (sheet.GetRowOrDefault(cfc) is not { } row) continue;
            var name = Normalize(row.Name.ExtractText());
            if (name.Length == 0) continue;
            names[name] = names.TryGetValue(name, out var e)
                ? new((VbmTag)Math.Max((sbyte)e.Vbm, (sbyte)tags.Vbm), MinBmr(e.Bmr, tags.Bmr), Math.Min(e.PackRank, tags.PackRank))
                : tags;
        }
        return names;
    }

    private static BmrTag MinBmr(BmrTag a, BmrTag b) => a == BmrTag.None ? b : b == BmrTag.None ? a : (BmrTag)Math.Min((sbyte)a, (sbyte)b);

    // Rows are ListItemRenderer components somewhere under the addon; walk until we find them.
    private void VisitManager(Dictionary<nint, RowLabels> rows, AtkUldManager* manager, int depth)
    {
        if (depth > 4) return;
        if (manager->NodeList != null)
            for (var i = 0; i < manager->NodeListCount; ++i)
                VisitNode(rows, manager->NodeList[i], depth);
        VisitChain(rows, manager->RootNode, depth, 0);
    }

    private void VisitChain(Dictionary<nint, RowLabels> rows, AtkResNode* first, int depth, int treeDepth)
    {
        if (treeDepth > 8) return;
        for (var node = first; node != null; node = node->PrevSiblingNode)
        {
            VisitNode(rows, node, depth);
            if ((int)node->Type < 1000 && node->ChildNode != null)
                VisitChain(rows, node->ChildNode, depth, treeDepth + 1);
        }
    }

    private void VisitNode(Dictionary<nint, RowLabels> rows, AtkResNode* node, int depth)
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

    private void UpdateRow(Dictionary<nint, RowLabels> rows, AtkComponentNode* rowNode)
    {
        var rowRes = (AtkResNode*)rowNode;
        var first = rowNode->Component->UldManager.RootNode;
        rows.TryGetValue((nint)rowNode, out var row);
        AtkResNode* ownA = row != null ? (AtkResNode*)row.Vbm.Node : null;
        AtkResNode* ownB = row != null ? (AtkResNode*)row.Bmr.Node : null;

        if (first == null || !rowRes->IsVisible())
        {
            Hide(row);
            return;
        }

        AtkTextNode* nameNode = null;
        var tags = DutyTags.None;
        FindName(first, ownA, ownB, ref nameNode, ref tags, 0);
        if (nameNode == null)
        {
            Hide(row);
            return;
        }

        // Tags sit just left of whatever comes first after the name
        // (duty support icon, level text), so they move with those icons.
        var nameX = RowRelativeX((AtkResNode*)nameNode, rowNode);
        var anchor = float.MaxValue;
        AtkTextNode* style = null;
        var styleX = float.MaxValue;
        for (var child = first; child != null; child = child->PrevSiblingNode)
            AccumulateAnchor(child, rowNode, ownA, ownB, nameX + NameMinGap, rowRes->Width * 0.5f, ref anchor, ref style, ref styleX, 0);
        if (anchor == float.MaxValue || style == null)
        {
            Hide(row);
            return;
        }

        if (row == null)
        {
            row = rows[(nint)rowNode] = new RowLabels { Vbm = CreateLabel(style, VbmText), Bmr = CreateLabel(style, BmrText) };
            row.Vbm.Node.AttachNode(style, NodePosition.AfterTarget);
            row.Bmr.Node.AttachNode(style, NodePosition.AfterTarget);
            rowNode->Component->UldManager.UpdateDrawNodeList();
            Measure(row.Vbm);
            Measure(row.Bmr);
        }

        var parent = ((AtkResNode*)row.Vbm.Node)->ParentNode;
        var offset = parent == null || parent == rowRes
            ? Vector2.Zero
            : new Vector2(RowRelativeX(parent, rowNode), RowRelativeY(parent, rowNode));
        var y = RowRelativeY((AtkResNode*)style, rowNode);
        var height = style->AtkResNode.Height;
        var right = anchor - LabelGap;

        var compact = false;
        if (Plugin.Configuration.DutyFinderModulesCompact)
        {
            var nameRight = nameX + VisualLeftOffset(nameNode) + TextWidth(nameNode);
            compact = LeftEdge(row, tags, right, false) < nameRight + LabelGap;
        }
        SetCompact(row.Vbm, compact);
        SetCompact(row.Bmr, compact);

        Place(row.Vbm, (sbyte)tags.Vbm, VbmColor(tags), right, y, height, offset);
        Place(row.Bmr, (sbyte)tags.Bmr, BmrColor(tags.Bmr), BmrRight(right, row.Vbm.Width), y, height, offset);
    }

    // Alongside keeps the VBM slot reserved, so BMR tags line up in their own column.
    private static float BmrRight(float right, float vbmWidth) => BmrMode == BmrTagMode.Alongside ? right - vbmWidth - LabelGap : right;

    private static float LeftEdge(RowLabels row, DutyTags tags, float right, bool compact)
    {
        var vbmWidth = compact ? row.Vbm.CompactWidth : row.Vbm.FullWidth;
        var left = tags.Vbm != VbmTag.None ? right - vbmWidth : right;
        if (tags.Bmr != BmrTag.None)
            left = Math.Min(left, BmrRight(right, vbmWidth) - (compact ? row.Bmr.CompactWidth : row.Bmr.FullWidth));
        return left;
    }

    private static void SetCompact(Label label, bool compact)
    {
        if (label.Compact == compact) return;
        label.Compact = compact;
        label.Node.String = compact ? CompactText : label.Text;
        label.Width = compact ? label.CompactWidth : label.FullWidth;
    }

    private static void Place(Label label, sbyte tag, Vector4 color, float right, float y, ushort height, Vector2 parentOffset)
    {
        if (tag < 0)
        {
            if (label.Node.IsVisible) label.Node.IsVisible = false;
            return;
        }
        if (label.Color != color)
        {
            label.Color = color;
            label.Node.TextColor = color;
        }
        var position = new Vector2(right - label.Width, y) - parentOffset;
        if (position != label.Position)
        {
            label.Position = position;
            label.Node.Size = new Vector2(label.Width, height);
            label.Node.Position = position;
        }
        if (!label.Node.IsVisible) label.Node.IsVisible = true;
    }

    private Vector4 VbmColor(DutyTags tags) => tags.Vbm switch
    {
        VbmTag.Pack => tags.PackRank < packColors.Count ? packColors[tags.PackRank] : PackColor,
        VbmTag.Incomplete => IncompleteColor,
        _ => CompleteColor,
    };

    // Every pack with a color, in priority order: the configured ones first, then
    // any other loaded pack alphabetically, in the default pack blue.
    private static List<ModulePackColor> PackOrder()
    {
        var order = new List<ModulePackColor>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var p in Plugin.Configuration.DutyFinderModulesPackColors)
            if (p.Pack.Length > 0 && seen.Add(p.Pack))
                order.Add(new ModulePackColor { Pack = p.Pack, Color = p.Color });
        foreach (var pack in BossModModules.LoadedPacks ?? [])
            if (seen.Add(pack))
                order.Add(new ModulePackColor { Pack = pack, Color = PackColor });
        return order;
    }

    private static Vector4 BmrColor(BmrTag tag)
    {
        foreach (var (t, color, _) in BmrColors)
            if (t == tag) return color;
        return CompleteColor;
    }

    private static Label CreateLabel(AtkTextNode* style, string text) => new()
    {
        Text = text,
        Node = new TextNode
        {
            String = text,
            FontType = style->FontType,
            FontSize = style->FontSize,
            TextFlags = style->TextFlags,
            AlignmentType = AlignmentType.Right,
            TextOutlineColor = new Vector4(style->EdgeColor.R, style->EdgeColor.G, style->EdgeColor.B, style->EdgeColor.A) / 255f,
            IsVisible = false,
        },
    };

    private static void Measure(Label label)
    {
        var full = label.Node.GetTextDrawSize(label.Text, false).X;
        var compact = label.Node.GetTextDrawSize(CompactText, false).X;
        label.FullWidth = full > 0 ? full : 48f;
        label.CompactWidth = compact > 0 ? compact : 12f;
        label.Width = label.FullWidth;
    }

    private static float TextWidth(AtkTextNode* text)
    {
        ushort width = 0, height = 0;
        text->GetTextDrawSize(&width, &height);
        return width;
    }

    private void FindName(AtkResNode* first, AtkResNode* ownA, AtkResNode* ownB, ref AtkTextNode* result, ref DutyTags tags, int depth)
    {
        if (depth > 6) return;
        for (var node = first; node != null && result == null; node = node->PrevSiblingNode)
        {
            if (node == ownA || node == ownB || !node->IsVisible()) continue;

            if (node->Type == NodeType.Text)
            {
                var t = LookupText((AtkTextNode*)node);
                if (t.Any)
                {
                    result = (AtkTextNode*)node;
                    tags = t;
                }
            }
            else if ((int)node->Type >= 1000)
            {
                var component = ((AtkComponentNode*)node)->Component;
                if (component != null && component->UldManager.RootNode != null)
                    FindName(component->UldManager.RootNode, ownA, ownB, ref result, ref tags, depth + 1);
            }
            else if (node->ChildNode != null)
            {
                FindName(node->ChildNode, ownA, ownB, ref result, ref tags, depth + 1);
            }
        }
    }

    // Runs for every text node of every row each frame, so results are cached by a hash of the raw bytes.
    private DutyTags LookupText(AtkTextNode* text)
    {
        var bytes = text->NodeText.AsSpan();
        if (bytes.IsEmpty) return DutyTags.None;

        var hash = 14695981039346656037ul;
        foreach (var b in bytes)
            hash = (hash ^ b) * 1099511628211ul;
        if (textCache.TryGetValue(hash, out var cached)) return cached;

        if (textCache.Count >= MaxTextCacheEntries) textCache.Clear();
        var name = Normalize(new ReadOnlySeStringSpan(bytes).ExtractText());
        var result = tagsByName!.TryGetValue(name, out var t) ? t : DutyTags.None;
        textCache[hash] = result;
        return result;
    }

    private static void AccumulateAnchor(AtkResNode* node, AtkComponentNode* rowNode, AtkResNode* ownA, AtkResNode* ownB, float minX, float maxLeafWidth,
        ref float anchor, ref AtkTextNode* style, ref float styleX, int depth)
    {
        if (node == ownA || node == ownB || !node->IsVisible() || node->Type is NodeType.Collision or NodeType.NineGrid || (int)node->Type >= 1000)
            return;

        if (node->Type == NodeType.Res)
        {
            if (depth < 4)
                for (var child = node->ChildNode; child != null; child = child->PrevSiblingNode)
                    AccumulateAnchor(child, rowNode, ownA, ownB, minX, maxLeafWidth, ref anchor, ref style, ref styleX, depth + 1);
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

    private static void Hide(RowLabels? row)
    {
        if (row == null) return;
        if (row.Vbm.Node.IsVisible) row.Vbm.Node.IsVisible = false;
        if (row.Bmr.Node.IsVisible) row.Bmr.Node.IsVisible = false;
    }

    // Duty names can carry soft hyphens, zero-width spaces and wrapped whitespace.
    private static string Normalize(string s)
    {
        var sb = new StringBuilder(s.Length);
        var pendingSpace = false;
        foreach (var c in s)
        {
            if (c is '\u00AD' or '\u200B') continue;
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
        foreach (var (rowPtr, row) in rows)
        {
            row.Vbm.Node.Dispose();
            row.Bmr.Node.Dispose();
            // KamiToolKit only refreshes the row's draw list when its object list is allocated.
            var component = ((AtkComponentNode*)rowPtr)->Component;
            if (component != null) component->UldManager.UpdateDrawNodeList();
        }
    }

    public override void DrawOptions()
    {
        Legend("VBM", CompleteColor, "has modules");
        Legend("VBM", IncompleteColor, "at least one module is marked incomplete");
        Legend("VBM", PackColor, "at least one module comes from a module pack, in that pack's color:");
        DrawPackColors();

        var bmrInstalled = IsBmrInstalled;
        ImGui.BeginDisabled(!bmrInstalled);
        ImGui.TextUnformatted("BossModReborn tags:");
        foreach (var (mode, label, tip) in BmrModes)
        {
            ImGui.SameLine();
            if (ImGui.RadioButton($"{label}##DutyFinderModulesBmr", BmrMode == mode))
            {
                Plugin.Configuration.DutyFinderModulesBmr = mode;
                Plugin.Configuration.Save();
                stale = true;
            }
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip(tip);
        }
        ImGui.EndDisabled();
        if (BmrMode != BmrTagMode.Off)
            foreach (var (_, color, meaning) in BmrColors)
                Legend("BMR", color, meaning);

        var compact = Plugin.Configuration.DutyFinderModulesCompact;
        if (ImGui.Checkbox("Shrink tags when a long name runs into them##DutyFinderModulesCompact", ref compact))
        {
            Plugin.Configuration.DutyFinderModulesCompact = compact;
            Plugin.Configuration.Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Shows just the colored checkmark on that row, so the name stays readable.");

        var testing = Plugin.Configuration.DutyFinderModulesTestingCheck;
        if (ImGui.Checkbox("Testing check##DutyFinderModulesTestingCheck", ref testing))
        {
            Plugin.Configuration.DutyFinderModulesTestingCheck = testing;
            Plugin.Configuration.Save();
            testingCheck.SetRunning(Enabled && testing);
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("When you pull a boss whose Boss Mod module is marked incomplete, says so in chat with the module's contributors, once per boss per duty. Uses the Notices chat setting.");

        ImGui.PushTextWrapPos(ImGui.GetContentRegionAvail().X + ImGui.GetCursorPosX());
        if (!IsBossModAvailable)
            ImGui.TextColored(UiColors.Warning, BmrMode != BmrTagMode.Off
                ? "Boss Mod isn't loaded, so only BMR tags show until it is."
                : "Boss Mod isn't loaded, so nothing is tagged until it is.");
        else if (BossModModules.LastError is { } error)
            ImGui.TextColored(UiColors.Warning, $"Couldn't read Boss Mod's modules: {error}");
        else if (tagsByName != null)
            ImGui.TextDisabled($"Boss Mod has modules for {vbmDutyCount} duties.");

        if (!bmrInstalled)
            ImGui.TextColored(Plugin.Configuration.DutyFinderModulesBmr != BmrTagMode.Off ? UiColors.Warning : UiColors.Disabled,
                "BossModReborn isn't installed, so BMR tags are off. It only needs to be installed, not enabled.");
        else if (IsBmrRunning && IsBossModAvailable)
            ImGui.TextColored(UiColors.Warning, "Boss Mod and BossModReborn are both running. Tags still work, but the two fight each other in game.");
        if (BmrMode != BmrTagMode.Off)
        {
            if (BmrModules.LastError is { } bmrError)
                ImGui.TextColored(UiColors.Warning, $"Couldn't read BossModReborn's modules: {bmrError}");
            else if (tagsByName != null)
                ImGui.TextDisabled($"BossModReborn has modules for {bmrDutyCount} duties (read from its installed files, it doesn't need to be running).");
        }
        ImGui.TextDisabled("Counts update when the Duty Finder opens.");
        ImGui.PopTextWrapPos();
    }

    private void DrawPackColors()
    {
        // The pack list only refreshes on a registry read, which otherwise waits for the Duty Finder.
        if (IsBossModAvailable && DateTime.UtcNow - packsReadAt > TimeSpan.FromSeconds(5))
        {
            packsReadAt = DateTime.UtcNow;
            BossModModules.Read();
        }

        var order = PackOrder();
        ImGui.Indent();
        if (order.Count == 0)
        {
            ImGui.TextDisabled(IsBossModAvailable ? "No module packs loaded." : "Module packs show here once Boss Mod is loaded.");
            ImGui.Unindent();
            return;
        }

        var loaded = BossModModules.LoadedPacks ?? [];
        var changed = false;
        int moveFrom = -1, moveTo = -1, reset = -1;
        for (var i = 0; i < order.Count; ++i)
        {
            var entry = order[i];
            ImGui.BeginDisabled(i == 0);
            if (ImGui.ArrowButton($"##DutyFinderModulesPackUp{i}", ImGuiDir.Up)) (moveFrom, moveTo) = (i, i - 1);
            ImGui.EndDisabled();
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip("Move up: a duty with modules from several packs takes the color of the highest one.");
            ImGui.SameLine(0, ImGui.GetStyle().ItemInnerSpacing.X);
            ImGui.BeginDisabled(i == order.Count - 1);
            if (ImGui.ArrowButton($"##DutyFinderModulesPackDown{i}", ImGuiDir.Down)) (moveFrom, moveTo) = (i, i + 1);
            ImGui.EndDisabled();

            ImGui.SameLine();
            var color = entry.Color;
            if (ImGui.ColorEdit4($"##DutyFinderModulesPackColor{i}", ref color, ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.NoAlpha))
            {
                entry.Color = color with { W = 1f };
                changed = true;
            }

            ImGui.SameLine();
            ImGui.AlignTextToFramePadding();
            var isLoaded = loaded.Contains(entry.Pack);
            ImGui.TextColored(entry.Color, entry.Pack);
            if (!isLoaded)
            {
                ImGui.SameLine();
                ImGui.TextDisabled("(not loaded)");
            }

            if (entry.Color != PackColor || !isLoaded)
            {
                ImGui.SameLine();
                if (ImGui.SmallButton($"{(isLoaded ? "Reset" : "Remove")}##DutyFinderModulesPackReset{i}")) reset = i;
                if (ImGui.IsItemHovered()) ImGui.SetTooltip(isLoaded ? "Back to the default pack blue." : "Forget this pack's color.");
            }
        }
        ImGui.Unindent();

        if (moveFrom >= 0)
        {
            (order[moveFrom], order[moveTo]) = (order[moveTo], order[moveFrom]);
            changed = true;
        }
        if (reset >= 0)
        {
            if (loaded.Contains(order[reset].Pack)) order[reset].Color = PackColor;
            else order.RemoveAt(reset);
            changed = true;
        }
        if (changed)
        {
            // Kept as the whole list, so the order shown is the order kept.
            Plugin.Configuration.DutyFinderModulesPackColors = order;
            packSavePending = true;
            stale = true;
        }
        // Dragging in the color picker changes it every frame; write the file once it's let go.
        if (packSavePending && !ImGui.IsAnyItemActive())
        {
            packSavePending = false;
            Plugin.Configuration.Save();
        }
    }

    private static void Legend(string text, Vector4 color, string meaning)
    {
        ImGui.TextColored(color, text);
        ImGui.SameLine();
        ImGui.TextDisabled(meaning);
    }

    public override bool HasWarning => Enabled &&
        (!IsBossModAvailable || BossModModules.LastError != null
            || (BmrMode != BmrTagMode.Off && BmrModules.LastError != null)
            || (!IsBmrInstalled && Plugin.Configuration.DutyFinderModulesBmr != BmrTagMode.Off)
            || (IsBmrRunning && IsBossModAvailable));
}
