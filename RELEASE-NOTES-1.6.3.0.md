# Local Arena v1.6.3.0

**中文** | [English](#english)

---

## 中文

调整人机模式的比赛节奏：开局热身、可暂停、中场准备更从容。

### 开局热身

竞技、休闲、Rush 三个模式在配置末尾加上了 `mp_warmup_start`。此前的配置只设了 `mp_warmuptime`（竞技 120 秒、休闲 90 秒、Rush 120 秒），但没有显式开始热身的指令，地图载入后可能直接进入第一局。现在地图一载入就进热身，时长仍由原有配置决定。

### 暂停与继续

竞技和休闲加了两个指令：

| 按键 | 指令 | 作用 |
|---|---|---|
| F9 | `mp_pause_match` | 当前回合结束后暂停 |
| F10 | `mp_unpause_match` | 继续比赛 |

这两个是引擎自带的服务器指令，不需要开启作弊，也不需要插件。`Commands.txt` 里新增了「MATCH CONTROL」一节，方便在游戏里查。

如果 F9/F10 和你自己的绑定冲突，改 `cfg/gamemode_competitive.cfg` 或 `cfg/gamemode_casual.cfg` 末尾那两行 `bind` 即可。

### 竞技的中场准备

竞技模式加了 `mp_halftime_duration 30`（默认 15 秒）。换边后的中场休息从 15 秒延长到 30 秒，第 13 局开打前有更充裕的时间买枪、沟通。

注意这里延长的是**中场休息**本身。如果你想要的是「第 13 局内部的买枪冻结时间更长」，那需要按回合改写 `mp_freezetime`，属于插件逻辑，配置层做不到——需要的话说一声，我加一个按回合计数的规则插件。

### 升级

面板覆盖安装即可。

### 许可证

[AGPL-3.0](LICENSE)

---

## English

Tunes the match pacing in bot mode: a real warmup, pausing, and a calmer halftime.

### Warmup at map start

Competitive, Casual and Rush now end with `mp_warmup_start`. The configs already set `mp_warmuptime` (120 / 90 / 120 seconds) but never started the warmup explicitly, so a map could drop straight into round one. Warmup now begins as soon as the map loads, still for the configured duration.

### Pause and resume

Competitive and Casual gain two commands:

| Key | Command | Effect |
|---|---|---|
| F9 | `mp_pause_match` | pause at the end of the current round |
| F10 | `mp_unpause_match` | resume |

Both are stock server commands: no cheats, no plugin. `Commands.txt` gained a MATCH CONTROL section.

Rebind them if F9/F10 clash with your own binds — the `bind` lines sit at the end of `cfg/gamemode_competitive.cfg` and `cfg/gamemode_casual.cfg`.

### Halftime in competitive

Competitive sets `mp_halftime_duration 30` (default 15). The break after the side switch doubles, giving more time to buy and talk before round 13.

This lengthens the **halftime break itself**. If you meant the buy/freeze time inside round 13, that requires rewriting `mp_freezetime` per round, which is plugin logic rather than configuration — say the word and I will add a small round-aware rule plugin.

### Upgrading

Install over the top with the Panel.

### License

[AGPL-3.0](LICENSE)
