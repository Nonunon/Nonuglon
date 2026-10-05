using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using Dalamud.Utility;
using ECommons.DalamudServices;
using ECommons.GameHelpers;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Nonuglon.Support;
using static Nonuglon.Support.CommandText;

namespace Nonuglon.Tweaks;

/// <summary>Port of NavigateFlag.lua: mount up, then let vnavmesh travel to the
/// map flag. Unlike "/vnav moveflag" it picks the destination itself, since
/// vnavmesh's FlagToPoint takes the highest floor within 5y of the flag (a rock
/// top over the real spot, say). Only touches the game while a run is active:
/// Framework.Update is subscribed per run, every exit path goes through Finish(),
/// and a path that stops making progress ends the run. No hooks or signatures.</summary>
public unsafe class NavigateFlag : TweakBase
{
    public override string Name => "Navigate to Flag";
    public override string Description => "Travels to your map flag using vnavmesh, mounting (and flying) when possible. Requires the vnavmesh plugin.";

    public override bool ConfigEnabled
    {
        get => Plugin.Configuration.NavigateFlagEnabled;
        set => Plugin.Configuration.NavigateFlagEnabled = value;
    }

    public override string[] CommandNames => ["navflag", "flag"];

    public override string[] UsageLines =>
        Enabled
            ? [..base.UsageLines, $"/Nonuglon {CommandNames[0]} <go|stop>"]
            : base.UsageLines;

    private enum Phase { Idle, Mounting, Pathing }

    private const long MountRetryAfterMs = 2200;
    private const long MountTimeoutMs = 6000;
    // vnavmesh pathfinds asynchronously; give it this long to report busy
    // before treating "not running" as finished.
    private const long PathStartGraceMs = 1500;
    private const int MaxCorrections = 2;
    private const int MaxRemounts = 2;
    // A path vnavmesh is following but that hasn't moved us this far in this
    // long is stuck (wall, dismounted under a flying path, ...). Pathfinding
    // time doesn't count.
    private const long StuckTimeoutMs = 10000;
    private const float StuckMoveDistance = 1f;

    private Phase phase = Phase.Idle;
    private Vector3 progressPos;
    private long progressAt;
    private long phaseStartedAt;
    private bool mountRetried;
    private bool sawPathRunning;
    private bool issuePending;
    private int corrections;
    private int remounts;
    private bool remounting;
    private bool flying;
    private Vector3 destination;
    private uint runTerritory;

    public bool IsRunning => phase != Phase.Idle;

    // Nothing to set up: all the work is per-run, started from "go".
    protected override void Enable() { }

    protected override void Disable()
    {
        CancelReadyWait();
        Finish("tweak disabled", quiet: true);
        Svc.Framework.Update -= OnPendingStop;
    }

    public override bool HasWarning => Enabled && !VnavmeshIpc.IsLoaded;

    public override void HandleCommand(string[] args)
    {
        if (!Enabled || args.Length == 0) { base.HandleCommand(args); return; }

        switch (args[0].ToLowerInvariant())
        {
            case "go":
                Start();
                return;
            case "stop":
                if (readyWaiting) CancelReadyWait();
                else if (IsRunning) Finish("stopped by command");
                else Print($"{Name}: nothing running.");
                return;
            default:
                base.HandleCommand(args);
                return;
        }
    }

    // -- Run lifecycle --

    private void Start()
    {
        if (IsRunning)
        {
            Retarget();
            return;
        }
        if (!VnavmeshIpc.IsLoaded) { Print($"{Name}: vnavmesh isn't loaded."); return; }
        if (!VnavmeshIpc.IsReady())
        {
            if (!readyWaiting) BeginReadyWait();
            return;
        }
        if (VnavmeshIpc.IsPathRunning() || VnavmeshIpc.IsPathfindInProgress())
        {
            // Someone else (another plugin, a manual /vnav) is driving; don't fight it.
            Print($"{Name}: vnavmesh is already busy.");
            return;
        }
        if (Svc.Objects.LocalPlayer is null) { Print($"{Name}: not available right now."); return; }
        if (GetFlag() is null) { Print($"{Name}: no flag set in this zone."); return; }

        runTerritory = Svc.ClientState.TerritoryType;
        corrections = 0;
        remounts = 0;
        remounting = false;
        Svc.Framework.Update -= OnUpdate;
        Svc.Framework.Update += OnUpdate;

        if (!MustWalk && Plugin.Configuration.NavigateFlagUseMount && !Svc.Condition[ConditionFlag.Mounted] && Player.CanMount)
        {
            BeginMounting();
            return;
        }

        BeginPathing();
    }

