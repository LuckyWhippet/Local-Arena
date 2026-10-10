using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;

namespace BotState;

public partial class BotState
{
    //---------------------------------------------------------------------------------------
    // Runs bot modules in their original order using one controller traversal.
    private void OnTick()
    {
        ProcessFlashbangAvoidance();
        ExpireReveals(Server.CurrentTime);

        foreach (var player in Utilities.FindAllEntitiesByDesignerName<CCSPlayerController>("cs_player_controller"))
        {
            if (!player.IsValid || !player.IsBot)
                continue;

            var pawn = player.PlayerPawn.Value;
            if (pawn == null || !pawn.IsValid)
                continue;

            var bot = pawn.Bot;
            if (bot == null)
                continue;
            // In case the bot has been taken over
            bool isTakenOver = player.HasBeenControlledByPlayerThisRound;
            if (isTakenOver) continue;

            int idx = (int)player.Index;
            float now = Server.CurrentTime;
            InterruptReload(player, pawn, bot, now);
            // Door Stuck Fix
            bool inDoorCooldown = _doorEventCooldown.TryGetValue(idx, out float doorCooldownEnd) && now < doorCooldownEnd;

            UpdateBotBehavior(bot, now);
            bool curIsAttacking = UpdateBotCombat(player, pawn, bot, idx);
            if (!UpdateBotMovement(pawn, bot, idx, curIsAttacking, inDoorCooldown))
                continue;

            RecoverStuckBot(player, pawn, bot, idx, now, curIsAttacking);
            ApplyMapMovementFix(pawn, bot);
        }
    }
}
