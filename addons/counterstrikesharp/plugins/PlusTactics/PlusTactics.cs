using System.Text.Json;
using System.Text.Json.Serialization;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;

namespace PlusTactics;

/// <summary>
/// Drives the bot behaviour tree selection and the named waypoints those trees
/// move to. CS2 picks one tree file per match through mp_bot_ai_bt, so a
/// "personality" here is a whole-match posture (rush / hold / split), not a
/// per-bot one: the tree vocabulary has no node that reads a bot identity.
/// </summary>
public sealed class PlusTacticsPlugin : BasePlugin
{
    public override string ModuleName => "PlusTactics";
    public override string ModuleVersion => "0.1.0";
    public override string ModuleAuthor => "Local Arena fork";
    public override string ModuleDescription =>
        "Applies a bot behaviour tree per match and spawns the waypoints it moves to";

    private const string TreeFolder = "localarena";
    private const string StockTree = "scripts/ai/rush/bt_default.kv3";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    private string? _csgoRoot;
    private string _currentMap = "";
    private TacticsConfig _config = new();
    private string? _activeTree;
    private bool _halfRollPending;
    private bool _mapReady;
    private readonly List<string> _spawnedWaypoints = [];

    public override void Load(bool hotReload)
    {
        RegisterListener<Listeners.OnMapStart>(OnMapStart);
        RegisterEventHandler<EventRoundStart>(OnRoundStart, HookMode.Pre);
        RegisterEventHandler<EventRoundAnnounceLastRoundHalf>(OnLastRoundOfHalf, HookMode.Pre);
        RegisterEventHandler<EventRoundEnd>(OnRoundEnd, HookMode.Pre);

        AddCommand("css_tac_status", "Show the active tactics tree", OnStatus);
        AddCommand("css_tac_tree", "Force a tactics tree for this match: css_tac_tree <id>", OnSetTree);
        AddCommand("css_tac_reroll", "Pick another random tactics tree", OnReroll);
        AddCommand("css_tac_probe", "Milestone probe tree: css_tac_probe <hunt|hold>", OnProbe);
        AddCommand("css_tac_wp", "Waypoints: css_tac_wp list | css_tac_wp add <name>", OnWaypoint);
        AddCommand("css_tac_dump", "Log which map entities the plugin can see", OnDump);

        Logger.LogInformation("[PlusTactics] loaded; tree folder scripts/ai/{0}", TreeFolder);
    }

    // ---- lifecycle ---------------------------------------------------------

    /// <summary>
    /// The front end reports its map as the literal string "&lt;empty&gt;", so an
    /// emptiness check alone lets it through and the spawn below crashes the client.
    /// </summary>
    private static bool IsFrontEnd(string? mapName) =>
        string.IsNullOrWhiteSpace(mapName) || mapName.StartsWith("<", StringComparison.Ordinal);

    private void OnMapStart(string mapName)
    {
        // The engine also fires this for the front end, with an empty name. That
        // world has no nav mesh, no bombsites and no bots: spawning an info_target
        // there crashes the client on startup, and mp_bot_ai_bt has nothing to
        // attach to, so every path below is skipped for it.
        if (IsFrontEnd(mapName))
        {
            _currentMap = "";
            Logger.LogInformation("[PlusTactics] front end {Map} detected; tactics stay idle", mapName);
            return;
        }

        _currentMap = mapName;
        _halfRollPending = false;
        _mapReady = false;
        LoadConfig();

        // Recorded before anything is written: this is the tree the engine chose
        // for the mode, which is what a custom tree has to be equivalent to.
        Logger.LogInformation("[PlusTactics] engine tree for {Map}: '{Tree}'", mapName, EngineTree());
        ApplyTree(PickTree(), "map_start");

        // The world is not populated at map start: the bombsites and spawn points
        // are not enumerable yet, and creating entities this early has taken the
        // client down. Seeding waits for the first round instead.
        AddTimer(6.0f, TryPrepareMap);
    }

