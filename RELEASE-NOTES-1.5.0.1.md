# Local Arena v1.5.0.1

A follow-up to v1.5.0.0 that fixes match progress being wiped whenever players changed sides, plus a telemetry bug that had been silently breaking match statistics.

## Side switches no longer reset the match

Changing sides mid-match reset the score and the clock. Two independent causes, both triggered by the same game event:

**The bot quota restore restarted the game.** The lineup injector called `RestoreBotQuota` whenever no lineup was configured, and that ends with `mp_restartgame 1`. The trigger was `EventPlayerTeam`, which fires not only for the opening side pick but also for a halftime switch and for a manual team change in deathmatch — so the match was restarted underneath the player. That restart is what put the deathmatch clock back to its full 10 minutes (`mp_timelimit 10` in `gamemode_dm_freeforall.cfg` and `gamemode_teamdeathmatch.cfg`). It now runs at most once per map.

**Telemetry read a halftime switch as a new match.** `TotalRoundsPlayed` is zeroed at halftime, and the plugin treated any drop in that counter as a finished match: it finalized the running match, cleared the start time and began a new one, which reset both the round number and the duration. A reset is now classified by score — halftime carries the score into the second half and the match continues with a rebased round counter, while a real restart clears the score and still begins a new match. Round numbers stay absolute across halves.

Verified against a live match: thirteen rounds, all recorded under the same match id, with a side-switch event in the middle and no restart.

## Match statistics were never actually recorded

`PlayerId` keyed players by SteamID alone. Bots carry SteamID 0, so any match with more than one bot collapsed them onto the same key and `PlayingPlayers().ToDictionary(PlayerId)` threw

```
System.ArgumentException: An item with the same key has already been added. Key: 0
```

once per tick. `PollLivePlayerEvents` aborted every time, so live kill and death attribution never ran in any offline match — which is every match this tool is used for. The error log had accumulated 4258 occurrences of it. Bots now fall back to their entity slot, and the three places that keyed by raw SteamID use the same helper.

## Everything from v1.5.0.0

This release includes all of v1.5.0.0:

- **Cosmetics work again after the 2026-10-23 CS2 update.** That update removed the native `SetAttributeByName` entry point every cosmetic write depended on. The plugin now calls the still-exported `SetOrAddAttributeValueByName` and checks each write's return value instead of failing silently.
- **CS2 no longer exits after a map loads.** CounterStrikeSharp is pinned to v1.0.378, the build that targets CS2 1.41.9.2.
- **Online updates resolve.** The feed points at this repository and manifests are signed with a key pair generated for this fork.
- **A fresh install is usable without a manual mode switch.** The installer applies the enhanced-bots `gameinfo.gi` layout once the transaction commits.

## Requirements

- Windows
- CS2 (verified against build 1.41.9.2, SourceRevision 11162119)
- Cosmetics preview and enhanced-bot modes launch with `-insecure` and cannot enter official matchmaking. Do not remove `-insecure` to play online; switch to the normal-matchmaking mode instead.

## Upgrading

Install over the existing build. Presets, match history and backups are preserved; player presets use the preserve-config policy and are not overwritten.

## If something looks wrong

Open the console and run `css_cs2bi_knives_status`. `signature=true` means the cosmetics pipeline is live. If the command is unknown, CounterStrikeSharp never loaded, which almost always means `gameinfo.gi` lists `Game csgo` before `Game csgo/addons/metamod` — CS2 stops at the first match, so the plain entry first means the stock `server.dll` loads and every plugin is skipped.

Export diagnostics from the Panel before reporting a problem.

## Attribution

Selected enhanced-bot components remain derived from upstream [CS2-Bot-Improver](https://github.com/ed0ard/CS2-Bot-Improver) (AGPL-3.0) and [Local Arena](https://github.com/numakkiyu/Local-Arena). This fork is not affiliated with, endorsed by, or maintained by those projects or their authors.

## License

[AGPL-3.0](LICENSE)
