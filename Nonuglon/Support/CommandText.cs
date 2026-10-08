using System;
using System.Collections.Generic;
using Dalamud.Game.Text;

namespace Nonuglon.Support;

/// <summary>Shared parsing/output helpers for /Nonuglon chat commands, so
/// Plugin's dispatcher and each tweak's HandleCommand don't depend on each
/// other.</summary>
public static class CommandText
{
    public static bool TryParseBool(string s, out bool value)
    {
        switch (s.ToLowerInvariant())
        {
            case "true": case "on": case "1": case "yes": case "enable": case "enabled":
                value = true;
                return true;
            case "false": case "off": case "0": case "no": case "disable": case "disabled":
                value = false;
                return true;
            default:
                value = false;
                return false;
        }
    }

    /// <summary>Same as TryParseBool, but also accepts "toggle" to flip the
    /// current value.</summary>
    public static bool ResolveBool(string s, bool currentValue, out bool value)
    {
        if (s.Equals("toggle", StringComparison.OrdinalIgnoreCase))
        {
            value = !currentValue;
            return true;
        }

        return TryParseBool(s, out value);
    }

    /// <summary>Sends a message to chat, or only to /xllog if its kind is set to
    /// that in General settings. Chat messages are mirrored at Verbose so they
    /// survive scrolling out of chat; xllog-only ones go at Information so they
    /// show without verbose on. Debug is reserved for ReportStateChange's no-ops.</summary>
    public static void Print(string message, MessageKind kind = MessageKind.Reply)
    {
        if (!ShowsInChat(kind))
        {
            Plugin.Log.Information(message);
            return;
        }
        PrintToChat(message);
        Plugin.Log.Verbose(message);
    }

    /// <summary>Always goes to chat, for output the user explicitly asked to
    /// read (the help list); hiding that would just look broken.</summary>
    public static void PrintAlways(string message)
    {
        PrintToChat(message);
        Plugin.Log.Verbose(message);
    }

    private static bool ShowsInChat(MessageKind kind) => kind switch
    {
        MessageKind.Notice => Plugin.Configuration.ChatNotices,
        MessageKind.Failure => Plugin.Configuration.ChatFailures,
        _ => Plugin.Configuration.ChatReplies,
    };

    /// <summary>ChatChannel None means Dalamud's own default chat type.</summary>
    private static void PrintToChat(string message)
    {
        var channel = Plugin.Configuration.ChatChannel;
        if (channel == XivChatType.None)
            Plugin.ChatGui.Print($"[Nonuglon] {message}");
        else
            Plugin.ChatGui.Print(new XivChatEntry { Message = $"[Nonuglon] {message}", Type = channel });
    }

    /// <summary>Prints only if the value actually changed; a no-op goes to the
    /// log instead, so it's visible via /xllog without cluttering chat.</summary>
    public static void ReportStateChange<T>(T previousValue, T newValue, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(previousValue, newValue))
            Print(message);
        else
            Plugin.Log.Debug($"{message} (already was, no change)");
    }

    /// <summary>Convenience overload for the common "X enabled/disabled." shape used
    /// by every boolean tweak toggle.</summary>
    public static void ReportStateChange(string label, bool previousValue, bool newValue) =>
        ReportStateChange(previousValue, newValue, $"{label} {(newValue ? "enabled" : "disabled")}.");
}

/// <summary>What a chat message is, for General settings' chat-or-xllog choice.</summary>
public enum MessageKind
{
    /// <summary>Answer to something the user just did (command, menu click, button).</summary>
    Reply,
    /// <summary>Something that happened on its own, e.g. Navigate to Flag arriving.</summary>
    Notice,
    /// <summary>Something that went wrong or was refused.</summary>
    Failure,
}