    private HookResult OnRoundStart(EventRoundStart @event, GameEventInfo info)
    {
        TryPrepareMap();
        return HookResult.Continue;
    }

    private void TryPrepareMap()
    {
        if (_mapReady || IsFrontEnd(_currentMap)) return;

        // Discovery can run before the map finishes populating: the timers fire
        // while the bombsites are still missing. Keep retrying on later rounds
        // instead of locking in an empty result.
        var sitesFound = DiscoverMapPoints();
        RemoveWaypoints();
        SpawnWaypoints();
        _mapReady = sitesFound;
    }

    private HookResult OnLastRoundOfHalf(EventRoundAnnounceLastRoundHalf @event, GameEventInfo info)
    {
        if (_config.Enabled && string.Equals(_config.Mode, "per_half", StringComparison.OrdinalIgnoreCase))
            _halfRollPending = true;
        return HookResult.Continue;
    }

    private HookResult OnRoundEnd(EventRoundEnd @event, GameEventInfo info)
    {
        if (!_halfRollPending) return HookResult.Continue;
        _halfRollPending = false;
        ApplyTree(PickTree(), "halftime");
        return HookResult.Continue;
    }

    // ---- tree selection ----------------------------------------------------

    private string PickTree()
    {
        if (!_config.Enabled) return "";
        if (string.Equals(_config.Mode, "fixed", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(_config.FixedTree))
            return _config.FixedTree!;

        var pool = _config.Pool.Where(id => !string.IsNullOrWhiteSpace(id)).ToArray();
        if (pool.Length == 0) return "";
        return pool[Random.Shared.Next(pool.Length)];
    }

    private void ApplyTree(string id, string reason)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            // Clearing the convar hands control back to the engine, which picks the
            // tree per game mode. Forcing the rush tree in a defusal match is not the
            // same thing and changes how bots buy and equip.
            Server.ExecuteCommand("mp_bot_ai_bt \"\"");
            Server.ExecuteCommand("mp_bot_ai_bt_clear_cache");
            _activeTree = null;
            Logger.LogInformation("[PlusTactics] tree cleared; engine default restored ({Reason})", reason);
            WriteActiveState("engine-default");
            return;
        }

        var relative = string.Format("scripts/ai/{0}/{1}.kv3", TreeFolder, id);

        if (!TreeFileExists(relative))
        {
            Logger.LogWarning("[PlusTactics] tree {Path} is missing; restoring the engine default", relative);
            Server.ExecuteCommand("mp_bot_ai_bt \"\"");
            Server.ExecuteCommand("mp_bot_ai_bt_clear_cache");
            _activeTree = null;
            WriteActiveState("engine-default");
            return;
        }

        Server.ExecuteCommand(string.Format("mp_bot_ai_bt \"{0}\"", relative));
        Server.ExecuteCommand("mp_bot_ai_bt_clear_cache");

