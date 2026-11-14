# Local Arena v1.6.0.0

**中文** | [English](#english)

---

## 中文

跟进上游 CS2-Bot-Improver v1.4.9（2026-11-14）。这是一次人机栈的同步版本，行为会有变化。

### 同步了哪些东西

上游从我们钉住的 v1.4.3 往前走了 97 个提交。这次同步的托管插件全部重新编译：

- **BotAI**：包含 `update Windows bombsite-search call for CS2 1.41.9.6`，这是炸弹点搜索在 1.41.9.6 之后的游戏上的修复
- **BotBuy 1.0.16**：新增购买武器的原主归属（`m_OriginalOwnerXuidLow/High`），修掉武器名牌和 demotracer 显示上一个主人的问题；`ConVar.Find()` 全面迁移到类型安全的 `ConVar<T>.Find()`
- **BotRandomizer**：签名与饰品库更新
- **NadeSystem**：Windows/Linux 签名修复
- **BotState**：战斗下蹲的逻辑错误修复
- **BotAimImprover**：上游把写死的偏移换成了 `Schema.GetSchemaOffset` 校验；本 Fork 的守卫版已经包含这项，继续沿用

这些插件在本 Fork 上的本地改动照旧保留（BotBuy 的购买门禁与守卫架构、BotAimImprover 的运行时状态发布、BotRandomizer 的饰品模型），并与上游改动做了三方合并。

### 行为树换成新布局

上游把行为树从平铺的 `overrides/scripts/bt_default.kv3` 改成了按模式分目录：

```
overrides/scripts/ai/rush/bt_default.kv3
overrides/scripts/ai/retakes/bt_default.kv3
overrides/scripts/ai/retakes/modules/bt_defend_bomb_during_defuse.kv3
overrides/scripts/ai/retakes/modules/bt_defuse_planted_bomb.kv3
```

旧的平铺文件内容与 `ai/rush/bt_default.kv3` 逐字节相同，已随本次同步删除。retake 模式因此获得专门的行为树。

### 配置跟随上游

- `gamemode_casual` / `gamemode_competitive` / `gamemode_competitive2v2`：`mp_death_drop_grenade` 2 → 3（阵亡时掉落全部投掷物）
- `gamemode_retakecasual`：冻结时间 7 → 5、回合重开延迟 5 → 3、新增 `sv_annotation_limits_max_rounds_per_half -1`

### 这次没有包含原生模块

BotHider、BotController、BotVision 三个原生模块**没有升级**（我们仍用 BotHider 0.3.3 与上游 v1.4.3 运行时布局）。原因不是漏了：

- 上游的 BotHider 已到 0.7.1、BotController 到 0.9.0、BotVision 到 0.4.0，原生 ABI 有变动
- 本 Fork 的 `BotHiderImpl` / `BotControllerImpl` 与上游已经分叉（同一文件我们 382 行、上游 175 行；BotController 侧是 209 行对 32 行的差距），原生与托管必须成对升级
- 强行混用会让原生导出与托管契约对不上

原生模块的升级单独一版做，届时需要实机验证。当前版本的管理型插件与旧原生模块的组合是自洽的。

### 升级

用面板覆盖安装即可。预设、对局历史、备份都会保留。

### 系统要求

- Windows
- CS2（已在 build 1.41.9.7 上验证托管插件与运行时）
- 饰品预览与增强人机模式使用 `-insecure` 启动，无法进入官方匹配

### 来源与署名

同步自 [CS2-Bot-Improver](https://github.com/ed0ard/CS2-Bot-Improver)（AGPL-3.0）与 [Local Arena](https://github.com/numakkiyu/Local-Arena)。

### 许可证

[AGPL-3.0](LICENSE)

---

## English

Follows upstream CS2-Bot-Improver v1.4.9 (2026-11-14). This is a bot-stack sync, so behaviour changes.

### What was synced

Upstream moved 97 commits past the v1.4.3 release this fork was pinned to. Every managed plugin was rebuilt:

- **BotAI**: carries `update Windows bombsite-search call for CS2 1.41.9.6`, the fix for bomb-site search on game builds after 1.41.9.6
- **BotBuy 1.0.16**: purchases are now attributed to the buying bot (`m_OriginalOwnerXuidLow/High`), fixing name tags and demotracer reports that named the previous owner; `ConVar.Find()` migrated to the type-safe `ConVar<T>.Find()`
- **BotRandomizer**: signature and cosmetic database refresh
- **NadeSystem**: Windows and Linux signature fixes
- **BotState**: combat-crouch logic fix
- **BotAimImprover**: upstream replaced hardcoded offsets with `Schema.GetSchemaOffset` validation; this fork's guarded build already did that, so it is kept as-is

Local fork changes were preserved and three-way merged with the upstream deltas (BotBuy purchase gates, BotAimImprover runtime status publishing, BotRandomizer cosmetic models).

### Behaviour trees use the new layout

Upstream replaced the flat `overrides/scripts/bt_default.kv3` with per-mode folders:

```
overrides/scripts/ai/rush/bt_default.kv3
overrides/scripts/ai/retakes/bt_default.kv3
overrides/scripts/ai/retakes/modules/bt_defend_bomb_during_defuse.kv3
overrides/scripts/ai/retakes/modules/bt_defuse_planted_bomb.kv3
```

The old flat file was byte-identical to `ai/rush/bt_default.kv3` and was removed in this sync. Retake mode now gets its own tree.

### Config follows upstream

- `gamemode_casual`, `gamemode_competitive`, `gamemode_competitive2v2`: `mp_death_drop_grenade` 2 → 3 (drop all grenades on death)
- `gamemode_retakecasual`: freezetime 7 → 5, round restart delay 5 → 3, adds `sv_annotation_limits_max_rounds_per_half -1`

### The native modules were not upgraded

BotHider, BotController and BotVision stay where they were (BotHider 0.3.3, plus the v1.4.3 runtime layout). This is deliberate:

- upstream now ships BotHider 0.7.1, BotController 0.9.0 and BotVision 0.4.0, and their native ABI moved
- this fork's `BotHiderImpl` and `BotControllerImpl` have diverged from upstream (382 local lines against 175 upstream in the same file; 209 against 32 on the BotController side), and native and managed sides must move together
- mixing them would leave the native exports and the managed contract out of step

The native upgrade gets its own release, and needs in-game verification. Today's combination — new managed plugins on the old natives — is internally consistent.

### Upgrading

Install over the top with the Panel. Presets, match history and backups are kept.

### Requirements

- Windows
- CS2 (managed plugins and runtime verified on build 1.41.9.7)
- Cosmetics preview and Enhanced Bots launch with `-insecure` and cannot join official matchmaking

### Attribution

Synced from [CS2-Bot-Improver](https://github.com/ed0ard/CS2-Bot-Improver) (AGPL-3.0) and [Local Arena](https://github.com/numakkiyu/Local-Arena).

### License

[AGPL-3.0](LICENSE)
