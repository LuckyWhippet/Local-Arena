using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using System.Runtime.InteropServices;

namespace OfflineMatchTelemetry;

public class OfflineMatchTelemetryPlugin : BasePlugin
{
    private const int PollInterval = 64;
    private string _databasePath = "";
    private IntPtr _sqliteNativeLibrary;
    private int _currentMatchId;
    private string _matchMap = "";
    private int _round;
    private int _lastObservedRounds = -1;
    private int _lastWrittenRound = -1;
    // Rounds the engine counted before the current half. Halftime zeroes
    // TotalRoundsPlayed, so the absolute round number is this plus what the
    // engine reports now.
    private int _roundOffset;
    // Highest TotalRoundsPlayed seen in the current half. A drop means the
    // counter was zeroed, which happens at halftime and at a genuine restart.
    private int _halfPeakRounds = -1;
    private int _tickCounter;
    private int _ctScore;
    private int _tScore;
    private int _teamAScore;
    private int _teamBScore;
    private readonly HashSet<string> _teamAPlayers = [];
    private readonly HashSet<string> _teamBPlayers = [];
    private bool _fixedTeamsReady;
    private string? _roundCtTeam;
    private string? _roundTTeam;
    private bool _matchActive;
    private DateTimeOffset _matchStartedAt;
    private readonly Dictionary<string, PrevStat> _prev = new();
    private List<PlayerSnapshot> _lastPlayers = [];
    private readonly Dictionary<string, RoundPlayerState> _roundState = new();
    private readonly Dictionary<string, MatchEventStats> _matchEventStats = new();
    private readonly Dictionary<string, LivePlayerState> _livePlayers = new();
    private readonly List<PendingTrade> _pendingTrades = [];
    private readonly List<UnresolvedDeath> _unresolvedDeaths = [];
    private ClutchCandidate? _tClutch;
    private ClutchCandidate? _ctClutch;
    private bool _eventRoundActive;
    private bool _roundRosterReady;
    private long _eventCallbacks;
    private long _roundStartEvents;
    private long _roundEndEvents;
    private long _deathEvents;
    private long _spawnEvents;
    private string _modeFamily = "competitive";
    private string _ruleset = "round_based";
    private int _gameType;
    private int _gameMode;
    private bool _isDeathmatch;
    private readonly Dictionary<string, DeathmatchPlayerState> _deathmatchState = new();
    private readonly List<DeathmatchLifeRecord> _deathmatchLives = [];

    public override string ModuleName => "OfflineMatchTelemetry";
    public override string ModuleVersion => "0.8.0";
    public override string ModuleAuthor => "CS2-Self-Stat";
    public override string ModuleDescription => "Offline bot match SQLite telemetry exporter";

    public override void Load(bool hotReload)
    {
        var pluginDir = Path.GetDirectoryName(GetType().Assembly.Location)
            ?? ModuleDirectory ?? Directory.GetCurrentDirectory();

        // Store under <csgo>/.csbip/cs2ss/ — same .csbip root used by the Local Arena Panel
        var csgoDir = Path.GetFullPath(Path.Combine(pluginDir, "..", "..", "..", ".."));
        var dataDir = Path.Combine(csgoDir, ".csbip", "cs2ss");
        Directory.CreateDirectory(dataDir);
        _databasePath = Path.Combine(dataDir, "telemetry.db");

        _sqliteNativeLibrary = NativeLibrary.Load(Path.Combine(pluginDir, "e_sqlite3.dll"));
        InitializeDatabases();
        CleanOrphanedMatches();
        Log("Database: {Database}", _databasePath);
        RegisterEventHandler<EventRoundStart>(OnRoundStart);
        RegisterEventHandler<EventRoundEnd>(OnRoundEnd);
        RegisterEventHandler<EventPlayerDeath>(OnPlayerDeath);
        RegisterEventHandler<EventPlayerSpawn>(OnPlayerSpawn);
        RegisterListener<Listeners.OnMapStart>(OnMapStart);
        RegisterListener<Listeners.OnMapEnd>(OnMapEnd);
        RegisterListener<Listeners.OnTick>(OnTick);
        AddCommand("css_omt_status", "Show OfflineMatchTelemetry event diagnostics", OnStatusCommand);
        Log("Event handlers registered (hotReload={HotReload}, API build=1.0.367). Run css_omt_status to verify.", hotReload);
    }

    private void OnMapStart(string mapName)
    {
        if (!IsValidMap(mapName)) return;

        if (_matchActive) FinalizeMatch("MAP_CHANGED");
        ResetMatchState();
        Log("Map start: {Map}", mapName);
    }

    private HookResult OnRoundStart(EventRoundStart @event, GameEventInfo info)
    {
        try
        {
            if (!BeginMatch()) return HookResult.Continue;
            if (_isDeathmatch) return HookResult.Continue;
            _roundStartEvents++;
            var roundsPlayed = GetTotalRoundsPlayed();
            if (roundsPlayed > _halfPeakRounds) _halfPeakRounds = roundsPlayed;
            _round = _roundOffset + roundsPlayed;
            _lastObservedRounds = _round;
            SnapshotBase();
            StartEventRound();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[OMT] RoundStart error");
        }
        return HookResult.Continue;
    }

    private HookResult OnRoundEnd(EventRoundEnd @event, GameEventInfo info)
    {
        try
        {
            _roundEndEvents++;
            _eventCallbacks++;
            if (_isDeathmatch) return HookResult.Continue;
            if (_matchActive) WriteRoundSummary(_round, Team(@event.Winner), @event.Reason, "event");
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[OMT] RoundEnd error");
        }
        return HookResult.Continue;
    }

    private HookResult OnPlayerSpawn(EventPlayerSpawn @event, GameEventInfo info)
    {
        _eventCallbacks++;
        var player = @event.Userid;
        if (_isDeathmatch) return HookResult.Continue;
        _spawnEvents++;
        if (!_eventRoundActive || !IsPlaying(player)) return HookResult.Continue;

        var validPlayer = player!;
        var id = PlayerId(validPlayer);
        _roundState[id] = NewRoundPlayerState(validPlayer);
        var stats = validPlayer.ActionTrackingServices?.MatchStats;
        _livePlayers[id] = new(validPlayer.PawnIsAlive, stats?.Kills ?? 0, stats?.Assists ?? 0);
        InvalidateClutch(validPlayer.Team);
        return HookResult.Continue;
    }

    private HookResult OnPlayerDeath(EventPlayerDeath @event, GameEventInfo info)
    {
        _eventCallbacks++;
        try
        {
            if (_isDeathmatch) return HookResult.Continue;
            _deathEvents++;
            if (!_eventRoundActive) return HookResult.Continue;
            var victim = @event.Userid;
            if (!IsPlaying(victim)) return HookResult.Continue;
            var validVictim = victim!;

            var now = DateTimeOffset.UtcNow;
            var victimId = PlayerId(validVictim);
            var victimState = GetRoundState(validVictim);
            victimState.Alive = false;
            victimState.Died = true;
            if (_livePlayers.TryGetValue(victimId, out var liveVictim))
                _livePlayers[victimId] = liveVictim with { Alive = false };

            var attacker = @event.Attacker;
            var enemyKill = IsPlaying(attacker) && attacker!.Team != validVictim.Team && PlayerId(attacker) != victimId;
            if (enemyKill)
            {
                var attackerId = PlayerId(attacker!);
                GetRoundState(attacker!).Kills++;
                foreach (var trade in _pendingTrades.Where(t => t.KillerId == victimId
                    && t.VictimTeam == attacker!.Team && now - t.At <= TimeSpan.FromSeconds(5)))
                {
                    GetRoundState(trade.VictimId).Traded = true;
                    GetRoundState(attacker!).TradeKills++;
                }

                _pendingTrades.Add(new(victimId, validVictim.Team, attackerId, now));
            }

            var assister = @event.Assister;
            if (IsPlaying(assister) && assister!.Team != validVictim.Team)
                GetRoundState(assister!).Assisted = true;

            EvaluateClutches();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[OMT] PlayerDeath error");
        }
        return HookResult.Continue;
    }