    // A "go" sent right after a zone change can beat vnavmesh's navmesh load, so
    // retry for a configurable while before reporting it isn't ready.
    private static long ReadyRetryMs => (long)(Math.Max(0f, Plugin.Configuration.NavigateFlagReadyRetrySeconds) * 1000);
    private static long ReadyTimeoutMs => (long)(Math.Max(0f, Plugin.Configuration.NavigateFlagReadyTimeoutSeconds) * 1000);
    private bool readyWaiting;
    private long readyDeadline;
    private long readyNextCheck;

    private void BeginReadyWait()
    {
        readyWaiting = true;
        readyDeadline = Environment.TickCount64 + ReadyTimeoutMs;
        readyNextCheck = Environment.TickCount64 + ReadyRetryMs;
        Svc.Framework.Update -= OnReadyWait;
        Svc.Framework.Update += OnReadyWait;
    }

    private void CancelReadyWait()
    {
        readyWaiting = false;
        Svc.Framework.Update -= OnReadyWait;
    }

    private void OnReadyWait(IFramework _)
    {
        var now = Environment.TickCount64;
        if (now < readyNextCheck) return;
        readyNextCheck = now + ReadyRetryMs;

        if (VnavmeshIpc.IsLoaded && VnavmeshIpc.IsReady())
        {
            CancelReadyWait();
            Start();
        }
        else if (now >= readyDeadline)
        {
            CancelReadyWait();
            Print($"{Name}: vnavmesh's navmesh isn't ready yet.");
        }
    }

    /// <summary>Same as NavigateFlag.lua: mounting would break stealth or drop
    /// a carried object (conditions 46 / 9), so walk instead.</summary>
    private static bool MustWalk => Svc.Condition[ConditionFlag.Stealthed] || Svc.Condition[ConditionFlag.CarryingObject];

    private void BeginMounting()
    {
        SummonMount();
        mountRetried = false;
        EnterPhase(Phase.Mounting);
    }

    private void BeginPathing()
    {
        var flag = GetFlag();
        var player = Svc.Objects.LocalPlayer;
        if (flag is null || player is null) { Finish("flag or player went away"); return; }

        flying = Svc.Condition[ConditionFlag.Mounted] && Player.CanFly;
        var picked = PickDestination(flag.Value, player.Position.Y, flying);
        if (picked is null) { Finish("couldn't find a navmesh point near the flag"); return; }
        destination = picked.Value;

        var offFlag = FlatDistance(destination, flag.Value);
        Svc.Log.Debug($"[NavFlag] dest {destination} ({offFlag:F1}y from flag), fly={flying}, try {corrections + 1}");

        IssuePath();
    }

    /// <summary>"go" while a run is active: aim the same run at the current flag,
    /// like re-sending /vnav flyflag mid-flight. Mounting just carries on, since
    /// the flag is read fresh once it's done.</summary>
    private void Retarget()
    {
        if (!Plugin.Configuration.NavigateFlagRestartOnGo) { Print($"{Name}: already running, use \"stop\" first."); return; }
        if (GetFlag() is null) { Print($"{Name}: no flag set in this zone, keeping the current run."); return; }

        corrections = 0;
        Svc.Log.Debug("[NavFlag] retargeting to the current flag");
        if (phase == Phase.Pathing) BeginPathing();
    }

    /// <summary>Hands the destination to vnavmesh. No Stop() first: a new path
    /// simply replaces the one being followed once computed, so there's no
    /// mid-air pause. vnavmesh refuses while a pathfind is still computing, so
    /// that case is deferred to TickPathing.</summary>
    private void IssuePath()
    {
        sawPathRunning = false;
        progressPos = Svc.Objects.LocalPlayer?.Position ?? default;
        progressAt = Environment.TickCount64;
        EnterPhase(Phase.Pathing);
        issuePending = VnavmeshIpc.IsPathfindInProgress();
        if (issuePending) return;
        if (!VnavmeshIpc.PathfindAndMoveTo(destination, flying)) Finish("vnavmesh refused the path");
    }

