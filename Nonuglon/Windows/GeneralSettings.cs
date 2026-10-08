using Dalamud.Bindings.ImGui;
using Dalamud.Game.Text;
using Dalamud.Interface.Utility;
using Nonuglon.Support;
using static Nonuglon.Support.CommandText;

namespace Nonuglon.Windows;

/// <summary>Plugin-wide settings, shown by ConfigWindow's pinned "General"
/// sidebar row: where each kind of chat message goes, and which chat type.</summary>
internal static class GeneralSettings
{
    public const string Name = "General";
    public const string Description = "Plugin-wide settings that aren't tied to any one tweak.";

    private static readonly (XivChatType Type, string Label)[] Channels =
    [
        (XivChatType.None, "Dalamud default"),
        (XivChatType.Echo, "Echo"),
        (XivChatType.SystemMessage, "System Messages"),
        (XivChatType.Debug, "Debug"),
        (XivChatType.Notice, "Notice"),
        (XivChatType.Urgent, "Urgent"),
        (XivChatType.SystemError, "System Error"),
    ];

    public static void Draw()
    {
        var config = Plugin.Configuration;

        ImGui.TextDisabled("Chat messages:");
        const ImGuiTableFlags flags = ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersOuter | ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.SizingFixedFit;
        if (ImGui.BeginTable("##NonuglonChatKinds", 3, flags))
        {
            ImGui.TableSetupColumn("Kind");
            ImGui.TableSetupColumn("Shown in");
            ImGui.TableSetupColumn("For example", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableHeadersRow();

            if (KindRow("Replies", config.ChatReplies, "on/off confirmations, usage, favorite added/removed, run stopped", out var replies))
            {
                config.ChatReplies = replies;
                config.Save();
            }
            if (KindRow("Notices", config.ChatNotices, "Navigate to Flag arrived", out var notices))
            {
                config.ChatNotices = notices;
                config.Save();
            }
            if (KindRow("Failures", config.ChatFailures, "run stuck or gave up, vnavmesh not ready, estate teleport failed", out var failures))
            {
                config.ChatFailures = failures;
                config.Save();
            }

            ImGui.EndTable();
        }
        ImGui.PushTextWrapPos(ImGui.GetContentRegionAvail().X + ImGui.GetCursorPosX());
        ImGui.TextDisabled("\"xllog only\" messages are logged at Information level, so they show in /xllog without verbose on. /Nonuglon help always prints to chat.");
        ImGui.PopTextWrapPos();

        ImGui.Spacing();
        ImGui.SetNextItemWidth(180f * ImGuiHelpers.GlobalScale);
        if (ImGui.BeginCombo("Chat channel##NonuglonChatChannel", ChannelLabel(config.ChatChannel)))
        {
            foreach (var (type, label) in Channels)
            {
                if (ImGui.Selectable(label, config.ChatChannel == type))
                {
                    config.ChatChannel = type;
                    config.Save();
                }
            }
            ImGui.EndCombo();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Which chat type Nonuglon's messages use. Your chat tabs' own filters (Log Filters in Character Configuration) can then show or hide them per tab. \"Dalamud default\" follows Dalamud's general chat channel setting.");
        ImGui.SameLine();
        if (ImGui.Button("Send test##NonuglonChatChannel"))
            PrintAlways("Test message, this is where Nonuglon's chat output goes.");
    }

    /// <summary>One table row with a Chat / xllog only choice; true when it changed.</summary>
    private static bool KindRow(string label, bool inChat, string examples, out bool value)
    {
        value = inChat;
        ImGui.TableNextRow();

        ImGui.TableNextColumn();
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(label);

        ImGui.TableNextColumn();
        var changed = false;
        if (ImGui.RadioButton($"Chat##{label}", inChat)) { value = true; changed = !inChat; }
        ImGui.SameLine();
        if (ImGui.RadioButton($"xllog only##{label}", !inChat)) { value = false; changed = inChat; }

        ImGui.TableNextColumn();
        ImGui.AlignTextToFramePadding();
        ImGui.TextDisabled(examples);

        return changed;
    }

    private static string ChannelLabel(XivChatType type)
    {
        foreach (var (t, label) in Channels)
            if (t == type) return label;
        return type.ToString();
    }
}
