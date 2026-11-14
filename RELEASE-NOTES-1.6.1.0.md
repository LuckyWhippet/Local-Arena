# Local Arena v1.6.1.0

**中文** | [English](#english)

---

## 中文

新增比「低」更简单的 **入门（Beginner）** 难度。

### 为什么加这一档

原有人机难度只有低、中、高三档，最低档的 Skill 仍是 49、反应时间 0.1 秒、准星聚焦间隔 0.01 秒——对刚开始接触这个工具、或者只想熟悉地图和回合流程的人来说依然偏难。入门档把整套参数再往下压一档，目标是「能打中站着不动的你，但不会在你转身时秒掉你」。

### 具体调了什么

机器人档案（`botprofile.db`）在低难度基础上：

- Skill 上限压到 30（低难度是 49，职业档是 100）
- 反应时间 +0.4 秒，开火延迟 +0.25 秒
- 初始准星偏差 +0.8，偏移系数下限提到 0.7（0 代表绝对精准）
- 准星聚焦间隔 +0.2 秒，聚焦衰减降到原来的约三分之一
- 侵略性 -40，团队协作 -35

行为树参数（`bt_config.kv3`）同步放宽：目标捕获与跟踪的插值时间翻倍，反应时间 +0.3 秒，角度容差与惩罚翻倍，战斗下蹲概率减半。

### 面板

- 概览的难度卡片新增「入门」，带一句说明
- 对局面板的难度分段从三档变四档
- 安装检测会同时检查这一档的匹配档案（`PlusMatchCoordinator/profiles/VeryLow/botprofile.db`）
- 打包脚本会为这一档从仓库的 `botprofile.db` 生成 `overrides/VeryLow/botprofile.vpk`

### 升级

面板覆盖安装即可。已有预设与本地配置保留。

### 许可证

[AGPL-3.0](LICENSE)

---

## English

Adds a **Beginner** tier below Low.

### Why

The bot stack only had Low, Medium and High. Even Low sat at Skill 49, a 0.1 s reaction time and a 0.01 s aim-focus interval — still punishing for someone learning the tool, the maps, or the flow of a round. Beginner pushes every parameter one notch further down: it should hit a standing target, but it should not delete you the moment you turn around.

### What changed

From the Low database:

- Skill capped at 30 (Low is 49, pro templates are 100)
- Reaction time +0.4 s, attack delay +0.25 s
- Initial aim spread +0.8, offset scale floored at 0.7 (0 means perfectly accurate)
- Aim-focus interval +0.2 s, focus decay cut to roughly a third
- Aggression -40, teamwork -35

The behaviour tree gets the matching treatment: acquisition and tracking lerp times doubled, reaction time +0.3 s, angle tolerances and penalties doubled, combat-crouch chance halved.

### Panel

- The overview difficulty card gains a fourth option with its own description
- The match panel selector goes from three segments to four
- Install checks verify the new match profile (`PlusMatchCoordinator/profiles/VeryLow/botprofile.db`)
- Packaging builds `overrides/VeryLow/botprofile.vpk` from the tracked `botprofile.db`

### Upgrading

Install over the top with the Panel. Existing presets and local config are kept.

### License

[AGPL-3.0](LICENSE)