        _activeTree = string.IsNullOrWhiteSpace(id) ? null : id;
        Logger.LogInformation("[PlusTactics] tree={Tree} path={Path} reason={Reason}",
            _activeTree ?? "stock", relative, reason);
        WriteActiveState(relative);
    }

    private static string EngineTree() =>
        ConVar.Find("mp_bot_ai_bt")?.StringValue ?? "<unavailable>";

    private bool TreeFileExists(string relative)
    {
        if (_csgoRoot == null) return true;
        var tail = relative.Replace('/', Path.DirectorySeparatorChar);
        if (File.Exists(Path.Combine(_csgoRoot, tail))) return true;
        return File.Exists(Path.Combine(_csgoRoot, "overrides", tail));
    }

    // ---- waypoints ---------------------------------------------------------

    private void SpawnWaypoints()
    {
        foreach (var pair in _config.Waypoints)
        {
            var point = pair.Value;
            if (point.Length < 3) continue;
            try
            {
                var entity = Utilities.CreateEntityByName<CBaseEntity>("info_target");
                if (entity == null)
                {
                    Logger.LogWarning("[PlusTactics] could not spawn waypoint {Name}", pair.Key);
                    continue;
                }
                entity.Entity.Name = pair.Key;
                entity.Teleport(new Vector(point[0], point[1], point[2]));
                entity.DispatchSpawn();
                _spawnedWaypoints.Add(pair.Key);
            }
            catch (Exception error)
            {
                Logger.LogWarning(error, "[PlusTactics] waypoint {Name} failed", pair.Key);
            }
        }
        Logger.LogInformation("[PlusTactics] spawned {Count} waypoints on {Map}", _spawnedWaypoints.Count, _currentMap);
    }

    private void RemoveWaypoints()
    {
        if (_spawnedWaypoints.Count == 0) return;
        var names = new HashSet<string>(_spawnedWaypoints, StringComparer.OrdinalIgnoreCase);
        foreach (var entity in Utilities.FindAllEntitiesByDesignerName<CBaseEntity>("info_target"))
        {
            if (!entity.IsValid) continue;
            var name = entity.Entity?.Name;
            if (name != null && names.Contains(name)) entity.Remove();
        }
        _spawnedWaypoints.Clear();
    }

    // ---- map discovery -----------------------------------------------------

    /// <summary>
    /// Seeds the points every map has: both bombsites and both spawns. Authored
    /// waypoints win, so a map only needs marks for the extra routes.
    /// </summary>
    private bool DiscoverMapPoints()
    {
        var added = 0;
        var sites = DiscoverSites();
        if (sites > 0) added += sites;
        // DiscoverSites reports how many points it added, which is zero once the
        // user has marked sites by hand. The retry flag needs a different signal,
        // so the entity presence is checked separately.
        var sitesPresent = Utilities.FindAllEntitiesByDesignerName<CBaseEntity>("func_bomb_target").Any();
        added += DiscoverSpawn("wp_t_spawn", "info_player_terrorist");
        added += DiscoverSpawn("wp_ct_spawn", "info_player_counterterrorist");
        if (added > 0) SaveConfig();
        return sitesPresent;
    }

    private int DiscoverSites()
    {
        var sites = new List<(string name, Vector origin)>();
        foreach (var entity in Utilities.FindAllEntitiesByDesignerName<CBaseEntity>("func_bomb_target"))
        {
            if (!entity.IsValid) continue;
            var origin = entity.AbsOrigin;
            if (origin == null) continue;
            var name = entity.Entity?.Name ?? "";
            sites.Add((name, origin));
        }
        if (sites.Count == 0)
        {
            Logger.LogWarning("[PlusTactics] no func_bomb_target on {Map}; bombsites stay unresolved", _currentMap);
            return 0;
        }

        // Prefer names that already spell out the site, otherwise fall back to
        // an X ordering so A and B stay stable across restarts of the same map.
        var ordered = sites
            .OrderBy(site => site.name.Contains("a", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(site => site.origin.X)
            .ToList();

        var added = 0;
        var labels = new[] { "wp_site_a", "wp_site_b" };
        for (var index = 0; index < ordered.Count && index < labels.Length; index++)
        {
            var site = ordered[index];
            Logger.LogInformation("[PlusTactics] bombsite {Label} <- name='{Name}' at {X} {Y} {Z}",
                labels[index], site.name, site.origin.X, site.origin.Y, site.origin.Z);
            if (_config.Waypoints.ContainsKey(labels[index])) continue;
            _config.Waypoints[labels[index]] = [site.origin.X, site.origin.Y, site.origin.Z];
            added++;
        }
        return added;
    }

    private int DiscoverSpawn(string label, string designerName)
    {
        var spawn = Utilities.FindAllEntitiesByDesignerName<CBaseEntity>(designerName).FirstOrDefault();
        var origin = spawn?.AbsOrigin;
        if (origin == null)
        {
            // The spawn entities are not enumerable by designer name on CS2 maps,
            // but the players themselves stand on them, so their position is the
            // same point whenever a round is live.
            var team = label.Contains("_t_", StringComparison.Ordinal) ? CsTeam.Terrorist : CsTeam.CounterTerrorist;
            origin = Utilities.GetPlayers()
                .Where(player => player is { IsValid: true } && player.Team == team)
                .Select(player => player.PlayerPawn?.Value?.AbsOrigin)
                .FirstOrDefault(point => point != null);
            if (origin == null)
            {
                Logger.LogWarning("[PlusTactics] no {Class} on {Map} and no live player to read it from", designerName, _currentMap);
                return 0;
            }
            Logger.LogInformation("[PlusTactics] spawn {Label} read from a live player", label);
        }
        Logger.LogInformation("[PlusTactics] spawn {Label} <- {Class} at {X} {Y} {Z}",
            label, designerName, origin.X, origin.Y, origin.Z);
        if (_config.Waypoints.ContainsKey(label)) return 0;
        _config.Waypoints[label] = [origin.X, origin.Y, origin.Z];
        return 1;
    }

    // ---- commands ----------------------------------------------------------

    private void OnStatus(CCSPlayerController? player, CommandInfo command)
    {
        command.ReplyToCommand(string.Format(
            "[PlusTactics] tree={0} convar='{1}' mode={2} map={3} waypoints={4}",
            _activeTree ?? "stock", EngineTree(), _config.Mode, _currentMap, _spawnedWaypoints.Count));
    }

    private void OnSetTree(CCSPlayerController? player, CommandInfo command)
    {
        var id = command.GetArg(1);
        if (string.IsNullOrWhiteSpace(id))
        {
            command.ReplyToCommand("[PlusTactics] usage: css_tac_tree <id>");
            return;
        }
        ApplyTree(id, "manual");
        command.ReplyToCommand(string.Format("[PlusTactics] tree set to {0}", _activeTree ?? "stock"));
    }

    private void OnReroll(CCSPlayerController? player, CommandInfo command)
    {
        var id = PickTree();
        ApplyTree(id, "reroll");
        command.ReplyToCommand(string.Format("[PlusTactics] rerolled to {0}", _activeTree ?? "stock"));
    }

    private void OnProbe(CCSPlayerController? player, CommandInfo command)
    {
        var kind = command.GetArg(1);
        if (kind != "hunt" && kind != "hold")
        {
            command.ReplyToCommand("[PlusTactics] usage: css_tac_probe <hunt|hold>");
            return;
        }
        ApplyTree("test_" + kind, "probe");
        command.ReplyToCommand(string.Format(
            "[PlusTactics] probe tree -> {0}; watch the bots, then css_tac_probe hunt to compare",
            "test_" + kind));
    }

    private void OnDump(CCSPlayerController? player, CommandInfo command)
    {
        string[] candidates =
        [
            "func_bomb_target", "func_buyzone", "info_player_terrorist",
            "info_player_counterterrorist", "info_player_start", "player_spawn",
            "info_map_parameters", "info_target"
        ];
        foreach (var name in candidates)
        {
            var count = Utilities.FindAllEntitiesByDesignerName<CBaseEntity>(name).Count();
            Logger.LogInformation("[PlusTactics] dump {Class} = {Count} on {Map}", name, count, _currentMap);
        }
        command.ReplyToCommand("[PlusTactics] entity dump written to the plugin log");
    }

    private void OnWaypoint(CCSPlayerController? player, CommandInfo command)
    {
        var action = command.GetArg(1);
        if (action == "list")
        {
            command.ReplyToCommand(string.Format("[PlusTactics] {0} waypoints: {1}",
                _config.Waypoints.Count, string.Join(", ", _config.Waypoints.Keys)));
            return;
        }

        if (action != "add")
        {
            command.ReplyToCommand("[PlusTactics] usage: css_tac_wp list | css_tac_wp add <name>");
            return;
        }

        var name = command.GetArg(2);
        if (string.IsNullOrWhiteSpace(name))
        {
            command.ReplyToCommand("[PlusTactics] usage: css_tac_wp add <name>");
            return;
        }

        var pawn = player?.PlayerPawn?.Value;
        var origin = pawn?.AbsOrigin;
        if (origin == null)
        {
            command.ReplyToCommand("[PlusTactics] stand somewhere alive first");
            return;
        }

        _config.Waypoints[name] = [origin.X, origin.Y, origin.Z];
        SaveConfig();
        SpawnWaypoints();
        command.ReplyToCommand(string.Format("[PlusTactics] waypoint {0} saved at {1},{2},{3}",
            name, (int)origin.X, (int)origin.Y, (int)origin.Z));
        Logger.LogInformation("[PlusTactics] waypoint {Name} -> {X} {Y} {Z}",
            name, origin.X, origin.Y, origin.Z);
    }

    // ---- files -------------------------------------------------------------

    private string CsgoRoot()
    {
        if (_csgoRoot != null) return _csgoRoot;
        var reported = Server.GameDirectory ?? "";
        var leaf = Path.GetFileName(reported.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var candidates = new List<string>();
        if (leaf.Equals("csgo", StringComparison.OrdinalIgnoreCase)) candidates.Add(reported);
        else if (leaf.Equals("game", StringComparison.OrdinalIgnoreCase)) candidates.Add(Path.Combine(reported, "csgo"));
        candidates.Add(Path.Combine(reported, "game", "csgo"));
        candidates.Add(Path.Combine(reported, "csgo"));

        foreach (var candidate in candidates)
        {
            if (File.Exists(Path.Combine(candidate, "gameinfo.gi")))
            {
                _csgoRoot = candidate;
                return candidate;
            }
        }
        _csgoRoot = reported;
        return reported;
    }

    private string SbipPath(string file) => Path.Combine(CsgoRoot(), ".csbip", file);

    private void LoadConfig()
    {
        var path = SbipPath("tactics.json");
        try
        {
            if (!File.Exists(path))
            {
                _config = new TacticsConfig();
                SaveConfig();
                return;
            }
            _config = JsonSerializer.Deserialize<TacticsConfig>(File.ReadAllText(path), JsonOptions) ?? new TacticsConfig();
            _config.Waypoints ??= new();
        }
        catch (Exception error)
        {
            Logger.LogError(error, "[PlusTactics] could not read {Path}; using defaults", path);
            _config = new TacticsConfig();
        }
    }

    private void SaveConfig()
    {
        try
        {
            var path = SbipPath("tactics.json");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(_config, JsonOptions));
        }
        catch (Exception error)
        {
            Logger.LogError(error, "[PlusTactics] could not write tactics.json");
        }
    }

    private void WriteActiveState(string relativePath)
    {
        try
        {
            var path = SbipPath("active-tactics.json");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var state = new Dictionary<string, object?>
            {
                ["tree"] = _activeTree ?? "stock",
                ["path"] = relativePath,
                ["map"] = _currentMap,
                ["mode"] = _config.Mode,
                ["applied_at"] = DateTimeOffset.UtcNow.ToString("O"),
            };
            File.WriteAllText(path, JsonSerializer.Serialize(state, JsonOptions));
        }
        catch (Exception error)
        {
            Logger.LogError(error, "[PlusTactics] could not write active-tactics.json");
        }
    }
}

public sealed class TacticsConfig
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    /// <summary>per_match | per_half | fixed</summary>
    [JsonPropertyName("mode")]
    public string Mode { get; set; } = "per_match";

    [JsonPropertyName("fixed_tree")]
    public string? FixedTree { get; set; }

    [JsonPropertyName("pool")]
    public string[] Pool { get; set; } = [];

    [JsonPropertyName("waypoints")]
    public Dictionary<string, float[]> Waypoints { get; set; } = new();
}
