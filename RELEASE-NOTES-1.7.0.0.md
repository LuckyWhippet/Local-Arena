# Local Arena v1.7.0.0

## 新增：战术板

把每回合开局 30 秒的跑位录下来，画在雷达上——科隆 Major 中场战术图的那套东西。

- **遥测采集**：`round_paths`（冻结结束起 8 Hz 采 30 秒）、`round_grenades`（起投点、落点、飞行时间）、`round_deaths`（阵亡坐标），以及 `matches.ai_tree`（本局用的行为树）。旧数据库自动迁移，老对局只是没有战术数据。
- **战术板**：雷达底图 + 路径 + 道具弧线 + 死亡标记，时间轴回放（0.5x / 1x / 2x / 4x 倍速、10/20/30 秒窗口），悬停图例可高亮单人，支持**单场**与**最近 3/5 场**聚合，按**树 / 玩家 / 角色 / 胜负 / 阵营**分组。
- **角色**不是编的：`botprofile.db` 里每个 bot 本来就带 `Personality`（`ProFast+SniperPro+SniperPersonality "Dank1ng"`），面板解析成 Rusher / Camper / Freeman / Sniper / Rifle / Scoper / Fastshot。
- **比赛模式**：检测到进行中的比赛时自动切到比赛视图，内含战术板与会话信息。
- **设置 → 战术**：树库勾选、轮换档位（每场 / 每半场 / 固定）、路径点列表；轮换开启但一棵树都没勾选时会给出警告。

## 新增：PlusTactics 插件

CS2 通过 `mp_bot_ai_bt` 每场比赛解析一棵行为树。插件按 `tactics.json` 的选择应用它、生成行为树移动用的具名路径点，并把当前树写进 `active-tactics.json` 供遥测记录。

**没有配置树时插件会清空该 convar**，把选择权交回引擎——实测在竞技里强行挂 rush 树会让 bot 开局把枪掉在地上，所以默认不覆盖。

随包附带四棵树：`rush_a`、`rush_b`、`hold`、`slow`，以松散文件安装在 `scripts/ai/localarena/`（`overrides` 只以 VPK 挂载，那是唯一能读到的地方）。

## 界面

- 雷达标定覆盖 9 张地图（`pos_x` / `pos_y` / `scale`），已在 Dust II 上用真实跑位核对。
- 路径做滑动平均 + 样条化，回放端点按分数插值，播放走 `requestAnimationFrame`，不再是 10 Hz 的跳格。
- 数据面板头部把「比赛类型筛选」和「页面跳转」拆成两组，不再共用一条胶囊。

## 已知限制

- 自定义行为树在完整竞技对局里仍未整场验证，默认保持引擎自己的树。
- `de_cache` 没有雷达底图（CS2 不提供该地图），界面会显示提示。
- `func_bomb_target` 是 brush 实体、没有 origin，包点仍靠游戏内 `css_tac_wp add` 手动标注。
- 150% 缩放下控制条会横向滚动，右侧倍速与窗口档位需要滑动才能看到。

## 升级说明

安装包替换后即可使用；`telemetry.db` 在插件加载时自动加表，不需要清库。行为树轮换默认关闭，需要在「设置 → 战术」里显式勾选树并保存。
