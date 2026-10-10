using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;

namespace BotState;

public partial class BotState
{

    // Resets the Bot's look-around bookkeeping so it can reacquire threats
    private static void ResetLookAroundForBot(CCSPlayerController? player)
    {
        if (player == null || !player.IsValid || !player.IsBot) return;
        var pawn = player.PlayerPawn?.Value;
        if (pawn == null || !pawn.IsValid) return;
        var bot = pawn.Bot;
        if (bot == null) return;

        ref float inhibitLookAroundTimestamp = ref bot.InhibitLookAroundTimestamp;
        inhibitLookAroundTimestamp = 0f;

        ref int checkedHidingSpotCount = ref bot.CheckedHidingSpotCount;
        checkedHidingSpotCount = 0;

        ref float lookAroundStateTimestamp = ref bot.LookAroundStateTimestamp;
        lookAroundStateTimestamp = 0f;
    }
    //---------------------------------------------------------------------------------------
    // Removes the native early-round safe period for this Bot.
    private static void ApplyBotState(CCSPlayerController player)
    {
        var pawn = player.PlayerPawn.Value;
        if (pawn == null || !pawn.IsValid) return;

        var bot = pawn.Bot;
        if (bot == null) return;

        ref float safeTime = ref bot.SafeTime;
        safeTime = 0f;

        ref bool hasVisitedEnemySpawn = ref bot.HasVisitedEnemySpawn;
        hasVisitedEnemySpawn = true;
    }

    // Keeps native awareness and reaction timers at the existing per-tick values.
    private static void UpdateBotBehavior(CCSBot bot, float now)
    {
        ref bool isSleeping = ref bot.IsSleeping;
        isSleeping = false;

        ref bool allowActive = ref bot.AllowActive;
        allowActive = true;

        ref bool isRapidFiring = ref bot.IsRapidFiring;
        isRapidFiring = true;

        ref float peripheralTimestamp = ref bot.PeripheralTimestamp;
        peripheralTimestamp = 0.0f;

        ref float fireWeaponTimestamp = ref bot.FireWeaponTimestamp;
        fireWeaponTimestamp = 0.0f;
        // Alert
        CountdownTimer alertTimer = bot.AlertTimer;
        ref float alertduration = ref alertTimer.Duration;
        alertduration = 600.0f;

        ref float alerttimestamp = ref alertTimer.Timestamp;
        alerttimestamp = now + alertduration;

        ref float alerttimescale = ref alertTimer.Timescale;
        alerttimescale = 1.0f;
        // Never ignore enemies
        CountdownTimer ignoreEnemiesTimer = bot.IgnoreEnemiesTimer;

        ref float ignoreEnemiesduration = ref ignoreEnemiesTimer.Duration;
        ignoreEnemiesduration = 0.0f;

        ref float ignoreEnemiestimestamp = ref ignoreEnemiesTimer.Timestamp;
        ignoreEnemiestimestamp = 0.0f;

        ref float ignoreEnemiestimescale = ref ignoreEnemiesTimer.Timescale;
        ignoreEnemiestimescale = 1.0f;

        // Never lookat (panic)
        CountdownTimer panicTimer = bot.PanicTimer;

        ref float panicduration = ref panicTimer.Duration;
        panicduration = 0.0f;

        ref float panictimestamp = ref panicTimer.Timestamp;
        panictimestamp = 0.0f;

        ref float panictimescale = ref panicTimer.Timescale;
        panictimescale = 1.0f;
        // Never be surprised
        CountdownTimer surpriseTimer = bot.SurpriseTimer;

        ref float surpriseDuration = ref surpriseTimer.Duration;
        surpriseDuration = 0.0f;

        ref float surpriseTimestamp = ref surpriseTimer.Timestamp;
        surpriseTimestamp = 0.0f;

        ref float surpriseTimescale = ref surpriseTimer.Timescale;
        surpriseTimescale = 1.0f;
        // Always dodge
        ref bool isEnemySniperVisible = ref bot.IsEnemySniperVisible;
        isEnemySniperVisible = true;

        CountdownTimer sawEnemySniperTimer = bot.SawEnemySniperTimer;

        ref float sawEnemySniperduration = ref sawEnemySniperTimer.Duration;
        sawEnemySniperduration = 600.0f;

        ref float sawEnemySniperTimestamp = ref sawEnemySniperTimer.Timestamp;
        sawEnemySniperTimestamp = now + sawEnemySniperduration;

        ref float sawEnemySniperTimescale = ref sawEnemySniperTimer.Timescale;
        sawEnemySniperTimescale = 1.0f;
        // Teammate Stuck Fix
        ref bool IsWaitingBehindFriend = ref bot.IsWaitingBehindFriend;
        IsWaitingBehindFriend = false;

        CountdownTimer politeTimer = bot.PoliteTimer;

        ref float politeTimerDuration = ref politeTimer.Duration;
        politeTimerDuration = 0.0f;

        ref float politeTimerTimestamp = ref politeTimer.Timestamp;
        politeTimerTimestamp = 0.0f;

        ref float politeTimerTimescale = ref politeTimer.Timescale;
        politeTimerTimescale = 1.0f;

    }
}
