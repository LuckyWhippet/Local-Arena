# Local Arena v1.5.0.2

**中文** | [English](#english)

---

## 中文

跟进 CS2 2026-11-11 更新（1.41.9.7）。上一版在这版游戏上会在进图几秒后崩溃。

### 进图后 CS2 崩溃

CS2 在 11 月 11 日更新到 1.41.9.7（ClientVersion 2000943、SourceRevision 11228971）。CounterStrikeSharp 钉在 v1.0.378，而那个版本针对的是 1.41.9.2：它的原生核心按旧游戏解析函数地址，换到新游戏上拿到空指针，进图 3 秒后以访问违例退出。

崩溃现场很干净，游戏自己留了 minidump（`game\bin\win64\cs2_<日期>_accessviolation.mdmp`）。解析出来是 `0xC0000005`、写地址 `0x0`，故障模块是 `addons\counterstrikesharp\bin\win64\counterstrikesharp.dll`，不是任何插件，也不是 `server.dll`。

CounterStrikeSharp 在 11 月 12 日发布了 **v1.0.380**，更新日志第一条就是 `fix: Game Update 1.41.9.7`。本版本把运行时与 API 一起钉到 v1.0.380，14 个插件全部重新编译。

### 顺带确认没坏的东西

- 换肤所依赖的 `SetOrAddAttributeValueByName` 签名在 1.41.9.7 上仍然匹配，日志里 `signature=true`。这次崩溃和饰品管线无关。
- `gameinfo.gi` 的挂载顺序（`metamod` 先于 `Game csgo`）保持不变。

### 升级

用面板覆盖安装即可。预设、对局历史、备份都会保留；`addons\counterstrikesharp\configs\core.json` 这类本地配置不会被覆盖。

### 如果还在崩

游戏目录里的 `game\bin\win64\cs2_*.mdmp` 是最省事的证据。先看故障模块：如果是 `counterstrikesharp.dll`，说明运行时又落后于游戏版本；如果是某个插件目录里的 DLL，就点名到具体插件。

### 系统要求

- Windows
- CS2（已在 build 1.41.9.7、SourceRevision 11228971 上验证）
- 饰品预览与增强人机模式使用 `-insecure` 启动，无法进入官方匹配

### 来源与署名

部分增强人机组件源自上游 [CS2-Bot-Improver](https://github.com/ed0ard/CS2-Bot-Improver)（AGPL-3.0）与 [Local Arena](https://github.com/numakkiyu/Local-Arena)。

### 许可证

[AGPL-3.0](LICENSE)

---

## English

Follows the CS2 update of 2026-11-11 (1.41.9.7). The previous build crashed a few seconds after a map loaded on that game version.

### CS2 crashed after a map loaded

CS2 moved to 1.41.9.7 on 11 November (ClientVersion 2000943, SourceRevision 11228971). CounterStrikeSharp was pinned to v1.0.378, which targets 1.41.9.2: its native core resolves engine function addresses for the older build, so on the new one it dereferenced null and the process exited with an access violation three seconds after the map started.

The evidence is a minidump the game writes by itself (`game\bin\win64\cs2_<date>_accessviolation.mdmp`): `0xC0000005`, write to `0x0`, faulting module `addons\counterstrikesharp\bin\win64\counterstrikesharp.dll` — not a plugin, and not `server.dll`.

CounterStrikeSharp shipped **v1.0.380** on 12 November with `fix: Game Update 1.41.9.7` as its first entry. This release pins the runtime and the API to v1.0.380 and rebuilds all fourteen plugins against it.

### What still works

- The `SetOrAddAttributeValueByName` signature the cosmetics pipeline depends on still matches on 1.41.9.7 (`signature=true` in the log). This crash was unrelated to cosmetics.
- The `gameinfo.gi` mount order (`metamod` before `Game csgo`) is unchanged.

### Upgrading

Install over the top with the Panel. Presets, match history and backups are kept; local files such as `addons\counterstrikesharp\configs\core.json` are not overwritten.

### If it still crashes

The `game\bin\win64\cs2_*.mdmp` file in the game directory is the cheapest evidence. Read the faulting module first: `counterstrikesharp.dll` means the runtime again lags the game build; a DLL inside a plugin folder names the plugin.

### Requirements

- Windows
- CS2 (verified on build 1.41.9.7, SourceRevision 11228971)
- Cosmetics preview and Enhanced Bots launch with `-insecure` and cannot join official matchmaking

### Attribution

Parts of the enhanced bot stack come from [CS2-Bot-Improver](https://github.com/ed0ard/CS2-Bot-Improver) (AGPL-3.0) and [Local Arena](https://github.com/numakkiyu/Local-Arena).

### License

[AGPL-3.0](LICENSE)
