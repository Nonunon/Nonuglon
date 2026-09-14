using System;
using System.Collections.Generic;

namespace Nonuglon.Support;

/// <summary>Shared parsing/output helpers for /Nonuglon chat commands. Lives here
/// (rather than on Plugin or TweakBase) so both Plugin's top-level command
/// dispatcher and each TweakBase's own HandleCommand override can share the same
/// bool-parsing and chat-reporting behavior without depending on each other.</summary>
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

    /// <summary>Same as TryParseBool, but also accepts "toggle" to flip whatever the
    /// current value already is - handy for macros/hotkeys where you don't want to
    /// track state yourself.</summary>
    public static bool ResolveBool(string s, bool currentValue, out bool value)
    {
        if (s.Equals("toggle", StringComparison.OrdinalIgnoreCase))
        {
            value = !currentValue;
            return true;
        }

        return TryParseBool(s, out value);
    }

    /// <summary>Prints to chat AND mirrors the same message to the plugin log at
    /// Verbose level, so every chat message has a corresponding /xllog entry even
    /// after it scrolls out of the chat window. Verbose (not Debug) on purpose -
    /// Debug is reserved for the "no-op, suppressed from chat" messages in
    /// ReportStateChange, so the two log levels stay distinct: Verbose = full
    /// cookie trail of everything sent to chat, Debug = the extra stuff that
    /// didn't make it to chat.</summary>
    public static void Print(string message)
    {
        Plugin.ChatGui.Print($"[Nonuglon] {message}");
        Plugin.Log.Verbose(message);
    }

    /// <summary>Prints a state-change message to chat only if the value actually
    /// changed. If the command was a no-op (e.g. "instantreturn on" while it was
    /// already on), the message is routed to the plugin log instead, so it's still
    /// visible via /xllog without cluttering chat. Generic so it covers any
    /// comparable config value (bool toggles, the target name, the timeout ms),
    /// not just booleans.</summary>
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
