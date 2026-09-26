namespace Nonuglon.Tweaks;

/// <summary>A toggle too small to justify its own sidebar entry - hosted inside
/// "Commands"' panel instead (see Commands.cs). No Enable()/Disable() hook
/// lifecycle and no status dot of its own; Commands is what appears in the
/// tweak list.</summary>
public interface IMiniTweak
{
    string Name { get; }
    string Description { get; }

    /// <summary>Own on/off state, independent of Commands' master switch - both
    /// must be true for the mini-tweak to actually do anything.</summary>
    bool Enabled { get; set; }

    /// <summary>Command name, both nested under "/Nonuglon commands &lt;name&gt;
    /// ..." and as a top-level alias (both wired up in Plugin.cs).</summary>
    string CommandName { get; }

    string[] UsageLines { get; }

    /// <summary>Draws the enabled checkbox inside Commands.DrawOptions. Not
    /// gated on Commands' state - only the effect should be.</summary>
    void DrawRow();

    void HandleCommand(string[] args);

    /// <summary>Called on plugin disposal (see Commands.Dispose) for a
    /// mini-tweak that leaves something live behind Enabled's setter doesn't
    /// already clean up (e.g. RenderToggle's Dtr bar entry). Default no-op.</summary>
    void Dispose() { }
}
