# Local Arena v1.5.0.0

**中文** | [English](#english)

---

## 中文

面向本地 CS2 对局的 Windows 工具箱：玩家饰品、增强人机、录像、诊断与受管安装。

这是社区延续分支。原仓库（[numakkiyu/Local-Arena](https://github.com/numakkiyu/Local-Arena)）已归档停止更新。Local Arena 的全部功能均保留，本版本另外修复了 2026-10-23 CS2 更新造成的损坏。

### 修复了什么

**换肤此前静默失效。** 2026-10-23 的更新移除了饰品插件写所有内容所依赖的原生入口 `SetAttributeByName`。刀、手套、武器皮肤、贴纸与挂件全部不再生效，而插件仍报告加载正常，任何地方都不报错。现在改为调用仍在导出的 `SetOrAddAttributeValueByName`（已通过 PE `.pdata` 函数表在 `server.dll` 中核实），并检查每次写入的返回值——将来再发生变化会自己暴露出来，而不是静默失败。

**CS2 进图后几秒内退出。** CounterStrikeSharp 1.0.376 是针对上一版游戏构建的，其 gamedata 签名与 CS2 1.41.9.2 不再匹配，进程会在 `Map start` 之后立即终止。本版本钉住 CounterStrikeSharp **v1.0.378**，即针对 1.41.9.2 发布的版本。

**在线更新此前完全无法工作。** 更新器从已归档的上游仓库读取更新源，生成的下载地址也指向该仓库，并内嵌了上游作者的 Ed25519 签名公钥。它既找不到也无法验证更新。现在更新源指向本仓库，清单由为本 Fork 生成的密钥对签名。

**新装后无法直接使用。** 安装负载时从不写入 `gameinfo.gi`，而 CS2 只有在 `Game csgo` 之前列出 `addons/metamod` 时才会搜索该目录。所有插件都被静默跳过。现在安装事务提交后会自动应用布局。

### 安全与健壮性

对全应用做了对抗式审查，本版本关闭了以下问题：

- 十个 IPC 命令用裸 `PathBuf::from` 解析 CS2 目录，允许 webview 把数据库读取和一次配置写入指向任意目录。现在全部按面板其余部分的同一方式校验，数据库以只读方式打开。
- 三处 `journal.json` 被篡改后可造成数据丢失的路径：未校验的 `transaction_dir` 会删除任意目录，未校验的 `journal.target` 与 `change.backup` 允许任意文件写入，而回滚失败后仍会删除恢复所需的 journal。三处现在都做路径约束，且只有在恢复确实成功后才清理。
- 诊断包会写入绝对路径（含 Windows 账户名）且无任何脱敏。现在所有上报路径都相对于已知根目录，收集器会跳过符号链接，避免被植入的 junction 把无关文件拉进用户要分享的压缩包。
- 备份在任何替换之前都会刷盘，断电不会出现 journal 声称有备份而文件实际被截断的情况。
- ZIP 解压按实际写入字节数限制，而不是压缩包声明的大小（后者可被构造的压缩包低报）。
- 清单验证使用 `verify_strict`，且拒绝不比当前版本更新的更新。

### 系统要求

- Windows
- CS2（已在 build 1.41.9.2、SourceRevision 11162119 上验证）
- 饰品预览与增强人机模式使用 `-insecure` 启动，无法进入官方匹配。**不要为了打官匹去掉 `-insecure`**，请改为切换到正常匹配模式。

### 从早期版本升级

先从本仓库的 release 安装一次以完成切换。从已归档仓库下载的版本无法验证本 Fork 的更新签名；装过这次之后更新即可正常工作。

预设、对局历史和备份都会保留。玩家预设使用保留配置策略，不会被修复或更新覆盖。

### 如果看起来不对

打开控制台执行 `css_cs2bi_knives_status`。返回 `signature=true` 说明饰品管线正常。若提示该指令不存在，说明 CounterStrikeSharp 根本没加载，这几乎总是因为 `gameinfo.gi` 里 `Game csgo` 排在 `Game csgo/addons/metamod` 之前——CS2 按顺序搜索并停在第一个命中项，`Game csgo` 在前就会加载原版 `server.dll`，所有插件被静默跳过。

报告问题前请先从面板导出诊断包；压缩包内含插件日志、gameinfo 状态和更新历史。

### 来源与署名

部分增强人机组件源自上游 [CS2-Bot-Improver](https://github.com/ed0ard/CS2-Bot-Improver)（AGPL-3.0）与 [Local Arena](https://github.com/numakkiyu/Local-Arena)，其来源与作者署名保留在仓库中。本 Fork 与上述项目及其作者无隶属、背书或维护关系。

### 许可证

[AGPL-3.0](LICENSE)

---

## English

Windows toolkit for local CS2 matches: player cosmetics, enhanced bots, demos, diagnostics and managed installation.

This is a community continuation fork. The original repository ([numakkiyu/Local-Arena](https://github.com/numakkiyu/Local-Arena)) is archived and receives no further updates. All Local Arena features are preserved, and this build additionally fixes the breakage the 2026-10-23 CS2 update caused.

### What was broken and is now fixed

**Cosmetics were silently dead.** The 2026-10-23 update removed the native `SetAttributeByName` entry point that the cosmetics plugin used for every write. Knives, gloves, weapon skins, stickers and charms stopped applying, while the plugin still reported a clean load and no error appeared anywhere. The plugin now calls the still-exported `SetOrAddAttributeValueByName` (verified against `server.dll` through the PE `.pdata` table) and checks each write's return value, so a future change reports itself instead of failing silently.

**CS2 exited a few seconds after a map loaded.** CounterStrikeSharp 1.0.376 was built for the previous game build, so its gamedata signatures no longer matched CS2 1.41.9.2 and the process terminated right after `Map start`. This release pins CounterStrikeSharp **v1.0.378**, the build that targets 1.41.9.2.

**Online updates could never work.** The updater read its feed from the archived upstream repository, wrote download URLs pointing at that same repository, and embedded the upstream author's Ed25519 signing key. It could neither find nor verify an update. The feed now points at this repository and the manifest is signed with a key pair generated for this fork.

**A fresh install was not usable.** Installing the payload never wrote `gameinfo.gi`, and CS2 only searches `addons/metamod` when that file lists it ahead of the plain `Game csgo` entry. Every plugin was skipped without an error. The installer now applies the layout once the transaction commits.

### Security and robustness

An adversarial review of the whole application found and this release closes:

- Ten IPC commands resolved the CS2 directory with a bare `PathBuf::from`, letting the webview point database reads and a config write at any directory. All of them now validate the directory the same way the rest of the Panel does, and the database is opened read-only.
- Three ways a tampered `journal.json` could cause data loss: an unvalidated `transaction_dir` removed an arbitrary directory, unvalidated `journal.target` and `change.backup` allowed arbitrary file writes, and a failed rollback still deleted the journal that recovery needs. All three now confine their paths, and cleanup happens only when recovery actually succeeded.
- Diagnostic exports wrote absolute paths (including the Windows account name) with no redaction. Every reported path is now relative to a known root, and the collector skips symlinks so a planted junction cannot pull unrelated files into a shared archive.
- Backups are now flushed to disk before any replacement, so a power loss cannot leave the journal claiming a backup exists while the file is truncated.
- ZIP extraction is bounded by the bytes actually written rather than the sizes the archive declares, which a crafted archive could understate.
- Manifest verification uses `verify_strict`, and an update that is not newer than the running version is refused.

### Requirements

- Windows
- CS2 (verified against build 1.41.9.2, SourceRevision 11162119)
- Cosmetics preview and enhanced-bot modes launch with `-insecure` and cannot enter official matchmaking. Do not remove `-insecure` to play online; switch to the normal-matchmaking mode instead.

### Upgrading from an earlier build

Install once from this repository's releases to switch over. Builds downloaded from the archived repository cannot verify this fork's update signatures; after this install, updates work normally.

Your presets, match history and backups are preserved. Player presets use the preserve-config policy and are not overwritten by repair or update.

### If something looks wrong

Open the console and run `css_cs2bi_knives_status`. `signature=true` means the cosmetics pipeline is live. If the command is unknown, CounterStrikeSharp never loaded, which almost always means `gameinfo.gi` lists `Game csgo` before `Game csgo/addons/metamod` — CS2 stops at the first match, so the plain entry first means the stock `server.dll` loads and every plugin is skipped.

Export diagnostics from the Panel before reporting a problem; the ZIP contains the plugin logs, gameinfo state and update history.

### Attribution

Selected enhanced-bot components remain derived from upstream [CS2-Bot-Improver](https://github.com/ed0ard/CS2-Bot-Improver) (AGPL-3.0) and [Local Arena](https://github.com/numakkiyu/Local-Arena). Their origin and authorship remain credited in the repository. This fork is not affiliated with, endorsed by, or maintained by those projects or their authors.

### License

[AGPL-3.0](LICENSE)