    /// <summary>The single exit point: unsubscribes, and stops vnavmesh only if
    /// this run is the one that started it.</summary>
    private void Finish(string reason, bool quiet = false)
    {
        if (phase == Phase.Idle) return;

        if (phase == Phase.Pathing)
        {
            VnavmeshIpc.Stop();
            // Stop() only clears the current path; a pathfind still computing
            // would start moving afterwards, so stop that too once it lands.
            if (VnavmeshIpc.IsPathfindInProgress()) WatchPendingPathfind();
        }
        phase = Phase.Idle;
        Svc.Framework.Update -= OnUpdate;

        Svc.Log.Debug($"[NavFlag] finished: {reason}");
        var config = Plugin.Configuration;
        var muted = (config.NavigateFlagMuteArrived && reason.StartsWith("arrived"))
                 || (config.NavigateFlagMuteStopped && reason == "stopped by command");
        if (!quiet && !muted) Print($"{Name}: {reason}.");
    }

    private const long PendingStopTimeoutMs = 60000;
    private long pendingStopDeadline;

    private void WatchPendingPathfind()
    {
        pendingStopDeadline = Environment.TickCount64 + PendingStopTimeoutMs;
        Svc.Framework.Update -= OnPendingStop;
        Svc.Framework.Update += OnPendingStop;
    }

    private void OnPendingStop(IFramework framework)
    {
        if (IsRunning || Environment.TickCount64 > pendingStopDeadline) { Svc.Framework.Update -= OnPendingStop; return; }
        if (VnavmeshIpc.IsPathfindInProgress()) return;
        VnavmeshIpc.Stop();
        Svc.Framework.Update -= OnPendingStop;
    }

    private void EnterPhase(Phase next)
    {
        phase = next;
        phaseStartedAt = Environment.TickCount64;
    }

    private void OnUpdate(IFramework framework)
    {
        // Anything here throwing would otherwise repeat every frame.
        try { Tick(); }
        catch (Exception ex)
        {
            Svc.Log.Error(ex, "[NavFlag] Tick threw, aborting run.");
            Finish("error, see /xllog");
        }
    }

    private void Tick()
    {
        var now = Environment.TickCount64;
        var player = Svc.Objects.LocalPlayer;

        if (player is null || player.IsDead || Svc.Condition[ConditionFlag.BetweenAreas] || Svc.Condition[ConditionFlag.BetweenAreas51]
            || Svc.ClientState.TerritoryType != runTerritory)
        { Finish("interrupted (zoning or died)"); return; }

        switch (phase)
        {
            case Phase.Mounting:
                TickMounting(now);
                break;
            case Phase.Pathing:
                TickPathing(now, player.Position);
                break;
        }
    }

    private void TickMounting(long now)
    {
        var elapsed = now - phaseStartedAt;
        if (Svc.Condition[ConditionFlag.Mounted]) { remounting = false; BeginPathing(); return; }
        // Combat (like /vnav) doesn't stop a run, but it does block mounting,
        // so don't sit out the mount timeout.
        if (Svc.Condition[ConditionFlag.InCombat])
        {
            if (remounting) { Finish("dismounted and can't remount in combat"); return; }
            Svc.Log.Debug("[NavFlag] in combat, walking instead of mounting");
            BeginPathing();
            return;
        }
        if (elapsed >= MountTimeoutMs)
        {
            if (remounting) { Finish("dismounted and couldn't remount"); return; }
            Svc.Log.Debug("[NavFlag] mount timed out, walking instead");
            BeginPathing();
            return;
        }
        if (!mountRetried && elapsed >= MountRetryAfterMs && Player.CanMount)
        {
            SummonMount();
            mountRetried = true;
        }
    }

