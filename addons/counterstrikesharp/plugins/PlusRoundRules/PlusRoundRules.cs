using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Cvars;
using Microsoft.Extensions.Logging;

namespace PlusRoundRules;

/// <summary>
/// Gives the first round of the second half a longer buy time. The engine reads
/// mp_freezetime when a round starts, so the value is raised while the last round
/// of the first half is ending and restored once round 13 freeze time is over.
/// </summary>
public sealed class PlusRoundRulesPlugin : BasePlugin
{
    public override string ModuleName => "PlusRoundRules";
    public override string ModuleVersion => "1.0.0";
    public override string ModuleAuthor => "Local Arena fork";
    public override string ModuleDescription =>
        "Longer buy time for the first round of the second half";

    private int _extendedFreeze = 30;
    private int _defaultFreeze = 15;
    private bool _halfEnding;
    private bool _restorePending;

    private static int ReadFreeze() => ConVar<int>.Find("mp_freezetime")?.Value ?? 15;

    private static bool HalfTimeEnabled() => ConVar<bool>.Find("mp_halftime")?.Value ?? true;

    public override void Load(bool hotReload)
    {
        _defaultFreeze = ReadFreeze();

        RegisterListener<Listeners.OnMapStart>(_ =>
        {
            _defaultFreeze = ReadFreeze();
            _halfEnding = false;
            _restorePending = false;
        });

        RegisterEventHandler<EventRoundAnnounceLastRoundHalf>(OnLastRoundOfHalf, HookMode.Pre);
        RegisterEventHandler<EventRoundEnd>(OnRoundEnd, HookMode.Pre);
        RegisterEventHandler<EventRoundFreezeEnd>(OnFreezeEnd, HookMode.Pre);

        AddCommand("css_rr_status", "Show the round 13 freeze rule", OnStatus);
        AddCommand("css_rr_set", "Set the round 13 buy time in seconds", OnSet);

        Logger.LogInformation(
            "[PlusRoundRules] loaded; round 13 buy time {Extended}s, other rounds {Default}s",
            _extendedFreeze, _defaultFreeze);
    }

    private HookResult OnLastRoundOfHalf(EventRoundAnnounceLastRoundHalf @event, GameEventInfo info)
    {
        if (HalfTimeEnabled()) _halfEnding = true;
        return HookResult.Continue;
    }

    private HookResult OnRoundEnd(EventRoundEnd @event, GameEventInfo info)
    {
        if (!_halfEnding) return HookResult.Continue;
        _halfEnding = false;
        if (!HalfTimeEnabled()) return HookResult.Continue;

        // Raised while the halves swap, so the engine picks it up for round 13.
        Server.ExecuteCommand($"mp_freezetime {_extendedFreeze}");
        _restorePending = true;
        Logger.LogInformation("[PlusRoundRules] second half next: buy time raised to {Seconds}s", _extendedFreeze);
        return HookResult.Continue;
    }

    private HookResult OnFreezeEnd(EventRoundFreezeEnd @event, GameEventInfo info)
    {
        if (!_restorePending) return HookResult.Continue;
        _restorePending = false;
        Server.ExecuteCommand($"mp_freezetime {_defaultFreeze}");
        Logger.LogInformation("[PlusRoundRules] buy time restored to {Seconds}s", _defaultFreeze);
        return HookResult.Continue;
    }

    private void OnStatus(CCSPlayerController? player, CommandInfo command)
    {
        command.ReplyToCommand(
            $"[PlusRoundRules] round 13 buy time: {_extendedFreeze}s, other rounds: {_defaultFreeze}s, pending: {_restorePending}");
    }

    private void OnSet(CCSPlayerController? player, CommandInfo command)
    {
        if (!int.TryParse(command.GetArg(1), out int seconds) || seconds < 5 || seconds > 120)
        {
            command.ReplyToCommand("[PlusRoundRules] usage: css_rr_set <5-120>");
            return;
        }
        _extendedFreeze = seconds;
        command.ReplyToCommand($"[PlusRoundRules] round 13 buy time set to {seconds}s");
    }
}
