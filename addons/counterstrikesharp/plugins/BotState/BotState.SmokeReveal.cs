using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;

namespace BotState;

public partial class BotState
{

    private const float HurtRevealSeconds = 0.8f;
    private const float DefuseRevealSeconds = 1.5f;
    private const float DefuseHiddenSeconds = 3.5f;

    private bool _isBombBeingDefused = false;
    private int _defuserSlot = -1;
    private float _defuseRevealUntil = 0f;
    private CounterStrikeSharp.API.Modules.Timers.Timer? _defuseRevealTimer = null;

    private readonly Dictionary<int, float> _revealUntil = new();
    //---------------------------------------------------------------------------------------
    // Reveals whoever hurt a Bot to every Bot through smoke for 1 second.
    // Further damage from the same source only pushes the window out. Windows never stack.
    private HookResult OnPlayerHurt(EventPlayerHurt @event, GameEventInfo _)
    {
        try
        {
            var victim = @event.Userid;
            if (victim == null || !victim.IsValid || !victim.IsBot) return HookResult.Continue;

            // World damage has no attacker, and self damage is nobody hurting
            // anyone else.
            var attacker = @event.Attacker;
            if (attacker == null || !attacker.IsValid || attacker.Slot == victim.Slot)
                return HookResult.Continue;

            RevealThroughSmoke(attacker.Slot, HurtRevealSeconds);
        }
        catch { }
        return HookResult.Continue;
    }
    //---------------------------------------------------------------------------------------
    // Hands one player slot to BotVision and extends its reveal window.
    private void RevealThroughSmoke(int slot, float seconds)
    {
        if (slot < 0) return;

        float until = Server.CurrentTime + seconds;
        if (_revealUntil.TryGetValue(slot, out float current))
        {
            if (until > current) _revealUntil[slot] = until;
            return;
        }

        _revealUntil[slot] = until;
        Server.ExecuteCommand($"bv_reveal add {slot}");
    }

    // Drops one reveal in BotVision and locally
    private void EndReveal(int slot)
    {
        if (!_revealUntil.Remove(slot)) return;
        Server.ExecuteCommand($"bv_reveal remove {slot}");
    }

    // Releases every reveal whose window has run out
    private void ExpireReveals(float now)
    {
        if (_revealUntil.Count == 0) return;

        List<int>? expired = null;
        foreach (var kvp in _revealUntil)
        {
            if (now < kvp.Value) continue;
            (expired ??= new List<int>()).Add(kvp.Key);
        }
        if (expired == null) return;

        foreach (int slot in expired) EndReveal(slot);
    }

    // Drops every reveal, including any BotVision still holds for a slot this
    // plugin has stopped tracking
    private void ClearReveals()
    {
        _revealUntil.Clear();
        Server.ExecuteCommand("bv_reveal clear");
    }

    // Stops defuse reveals while preserving a longer damage-triggered reveal.
    private void StopDefuseReveal()
    {
        _isBombBeingDefused = false;
        _defuseRevealTimer?.Kill();
        _defuseRevealTimer = null;

        // Keep the reveal only when damage extended it past the defuse window
        if (_defuserSlot >= 0 &&
            _revealUntil.TryGetValue(_defuserSlot, out float until) &&
            until <= _defuseRevealUntil)
        {
            EndReveal(_defuserSlot);
        }
        _defuserSlot = -1;
        _defuseRevealUntil = 0f;
    }

    // Reveals the bomb-defuser for 1.5s out of every 5s of defusing
    private void StartDefuseRevealCycle()
    {
        if (_defuseRevealTimer != null) return;

        _defuseRevealTimer = AddTimer(DefuseHiddenSeconds, () =>
        {
            _defuseRevealTimer = null;
            if (!_isBombBeingDefused) return;

            _defuseRevealUntil = Server.CurrentTime + DefuseRevealSeconds;
            RevealThroughSmoke(_defuserSlot, DefuseRevealSeconds);

            AddTimer(DefuseRevealSeconds, () =>
            {
                if (_isBombBeingDefused) StartDefuseRevealCycle();
            });
        });
    }
}