    private void TickPathing(long now, Vector3 playerPos)
    {
        if (issuePending)
        {
            if (!VnavmeshIpc.IsPathfindInProgress()) IssuePath();
            return;
        }

        var running = VnavmeshIpc.IsPathRunning();
        var computing = VnavmeshIpc.IsPathfindInProgress();
        sawPathRunning |= running || computing;

        // vnavmesh can't take off without a mount, so a flying path would just
        // sit there forever: remount and re-path (a couple of times), else end.
        if (flying && running && !Svc.Condition[ConditionFlag.Mounted])
        {
            VnavmeshIpc.Stop();
            if (MustWalk)
            {
                Svc.Log.Debug("[NavFlag] dismounted while stealthed/carrying, walking the rest");
                BeginPathing();
                return;
            }
            if (remounts >= MaxRemounts || !Plugin.Configuration.NavigateFlagUseMount) { Finish("dismounted during a flying path"); return; }
            remounts++;
            remounting = true;
            Svc.Log.Debug($"[NavFlag] dismounted under a flying path, remounting ({remounts}/{MaxRemounts})");
            BeginMounting();
            return;
        }

        if (computing || Vector3.Distance(playerPos, progressPos) >= StuckMoveDistance)
        {
            progressPos = playerPos;
            progressAt = now;
        }
        else if (running && now - progressAt >= StuckTimeoutMs)
        {
            Finish($"stuck, no progress for {StuckTimeoutMs / 1000}s");
            return;
        }

        if (running || computing) return;
        if (!sawPathRunning && now - phaseStartedAt < PathStartGraceMs) return;

        // vnavmesh is done (or gave up). Check flat distance, since the flag has no height.
        var remaining = FlatDistance(playerPos, destination);
        if (remaining <= Plugin.Configuration.NavigateFlagArriveDistance)
        {
            Finish("arrived");
            return;
        }

        // Flying paths end on the exact point, so a miss there is usually the
        // destination itself being awkward; retrying is opt-in.
        if (flying && !Plugin.Configuration.NavigateFlagCorrectFlying) { Finish($"arrived ({remaining:F1}y off, flying corrections off)"); return; }
        if (corrections >= MaxCorrections) { Finish($"gave up {remaining:F1}y short"); return; }
        corrections++;
        BeginPathing();
    }

    // -- Destination picking --

    /// <summary>Ground: the reachable layer nearest the player's own height, so a
    /// ledge over the flag isn't preferred to the floor you're on. Flying: one of
    /// the stacked layers under the flag, per NavigateFlagFlyLayer. Falls back to
    /// vnavmesh's own pick if nothing closer turns up.</summary>
    private static Vector3? PickDestination(Vector2 flag, float playerY, bool fly) =>
        PickDestinationWithSource(flag, playerY, fly).Point;

    /// <summary>Same pick, plus which step produced it (for the debug table).</summary>
    private static (Vector3? Point, string Source) PickDestinationWithSource(Vector2 flag, float playerY, bool fly)
    {
        if (fly)
        {
            var layers = EnumerateLayers(flag);
            var mode = Plugin.Configuration.NavigateFlagFlyLayer;
            if (mode == FlyLayerMode.Ground)
            {
                if (VnavmeshIpc.PointOnFloor(new(flag.X, 1024, flag.Y), false, 1f) is { } ground) return (ground, "walkable ground");
            }
            else if (layers.Count > 0)
            {
                var index = -1;
                for (var i = 0; i < layers.Count; i++)
                {
                    if (layers[i].Ignored) continue;
                    if (index < 0 || (mode == FlyLayerMode.Nearest && MathF.Abs(layers[i].Pos.Y - playerY) < MathF.Abs(layers[index].Pos.Y - playerY))) index = i;
                }
                return (layers[index].Pos, $"layer {index + 1}/{layers.Count} ({(mode == FlyLayerMode.Top ? "top" : "nearest")})");
            }
            return (VnavmeshIpc.FlagToPoint(), "vnav fallback");
        }

        var seed = new Vector3(flag.X, playerY, flag.Y);
        if (VnavmeshIpc.NearestPointReachable(seed, 1f, 200f) is { } near) return (near, "exact 1y");
        if (VnavmeshIpc.NearestPointReachable(seed, 5f, 200f) is { } wide) return (wide, "widened 5y");
        return (VnavmeshIpc.FlagToPoint(), "vnav fallback");
    }

    private static readonly (FlyLayerMode Mode, string Label, string Tip)[] FlyLayerOptions =
    [
        (FlyLayerMode.Nearest, "Nearest to me", "The surface under the flag closest to your current height. Up among floating islands you get the island, near the ground you get the ground."),
        (FlyLayerMode.Top, "Top", "The highest surface under the flag, same as /vnav flyflag (but within 1y of the flag rather than 5y)."),
        (FlyLayerMode.Ground, "Walkable ground", "Only surfaces you could walk to, never a floating island or rooftop."),
    ];

    private const int MaxLayers = 8;
    // Surfaces closer together than this count as one floor.
    private const float LayerGap = 1f;

    private readonly record struct Layer(Vector3 Pos, bool Reachable, bool Ignored);

