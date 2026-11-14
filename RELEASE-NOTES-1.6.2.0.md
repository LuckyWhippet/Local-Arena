# Local Arena v1.6.2.0

**中文** | [English](#english)

---

## 中文

修正饰品预览模式的一个长期偏差：**预览模式下的人机不再是增强版**。

### 问题

启动模式的实现里，只要不是「正常匹配」，就会往 `gameinfo.gi` 同时插入两条搜索路径：

```
Game csgo/addons/metamod
Game csgo/overrides/botprofile.vpk
```

第一条是 Metamod，CounterStrikeSharp 和饰品插件靠它加载；第二条是增强人机用来替换机器人档案的 VPK。结果就是「饰品预览」虽然是去看皮肤的，人机却跟着变成了增强版——而 README 里写的是「只启用玩家饰品，保留官方普通人机」。文档和行为对不上，代码是错的那一方。

### 改动

- 预览模式只插入 `Game csgo/addons/metamod`，不再挂载 `botprofile.vpk`，人机保持官方原版
- 增强人机模式保持原样，两条都插入
- 模式校验拆开：增强人机要求 `botprofile.vpk` 在场，预览则要求它**不在场**（和正常匹配的检查对称）
- 补了一条测试：预览模式必须带 Metamod、必须不带 botprofile

### 影响

- 饰品、刀、手套、贴纸在预览模式下照旧生效
- 只有机器人的强度回到官方水平
- 想打增强人机仍然用「增强人机」模式，不受影响

### 升级

面板覆盖安装即可。切换模式时会自动重写 `gameinfo.gi`，不需要手动改文件。

### 许可证

[AGPL-3.0](LICENSE)

---

## English

Fixes a long-standing mismatch: **cosmetics preview no longer runs the enhanced bot profiles**.

### The bug

Every non-Online launch mode wrote both search paths into `gameinfo.gi`:

```
Game csgo/addons/metamod
Game csgo/overrides/botprofile.vpk
```

The first is Metamod, which CounterStrikeSharp and the cosmetics plugin need. The second swaps in the enhanced bot profiles. So "Cosmetics Preview" also gave you enhanced bots, even though the README promises "player cosmetics only, official vanilla bots". The documentation was right and the code was wrong.

### What changed

- Preview now inserts only `Game csgo/addons/metamod`, leaving `botprofile.vpk` unmounted, so bots stay vanilla
- Enhanced Bots is unchanged and still mounts both
- Validation split accordingly: Bots requires `botprofile.vpk`, Preview requires it to be absent (mirroring the Online checks)
- A test pins the behaviour: preview must have Metamod and must not have the bot profile

### Impact

- Cosmetics, knives, gloves and stickers still apply in preview
- Only bot strength drops back to the official level
- Enhanced Bots mode is untouched

### Upgrading

Install over the top with the Panel. Switching modes rewrites `gameinfo.gi` automatically; no manual file editing.

### License

[AGPL-3.0](LICENSE)