    private void OnTick()
    {
        if (!_matchActive) BeginMatch();
        if (!_matchActive) return;
        try
        {
            if (_isDeathmatch) PollDeathmatchPlayers();
            else PollLivePlayerEvents();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[OMT] Live player poll error");
        }

        if (++_tickCounter < PollInterval) return;
        _tickCounter = 0;

        if (_isDeathmatch)
        {
            var players = CapturePlayers();
            if (players.Count > 0) _lastPlayers = players;
            return;
        }

        try
        {
            var roundsPlayed = GetTotalRoundsPlayed();
            if (_halfPeakRounds >= 0 && roundsPlayed < _halfPeakRounds)
            {
                // The engine zeroed TotalRoundsPlayed. That happens at halftime,
                // and it also happens when a finished offline match returns to
                // team selection on the same map. Tell them apart by score: a
                // halftime switch keeps the score, a real restart clears it.
                if (IsHalftimeReset())
                {
                    // Same match, new half: keep the running totals, rebase the
                    // round counter so round numbers keep increasing, and leave
                    // _matchStartedAt alone so duration is not reset.
                    _roundOffset += _halfPeakRounds;
                    _halfPeakRounds = -1;
                    _lastObservedRounds = _roundOffset;
                    _round = _roundOffset;
                    Log("Halftime switch on {Map}: continuing match {MatchId} at round {Round}",
                        _matchMap, _currentMatchId, _roundOffset);
                    return;
                }

                // A finished offline match returns to team selection on the same map. The game
                // resets this counter before the next selected side begins a new match.
                FinalizeMatch("ROUND_COUNTER_RESET");
                ResetMatchState();
                if (!BeginMatch()) return;
            }

            if (roundsPlayed > _halfPeakRounds) _halfPeakRounds = roundsPlayed;
            var absoluteRound = _roundOffset + roundsPlayed;

            if (_lastObservedRounds == absoluteRound) return;

            var gameRules = GetGameRules();
            if (!_eventRoundActive) StartEventRound();
            if (_lastObservedRounds >= 0)
            {
                var completedRound = absoluteRound - 1;
                if (completedRound >= 0 && completedRound > _lastWrittenRound)
                    WriteRoundSummary(completedRound,
                        Team(gameRules?.RoundEndWinnerTeam ?? 0),
                        gameRules?.RoundEndReason, "poll");
            }

            _lastObservedRounds = absoluteRound;
            _round = absoluteRound;
            SnapshotBase();
            StartEventRound();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[OMT] Poll error");
        }
    }

    private void OnMapEnd()
    {
        if (!_matchActive) return;

        try
        {
            var gameRules = GetGameRules();
            var lastRound = _roundOffset + GetTotalRoundsPlayed() - 1;
            if (!_isDeathmatch && lastRound >= 0 && lastRound > _lastWrittenRound)
                WriteRoundSummary(lastRound,
                    Team(gameRules?.RoundEndWinnerTeam ?? 0),
                    gameRules?.RoundEndReason, "poll");
            FinalizeMatch("MAP_END");
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[OMT] MapEnd error");
        }
        finally
        {
            ResetMatchState();
        }
    }

    private bool BeginMatch()
    {
        if (_matchActive) return true;
        if (!IsValidMap(Server.MapName)) return false;
        DetectMode();
        if (!_isDeathmatch && !HasBalancedPlayingRoster()) return false;

        _matchStartedAt = DateTimeOffset.UtcNow;
        _matchMap = Server.MapName;
        _currentMatchId = InsertMatch(_matchStartedAt);
        _matchActive = true;
        _ctScore = 0;
        _tScore = 0;
        _teamAScore = 0;
        _teamBScore = 0;
        _round = 0;
        _roundOffset = 0;
        _halfPeakRounds = -1;
        _prev.Clear();
        Log("Match {MatchId} started on {Map} mode={Mode} ruleset={Ruleset}",
            _currentMatchId, Server.MapName, _modeFamily, _ruleset);
        return true;
    }