    /// <summary>Every walkable surface stacked within 1y of the flag, top first.
    /// vnavmesh only answers "highest floor below Y", so this asks repeatedly,
    /// each time from just under the last hit. An unreachable layer *above*
    /// walkable ground is a floating island or roof (fly-to-able); one *below*
    /// walkable ground is mesh sealed inside terrain, so it's Ignored.</summary>
    private static List<Layer> EnumerateLayers(Vector2 flag)
    {
        var layers = new List<Layer>();
        var y = 1024f;
        var underWalkable = false;
        while (layers.Count < MaxLayers && VnavmeshIpc.PointOnFloor(new(flag.X, y, flag.Y), true, 1f) is { } hit)
        {
            var reachable = VnavmeshIpc.IsPointOnMesh(hit, 1f, false);
            layers.Add(new(hit, reachable, !reachable && underWalkable));
            underWalkable |= reachable;
            y = hit.Y - LayerGap;
        }
        return layers;
    }

    /// <summary>Flag world X/Z, or null if none is set or it's in another zone.</summary>
    private static Vector2? GetFlag()
    {
        var map = AgentMap.Instance();
        if (map == null || map->FlagMarkerCount == 0) return null;
        var marker = map->FlagMapMarkers[0];
        if (marker.TerritoryId != Svc.ClientState.TerritoryType) return null;
        return new(marker.XFloat, marker.YFloat);
    }

    private static float FlatDistance(Vector3 a, Vector3 b) => Vector2.Distance(new(a.X, a.Z), new(b.X, b.Z));
    private static float FlatDistance(Vector3 a, Vector2 flag) => Vector2.Distance(new(a.X, a.Z), flag);

    private static void SummonMount()
    {
        var id = Plugin.Configuration.NavigateFlagMountId;
        if (!MountPicker.Use(id))
            Svc.Log.Debug($"[NavFlag] {MountPicker.GetName(id)} not owned on this character, used roulette");
    }

    // -- Config UI --

