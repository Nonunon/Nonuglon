namespace Nonuglon.Tweaks;

/// <summary>A tiny, self-contained toggle too small to justify its own entry in
/// the tweak sidebar - hosted instead inside the "Commands" tweak's own panel
/// (see Commands.cs). A mini-tweak owns its own persisted enabled flag and chat
/// subcommand, same idea as a full TweakBase, but has no Enable()/Disable() hook
/// lifecycle and no sidebar status dot of its own - Commands is what actually
/// appears in the tweak list.</summary>
public interface IMiniTweak
{
    string Name { get; }
    string Description { get; }

    /// <summary>This mini-tweak's own on/off state, independent of whether
    /// Commands itself (the master switch) is currently on - both need to be
    /// true before the mini-tweak actually does anything.</summary>
    bool Enabled { get; set; }

    /// <summary>Chat command name this mini-tweak answers to, both nested under
    /// "/Nonuglon commands &lt;name&gt; ..." and as a standalone top-level
    /// "/Nonuglon &lt;name&gt; ..." alias (both wired up in Plugin.cs).</summary>
    string CommandName { get; }

    string[] UsageLines { get; }

    /// <summary>Draws this mini-tweak's own enabled checkbox plus whatever else
    /// it needs, inside Commands.DrawOptions. Not itself gated on Commands being
    /// on - the checkbox should stay togglable regardless, so a mini-tweak can be
    /// pre-configured while Commands is off; only the *effect* of being enabled
    /// should check Commands' state.</summary>
    void DrawRow();

    void HandleCommand(string[] args);
}
