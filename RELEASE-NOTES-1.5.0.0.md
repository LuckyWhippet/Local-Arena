# Local Arena v1.5.0.0

Windows toolkit for local CS2 matches: player cosmetics, enhanced bots, demos, diagnostics and managed installation.

This is a community continuation fork. The original repository ([numakkiyu/Local-Arena](https://github.com/numakkiyu/Local-Arena)) is archived and receives no further updates. All Local Arena features are preserved, and this build additionally fixes the breakage the 2026-10-23 CS2 update caused.

## What was broken and is now fixed

**Cosmetics were silently dead.** The 2026-10-23 update removed the native `SetAttributeByName` entry point that the cosmetics plugin used for every write. Knives, gloves, weapon skins, stickers and charms stopped applying, while the plugin still reported a clean load and no error appeared anywhere. The plugin now calls the still-exported `SetOrAddAttributeValueByName` (verified against `server.dll` through the PE `.pdata` table) and checks each write's return value, so a future change reports itself instead of failing silently.

**CS2 exited a few seconds after a map loaded.** CounterStrikeSharp 1.0.376 was built for the previous game build, so its gamedata signatures no longer matched CS2 1.41.9.2 and the process terminated right after `Map start`. This release pins CounterStrikeSharp **v1.0.378**, the build that targets 1.41.9.2.

**Online updates could never work.** The updater read its feed from the archived upstream repository, wrote download URLs pointing at that same repository, and embedded the upstream author's Ed25519 signing key. It could neither find nor verify an update. The feed now points at this repository and the manifest is signed with a key pair generated for this fork.

**A fresh install was not usable.** Installing the payload never wrote `gameinfo.gi`, and CS2 only searches `addons/metamod` when that file lists it ahead of the plain `Game csgo` entry. Every plugin was skipped without an error. The installer now applies the layout once the transaction commits.

## Security and robustness

An adversarial review of the whole application found and this release closes:

- Ten IPC commands resolved the CS2 directory with a bare `PathBuf::from`, letting the webview point database reads and a config write at any directory. All of them now validate the directory the same way the rest of the Panel does, and the database is opened read-only.
- Three ways a tampered `journal.json` could cause data loss: an unvalidated `transaction_dir` removed an arbitrary directory, unvalidated `journal.target` and `change.backup` allowed arbitrary file writes, and a failed rollback still deleted the journal that recovery needs. All three now confine their paths, and cleanup happens only when recovery actually succeeded.
- Diagnostic exports wrote absolute paths (including the Windows account name) with no redaction. Every reported path is now relative to a known root, and the collector skips symlinks so a planted junction cannot pull unrelated files into a shared archive.
- Backups are now flushed to disk before any replacement, so a power loss cannot leave the journal claiming a backup exists while the file is truncated.
- ZIP extraction is bounded by the bytes actually written rather than the sizes the archive declares, which a crafted archive could understate.
- Manifest verification uses `verify_strict`, and an update that is not newer than the running version is refused.

## Requirements

- Windows
- CS2 (verified against build 1.41.9.2, SourceRevision 11162119)
- Cosmetics preview and enhanced-bot modes launch with `-insecure` and cannot enter official matchmaking. Do not remove `-insecure` to play online; switch to the normal-matchmaking mode instead.

## Upgrading from an earlier build

Install once from this repository's releases to switch over. Builds downloaded from the archived repository cannot verify this fork's update signatures; after this install, updates work normally.

Your presets, match history and backups are preserved. Player presets use the preserve-config policy and are not overwritten by repair or update.

## If something looks wrong

Open the console and run `css_cs2bi_knives_status`. `signature=true` means the cosmetics pipeline is live. If the command is unknown, CounterStrikeSharp never loaded, which almost always means `gameinfo.gi` lists `Game csgo` before `Game csgo/addons/metamod` — CS2 stops at the first match, so the plain entry first means the stock `server.dll` loads and every plugin is skipped.

Export diagnostics from the Panel before reporting a problem; the ZIP contains the plugin logs, gameinfo state and update history.

## Attribution

Selected enhanced-bot components remain derived from upstream [CS2-Bot-Improver](https://github.com/ed0ard/CS2-Bot-Improver) (AGPL-3.0) and [Local Arena](https://github.com/numakkiyu/Local-Arena). Their origin and authorship remain credited in the repository. This fork is not affiliated with, endorsed by, or maintained by those projects or their authors.

## License

[AGPL-3.0](LICENSE)