    public override void DrawOptions()
    {
        var config = Plugin.Configuration;

        if (!VnavmeshIpc.IsLoaded)
        {
            ImGui.PushTextWrapPos(ImGui.GetContentRegionAvail().X + ImGui.GetCursorPosX());
            ImGui.TextColored(UiColors.Warning, "vnavmesh not detected - this tweak has no effect until it's installed and loaded.");
            ImGui.PopTextWrapPos();
        }

        var useMount = config.NavigateFlagUseMount;
        if (ImGui.Checkbox("Mount up first##NavigateFlag", ref useMount))
        {
            config.NavigateFlagUseMount = useMount;
            config.Save();
        }

        if (config.NavigateFlagUseMount)
        {
            var mountId = config.NavigateFlagMountId;
            ImGui.SetNextItemWidth(250f * ImGui.GetIO().FontGlobalScale);
            if (MountPicker.Draw("Mount##NavigateFlag", ref mountId))
            {
                config.NavigateFlagMountId = mountId;
                config.Save();
            }
            if (!MountPicker.IsOwned(mountId))
                ImGui.TextColored(UiColors.Warning, "This character doesn't own that mount, Mount Roulette is used instead.");
        }

        var correctFlying = config.NavigateFlagCorrectFlying;
        if (ImGui.Checkbox("Retry flying runs that end off target##NavigateFlag", ref correctFlying))
        {
            config.NavigateFlagCorrectFlying = correctFlying;
            config.Save();
        }

        var restartOnGo = config.NavigateFlagRestartOnGo;
        if (ImGui.Checkbox("\"Go\" while running retargets to the current flag##NavigateFlag", ref restartOnGo))
        {
            config.NavigateFlagRestartOnGo = restartOnGo;
            config.Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Drop a new flag mid-run and press Go again (button, command or macro) to head there without stopping. Off: Go is refused until the run ends or is stopped.");

        ImGui.TextUnformatted("When flying, aim for:");
        foreach (var (mode, label, tip) in FlyLayerOptions)
        {
            ImGui.SameLine();
            if (ImGui.RadioButton($"{label}##NavigateFlagFlyLayer", config.NavigateFlagFlyLayer == mode))
            {
                config.NavigateFlagFlyLayer = mode;
                config.Save();
            }
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(tip);
        }

        var arrive = config.NavigateFlagArriveDistance;
        if (ImGui.SliderFloat("Arrive distance (y)##NavigateFlag", ref arrive, 0.5f, 10f, "%.1f"))
        {
            config.NavigateFlagArriveDistance = arrive;
            config.Save();
        }

        var retry = config.NavigateFlagReadyRetrySeconds;
        if (ImGui.SliderFloat("Navmesh retry interval (s)##NavigateFlag", ref retry, 0.1f, 1f, "%.1f"))
        {
            config.NavigateFlagReadyRetrySeconds = retry;
            config.Save();
        }
        var timeout = config.NavigateFlagReadyTimeoutSeconds;
        if (ImGui.SliderFloat("Navmesh retry timeout (s)##NavigateFlag", ref timeout, 0.1f, 10f, "%.1f"))
        {
            config.NavigateFlagReadyTimeoutSeconds = timeout;
            config.Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("If Go is sent before vnavmesh's navmesh is ready, check again every interval until the timeout, then give up. Ctrl+click a slider to type any value.");

        if (!Enabled) return;
        ImGui.Spacing();
        if (!IsRunning || config.NavigateFlagRestartOnGo)
        {
            if (ImGui.Button("Go to flag##NavigateFlag")) Start();
            if (IsRunning) ImGui.SameLine();
        }
        if (IsRunning)
        {
            if (ImGui.Button("Stop##NavigateFlag")) Finish("stopped from config window");
            ImGui.SameLine();
            ImGui.TextDisabled($"Running ({phase}).");
        }

        ImGui.Spacing();
        if (ImGui.TreeNode("Debug##NavigateFlagDebug"))
        {
            DrawDebug();
            ImGui.TreePop();
        }
    }

    // -- Live debug panel --

    /// <summary>One row of the debug table. Y is NaN for the flag itself,
    /// which has no height.</summary>
    private readonly record struct DebugRow(string Label, Vector3 World, string Map, float ToFlag, float ToYou, string Reachable, string Source);

    private const long DebugRefreshMs = 250;
    private long nextDebugAt;
    private List<DebugRow> debugRows = [];
    private string debugHeader = string.Empty;

    private void DrawDebug()
    {
        var config = Plugin.Configuration;
        var muteArrived = config.NavigateFlagMuteArrived;
        if (ImGui.Checkbox("Hide \"arrived\" chat message##NavigateFlagDebug", ref muteArrived))
        {
            config.NavigateFlagMuteArrived = muteArrived;
            config.Save();
        }
        var muteStopped = config.NavigateFlagMuteStopped;
        if (ImGui.Checkbox("Hide \"stopped by command\" chat message##NavigateFlagDebug", ref muteStopped))
        {
            config.NavigateFlagMuteStopped = muteStopped;
            config.Save();
        }

        // The IPC queries aren't free, so only refresh a few times a second,
        // and only while this node is open.
        if (Environment.TickCount64 >= nextDebugAt)
        {
            nextDebugAt = Environment.TickCount64 + DebugRefreshMs;
            RefreshDebug();
        }

        ImGui.PushTextWrapPos(ImGui.GetContentRegionAvail().X + ImGui.GetCursorPosX());
        ImGui.TextDisabled(debugHeader);
        ImGui.PopTextWrapPos();
        if (debugRows.Count == 0) return;

        if (ImGui.SmallButton("Copy all##NavigateFlagDebug"))
        {
            var sb = new StringBuilder(debugHeader).AppendLine();
            foreach (var row in debugRows) sb.AppendLine(FormatRow(row));
            ImGui.SetClipboardText(sb.ToString());
        }

        const ImGuiTableFlags flags = ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.ScrollX;
        if (!ImGui.BeginTable("##NavigateFlagDebugTable", 8, flags)) return;

        foreach (var header in (string[])["", "World X, Y, Z", "Map", "To flag", "To you", "Reachable", "Source", ""])
            ImGui.TableSetupColumn(header);
        ImGui.TableHeadersRow();

        for (var i = 0; i < debugRows.Count; i++)
        {
            var row = debugRows[i];
            ImGui.TableNextRow();
            ImGui.TableNextColumn(); ImGui.TextUnformatted(row.Label);
            ImGui.TableNextColumn(); ImGui.TextUnformatted(FormatWorld(row.World));
            ImGui.TableNextColumn(); ImGui.TextUnformatted(row.Map);
            ImGui.TableNextColumn(); ImGui.TextUnformatted(FormatDistance(row.ToFlag));
            ImGui.TableNextColumn(); ImGui.TextUnformatted(FormatDistance(row.ToYou));
            ImGui.TableNextColumn(); ImGui.TextUnformatted(row.Reachable);
            ImGui.TableNextColumn(); ImGui.TextUnformatted(row.Source);
            ImGui.TableNextColumn();
            if (ImGui.SmallButton($"Copy##NavigateFlagDebug{i}"))
                ImGui.SetClipboardText(FormatRow(row));
        }

        ImGui.EndTable();
    }

    private void RefreshDebug()
    {
        debugRows = [];
        var player = Svc.Objects.LocalPlayer;
        var map = AgentMap.Instance();
        if (player is null || map == null || map->FlagMarkerCount == 0)
        {
            debugHeader = "No flag set.";
            return;
        }

        // Read the marker directly (not GetFlag()) so a flag in another zone
        // still shows: vnavmesh's FlagToPoint doesn't check the zone either.
        var marker = map->FlagMapMarkers[0];
        var flag = new Vector2(marker.XFloat, marker.YFloat);
        var sameZone = marker.TerritoryId == Svc.ClientState.TerritoryType;
        var mapRow = Svc.Data.GetExcelSheet<Lumina.Excel.Sheets.Map>().GetRowOrDefault(marker.MapId);
        var you = player.Position;
        var vnavReady = VnavmeshIpc.IsLoaded && VnavmeshIpc.IsReady();

        debugHeader = $"Both vnavmesh and this tweak read the same flag marker (AgentMap.FlagMapMarkers[0]); only the floor pick differs. "
            + $"Flag zone {marker.TerritoryId} vs current {Svc.ClientState.TerritoryType}{(sameZone ? "" : " (DIFFERENT zone, this tweak refuses; vnavmesh doesn't check)")}. "
            + (vnavReady ? $"vnavmesh path running: {VnavmeshIpc.IsPathRunning()}." : "vnavmesh not loaded/ready, picks unavailable.");

        AddRow("Flag", new(flag.X, float.NaN, flag.Y), reachable: "-", source: "-");
        AddRow("You", you, reachable: "-", source: "-");
        if (!vnavReady) return;

        AddPick("vnav pick (moveflag/flyflag)", (VnavmeshIpc.FlagToPoint(), "vnav highest in 5y"));
        AddPick("Our ground pick", PickDestinationWithSource(flag, you.Y, false));
        AddPick("Our flying pick", PickDestinationWithSource(flag, you.Y, true));
        if (VnavmeshIpc.ListWaypoints() is { Count: > 0 } waypoints)
            AddPick("vnav current path end", (waypoints[^1], "-"));

        var layers = EnumerateLayers(flag);
        for (var i = 0; i < layers.Count; i++)
            AddPick($"Layer {i + 1}/{layers.Count}{(i == 0 ? " (top)" : "")}",
                (layers[i].Pos, $"{layers[i].Pos.Y - you.Y:+0.0;-0.0}y vs you{(layers[i].Ignored ? ", ignored (under walkable ground)" : "")}"));

        void AddPick(string label, (Vector3? Point, string Source) pick)
        {
            if (pick.Point is not { } v) { debugRows.Add(new(label, new(float.NaN), "-", float.NaN, float.NaN, "none", pick.Source)); return; }
            AddRow(label, v, VnavmeshIpc.IsPointOnMesh(v, 2f, false) ? "yes" : "NO", pick.Source);
        }

        void AddRow(string label, Vector3 world, string reachable, string source)
        {
            var mapCoords = "-";
            if (mapRow is { } m)
            {
                var c = MapUtil.WorldToMap(new Vector2(world.X, world.Z), m);
                mapCoords = $"{c.X:F1}, {c.Y:F1}";
            }
            var flat = new Vector2(world.X, world.Z);
            debugRows.Add(new(label, world, mapCoords, Vector2.Distance(flat, flag), Vector2.Distance(flat, new(you.X, you.Z)), reachable, source));
        }
    }

    private static string FormatWorld(Vector3 v) =>
        float.IsNaN(v.X) ? "-" : $"{v.X:F2}, {(float.IsNaN(v.Y) ? "?" : v.Y.ToString("F2"))}, {v.Z:F2}";

    private static string FormatDistance(float d) => float.IsNaN(d) ? "-" : $"{d:F2}y";

    private static string FormatRow(DebugRow r) =>
        $"{r.Label}: world {FormatWorld(r.World)} | map {r.Map} | to flag {FormatDistance(r.ToFlag)} | to you {FormatDistance(r.ToYou)} | reachable {r.Reachable} | source {r.Source}";
}
