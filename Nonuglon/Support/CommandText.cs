using System;
using System.Collections.Generic;

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

    /// <summary>Mirrors every chat message to the plugin log at Verbose, so it
    /// survives scrolling out of the chat window. Debug is reserved for
    /// no-op messages in ReportStateChange, keeping the two levels distinct.</summary>
    public static void Print(string message)
    {
        Plugin.ChatGui.Print($"[Nonuglon] {message}");
        Plugin.Log.Verbose(message);
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
