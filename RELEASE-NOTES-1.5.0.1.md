# Local Arena v1.5.0.1

**中文** | [English](#english)

---

## 中文

v1.5.0.0 的后续版本。修复了换边时对局进度被清空的问题，以及一个一直在悄悄让对局统计失效的 bug。

### 换边不再重置对局

中途换边会清空比分和计时。有两个独立原因，都由同一个游戏事件触发：

**恢复人机配额会重启游戏。** 阵容注入插件在未配置阵容时会调用 `RestoreBotQuota`，而它的最后一步是 `mp_restartgame 1`。触发条件是 `EventPlayerTeam`，这个事件不只在开局选边时触发——半场换边、死斗手动换队同样会触发，于是对局在玩家脚下被重启了。死斗计时回到 10 分钟满值就是这个重启造成的（`gamemode_dm_freeforall.cfg` 和 `gamemode_teamdeathmatch.cfg` 里都是 `mp_timelimit 10`）。现在每张地图最多只执行一次。

**遥测把半场换边当成了新对局。** `TotalRoundsPlayed` 在半场时会被归零，而插件把「这个计数器变小」一律当作对局结束：它会终结当前对局、清空开始时间、再开一局，于是回合数和时长都被重置。现在改用比分来区分——半场换边会带着比分进入下半场，对局继续、回合基准重设；真正的重开则比分清零，仍然开新对局。回合编号跨半场保持连续。

已在一场实机对局中验证：13 个回合全部记录在同一个对局 id 下，中间发生过换边事件，没有任何重启。

### 对局统计其实一直没在工作

`PlayerId` 只用 SteamID 作为玩家标识。人机的 SteamID 都是 0，所以任何有多个人的对局都会把它们挤到同一个 key 上，`PlayingPlayers().ToDictionary(PlayerId)` 就会抛异常：

```
System.ArgumentException: An item with the same key has already been added. Key: 0
```

每个 tick 抛一次。`PollLivePlayerEvents` 每次都中止，因此实时击杀/死亡归属在任何本地对局里都没运行过——而这正是本工具的全部使用场景。错误日志里累计了 4258 次这个异常。现在人机会回退到实体槽位作为标识，三处按原始 SteamID 建 key 的地方都改用同一个辅助方法。

### 包含 v1.5.0.0 的全部内容

本版本包含 v1.5.0.0 的所有修复：

- **2026-10-23 的 CS2 更新后换肤重新可用。** 那次更新移除了所有饰品写入所依赖的原生入口 `SetAttributeByName`。插件现在改为调用仍在导出的 `SetOrAddAttributeValueByName`，并检查每次写入的返回值，不再静默失败。
- **进图后 CS2 不再退出。** CounterStrikeSharp 钉在 v1.0.378，即针对 CS2 1.41.9.2 发布的版本。
- **在线更新真正可用。** 更新源指向本仓库，清单由为本 Fork 生成的密钥对签名。
- **新装即可用，无需手动切模式。** 安装事务提交后会自动应用增强人机的 `gameinfo.gi` 布局。

### 系统要求

- Windows
- CS2（已在 build 1.41.9.2、SourceRevision 11162119 上验证）
- 饰品预览与增强人机模式使用 `-insecure` 启动，无法进入官方匹配。**不要为了打官匹去掉 `-insecure`**，请改为切换到正常匹配模式。

### 升级

直接覆盖安装即可。预设、对局历史和备份都会保留；玩家预设使用保留配置策略，不会被覆盖。

### 如果看起来不对

打开控制台执行 `css_cs2bi_knives_status`。返回 `signature=true` 说明饰品管线正常。若提示该指令不存在，说明 CounterStrikeSharp 根本没加载，这几乎总是因为 `gameinfo.gi` 里 `Game csgo` 排在 `Game csgo/addons/metamod` 之前——CS2 按顺序搜索并停在第一个命中项，`Game csgo` 在前就会加载原版 `server.dll`，所有插件被静默跳过。

报告问题前请先从面板导出诊断包。

### 来源与署名

部分增强人机组件源自上游 [CS2-Bot-Improver](https://github.com/ed0ard/CS2-Bot-Improver)（AGPL-3.0）与 [Local Arena](https://github.com/numakkiyu/Local-Arena)。本 Fork 与上述项目及其作者无隶属、背书或维护关系。

### 许可证

[AGPL-3.0](LICENSE)

---

## English

A follow-up to v1.5.0.0 that fixes match progress being wiped whenever players changed sides, plus a telemetry bug that had been silently breaking match statistics.

### Side switches no longer reset the match

Changing sides mid-match reset the score and the clock. Two independent causes, both triggered by the same game event:

**The bot quota restore restarted the game.** The lineup injector called `RestoreBotQuota` whenever no lineup was configured, and that ends with `mp_restartgame 1`. The trigger was `EventPlayerTeam`, which fires not only for the opening side pick but also for a halftime switch and for a manual team change in deathmatch — so the match was restarted underneath the player. That restart is what put the deathmatch clock back to its full 10 minutes (`mp_timelimit 10` in `gamemode_dm_freeforall.cfg` and `gamemode_teamdeathmatch.cfg`). It now runs at most once per map.

**Telemetry read a halftime switch as a new match.** `TotalRoundsPlayed` is zeroed at halftime, and the plugin treated any drop in that counter as a finished match: it finalized the running match, cleared the start time and began a new one, which reset both the round number and the duration. A reset is now classified by score — halftime carries the score into the second half and the match continues with a rebased round counter, while a real restart clears the score and still begins a new match. Round numbers stay absolute across halves.

Verified against a live match: thirteen rounds, all recorded under the same match id, with a side-switch event in the middle and no restart.

### Match statistics were never actually recorded

`PlayerId` keyed players by SteamID alone. Bots carry SteamID 0, so any match with more than one bot collapsed them onto the same key and `PlayingPlayers().ToDictionary(PlayerId)` threw

```
System.ArgumentException: An item with the same key has already been added. Key: 0
```

once per tick. `PollLivePlayerEvents` aborted every time, so live kill and death attribution never ran in any offline match — which is every match this tool is used for. The error log had accumulated 4258 occurrences of it. Bots now fall back to their entity slot, and the three places that keyed by raw SteamID use the same helper.

### Everything from v1.5.0.0

This release includes all of v1.5.0.0:

- **Cosmetics work again after the 2026-10-23 CS2 update.** That update removed the native `SetAttributeByName` entry point every cosmetic write depended on. The plugin now calls the still-exported `SetOrAddAttributeValueByName` and checks each write's return value instead of failing silently.
- **CS2 no longer exits after a map loads.** CounterStrikeSharp is pinned to v1.0.378, the build that targets CS2 1.41.9.2.
- **Online updates resolve.** The feed points at this repository and manifests are signed with a key pair generated for this fork.
- **A fresh install is usable without a manual mode switch.** The installer applies the enhanced-bots `gameinfo.gi` layout once the transaction commits.

### Requirements

- Windows
- CS2 (verified against build 1.41.9.2, SourceRevision 11162119)
- Cosmetics preview and enhanced-bot modes launch with `-insecure` and cannot enter official matchmaking. Do not remove `-insecure` to play online; switch to the normal-matchmaking mode instead.

### Upgrading

Install over the existing build. Presets, match history and backups are preserved; player presets use the preserve-config policy and are not overwritten.

### If something looks wrong

Open the console and run `css_cs2bi_knives_status`. `signature=true` means the cosmetics pipeline is live. If the command is unknown, CounterStrikeSharp never loaded, which almost always means `gameinfo.gi` lists `Game csgo` before `Game csgo/addons/metamod` — CS2 stops at the first match, so the plain entry first means the stock `server.dll` loads and every plugin is skipped.

Export diagnostics from the Panel before reporting a problem.

### Attribution

Selected enhanced-bot components remain derived from upstream [CS2-Bot-Improver](https://github.com/ed0ard/CS2-Bot-Improver) (AGPL-3.0) and [Local Arena](https://github.com/numakkiyu/Local-Arena). This fork is not affiliated with, endorsed by, or maintained by those projects or their authors.

### License

[AGPL-3.0](LICENSE)