    private void FinalizeMatch(string endReason)
    {
        if (!_matchActive) return;

        CachePlayers(CapturePlayers());
        var players = _lastPlayers;
        if (_isDeathmatch) CloseActiveDeathmatchLives("session_end");
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        try
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                UPDATE matches
                SET rounds_played = $roundsPlayed, ended_at = $endedAt, end_reason = $endReason,
                    ct_score = $ctScore, t_score = $tScore,
                    team_a_score = $teamAScore, team_b_score = $teamBScore,
                    duration_seconds = $durationSeconds, status = 'completed'
                WHERE match_id = $matchId;
                """;
            command.Parameters.AddWithValue("$roundsPlayed", _isDeathmatch ? 0 : CompletedRounds());
            command.Parameters.AddWithValue("$endedAt", DateTimeOffset.UtcNow.ToString("O"));
            command.Parameters.AddWithValue("$endReason", endReason);
            command.Parameters.AddWithValue("$ctScore", _ctScore);
            command.Parameters.AddWithValue("$tScore", _tScore);
            command.Parameters.AddWithValue("$teamAScore", _teamAScore);
            command.Parameters.AddWithValue("$teamBScore", _teamBScore);
            command.Parameters.AddWithValue("$durationSeconds", Math.Max(0, (int)(DateTimeOffset.UtcNow - _matchStartedAt).TotalSeconds));
            command.Parameters.AddWithValue("$matchId", _currentMatchId);
            command.ExecuteNonQuery();

            foreach (var player in players)
            {
                using var playerCommand = connection.CreateCommand();
                playerCommand.Transaction = transaction;
                playerCommand.CommandText = """
                    INSERT INTO match_players (
                        match_id, steam_id, name, team, is_bot, alive, health, total_kills, total_deaths,
                        total_assists, total_damage, total_headshot_kills, score, money, kast_rounds,
                        multikill_2, multikill_3, multikill_4, multikill_5, trade_kills,
                        clutch_attempts, clutches_won, dm_spawn_count, dm_completed_lives,
                        dm_max_kill_streak, dm_alive_seconds, dm_longest_life_seconds,
                        dm_burst_5s_2, dm_burst_5s_3, dm_burst_5s_4,
                        dm_burst_10s_2, dm_burst_10s_3, dm_burst_10s_4)
                    VALUES (
                        $matchId, $steamId, $name, $team, $isBot, $alive, $health, $totalKills, $totalDeaths,
                        $totalAssists, $totalDamage, $totalHeadshotKills, $score, $money, $kastRounds,
                        $multikill2, $multikill3, $multikill4, $multikill5, $tradeKills,
                        $clutchAttempts, $clutchesWon, $dmSpawnCount, $dmCompletedLives,
                        $dmMaxKillStreak, $dmAliveSeconds, $dmLongestLifeSeconds,
                        $dmBurst5s2, $dmBurst5s3, $dmBurst5s4,
                        $dmBurst10s2, $dmBurst10s3, $dmBurst10s4)
                    ON CONFLICT (match_id, steam_id) DO UPDATE SET
                        name = excluded.name, team = excluded.team, is_bot = excluded.is_bot,
                        alive = excluded.alive, health = excluded.health, total_kills = excluded.total_kills,
                        total_deaths = excluded.total_deaths, total_assists = excluded.total_assists,
                        total_damage = excluded.total_damage, total_headshot_kills = excluded.total_headshot_kills,
                        score = excluded.score, money = excluded.money, kast_rounds = excluded.kast_rounds,
                        multikill_2 = excluded.multikill_2, multikill_3 = excluded.multikill_3,
                        multikill_4 = excluded.multikill_4, multikill_5 = excluded.multikill_5,
                        trade_kills = excluded.trade_kills,
                        clutch_attempts = excluded.clutch_attempts, clutches_won = excluded.clutches_won,
                        dm_spawn_count = excluded.dm_spawn_count, dm_completed_lives = excluded.dm_completed_lives,
                        dm_max_kill_streak = excluded.dm_max_kill_streak,
                        dm_alive_seconds = excluded.dm_alive_seconds,
                        dm_longest_life_seconds = excluded.dm_longest_life_seconds,
                        dm_burst_5s_2 = excluded.dm_burst_5s_2, dm_burst_5s_3 = excluded.dm_burst_5s_3,
                        dm_burst_5s_4 = excluded.dm_burst_5s_4, dm_burst_10s_2 = excluded.dm_burst_10s_2,
                        dm_burst_10s_3 = excluded.dm_burst_10s_3, dm_burst_10s_4 = excluded.dm_burst_10s_4;
                    """;
                playerCommand.Parameters.AddWithValue("$matchId", _currentMatchId);
                playerCommand.Parameters.AddWithValue("$steamId", player.SteamId);
                playerCommand.Parameters.AddWithValue("$name", player.Name);
                playerCommand.Parameters.AddWithValue("$team", player.Team);
                playerCommand.Parameters.AddWithValue("$isBot", player.IsBot);
                playerCommand.Parameters.AddWithValue("$alive", player.Alive);
                playerCommand.Parameters.AddWithValue("$health", player.Health);
                playerCommand.Parameters.AddWithValue("$totalKills", player.TotalKills);
                playerCommand.Parameters.AddWithValue("$totalDeaths", player.TotalDeaths);
                playerCommand.Parameters.AddWithValue("$totalAssists", player.TotalAssists);
                playerCommand.Parameters.AddWithValue("$totalDamage", player.TotalDamage);
                playerCommand.Parameters.AddWithValue("$totalHeadshotKills", player.TotalHeadshotKills);
                playerCommand.Parameters.AddWithValue("$score", player.Score);
                playerCommand.Parameters.AddWithValue("$money", player.Money);
                var eventStats = _matchEventStats.GetValueOrDefault(player.SteamId) ?? new();
                playerCommand.Parameters.AddWithValue("$kastRounds", eventStats.KastRounds);
                playerCommand.Parameters.AddWithValue("$multikill2", eventStats.Multikill2);
                playerCommand.Parameters.AddWithValue("$multikill3", eventStats.Multikill3);
                playerCommand.Parameters.AddWithValue("$multikill4", eventStats.Multikill4);
                playerCommand.Parameters.AddWithValue("$multikill5", eventStats.Multikill5);
                playerCommand.Parameters.AddWithValue("$tradeKills", eventStats.TradeKills);
                playerCommand.Parameters.AddWithValue("$clutchAttempts", eventStats.ClutchAttempts);
                playerCommand.Parameters.AddWithValue("$clutchesWon", eventStats.ClutchesWon);
                var dm = _deathmatchState.GetValueOrDefault(player.SteamId);
                var aliveSeconds = dm?.AliveSeconds ?? 0;
                var longestLifeSeconds = dm?.LongestLifeSeconds ?? 0;
                if (dm is { Alive: true, LifeStartedAt: not null })
                {
                    var currentLifeSeconds = Math.Max(0, (DateTimeOffset.UtcNow - dm.LifeStartedAt.Value).TotalSeconds);
                    aliveSeconds += currentLifeSeconds;
                    longestLifeSeconds = Math.Max(longestLifeSeconds, currentLifeSeconds);
                }
                playerCommand.Parameters.AddWithValue("$dmSpawnCount", dm?.SpawnCount ?? 0);
                playerCommand.Parameters.AddWithValue("$dmCompletedLives", dm?.CompletedLives ?? 0);
                playerCommand.Parameters.AddWithValue("$dmMaxKillStreak", dm?.MaxKillStreak ?? 0);
                playerCommand.Parameters.AddWithValue("$dmAliveSeconds", (int)aliveSeconds);
                playerCommand.Parameters.AddWithValue("$dmLongestLifeSeconds", (int)longestLifeSeconds);
                playerCommand.Parameters.AddWithValue("$dmBurst5s2", dm?.Burst5s2 ?? 0);
                playerCommand.Parameters.AddWithValue("$dmBurst5s3", dm?.Burst5s3 ?? 0);
                playerCommand.Parameters.AddWithValue("$dmBurst5s4", dm?.Burst5s4 ?? 0);
                playerCommand.Parameters.AddWithValue("$dmBurst10s2", dm?.Burst10s2 ?? 0);
                playerCommand.Parameters.AddWithValue("$dmBurst10s3", dm?.Burst10s3 ?? 0);
                playerCommand.Parameters.AddWithValue("$dmBurst10s4", dm?.Burst10s4 ?? 0);
                playerCommand.ExecuteNonQuery();
            }
            foreach (var life in _deathmatchLives)
            {
                using var lifeCommand = connection.CreateCommand();
                lifeCommand.Transaction = transaction;
                lifeCommand.CommandText = """
                    INSERT INTO deathmatch_lives (
                        match_id, steam_id, life_index, spawned_at, ended_at, end_kind,
                        duration_seconds, kills, damage)
                    VALUES ($matchId, $steamId, $lifeIndex, $spawnedAt, $endedAt, $endKind,
                        $durationSeconds, $kills, $damage)
                    ON CONFLICT (match_id, steam_id, life_index) DO UPDATE SET
                        ended_at = excluded.ended_at, end_kind = excluded.end_kind,
                        duration_seconds = excluded.duration_seconds, kills = excluded.kills,
                        damage = excluded.damage;
                    """;
                lifeCommand.Parameters.AddWithValue("$matchId", _currentMatchId);
                lifeCommand.Parameters.AddWithValue("$steamId", life.SteamId);
                lifeCommand.Parameters.AddWithValue("$lifeIndex", life.LifeIndex);
                lifeCommand.Parameters.AddWithValue("$spawnedAt", life.SpawnedAt.ToString("O"));
                lifeCommand.Parameters.AddWithValue("$endedAt", life.EndedAt.ToString("O"));
                lifeCommand.Parameters.AddWithValue("$endKind", life.EndKind);
                lifeCommand.Parameters.AddWithValue("$durationSeconds", life.DurationSeconds);
                lifeCommand.Parameters.AddWithValue("$kills", life.Kills);
                lifeCommand.Parameters.AddWithValue("$damage", life.Damage);
                lifeCommand.ExecuteNonQuery();
            }
            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
        Log("Match {MatchId} ended: {Map} reason={Reason}", _currentMatchId, _matchMap, endReason);
    }

    private void WriteRoundSummary(int round, string? winner, int? endReason, string source)
    {
        if (round <= _lastWrittenRound) return;
        if (!_roundRosterReady) return;
        FinalizeEventRound(winner);
        if (winner == "CT") _ctScore++;
        if (winner == "T") _tScore++;
        var winnerIdentity = FixedTeamForSide(winner);
        if (winnerIdentity == "A") _teamAScore++;
        if (winnerIdentity == "B") _teamBScore++;

        var players = CapturePlayers();
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        try
        {
            using var roundCommand = connection.CreateCommand();
            roundCommand.Transaction = transaction;
            roundCommand.CommandText = """
                INSERT INTO rounds (match_id, round_number, captured_at, source, winner_team, end_reason,
                    ct_score, t_score, team_a_score, team_b_score)
                VALUES ($matchId, $roundNumber, $capturedAt, $source, $winnerTeam, $endReason,
                    $ctScore, $tScore, $teamAScore, $teamBScore);
                SELECT last_insert_rowid();
                """;
            roundCommand.Parameters.AddWithValue("$matchId", _currentMatchId);
            roundCommand.Parameters.AddWithValue("$roundNumber", round);
            roundCommand.Parameters.AddWithValue("$capturedAt", DateTimeOffset.UtcNow.ToString("O"));
            roundCommand.Parameters.AddWithValue("$source", source);
            roundCommand.Parameters.AddWithValue("$winnerTeam", (object?)winner ?? DBNull.Value);
            roundCommand.Parameters.AddWithValue("$endReason", (object?)endReason ?? DBNull.Value);
            roundCommand.Parameters.AddWithValue("$ctScore", _ctScore);
            roundCommand.Parameters.AddWithValue("$tScore", _tScore);
            roundCommand.Parameters.AddWithValue("$teamAScore", _teamAScore);
            roundCommand.Parameters.AddWithValue("$teamBScore", _teamBScore);
            var roundId = Convert.ToInt64(roundCommand.ExecuteScalar());

            foreach (var player in players)
            {
                using var playerCommand = connection.CreateCommand();
                playerCommand.Transaction = transaction;
                playerCommand.CommandText = """
                    INSERT INTO round_players (
                        round_id, match_id, steam_id, name, team, is_bot, alive, health, kills, deaths, assists,
                        damage, headshot_kills, total_kills, total_deaths, total_damage, score, money,
                        kast, survived, traded, trade_kills, event_kills, multikill,
                        clutch_attempt, clutch_won, clutch_size)
                    VALUES (
                        $roundId, $matchId, $steamId, $name, $team, $isBot, $alive, $health, $kills, $deaths, $assists,
                        $damage, $headshotKills, $totalKills, $totalDeaths, $totalDamage, $score, $money,
                        $kast, $survived, $traded, $tradeKills, $eventKills, $multikill,
                        $clutchAttempt, $clutchWon, $clutchSize);
                    """;
                playerCommand.Parameters.AddWithValue("$roundId", roundId);
                playerCommand.Parameters.AddWithValue("$matchId", _currentMatchId);
                playerCommand.Parameters.AddWithValue("$steamId", player.SteamId);
                playerCommand.Parameters.AddWithValue("$name", player.Name);
                playerCommand.Parameters.AddWithValue("$team", player.Team);
                playerCommand.Parameters.AddWithValue("$isBot", player.IsBot);
                var eventState = _roundState.GetValueOrDefault(player.SteamId);
                var alive = eventState?.Survived ?? player.Alive;
                playerCommand.Parameters.AddWithValue("$alive", alive);
                playerCommand.Parameters.AddWithValue("$health", alive ? player.Health : 0);
                playerCommand.Parameters.AddWithValue("$kills", player.Kills);
                playerCommand.Parameters.AddWithValue("$deaths", player.Deaths);
                playerCommand.Parameters.AddWithValue("$assists", player.Assists);
                playerCommand.Parameters.AddWithValue("$damage", player.Damage);
                playerCommand.Parameters.AddWithValue("$headshotKills", player.HeadshotKills);
                playerCommand.Parameters.AddWithValue("$totalKills", player.TotalKills);
                playerCommand.Parameters.AddWithValue("$totalDeaths", player.TotalDeaths);
                playerCommand.Parameters.AddWithValue("$totalDamage", player.TotalDamage);
                playerCommand.Parameters.AddWithValue("$score", player.Score);
                playerCommand.Parameters.AddWithValue("$money", player.Money);
                playerCommand.Parameters.AddWithValue("$kast", eventState?.Kast == true);
                playerCommand.Parameters.AddWithValue("$survived", eventState?.Survived == true);
                playerCommand.Parameters.AddWithValue("$traded", eventState?.Traded == true);
                playerCommand.Parameters.AddWithValue("$tradeKills", eventState?.TradeKills ?? 0);
                playerCommand.Parameters.AddWithValue("$eventKills", eventState?.Kills ?? 0);
                playerCommand.Parameters.AddWithValue("$multikill", eventState?.Kills >= 2 ? eventState.Kills : 0);
                playerCommand.Parameters.AddWithValue("$clutchAttempt", eventState?.ClutchAttempt == true);
                playerCommand.Parameters.AddWithValue("$clutchWon", eventState?.ClutchWon == true);
                playerCommand.Parameters.AddWithValue("$clutchSize", eventState?.ClutchSize ?? 0);
                playerCommand.ExecuteNonQuery();
            }
            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }

        _lastWrittenRound = round;
        CachePlayers(players);
        SnapshotBase();
        _eventRoundActive = false;
        _roundRosterReady = false;
        Log("Round {Round}: match={MatchId} source={Source}", round, _currentMatchId, source);
    }

    private int InsertMatch(DateTimeOffset startedAt)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO matches (map, started_at, status, mode_family, ruleset, game_type, game_mode)
            VALUES ($map, $startedAt, 'in_progress', $modeFamily, $ruleset, $gameType, $gameMode);
            SELECT last_insert_rowid();
            """;
        command.Parameters.AddWithValue("$map", _matchMap);
        command.Parameters.AddWithValue("$startedAt", startedAt.ToString("O"));
        command.Parameters.AddWithValue("$modeFamily", _modeFamily);
        command.Parameters.AddWithValue("$ruleset", _ruleset);
        command.Parameters.AddWithValue("$gameType", _gameType);
        command.Parameters.AddWithValue("$gameMode", _gameMode);
        return Convert.ToInt32(command.ExecuteScalar());
    }

