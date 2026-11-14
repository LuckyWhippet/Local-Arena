# Local Arena v1.6.4.0

**中文** | [English](#english)

---

## 中文

新增 `PlusRoundRules` 插件：**第 13 局的买枪冻结时间延长到 30 秒**，其余回合维持配置里的 15 秒。

### 行为

- 第一半场的最后一局哨声响起时（引擎的 `round_announce_last_round_half` 事件），插件把 `mp_freezetime` 提前改写为 30
- 引擎在回合开始时读取这个值，因此第 13 局开局就是 30 秒买枪时间
- 第 13 局的冻结时间结束（`round_freeze_end`）后立刻改回 15，第 14 局起恢复正常
- 只在 `mp_halftime 1` 的模式里生效；休闲、Rush 这类不换边的模式完全不受影响
- 换地图、重开对局都会重置内部状态，不会把 30 秒泄漏到后面的回合

### 指令

| 指令 | 作用 |
|---|---|
| `css_rr_status` | 查看当前设置（延长值、默认值、是否有待恢复的改写） |
| `css_rr_set <5-120>` | 修改第 13 局的买枪时间，立即生效 |

### 为什么不是配置项

`mp_freezetime` 是全局值，引擎每回合开始时读取一次。想在**只有第 13 局**延长，就必须在两半场之间改写它、再在冻结结束时改回来——这是按回合的逻辑，配置层做不到。

### 附带

竞技模式的中场休息仍是 v1.6.3.0 起的 30 秒（`mp_halftime_duration`），那段是换边等待，买枪菜单打不开；这次改的是第 13 局真正的买枪时间。

### 升级

面板覆盖安装即可。

### 许可证

[AGPL-3.0](LICENSE)

---

## English

Adds the `PlusRoundRules` plugin: **round 13 gets a 30 second buy time**, every other round keeps the configured 15.

### Behaviour

- When the last round of the first half is announced (`round_announce_last_round_half`), the plugin raises `mp_freezetime` to 30 early
- The engine reads that value when a round starts, so round 13 begins with 30 seconds of buy time
- As soon as round 13 freeze time ends (`round_freeze_end`) the value is restored, so round 14 is back to normal
- Only active when `mp_halftime 1`; modes without a side switch (Casual, Rush) are untouched
- Map start and match restarts reset the internal state, so the longer value never leaks into later rounds

### Commands

| Command | Effect |
|---|---|
| `css_rr_status` | show the current values and whether a restore is pending |
| `css_rr_set <5-120>` | change round 13's buy time immediately |

### Why this is not a config switch

`mp_freezetime` is global and read once per round start. Extending **only** round 13 means rewriting it across the halftime boundary and restoring it afterwards, which is round-aware logic rather than configuration.

### Also

The competitive halftime break stays at 30 seconds from v1.6.3.0 (`mp_halftime_duration`). That is the side-switch wait where the buy menu is closed; this release changes the actual buy time inside round 13.

### Upgrading

Install over the top with the Panel.

### License

[AGPL-3.0](LICENSE)