    private void InitializeDatabases()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode = WAL;
            CREATE TABLE IF NOT EXISTS matches (
                match_id INTEGER PRIMARY KEY AUTOINCREMENT,
                map TEXT NOT NULL,
                started_at TEXT NOT NULL,
                ended_at TEXT,
                end_reason TEXT,
                rounds_played INTEGER,
                ct_score INTEGER NOT NULL DEFAULT 0,
                t_score INTEGER NOT NULL DEFAULT 0,
                status TEXT NOT NULL CHECK (status IN ('in_progress', 'completed', 'abandoned'))
            );
            CREATE TABLE IF NOT EXISTS rounds (
                round_id INTEGER PRIMARY KEY AUTOINCREMENT,
                match_id INTEGER NOT NULL REFERENCES matches(match_id),
                round_number INTEGER NOT NULL,
                captured_at TEXT NOT NULL,
                source TEXT NOT NULL CHECK (source IN ('event', 'poll')),
                winner_team TEXT,
                end_reason INTEGER,
                ct_score INTEGER NOT NULL,
                t_score INTEGER NOT NULL,
                UNIQUE (match_id, round_number)
            );
            CREATE INDEX IF NOT EXISTS ix_rounds_match_id ON rounds (match_id);
            CREATE TABLE IF NOT EXISTS round_players (
                round_player_id INTEGER PRIMARY KEY AUTOINCREMENT,
                round_id INTEGER NOT NULL REFERENCES rounds(round_id),
                match_id INTEGER NOT NULL REFERENCES matches(match_id),
                steam_id TEXT NOT NULL,
                name TEXT NOT NULL,
                team TEXT NOT NULL,
                is_bot INTEGER NOT NULL,
                alive INTEGER NOT NULL,
                health INTEGER NOT NULL,
                kills INTEGER NOT NULL,
                deaths INTEGER NOT NULL,
                assists INTEGER NOT NULL,
                damage INTEGER NOT NULL,
                headshot_kills INTEGER NOT NULL,
                total_kills INTEGER NOT NULL,
                total_deaths INTEGER NOT NULL,
                total_damage INTEGER NOT NULL,
                score INTEGER NOT NULL,
                money INTEGER NOT NULL,
                UNIQUE (round_id, steam_id)
            );
            CREATE INDEX IF NOT EXISTS ix_round_players_match_id ON round_players (match_id);
            CREATE TABLE IF NOT EXISTS match_players (
                match_player_id INTEGER PRIMARY KEY AUTOINCREMENT,
                match_id INTEGER NOT NULL REFERENCES matches(match_id),
                steam_id TEXT NOT NULL,
                name TEXT NOT NULL,
                team TEXT NOT NULL,
                is_bot INTEGER NOT NULL,
                alive INTEGER NOT NULL,
                health INTEGER NOT NULL,
                total_kills INTEGER NOT NULL,
                total_deaths INTEGER NOT NULL,
                total_assists INTEGER NOT NULL,
                total_damage INTEGER NOT NULL,
                total_headshot_kills INTEGER NOT NULL,
                score INTEGER NOT NULL,
                money INTEGER NOT NULL,
                UNIQUE (match_id, steam_id)
            );
            CREATE INDEX IF NOT EXISTS ix_match_players_match_id ON match_players (match_id);
            CREATE TABLE IF NOT EXISTS deathmatch_lives (
                life_id INTEGER PRIMARY KEY AUTOINCREMENT,
                match_id INTEGER NOT NULL REFERENCES matches(match_id),
                steam_id TEXT NOT NULL,
                life_index INTEGER NOT NULL,
                spawned_at TEXT NOT NULL,
                ended_at TEXT NOT NULL,
                end_kind TEXT NOT NULL,
                duration_seconds REAL NOT NULL,
                kills INTEGER NOT NULL,
                damage INTEGER NOT NULL,
                UNIQUE (match_id, steam_id, life_index)
            );
            CREATE INDEX IF NOT EXISTS ix_dm_lives_match_player
                ON deathmatch_lives (match_id, steam_id);
            """;
        command.ExecuteNonQuery();
        AddColumnIfMissing(connection, "round_players", "kast", "INTEGER NOT NULL DEFAULT 0");
        AddColumnIfMissing(connection, "matches", "team_a_score", "INTEGER NOT NULL DEFAULT 0");
        AddColumnIfMissing(connection, "matches", "team_b_score", "INTEGER NOT NULL DEFAULT 0");
        AddColumnIfMissing(connection, "matches", "mode_family", "TEXT NOT NULL DEFAULT 'competitive'");
        AddColumnIfMissing(connection, "matches", "ruleset", "TEXT NOT NULL DEFAULT 'round_based'");
        AddColumnIfMissing(connection, "matches", "game_type", "INTEGER NOT NULL DEFAULT 0");
        AddColumnIfMissing(connection, "matches", "game_mode", "INTEGER NOT NULL DEFAULT 1");
        AddColumnIfMissing(connection, "matches", "duration_seconds", "INTEGER NOT NULL DEFAULT 0");
        AddColumnIfMissing(connection, "rounds", "team_a_score", "INTEGER NOT NULL DEFAULT 0");
        AddColumnIfMissing(connection, "rounds", "team_b_score", "INTEGER NOT NULL DEFAULT 0");
        AddColumnIfMissing(connection, "round_players", "survived", "INTEGER NOT NULL DEFAULT 0");
        AddColumnIfMissing(connection, "round_players", "traded", "INTEGER NOT NULL DEFAULT 0");
        AddColumnIfMissing(connection, "round_players", "trade_kills", "INTEGER NOT NULL DEFAULT 0");
        AddColumnIfMissing(connection, "round_players", "event_kills", "INTEGER NOT NULL DEFAULT 0");
        AddColumnIfMissing(connection, "round_players", "multikill", "INTEGER NOT NULL DEFAULT 0");
        AddColumnIfMissing(connection, "round_players", "clutch_attempt", "INTEGER NOT NULL DEFAULT 0");
        AddColumnIfMissing(connection, "round_players", "clutch_won", "INTEGER NOT NULL DEFAULT 0");
        AddColumnIfMissing(connection, "round_players", "clutch_size", "INTEGER NOT NULL DEFAULT 0");
        AddColumnIfMissing(connection, "match_players", "kast_rounds", "INTEGER NOT NULL DEFAULT 0");
        AddColumnIfMissing(connection, "match_players", "multikill_2", "INTEGER NOT NULL DEFAULT 0");
        AddColumnIfMissing(connection, "match_players", "multikill_3", "INTEGER NOT NULL DEFAULT 0");
        AddColumnIfMissing(connection, "match_players", "multikill_4", "INTEGER NOT NULL DEFAULT 0");
        AddColumnIfMissing(connection, "match_players", "multikill_5", "INTEGER NOT NULL DEFAULT 0");
        AddColumnIfMissing(connection, "match_players", "trade_kills", "INTEGER NOT NULL DEFAULT 0");
        AddColumnIfMissing(connection, "match_players", "clutch_attempts", "INTEGER NOT NULL DEFAULT 0");
        AddColumnIfMissing(connection, "match_players", "clutches_won", "INTEGER NOT NULL DEFAULT 0");
        AddColumnIfMissing(connection, "match_players", "dm_spawn_count", "INTEGER NOT NULL DEFAULT 0");
        AddColumnIfMissing(connection, "match_players", "dm_completed_lives", "INTEGER NOT NULL DEFAULT 0");
        AddColumnIfMissing(connection, "match_players", "dm_max_kill_streak", "INTEGER NOT NULL DEFAULT 0");
        AddColumnIfMissing(connection, "match_players", "dm_alive_seconds", "INTEGER NOT NULL DEFAULT 0");
        AddColumnIfMissing(connection, "match_players", "dm_longest_life_seconds", "INTEGER NOT NULL DEFAULT 0");
        AddColumnIfMissing(connection, "match_players", "dm_burst_5s_2", "INTEGER NOT NULL DEFAULT 0");
        AddColumnIfMissing(connection, "match_players", "dm_burst_5s_3", "INTEGER NOT NULL DEFAULT 0");
        AddColumnIfMissing(connection, "match_players", "dm_burst_5s_4", "INTEGER NOT NULL DEFAULT 0");
        AddColumnIfMissing(connection, "match_players", "dm_burst_10s_2", "INTEGER NOT NULL DEFAULT 0");
        AddColumnIfMissing(connection, "match_players", "dm_burst_10s_3", "INTEGER NOT NULL DEFAULT 0");
        AddColumnIfMissing(connection, "match_players", "dm_burst_10s_4", "INTEGER NOT NULL DEFAULT 0");
    }

    private static void AddColumnIfMissing(SqliteConnection connection, string table, string column, string definition)
    {
        using var check = connection.CreateCommand();
        check.CommandText = $"PRAGMA table_info({table});";
        using var reader = check.ExecuteReader();
        while (reader.Read())
            if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase)) return;
        reader.Close();
        using var alter = connection.CreateCommand();
        alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {definition};";
        alter.ExecuteNonQuery();
    }

    private void CleanOrphanedMatches()
    {
        try
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                UPDATE matches
                SET status = 'abandoned', ended_at = $now, end_reason = 'INTERRUPTED'
                WHERE status = 'in_progress';
                """;
            command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
            command.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "[OMT] CleanOrphanedMatches failed; may be an older DB schema");
            try
            {
                using var connection = OpenConnection();
                using var command = connection.CreateCommand();
                command.CommandText = """
                    UPDATE matches
                    SET status = 'completed', ended_at = $now, end_reason = 'ABANDONED'
                    WHERE status = 'in_progress';
                    """;
                command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
                command.ExecuteNonQuery();
            }
            catch (Exception ex2)
            {
                Logger.LogError(ex2, "[OMT] CleanOrphanedMatches fallback also failed");
            }
        }
    }

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            ForeignKeys = true
        }.ToString());
        connection.Open();
        return connection;
    }

    public override void Unload(bool hotReload)
    {
        if (_matchActive)
        {
            try { FinalizeMatch("PLUGIN_UNLOAD"); }
            catch (Exception ex) { Logger.LogError(ex, "[OMT] Unload finalize error"); }
        }

        if (_sqliteNativeLibrary != IntPtr.Zero)
        {
            NativeLibrary.Free(_sqliteNativeLibrary);
            _sqliteNativeLibrary = IntPtr.Zero;
        }
    }

    private void ResetMatchState()
    {
        _matchActive = false;
        _currentMatchId = 0;
        _matchMap = "";
        _round = 0;
        _lastObservedRounds = -1;
        _lastWrittenRound = -1;
        _roundOffset = 0;
        _halfPeakRounds = -1;
        _tickCounter = 0;
        _ctScore = 0;
        _tScore = 0;
        _teamAScore = 0;
        _teamBScore = 0;
        _teamAPlayers.Clear();
        _teamBPlayers.Clear();
        _fixedTeamsReady = false;
        _roundCtTeam = null;
        _roundTTeam = null;
        _prev.Clear();
        _lastPlayers = [];
        _roundState.Clear();
        _matchEventStats.Clear();
        _livePlayers.Clear();
        _pendingTrades.Clear();
        _unresolvedDeaths.Clear();
        _tClutch = null;
        _ctClutch = null;
        _eventRoundActive = false;
        _modeFamily = "competitive";
        _ruleset = "round_based";
        _gameType = 0;
        _gameMode = 1;
        _isDeathmatch = false;
        _deathmatchState.Clear();
        _deathmatchLives.Clear();
    }

    private int GetTotalRoundsPlayed() => Utilities
        .FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules")
        .FirstOrDefault()?.GameRules?.TotalRoundsPlayed ?? 0;

    // Distinguishes a halftime side switch from a real restart on the same map.
    //
    // Both zero TotalRoundsPlayed, so the counter alone cannot tell them apart.
    // The score can: halftime carries the score into the second half, while a
    // finished match returns to team selection with everything cleared. A
    // deathmatch has no halves, so a reset there is always a real restart.
    private bool IsHalftimeReset()
    {
        if (_isDeathmatch) return false;
        // Rounds must already have been played; otherwise there is nothing to carry.
        if (_halfPeakRounds <= 0) return false;
        var scored = _ctScore + _tScore + _teamAScore + _teamBScore;
        if (scored <= 0) return false;
        // Halftime happens after a half of a competitive match. Treat a reset that
        // arrives before any round was recorded as a restart, not a switch.
        return _roundOffset + _halfPeakRounds > 0;
    }

    private CCSGameRules? GetGameRules() => Utilities
        .FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules")
        .FirstOrDefault()?.GameRules;

    private int CompletedRounds() => Math.Max(_lastWrittenRound + 1, Math.Max(_lastObservedRounds, _roundOffset + GetTotalRoundsPlayed()));
    private static bool IsValidMap(string? mapName) => !string.IsNullOrWhiteSpace(mapName)
        && !string.Equals(mapName, "<empty>", StringComparison.OrdinalIgnoreCase);

    private List<PlayerSnapshot> CapturePlayers()
    {
        var players = new List<PlayerSnapshot>();
        foreach (var player in Utilities.FindAllEntitiesByDesignerName<CCSPlayerController>("cs_player_controller"))
        {
            if (!player.IsValid || player.Team is not CsTeam.Terrorist and not CsTeam.CounterTerrorist) continue;
            var stats = player.ActionTrackingServices?.MatchStats;
            // Same identity rule as PlayerId: bots share SteamID 0, so keying them
            // by SteamID would merge every bot into one snapshot.
            var steamId = PlayerId(player);
            var previous = _prev.GetValueOrDefault(steamId);
            players.Add(new(
                steamId, player.PlayerName, Team(player.Team)!, player.IsBot, player.PawnIsAlive,
                player.PlayerPawn?.Value?.Health ?? 0,
                (stats?.Kills ?? 0) - previous.Kills, (stats?.Deaths ?? 0) - previous.Deaths,
                (stats?.Assists ?? 0) - previous.Assists, (stats?.Damage ?? 0) - previous.Damage,
                (stats?.HeadShotKills ?? 0) - previous.HeadShotKills,
                stats?.Kills ?? 0, stats?.Deaths ?? 0, stats?.Assists ?? 0, stats?.Damage ?? 0,
                stats?.HeadShotKills ?? 0,
                player.Score, player.InGameMoneyServices?.Account ?? 0));
        }
        return players;
    }

    private void CachePlayers(IEnumerable<PlayerSnapshot> players)
    {
        // GroupBy rather than ToDictionary: several bots can share SteamId "0",
        // and a duplicate key would throw instead of caching them.
        var cached = _lastPlayers
            .GroupBy(player => player.SteamId)
            .ToDictionary(group => group.Key, group => group.Last());
        foreach (var player in players) cached[player.SteamId] = player;
        _lastPlayers = [.. cached.Values];
    }

    private void SnapshotBase()
    {
        _prev.Clear();
        foreach (var player in Utilities.FindAllEntitiesByDesignerName<CCSPlayerController>("cs_player_controller"))
        {
            if (!player.IsValid || player.Team is not CsTeam.Terrorist and not CsTeam.CounterTerrorist) continue;
            var stats = player.ActionTrackingServices?.MatchStats;
            _prev[PlayerId(player)] = new(
                stats?.Kills ?? 0, stats?.Deaths ?? 0, stats?.Assists ?? 0,
                stats?.Damage ?? 0, stats?.HeadShotKills ?? 0);
        }
    }

    private static string? Team(int team) => Team((CsTeam)team);
    private static string? Team(CsTeam team) => team switch
    {
        CsTeam.Terrorist => "T",
        CsTeam.CounterTerrorist => "CT",
        _ => null
    };

    private void Log(string message, params object?[] arguments) => Logger.LogInformation("[OMT] " + message, arguments);

    private void DetectMode()
    {
        _gameType = ConVar.Find("game_type")?.GetPrimitiveValue<int>() ?? 0;
        _gameMode = ConVar.Find("game_mode")?.GetPrimitiveValue<int>() ?? 0;
        var respawnT = ConVarEnabled("mp_respawn_on_death_t");
        var respawnCt = ConVarEnabled("mp_respawn_on_death_ct");
        var teammatesAreEnemies = ConVarEnabled("mp_teammates_are_enemies");
        var teamDm = ConVarEnabled("mp_dm_teammode");

        _isDeathmatch = _gameType == 1 && _gameMode == 2 || respawnT && respawnCt;
        if (!_isDeathmatch) return;
        _modeFamily = "deathmatch";
        _ruleset = teammatesAreEnemies ? "ffa" : teamDm ? "team_dm" : "respawn";
    }

    private static bool ConVarEnabled(string name)
    {
        var value = ConVar.Find(name)?.StringValue;
        return value is not null && (value == "1" || bool.TryParse(value, out var enabled) && enabled);
    }

    private void PollDeathmatchPlayers()
    {
        if (!_matchActive) return;
        var now = DateTimeOffset.UtcNow;
        var snapshots = CapturePlayers();
        if (snapshots.Count > 0) CachePlayers(snapshots);

        foreach (var player in PlayingPlayers())
        {
            var id = PlayerId(player);
            var stats = player.ActionTrackingServices?.MatchStats;
            var kills = stats?.Kills ?? 0;
            var damage = stats?.Damage ?? 0;
            var alive = player.PawnIsAlive;
            if (!_deathmatchState.TryGetValue(id, out var state))
            {
                state = new()
                {
                    Alive = alive,
                    LastKills = kills,
                    LastDamage = damage,
                    SpawnCount = alive ? 1 : 0,
                    LifeStartedAt = alive ? now : null,
                    LifeStartKills = kills,
                    LifeStartDamage = damage
                };
                _deathmatchState[id] = state;
                continue;
            }

            var killDelta = Math.Max(0, kills - state.LastKills);
            if (killDelta > 0 && state.Alive)
            {
                state.CurrentKillStreak += killDelta;
                state.MaxKillStreak = Math.Max(state.MaxKillStreak, state.CurrentKillStreak);
                for (var i = 0; i < killDelta; i++) RecordDeathmatchKill(state, now);
            }

            if (!state.Alive && alive)
            {
                state.Alive = true;
                state.SpawnCount++;
                state.CurrentKillStreak = 0;
                state.LifeStartedAt = now;
                state.LifeStartKills = kills;
                state.LifeStartDamage = damage;
                ResetDeathmatchBurstWindow(state);
                _spawnEvents++;
            }
            else if (state.Alive && !alive)
            {
                state.Alive = false;
                state.CompletedLives++;
                state.CurrentKillStreak = 0;
                if (state.LifeStartedAt is not null)
                {
                    var lifeSeconds = Math.Max(0, (now - state.LifeStartedAt.Value).TotalSeconds);
                    state.AliveSeconds += lifeSeconds;
                    state.LongestLifeSeconds = Math.Max(state.LongestLifeSeconds, lifeSeconds);
                    _deathmatchLives.Add(new(id, state.SpawnCount, state.LifeStartedAt.Value, now, "death",
                        lifeSeconds, Math.Max(0, kills - state.LifeStartKills),
                        Math.Max(0, damage - state.LifeStartDamage)));
                }
                state.LifeStartedAt = null;
                ResetDeathmatchBurstWindow(state);
                _deathEvents++;
            }
            state.LastKills = kills;
            state.LastDamage = damage;
        }
    }

    private static void RecordDeathmatchKill(DeathmatchPlayerState state, DateTimeOffset now)
    {
        if (!state.KillTimes.Any(time => now - time <= TimeSpan.FromSeconds(5))) state.Awarded5s.Clear();
        if (!state.KillTimes.Any(time => now - time <= TimeSpan.FromSeconds(10))) state.Awarded10s.Clear();
        state.KillTimes.Add(now);
        state.KillTimes.RemoveAll(time => now - time > TimeSpan.FromSeconds(10));
        AwardBurst(state, state.KillTimes.Count(time => now - time <= TimeSpan.FromSeconds(5)), true);
        AwardBurst(state, state.KillTimes.Count, false);
    }

    private static void ResetDeathmatchBurstWindow(DeathmatchPlayerState state)
    {
        state.KillTimes.Clear();
        state.Awarded5s.Clear();
        state.Awarded10s.Clear();
    }

    private static void AwardBurst(DeathmatchPlayerState state, int count, bool fiveSeconds)
    {
        var awarded = fiveSeconds ? state.Awarded5s : state.Awarded10s;
        if (count < 2 || awarded.Contains(count >= 4 ? 4 : count)) return;
        var threshold = count >= 4 ? 4 : count;
        awarded.Add(threshold);
        if (fiveSeconds)
        {
            if (threshold == 2) state.Burst5s2++;
            else if (threshold == 3) state.Burst5s3++;
            else state.Burst5s4++;
        }
        else
        {
            if (threshold == 2) state.Burst10s2++;
            else if (threshold == 3) state.Burst10s3++;
            else state.Burst10s4++;
        }
    }

    private void CloseActiveDeathmatchLives(string endKind)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var (id, state) in _deathmatchState)
        {
            if (!state.Alive || state.LifeStartedAt is null) continue;
            var snapshot = _lastPlayers.FirstOrDefault(player => player.SteamId == id);
            var lifeSeconds = Math.Max(0, (now - state.LifeStartedAt.Value).TotalSeconds);
            _deathmatchLives.Add(new(id, state.SpawnCount, state.LifeStartedAt.Value, now, endKind,
                lifeSeconds, Math.Max(0, (snapshot?.TotalKills ?? state.LastKills) - state.LifeStartKills),
                Math.Max(0, (snapshot?.TotalDamage ?? state.LastDamage) - state.LifeStartDamage)));
            state.Alive = false;
            state.AliveSeconds += lifeSeconds;
            state.LongestLifeSeconds = Math.Max(state.LongestLifeSeconds, lifeSeconds);
            state.LifeStartedAt = null;
        }
    }

    private void CaptureInitialTeams(IEnumerable<CCSPlayerController> players)
    {
        if (_fixedTeamsReady) return;
        _teamAPlayers.Clear();
        _teamBPlayers.Clear();
        foreach (var player in players)
        {
            if (player.Team == CsTeam.CounterTerrorist) _teamAPlayers.Add(PlayerId(player));
            if (player.Team == CsTeam.Terrorist) _teamBPlayers.Add(PlayerId(player));
        }
        _fixedTeamsReady = _teamAPlayers.Count > 0 && _teamAPlayers.Count == _teamBPlayers.Count;
    }

    private string? FixedTeamForSide(string? side)
    {
        return side == "CT" ? _roundCtTeam : side == "T" ? _roundTTeam : null;
    }

    private string? FixedTeamForPlayers(IEnumerable<CCSPlayerController> sidePlayers)
    {
        var players = sidePlayers.Select(PlayerId).ToList();
        var teamACount = players.Count(_teamAPlayers.Contains);
        var teamBCount = players.Count(_teamBPlayers.Contains);
        return teamACount > teamBCount ? "A" : teamBCount > teamACount ? "B" : null;
    }

    private void OnStatusCommand(CCSPlayerController? player, CommandInfo command)
    {
        var aliveT = _roundState.Count(x => x.Value.Team == CsTeam.Terrorist && x.Value.Alive);
        var aliveCt = _roundState.Count(x => x.Value.Team == CsTeam.CounterTerrorist && x.Value.Alive);
        var message = $"[OMT] v{ModuleVersion} match={_currentMatchId} round={_round} active={_eventRoundActive} "
            + $"mode={_modeFamily}/{_ruleset} rosterReady={_roundRosterReady} "
            + $"events={_eventCallbacks} start={_roundStartEvents} end={_roundEndEvents} "
            + $"death={_deathEvents} spawn={_spawnEvents} tracked={_roundState.Count} alive=T{aliveT}/CT{aliveCt}";
        command.ReplyToCommand(message);
        Log("Status requested: {Status}", message);
    }

    private void StartEventRound()
    {
        _roundState.Clear();
        _pendingTrades.Clear();
        _tClutch = null;
        _ctClutch = null;
        foreach (var player in PlayingPlayers()) _roundState[PlayerId(player)] = NewRoundPlayerState(player);
        SnapshotLivePlayers();
        _eventRoundActive = true;
        _roundRosterReady = false;
        _roundCtTeam = null;
        _roundTTeam = null;
    }

    private void FinalizeEventRound(string? winner)
    {
        if (!_eventRoundActive) return;
        ResolveDelayedAttribution(PlayingPlayers().ToDictionary(PlayerId));
        foreach (var player in PlayingPlayers())
        {
            var id = PlayerId(player);
            var baseline = _prev.GetValueOrDefault(id);
            var stats = player.ActionTrackingServices?.MatchStats;
            var state = GetRoundState(player);
            state.Kills = Math.Max(state.Kills, (stats?.Kills ?? 0) - baseline.Kills);
            state.Assisted |= (stats?.Assists ?? 0) - baseline.Assists > 0;
            if ((stats?.Deaths ?? 0) - baseline.Deaths > 0)
            {
                state.Died = true;
                state.Alive = false;
            }
        }
        var winnerTeam = winner == "T" ? CsTeam.Terrorist : winner == "CT" ? CsTeam.CounterTerrorist : CsTeam.None;
        var winningClutch = winnerTeam == CsTeam.Terrorist ? _tClutch : winnerTeam == CsTeam.CounterTerrorist ? _ctClutch : null;
        if (winningClutch is not null && _roundState.TryGetValue(winningClutch.PlayerId, out var clutchState)
            && clutchState.Alive)
            clutchState.ClutchWon = true;

        foreach (var (id, state) in _roundState)
        {
            state.Survived = state.Participated && !state.Died;
            state.Kast = state.Kills > 0 || state.Assisted || state.Survived || state.Traded;
            var stats = _matchEventStats.GetValueOrDefault(id) ?? new();
            _matchEventStats[id] = stats;
            if (state.Kast) stats.KastRounds++;
            if (state.Kills == 2) stats.Multikill2++;
            else if (state.Kills == 3) stats.Multikill3++;
            else if (state.Kills == 4) stats.Multikill4++;
            else if (state.Kills >= 5) stats.Multikill5++;
            stats.TradeKills += state.TradeKills;
            if (state.ClutchAttempt) stats.ClutchAttempts++;
            if (state.ClutchWon) stats.ClutchesWon++;
        }
    }

    private void EvaluateClutches()
    {
        if (!_roundRosterReady) return;
        EvaluateClutch(CsTeam.Terrorist, ref _tClutch);
        EvaluateClutch(CsTeam.CounterTerrorist, ref _ctClutch);
    }

    private void EvaluateClutch(CsTeam team, ref ClutchCandidate? candidate)
    {
        var alive = _roundState.Where(x => x.Value.Team == team && x.Value.Alive).Select(x => x.Key).ToList();
        var enemyTeam = team == CsTeam.Terrorist ? CsTeam.CounterTerrorist : CsTeam.Terrorist;
        var enemies = _roundState.Count(x => x.Value.Team == enemyTeam && x.Value.Alive);
        if (alive.Count != 1 || enemies < 1 || candidate is not null) return;
        candidate = new(alive[0], team, enemies);
        var state = _roundState[alive[0]];
        state.ClutchAttempt = true;
        state.ClutchSize = enemies;
    }

    private void InvalidateClutch(CsTeam team)
    {
        if (team == CsTeam.Terrorist) _tClutch = null;
        if (team == CsTeam.CounterTerrorist) _ctClutch = null;
    }

    private RoundPlayerState GetRoundState(CCSPlayerController player) => GetRoundState(PlayerId(player), player);
    private RoundPlayerState GetRoundState(string id, CCSPlayerController? player = null)
    {
        if (_roundState.TryGetValue(id, out var state)) return state;
        state = player is null ? new() : NewRoundPlayerState(player);
        _roundState[id] = state;
        return state;
    }

    private static RoundPlayerState NewRoundPlayerState(CCSPlayerController player) => new()
    {
        Participated = true,
        Alive = player.PawnIsAlive,
        Team = player.Team
    };

    private static bool IsPlaying(CCSPlayerController? player) => player is { IsValid: true }
        && player.Team is CsTeam.Terrorist or CsTeam.CounterTerrorist;
    private static string PlayerId(CCSPlayerController player)
    {
        // Bots carry SteamID 0, so keying by SteamID alone collapses every bot
        // into the same entry and ToDictionary throws "same key has already been
        // added: 0". Fall back to the entity slot for anything without a real
        // SteamID so each bot keeps a stable identity of its own.
        var steamId = player.SteamID;
        if (steamId != 0) return steamId.ToString();
        return player.IsBot ? $"bot:{player.Slot}" : $"player:{player.Slot}";
    }
    private static IEnumerable<CCSPlayerController> PlayingPlayers() => Utilities
        .FindAllEntitiesByDesignerName<CCSPlayerController>("cs_player_controller").Where(IsPlaying);
    private static bool HasBalancedPlayingRoster()
    {
        var players = PlayingPlayers().ToList();
        var terrorists = players.Count(player => player.Team == CsTeam.Terrorist);
        var counterTerrorists = players.Count(player => player.Team == CsTeam.CounterTerrorist);
        return terrorists > 0 && terrorists == counterTerrorists;
    }

    private void PollLivePlayerEvents()
    {
        if (!_matchActive) return;
        if (!_eventRoundActive) StartEventRound();

        var current = PlayingPlayers().ToDictionary(PlayerId);
        ResolveDelayedAttribution(current);
        foreach (var (id, player) in current)
        {
            var stats = player.ActionTrackingServices?.MatchStats;
            var next = new LivePlayerState(player.PawnIsAlive, stats?.Kills ?? 0, stats?.Assists ?? 0);
            if (!_livePlayers.TryGetValue(id, out var previous))
            {
                _livePlayers[id] = next;
                if (!_roundState.ContainsKey(id)) _roundState[id] = NewRoundPlayerState(player);
                continue;
            }

            var roundState = GetRoundState(player);
            if (roundState.Team != player.Team)
            {
                roundState.Team = player.Team;
                _tClutch = null;
                _ctClutch = null;
                _roundRosterReady = false;
            }

            if (!previous.Alive && next.Alive)
            {
                _roundState[id] = NewRoundPlayerState(player);
                InvalidateClutch(player.Team);
                _spawnEvents++;
            }

            if (previous.Alive && !next.Alive)
                ProcessPolledDeath(player, current);

            _livePlayers[id] = next;
        }


        if (!_roundRosterReady && current.Count > 0 && current.Values.All(x => x.PawnIsAlive))
        {
            var terrorists = current.Values.Count(x => x.Team == CsTeam.Terrorist);
            var counterTerrorists = current.Values.Count(x => x.Team == CsTeam.CounterTerrorist);
            if (terrorists > 0 && terrorists == counterTerrorists)
            {
                CaptureInitialTeams(current.Values);
                foreach (var player in current.Values) GetRoundState(player).Team = player.Team;
                _roundCtTeam = FixedTeamForPlayers(current.Values.Where(x => x.Team == CsTeam.CounterTerrorist));
                _roundTTeam = FixedTeamForPlayers(current.Values.Where(x => x.Team == CsTeam.Terrorist));
                _roundRosterReady = true;
            }
        }
    }

    private void ProcessPolledDeath(CCSPlayerController victim, Dictionary<string, CCSPlayerController> players)
    {
        var now = DateTimeOffset.UtcNow;
        var victimId = PlayerId(victim);
        var victimState = GetRoundState(victim);
        victimState.Alive = false;
        victimState.Died = true;

        var killer = players.Values.FirstOrDefault(player => player.Team != victim.Team
            && _livePlayers.TryGetValue(PlayerId(player), out var old)
            && (player.ActionTrackingServices?.MatchStats?.Kills ?? 0) > old.Kills);
        if (killer is not null)
        {
            var killerId = PlayerId(killer);
            GetRoundState(killer).Kills++;
            foreach (var trade in _pendingTrades.Where(t => t.KillerId == victimId
                && t.VictimTeam == killer.Team && now - t.At <= TimeSpan.FromSeconds(5)))
            {
                GetRoundState(trade.VictimId).Traded = true;
                GetRoundState(killer).TradeKills++;
            }
            _pendingTrades.Add(new(victimId, victim.Team, killerId, now));
        }
        else
        {
            _unresolvedDeaths.Add(new(victimId, victim.Team, now));
        }

        var assister = players.Values.FirstOrDefault(player => player.Team != victim.Team
            && _livePlayers.TryGetValue(PlayerId(player), out var old)
            && (player.ActionTrackingServices?.MatchStats?.Assists ?? 0) > old.Assists);
        if (assister is not null) GetRoundState(assister).Assisted = true;

        _deathEvents++;
        EvaluateClutches();
    }

    private void ResolveDelayedAttribution(Dictionary<string, CCSPlayerController> players)
    {
        if (_unresolvedDeaths.Count == 0) return;
        var now = DateTimeOffset.UtcNow;
        foreach (var killer in players.Values)
        {
            var killerId = PlayerId(killer);
            if (!_livePlayers.TryGetValue(killerId, out var old)) continue;
            var killDelta = (killer.ActionTrackingServices?.MatchStats?.Kills ?? 0) - old.Kills;
            while (killDelta-- > 0)
            {
                var death = _unresolvedDeaths.FirstOrDefault(x => x.VictimTeam != killer.Team
                    && now - x.At <= TimeSpan.FromSeconds(2));
                if (death is null) break;
                _unresolvedDeaths.Remove(death);
                GetRoundState(killer).Kills++;
                foreach (var trade in _pendingTrades.Where(t => t.KillerId == death.VictimId
                    && t.VictimTeam == killer.Team && now - t.At <= TimeSpan.FromSeconds(5)))
                {
                    GetRoundState(trade.VictimId).Traded = true;
                    GetRoundState(killer).TradeKills++;
                }
                _pendingTrades.Add(new(death.VictimId, death.VictimTeam, killerId, death.At));
            }
        }

        _unresolvedDeaths.RemoveAll(x => now - x.At > TimeSpan.FromSeconds(2));
    }

    private void SnapshotLivePlayers()
    {
        _livePlayers.Clear();
        foreach (var player in PlayingPlayers())
        {
            var stats = player.ActionTrackingServices?.MatchStats;
            _livePlayers[PlayerId(player)] = new(player.PawnIsAlive, stats?.Kills ?? 0, stats?.Assists ?? 0);
        }
    }

    private record struct PrevStat(int Kills, int Deaths, int Assists, int Damage, int HeadShotKills);
    private record PlayerSnapshot(
        string SteamId, string Name, string Team, bool IsBot, bool Alive, int Health,
        int Kills, int Deaths, int Assists, int Damage, int HeadshotKills,
        int TotalKills, int TotalDeaths, int TotalAssists, int TotalDamage, int TotalHeadshotKills,
        int Score, int Money);
    private sealed class RoundPlayerState
    {
        public bool Participated { get; set; }
        public bool Alive { get; set; }
        public bool Died { get; set; }
        public bool Assisted { get; set; }
        public bool Survived { get; set; }
        public bool Traded { get; set; }
        public int TradeKills { get; set; }
        public bool Kast { get; set; }
        public int Kills { get; set; }
        public CsTeam Team { get; set; }
        public bool ClutchAttempt { get; set; }
        public bool ClutchWon { get; set; }
        public int ClutchSize { get; set; }
    }
    private sealed class MatchEventStats
    {
        public int KastRounds { get; set; }
        public int Multikill2 { get; set; }
        public int Multikill3 { get; set; }
        public int Multikill4 { get; set; }
        public int Multikill5 { get; set; }
        public int TradeKills { get; set; }
        public int ClutchAttempts { get; set; }
        public int ClutchesWon { get; set; }
    }
    private sealed class DeathmatchPlayerState
    {
        public bool Alive { get; set; }
        public int LastKills { get; set; }
        public int LastDamage { get; set; }
        public int SpawnCount { get; set; }
        public int CompletedLives { get; set; }
        public int CurrentKillStreak { get; set; }
        public int MaxKillStreak { get; set; }
        public double AliveSeconds { get; set; }
        public double LongestLifeSeconds { get; set; }
        public DateTimeOffset? LifeStartedAt { get; set; }
        public int LifeStartKills { get; set; }
        public int LifeStartDamage { get; set; }
        public List<DateTimeOffset> KillTimes { get; } = [];
        public HashSet<int> Awarded5s { get; } = [];
        public HashSet<int> Awarded10s { get; } = [];
        public int Burst5s2 { get; set; }
        public int Burst5s3 { get; set; }
        public int Burst5s4 { get; set; }
        public int Burst10s2 { get; set; }
        public int Burst10s3 { get; set; }
        public int Burst10s4 { get; set; }
    }
    private record DeathmatchLifeRecord(string SteamId, int LifeIndex, DateTimeOffset SpawnedAt,
        DateTimeOffset EndedAt, string EndKind, double DurationSeconds, int Kills, int Damage);
    private record PendingTrade(string VictimId, CsTeam VictimTeam, string KillerId, DateTimeOffset At);
    private record UnresolvedDeath(string VictimId, CsTeam VictimTeam, DateTimeOffset At);
    private record ClutchCandidate(string PlayerId, CsTeam Team, int Opponents);
    private record LivePlayerState(bool Alive, int Kills, int Assists);
}
