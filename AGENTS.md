# 偏好设置

---

# Lucky Dog Pub - Godot 项目

## 项目概况

类《Bongo Cat》桌宠游戏，扑克部分采用电视扑克变体规则；当前任意点数的一对及以上牌型均可中奖，不使用 Jacks or Better 的高对门槛。
使用 Godot 4.6 + C#
美术资产来自 PSD 1200x1200 画布导出，坐标系 1:1 对应。

## 当前实现状态

- 桌宠模式：已支持打字/点击统计、输入加筹码、舌头反馈、根据 `DesktopActivityState` 切换小狗表情、启动 Rise 动画、桌宠气泡盲盒提示与开盒表演。
- 小狗视觉：已迁移到 v1 资源和 `DogReaction` 数据驱动，旧硬编码入口仅保留 TODO 待清理。
- 背包：已支持分页、空分页提示、数量堆叠、New 标记、装备/卸下、可空闲装备位。
- 存档：本地 JSON 存档当前为 V15；V14 及更早存档归档为 `legacy` 后创建新档，并保留损坏备份、重置存档确认和开盒表演中断恢复。
- 调试：Dev 启动器支持综合调试环境与启动级 Steam 模拟环境；Debug 页和顶部 Steam Mock 面板支持盲盒/LinkTree 网络场景、展示点推进、新手 12 个盲盒或普通循环起点，以及独立内存沙箱。Mock 的“正常掉落规则”会让每个 Generator 首次预热返回空结果，再按 Schedule 的资格间隔与窗口上限返回奖励或空结果；“强制快速成功”允许首次预热直接生成装扮，用于验证暂扣和调度交接保护。
- 盲盒：已迁移为 PlaytimeGenerator 直接生成具体 Steam 装扮；支持按当前进度预热 Generator、实例级库存基线与复查、单槽位待揭晓奖励、12 个新手 Schedule、第 5 / 第 12 个新手进度回执、普通循环、Refreshment Fallback、迟到奖励、价格覆盖、桌宠/扑克开盒表演及中断恢复。玩家点击开盒时不访问 Steam。
- LinkTree：已支持数据驱动入口、四阶段领取状态、外部操作等待、Steam Inventory 永久领取回执和奖励反馈动画。
- Steam 平台：已接入 Steamworks.NET，并支持运行期重连、库存同步超时、退避重试和待处理事务恢复。
- 待完成：生成正式 Playtest 候选包并执行普通玩家帐号/亲友外部回归，以及重复装扮补偿/分解等后续系统。新版 Steam schema 已上传，开发者帐号真实 Steam 核心回归已完成。

## 技术栈

- **引擎**: Godot 4.6.3 Mono (C#)
- **IDE**: VS Code + Wick MCP 插件
- **语言**: C# (.NET 8.0)
- **项目工作空间**: `g:/Workspace/godot-project/lucky-dog-pub-2026/`
- **游戏项目路径**: `g:/Workspace/godot-project/lucky-dog-pub-2026/lucky-dog-rise/`

## 架构概览

```
ModeManager.tscn（主入口, Control）
├── BossKeyContent.tscn（Node2D, 伪装模式 A 区）
│   ├── CanvasLayer（桌宠 Counter 状态栏：模式切换/数值显示/系统按钮）
│   ├── ContentA（DogArea/WindowSize/TaskBar 标记）
│   └── Bubble（CanvasLayer）
├── PlayContent.tscn（Node2D, 游玩模式内容）
│   └── SubViewportContainer（600×600, Scale=0.5）
│       └── SubViewport（1200×1200）
│           └── Main.tscn（扑克游戏, GameManager）
├── InfoPanel（CanvasLayer, 240×600, ModeManager 直接管理）
├── SettingsPanel（CanvasLayer, 420×600, 系统功能面板：设置/背包/Debug）
└── GlobalInputTracker（Node, 打字统计）
```

三种模式共享同一个宿主窗口，不切场景。切换时只改显隐 + 窗口 resize。
窗口为"胖窗口"设计：A 区周围预留 B 的完整尺寸空间，面板展开不改变窗口大小。

## 游戏目录结构

```
lucky-dog-rise/
├── Assets/              # 字体/UI 等通用资源；旧版美术资源已基本清理，不应新增旧路径依赖
├── Assets/v1/           # 新版 PSD 导出素材（数据驱动）
│   ├── Background/      # 背景（大小不一，JSON 定位）
│   ├── Table/           # 桌布
│   ├── Shiba/           # 小狗素材（四种毛色）
│   │   ├── Red/         # 头/眼/耳/爪
│   │   ├── Black/
│   │   ├── Cream/
│   │   └── Sesame/
│   ├── Player/          # 玩家（手臂/衣服/饰品）
│   ├── Headwear/        # 头部装饰
│   ├── Eyewear/         # 眼部装饰
│   ├── Treat/           # 饮品
│   ├── CardBack/        # 卡背
│   ├── CardFace/        # 卡面
│   ├── ItemIcon/        # 背包道具图标
│   ├── ChipStack/       # 筹码
│   └── layer_index.json # PSD 层坐标数据（doc_x/doc_y/width/height）
├── Data/
│   └── Json/            # Luban 导出的 JSON 数据
│       ├── tbpaytable.json
│       ├── tbitem.json
│       ├── tbdogskin.json
│       ├── tbdogreaction.json
│       ├── tbequipmentslotconfig.json
│       ├── tbdesktopactivitystate.json
│       ├── tbblindbox.json
│       ├── tbblindboxschedule.json
│       ├── tbblindboxrarityrate.json
│       ├── tbblindboxrevealpath.json
│       ├── tbblindboxvisual.json
│       ├── tbtabgroup.json
│       └── tbgamedevelopconfig.json
├── Audio/
│   ├── BGM/             # 背景音乐（OGG 格式）
│   └── SFX/             # 音效文件（大驼峰命名，如 Knock.wav）
├── Scenes/
│   ├── ModeManager.tscn     # 主入口场景，根节点 Control
│   ├── BossKeyContent.tscn  # BossKey A 区（狗+按钮+气泡）
│   ├── PlayContent.tscn     # 游玩模式布局（SubViewportContainer）
│   ├── InfoPanel.tscn       # 信息面板（240×600）
│   ├── BlindBoxRevealOverlay.tscn        # 扑克模式全屏盲盒表演外壳
│   ├── DesktopBlindBoxRevealOverlay.tscn # 桌宠模式圆角气泡盲盒表演外壳
│   ├── Main.tscn            # 扑克游戏内容（SubViewport 内渲染）
│   ├── SystemPanel.tscn     # 系统功能面板（设置/装扮/Debug 页签）
│   ├── DogArea.tscn         # 小狗场景（表情系统）
│   ├── HandArea.tscn        # 手臂场景（敲桌交互）
│   ├── ItemArea.tscn        # 道具展示场景（饮品等 Treat）
│   ├── ChipStack.tscn       # 筹码堆场景（下注交互）
│   ├── ChipReward.tscn      # 奖励筹码场景（收集动画）
│   └── Prefabs/
│       ├── ConfirmOverlay.tscn # 面板内确认遮罩（替代 Godot 原生弹窗）
│       ├── BalloonHint.tscn # 气球提示（盲盒倒计时/消耗提示）
│       ├── BlindBoxRevealStage.tscn # 盲盒表演共用舞台（盲盒、阴影、奖励图标）
│       ├── DogClaw.tscn     # 狗爪子（手心/手背切换）
│       └── ItemCell.tscn    # 背包单格道具（品质框+图标+New/装备中/数量）
├── Scripts/
│   ├── ModeManager.cs        # 主入口控制器（窗口管理+模式切换+面板避让）
│   ├── GameManager.cs        # 扑克游戏状态机（被 SubViewport 承载）
│   │                          + 装备视觉应用 + Luban 数据加载
│   ├── Desktop/              # 桌宠宿主窗口底层
│   │   ├── WindowNative.cs        # Windows API P/Invoke
│   │   ├── SystemPanelController.cs   # 系统功能面板（设置/装扮/Debug）
│   │   ├── InfoPanelController.cs     # 信息面板（赔率表+筹码+牌型）
│   │   ├── GameData.cs                # 共享游戏数据（筹码/段位/Inventory）
│   │   ├── PlayerInventory.cs         # 玩家背包（数量堆叠+New 标记+装备状态）
│   │   ├── SaveManager.cs             # 本地 JSON 存档（user://saves/profile_0.json）
│   │   ├── LubanData.cs               # Luban 数据表加载器（静态懒加载）
│   │   ├── SettingsManager.cs         # 设置持久化（ConfigFile）
│   │   ├── ConfirmOverlayController.cs # 通用确认遮罩控制器
│   │   ├── BalloonHintController.cs     # 气球提示控制器
│   │   ├── BlindBoxService.cs           # 盲盒调度/开奖/提示状态
│   │   ├── BlindBoxRevealOverlayController.cs # 盲盒表演控制器（扑克/桌宠共用）
│   │   └── GlobalInputTracker.cs      # 全局键鼠钩子（打字/点击统计）
│   ├── CardEvaluator.cs      # 牌型判定（纯静态）
│   ├── DeckManager.cs        # 牌组管理（作弊发牌）
│   ├── DogHintSystem.cs      # 小狗提示逻辑
│   ├── DogVisual.cs          # 小狗视觉表现（JSON 坐标定位）
│   ├── HandAreaController.cs # 手臂交互+敲桌动画（JSON 坐标定位）
│   ├── ItemAreaController.cs # 道具展示（Treat 等，JSON 坐标定位）
│   ├── ItemCellController.cs # 背包单格道具预制体控制器
│   ├── ChipStackController.cs    # 筹码堆交互
│   ├── ChipRewardController.cs   # 奖励筹码收集
│   ├── ProgressionManager.cs     # 成长系统（段位）
│   ├── AudioManager.cs       # 音效管理（autoload 单例）
│   ├── DogProverbs.cs        # Game Over 格言
│   ├── HUDController.cs      # HUD（消息/Overlay）
│   ├── TutorialManager.cs    # 新手引导弹跳
│   └── CardTableController.cs # 卡牌显示/保留/动画
├── Scripts/DataTables/       # Luban 生成的 C# 表类型（namespace DataTables）
│   ├── Tables.cs             # 所有表的访问入口
│   ├── PayTable.cs / TbPayTable.cs
│   ├── Item.cs / TbItem.cs
│   ├── DogSkin.cs / TbDogSkin.cs
│   ├── TabGroup.cs / TbTabGroup.cs
│   ├── GameDevelopConfig.cs / TbGameDevelopConfig.cs
│   ├── BlindBox.cs / TbBlindBox.cs 等盲盒表
│   └── 枚举：EItemType.cs / ERarity.cs / EHiddenRegionFlag.cs / EDogReactionTrigger.cs / EHandRank.cs / EBlindBoxType.cs
├── Scripts/Luban/            # Luban 运行时库
│   ├── BeanBase.cs
│   ├── SimpleJSON/
│   └── StringUtil.cs
├── Themes/
│   └── DefaultTheme.tres     # Duolingo 风格主题
├── project.godot
└── LuckyDogRise.csproj
```

## 数据驱动说明

所有道具和可替换资源的数据来自 Luban 表（Excel → JSON → C#），不硬编码路径。

**加载器：** `LubanData.Tables`（`Scripts/Desktop/LubanData.cs`）懒加载 `Data/Json/*.json`

**PSD 坐标定位：** 使用 `layer_index.json` 的 `doc_x/doc_y/width/height`，
以**参考物手调位置 + PSD 中心点差值**公式计算 `Sprite2D.Position`。
详细算法见 `docs/guide/psd-json-sprite-offset-guide.md`

**小狗表现：** `DogVisual` 通过 `DogReaction` 表应用眼睛、耳朵、舌头、爪子、眼镜等表现。`Bespoke` 行需要在代码中按行 id 约定特殊逻辑，不要硬改 Luban 生成的 C# 文件。

**桌宠输入状态：** `GlobalInputTracker` 监听全局键鼠事件，`ModeManager` 根据 `DesktopActivityState` 表统计输入频率并切换小狗表情。桌宠吐舌头支持平滑模式和即时模式，设置项由 `SettingsManager` 持久化。

**盲盒系统：** `BlindBoxService` 根据 `BlindBoxSchedule`、`BlindBox`、`BlindBoxRarityRate`、`BlindBoxRevealPath`、`BlindBoxVisual` 和 `Item` 权重列计算盲盒投放、消耗、品质、奖励与表演路径。`BlindBoxRevealStage.tscn` 是扑克模式和桌宠模式共用的开盒舞台；`BlindBoxRevealOverlay.tscn` 是扑克模式全屏外壳，`DesktopBlindBoxRevealOverlay.tscn` 是桌宠模式圆角气泡外壳。桌宠开盒外壳的位置由 `BossKeyContent.tscn` 中的 `ContentA/DesktopBlindBoxRevealAnchor` 作为 0 点参照。

盲盒奖励准备和本地展示节奏是两层逻辑。前 12 个非循环 `BlindBoxSchedule` 按顺序展示；Steam 库存可信后，客户端只激活当前需要的 PlaytimeGenerator：当前行为 Steam 行时激活当前行，当前行为本地消耗品时激活后面最近的一条 Steam 行。最后一条本地新手 Schedule 1012 会提前激活循环 Generator。所有激活、正式准备和重试共用 65 秒写入节流；正式到期请求优先。预热空回执不产生 Fallback；预热意外返回的装扮按实例暂扣到对应 Schedule。第 12 个奖励入账后进入普通循环，并按循环 Schedule 的 `IntervalSeconds` 产生展示点和独立 Trigger 心跳。所有 Schedule 时间字段均为真实秒；Steam 资格间隔只由该行 `SteamDropIntervalSeconds` 明确配置，转换器按分钟向上取整，不再应用等待倍率、提前量或整队列激活预算。运行时最多保留一笔未决平台写请求，并可保存一件因预热意外返回而暂扣的装扮。展示点有可信待揭晓装扮时锁定装扮盲盒，否则按当前 Schedule 的 `FallbackBlindBoxId` 锁定本地 Refreshment Fallback；锁定后不被后续库存变化替换。Schedule 1005 / 1012 通过 `SteamCompletionReceiptItemDefId` 分别写入第 5 / 第 12 个新手进度回执；可信库存同步只允许把本地新手进度向前恢复，不能覆盖已锁定气球或进行中的表演。

## 背包与存档

- 背包数据在 `PlayerInventory` 中维护，拥有状态使用 `Dictionary<int, int>` 表示 `itemId -> count`，支持重复道具堆叠显示。
- `ItemCell.tscn` 显示品质框、图标、`MarkNew`、装备中标记和数量角标。数量为 1 时不显示数量。
- `NewItemIds` 会写入存档。点击带 New 标记的已装备道具时，只清除 New，不立刻卸下装备。
- 装备位规则来自 `EquipmentSlotConfig`。`CanUnequip=False` 的槽位需要默认装备玩家已拥有道具；`CanUnequip=True` 的槽位允许空闲。
- `EquipmentSlotConfig` 中存在对应 `ItemType` 才表示该类型是装扮/可装备物品；`EItemType` 也可以包含活动令牌等普通库存类型。普通库存类型不得自动装备、写入装备存档或参与随机穿戴，也不要求在 PSD 导出的坐标 JSON 中存在记录。历史盲盒券 ItemDef 由当前客户端忽略。
- 新建/重置本地存档时，默认只拥有 `Item.AcquisitionType == Initial` 的道具。`调试全道具` 模式仍然拥有全部道具，不写入真实存档。
- 获得道具时，如果该道具所属槽位当前为空，会自动装备本次获得的道具；不会顺手补齐其它可空闲槽位。
- 相同道具可以重复获得并累计数量。`AcquisitionType=Initial` 的道具是永久基础物品，不进入盲盒奖池；未来的回收系统也不得消耗这类物品。
- 本地存档由 `SaveManager` 写入 `user://saves/profile_0.json`，同时维护 `profile_0.backup.json` 和损坏档 `profile_0.corrupt.json`。
- Playtest 包存档根目录：`C:\Users\carlo\AppData\Roaming\LuckyDogRise\Playtest\accounts\steam\<SteamID64>`；小号目录为 0275 结尾。
- V15 存档含 `Version`、`Chips`、`TotalPlaySeconds`、`OwnedItemCounts`、`EquippedItemIdsByType`、`NewItemIds`、`BlindBoxRuntimeState`、`PendingBlindBoxReward`、`PendingBlindBoxCompletionReceiptItemDefId`、`CreatedAt`、`UpdatedAt`；盲盒运行态包含未决准备、已确认待揭晓奖励和已锁定展示来源。
- 当前不保存单局牌局状态（手牌、弃牌/保留、牌堆等）。盲盒开盒中断状态会保存并恢复，包括当前盲盒、奖励、RevealStep 与奖励是否已展示。
- 盲盒倒计时每秒刷新 UI，但不应因此每秒写存档。普通存档变化使用 0.75 秒防抖，持续游玩时间每 60 秒保存一次快照；开盒资格消耗、奖励入账和待揭晓状态变化等关键节点立即保存。

## 术语约定

| 术语 | 说明 |
|------|------|
| **宿主窗口** | 唯一的 OS 窗口，透明+无边框+置顶+鼠标穿透。胖窗口设计 |
| **A 区** | 主内容区域。三种模式各不同：BossKey(狗)/Play(600×600 扑克)/Immersive(1200×1200 扑克) |
| **缓冲区** | A 区周围的预留空间，面板在此展开/收起，不改变窗口大小 |
| **面板** | 缓冲区中可独立显示/隐藏的 UI 控件，遵循显示器边缘避让 |

关系：**宿主窗口 = A 区 + 缓冲区 + 面板**。面板不是独立 OS 窗口，而是宿主窗口场景树中的节点。

## 面板避让算法

9 宫格编号按键盘顺序：
```
789
456
123
```

桌宠模式继续使用九宫格槽位，优先级链：
```
8(上中) → 9(上右) → 7(上左) → 6(右) → 4(左) → 2(下中) → 3(下右) → 1(下左)
```
桌宠模式改优先级只改 `BossKeyPanelSlotPriority` 数组顺序，不改 `GetPanelSlotPosition()`。兜底策略：所有完整槽位都不可用时，若 5 区覆盖 A 区后仍能完整显示标题栏、关闭按钮和顶部操作按钮，则继续使用 5 区；否则按 `2 → 1 → 3` 尝试允许底部超屏的槽位，并保证上述关键操作区可见；仍无法满足时以 2 区为基础夹紧关键操作区坐标。

桌宠模式的 4 宫和 6 宫以 `ContentA/TaskBar` 锚点作为面板底边，不使用整个缩放后 A 区的底边；这样高倍率下系统功能面板不会被额外向下推。

扑克模式不再遍历九宫格：
- 低倍率 `0.75x / 1x / 1.25x`：宿主窗口 `W = 信息面板 + A区 + 系统面板`，`H = A区 × 2`；右侧不足时优先把系统面板放到 A 区上方并右对齐，否则覆盖 A 区。
- 高倍率 `1.5x / 2x`：宿主窗口宽度同上，`H = A区`；右侧不足时系统面板覆盖 A 区并右对齐。
- 左侧出屏时先把信息面板移到 A 区右侧，再让系统面板覆盖 A 区并右对齐。拖拽时必须先更新信息面板换边，再计算系统面板位置。
- Debug Mock 可见时，宿主窗口顶部计入 Mock 面板实际高度，系统面板不得与 Mock 重叠，Mock 显隐后立即重排。
- 系统面板覆盖扑克 A 区时显示 50% 黑色遮罩；点击遮罩关闭系统面板。普通淡出路径中遮罩在 0.1 秒淡出结束后消失，立即关闭路径中二者同时隐藏。

## 卡牌编码

- 0-51 整数编码：`suit = card / 13` (0=Club,1=Diamond,2=Heart,3=Spade)，`rank = card % 13` (0=Ace,...,12=King)
- 文件名：`CardToString()` → `Club1` ~ `Spade13`（rank+1，美术资源从 1 开始）
- **保留/弃牌交互**：默认全保留（正面朝上），点击卡牌翻到背面（弃牌），再点击反悔翻回。补牌时背面的牌翻转变成新牌

## 透明窗口（桌宠）技术说明

**启用透明背景**：
- 项目设置 → Rendering → Viewport → Transparent Background → ON
- 编辑器 F5 运行时不支持透明窗口（显示棋盘格），需切到独立窗口模式运行
- 命令行独立运行：`godot --path lucky-dog-rise/ res://Scenes/ModeManager.tscn`

**Windows API**（lucky-dog-rise/Scripts/Desktop/WindowNative.cs）：
- `WS_EX_LAYERED` — per-pixel alpha 层叠窗口
- `WS_EX_TRANSPARENT` — 点击穿透（ModeManager._Process 动态开关）
- 不需要 `DwmExtendFrameIntoClientArea`（D3D12 下导致黑色背景）
- 置顶用 `SetWindowPos(hWnd, HWND_TOPMOST, ...)`

**点击穿透机制**（ModeManager._Process）：
- BossKey 模式：每帧检测鼠标是否在狗/按钮/面板/盲盒气球/盲盒表演气泡区域，动态开关 WS_EX_TRANSPARENT。视觉透明区域必须继续穿透，不可因临时模态 UI 让整个胖窗口拦截点击。
- 需要判断"是否点在游戏内容外"时，不要使用整个宿主窗口矩形。宿主窗口包含胖窗口缓冲区，透明空白区域应按非游戏内容处理。优先复用 `ModeManager.IsScreenPointOverInteractiveContent()`，该方法维护真实可交互内容命中区，并与点击穿透规则保持一致。
- 桌宠 Counter 状态栏会根据任务栏高度和舌头位置动态调整位置/高度，点击穿透命中区必须使用运行时实际 `Panel` 矩形，不要依赖场景里的固定按钮矩形。
- Play 模式：始终关闭穿透，保证游戏交互正常
- 拖拽时强制关闭穿透，松开后恢复

**全屏应用兼容**：
- 设置项 `全屏时显示` 默认开启。
- 关闭时，若检测到其它程序全屏，会隐藏 BossKey/Play 内容和 CanvasLayer UI，避免覆盖 3D 游戏。

**增强置顶**：
- 设置项 `增强置顶` 默认关闭。
- 开启后，仅在任务栏点击、Win 菜单收起后的短窗口期等特定场景高频 `SetWindowPos(HWND_TOPMOST, SWP_NOACTIVATE)`。
- Win 键处理规则：按下 Win 后等待；随后鼠标点击、再次 Win、Esc 会触发抢回置顶，并在短时间内监听下一次鼠标点击补抢。
- 平时不持续抢置顶，避免无意义打扰其它软件。

## Steam 平台与库存

- Steam ItemDef ID 分段规划的唯一权威来源是导出的 `lucky-dog-rise/Data/Json/tbsteamitemdefidrange.json`。修改 Steam schema 转换器、分配或校验 ItemDef ID、准备正式数据时，应直接读取该 JSON，不根据历史 ID、文字文档或编号习惯自行推断；编程阶段也不需要为此调用 Excel MCP。生成的 `SteamItemDefIdRange.cs` / `TbSteamItemDefIdRange.cs` 只是 Luban 运行时代码产物，不替代 JSON 作为转换器输入。
- 平台入口目前由 `GamePlatformServiceFactory` 创建，普通启动使用 `RecoveringSteamPlatformService`。后续 Steam 库存功能优先复用这套入口、连接恢复和回调泵，不在各业务模块内重复创建或持有 `SteamworksRuntime`。`SteamGamePlatformService` 负责单次已连接 Steam 会话内的 API、回调和 `SteamInventoryResult_t` Handle 生命周期。
- `IRecoverablePlatformService.ConnectionState` 是当前跨系统共享状态：`Offline / Connecting / InventorySyncing / Ready / Unavailable`。LinkTree、Steam 盲盒和其他平台功能订阅同一状态，避免各自形成含义不同的“是否在线”判断；库存归因还需同时检查 `PlatformInventoryTrustState`。
- 当前以 `Ready` 表示本次 Steam 库存已同步并可执行库存写操作。缓存库存适合用于展示；涉及消耗、兑换或发放时，应以服务器同步结果为准。
- Steam 初始化失败、客户端掉线或回调泵失效时，恢复层会在运行期间重建 `SteamworksRuntime`。库存请求和 Promo 发放请求使用 10 秒超时；失败后按 `5 → 15 → 30 → 60` 秒退避重试，60 秒封顶。
- 窗口重新获得焦点或玩家进入 LinkTree 时，可以通过 `RequestReconnect()` 提示恢复层加快检查；`Ready + Trusted` 时应静默忽略，已有同步请求进行中时复用当前请求，避免重复提交。
- Steam 库存请求目前按同一类型保留一个在途 Handle。处理 `SteamInventoryResult_t` 时需要校验 SteamID、读取结果并最终 `DestroyResult`；超时取消时也要回收对应 Handle。
- Steam 写操作采用“本地待处理事务 + Steam 结果复查”。回调丢失或断线后，先通过 `GetAllItems()` 确认服务器结果，再决定是否继续操作，避免直接重试造成重复发放或重复扣除。
- LinkTree 对 `SteamClaimBundleItemDefId` 调用 `AddPromoItem`，Bundle 中必须包含独立、永久且不销毁的 `SteamReceiptItemDefId` 作为一次性领取回执。启动同步时以回执恢复 `Claimed`；待处理事务同时保存 LinkTree、Bundle 和回执 ID，并在回调丢失或断线后通过完整库存复查。固定物品由 Bundle 直接写入 Steam 库存并同步到本地背包，客户端不得根据 `RewardItemId` 凭空发放；固定筹码只在回执确认后本地增加。单纯删除本地存档不得再次领取。
- Steam 盲盒与 LinkTree 共用平台服务、同步状态、重连机制和库存写入单飞保护。盲盒后台准备、LinkTree Promo 发放与完整库存复查不得交叉提交，避免库存差分串单。
- Steam 游玩投放由 `BlindBoxSchedule.SteamPlaytimeGeneratorItemDefId` 驱动，通过共享平台服务调用 `TriggerItemDrop`。PlaytimeGenerator 在 Steam 端递归展开装扮 Generator 并直接写入最终具体物品；客户端不生成、显示或消费盲盒券，也不在玩家点击时调用 Exchange。
- 对配置独立掉落参数的新 PlaytimeGenerator，真实 Steam 实测表明首次接受的 `TriggerItemDrop` 才建立该 Generator 的独立资格起点；上传时间、账号历史时长和首次调用前的游玩不能替代激活。客户端只预热当前需要的 Generator；当前 Schedule 为本地盲盒时，预热后面最近的一条 Steam Schedule，1012 则预热循环 Generator。激活状态、实例基线和意外返回奖励持久化。转换器直接把每行真实秒 `SteamDropIntervalSeconds` 向上取整为 Steam 分钟，不再从 `StartSeconds` 推导资格，也不扣除整队列预算或提前量。
- `BlindBoxSchedule.SteamCompletionReceiptItemDefId` 是粗粒度新手换机恢复检查点，不是盲盒奖励。当前 Schedule 1005 / 1012 分别映射永久隐藏回执 `500005` / `500012`；奖励真正领取后才通过 `AddPromoItem` 补交，待补交 ID 写入 V15 存档。回执事务与盲盒准备、LinkTree 领奖共用库存写入单飞保护。
- 提交 PlaytimeGenerator 前把 Generator、Schedule、BlindBox、提交时间和实例数量基线保存到 `PendingBlindBoxPreparation`。正常回调优先使用实际变化实例；回调丢失、超时或断联时通过可信完整库存差分复查。只有唯一且属于当前盲盒合法候选的增量才能进入 `PreparedBlindBoxReward`。
- `TriggerItemDrop` 成功但没有合法增量只表示本次没有确认奖励。系统遵守 65 秒公共节流重试；展示点没有可信准备奖励时立即锁定本地 Refreshment Fallback，玩家领取后完成当前新手 Schedule。已提交但结果未知的旧请求只做一次最终库存复查，不再重试已略过的 Generator；复查后确认的奖励标记为 `LateSteam`，只占用后续展示点，领取时不推进当前调度。旧事务结算后再按当前进度激活后续 Generator。
- `PlatformInventorySnapshot` 保存实例 ID、ItemDef 和数量，不能退化成仅记录 ItemDef 集合；重复装扮与堆叠物品的同步和事务复查都依赖实例级数据。Steam 返回的奖励必须映射到本地 `Item.SteamItemDefId`，并属于当前盲盒的表配置候选。
- Steam 获得的非 Initial 装扮物品以平台库存数量为准同步到本地背包。`AcquisitionType=RefreshmentBlindBox` 的消耗品属于本地奖励，即使表中暂时存在 `SteamItemDefId` 也不参与 Steam 数量对账。Steam 盲盒奖励在展示期间会从同步数量中暂扣一份，玩家完成领取表演后再加入本地背包，避免“库存同步一次 + 动画领取一次”造成重复计数。
- 展示点一旦锁定装扮、Schedule 本地盲盒或 Fallback，后续 Steam 状态变化不得替换当前气球。玩家点击只执行本地扣款、至少 500 毫秒反馈和既有表演，不发起 Steam 网络调用；Fallback 是正式必选分支，不提供关闭开关。
- Dev 启动器在平台服务与 `GameData` 初始化前选择“综合调试环境”或“Steam 模拟环境”。Steam 模拟环境不创建真实 Steam 会话，并从业务第一帧进入独立内存沙箱；顶部 Mock 面板可选择网络场景和“新手第 1 个盲盒/普通循环”测试起点。“推进到下一展示点”同时推进等量的本地调度时间、业务游玩时间和模拟 Steam 游玩时间；Steam Schedule 先走正式首次预热空回执，再按资格间隔与窗口限额准备奖励。本地 Schedule 不被预热阻塞，1012 会后台激活循环 Generator；需要验证预热直接发奖及暂扣交接时使用“强制快速成功”。
- Dev 的本地测试模式与启动级 Steam Mock 都必须隔离真实存档、Steam 库存、成就和玩家统计。沙箱复用正式开盒与奖励入账流程，退出或关闭游戏时恢复进入前的筹码、游玩时间、背包、装备、New、Buff 和统计。Debug 入口不持久化业务状态，Playtest/Release 不包含 Mock 场景、控制接口或模拟字符串。
- `Item.AcquisitionType=Initial` 的基础物品是永久本地权益，不参与 Steam 数量对账。存档加载时始终静默补齐 Initial 物品；即使这些行配置了 `SteamItemDefId`，Steam 缺少对应实例也不得删除基础物品、清空必选装备位或添加 New 标记。
- `--disable-steam` 是显式离线开发模式，不参与自动恢复。Dev 渠道可保留表现层模拟；Playtest/Release 在 Steam 库存不可用时采用失败关闭，避免回退为可重复领取的内存实现。

## 胖窗口设计

每种模式初始化时设定一次宿主窗口尺寸：
- **伪装模式**：265×235 基础 A 区乘桌宠倍率 + 固定面板缓冲；顶部缓冲还需覆盖桌宠盲盒表演向上的实际范围
- **游玩模式低倍率 `0.75x / 1x / 1.25x`**：`W = 信息面板 + A区 + 系统面板`，`H = A区 × 2`
- **游玩模式高倍率 `1.5x / 2x`**：宽度同上，`H = A区`；Debug Mock 可见时额外计入其实际顶部高度
- **沉浸模式**：1200×1200 全屏

面板展开/收起不改变窗口大小。模式切换时窗口 resize，保留屏幕位置（不重定位）。

桌宠倍率共五档：`0.5x / 1x / 2x / 3x / 4x`，默认 `1x`，对应小狗原素材最终倍率 `12.5% / 25% / 50% / 75% / 100%`。倍率只影响小狗、Counter、普通盲盒提示、桌宠开盒表演和 Rise 动画；420×600 系统功能面板不缩放。候选倍率只有在十秒确认中选择“保留”才写入设置。

从伪装模式切到扑克模式时，优先原地展开；若按当前其他 UI 倍率计算的信息面板与扑克 A 区会超出工作区，则移动宿主窗口，保证这两块实际内容留在屏幕内。无需强行保证整个胖窗口都在屏幕内。

**桌宠 Counter 布局**：
- `BossKeyContent.tscn` 的 `ContentA/TaskBar` 标记表示 Windows 任务栏上沿参考线。任务栏高度根据屏幕尺寸和可用工作区计算，不写死固定像素。
- 设置页 Display 组里的 `Center Counter on Taskbar` 只在“自动吸附任务栏”开启且桌宠当前确实吸附时生效；未吸附时使用场景中手调的原始位置/高度。
- Counter 尺寸严格跟随桌宠倍率，不因任务栏高度或舌头空间不足而缩小；发生冲突时只移动避让。
- 爪子和舌头视觉上应压在 Counter 前方；当前实现通过 Counter 自身避让舌头，而不是额外复制一层 DogArea 盖在状态栏上。
- 任务栏自动吸附只在桌宠模式生效。桌宠未吸附时，任务栏高度变化后恢复玩家最后一次拖拽确定的位置；Windows/DWM 先重排宿主窗口造成的一帧瞬时闪动是已接受边界。

## 系统面板与确认遮罩

- `SystemPanel.tscn` 是系统功能面板，含 Settings / Wardrobe / Debug 三个页签。
- Settings 页放玩家可见设置；Debug 页放开发/内测功能，例如数据来源、随机获得道具、随机狗/场景、狗反应测试。
- Settings 页的 Display 组负责桌宠显示类设置，包括 Counter 显示方式和 Counter 是否按任务栏高度居中。
- `背包数据来源` 属于 Debug 页：`调试全道具` 不读写真实存档，`本地存档` 读写 `profile_0.json`。
- 危险操作使用 `ConfirmOverlay.tscn`，不要使用 Godot 原生 `ConfirmationDialog`。原生 Dialog 会脱离面板、可拖出窗口，并且容易和透明窗口点击穿透冲突。
- `ConfirmOverlay` 覆盖系统面板区域，黑色 70% 遮罩，按钮区自动布局。

## 信号连接

ModeManager 持有设置面板实例，负责连接信号：
- `SettingsPanel.SwitchToPlayRequested` → `ModeManager.SwitchToPlay()`
- `SettingsPanel.SwitchToBossKeyRequested` → `ModeManager.SwitchToBossKey()`
- `SettingsPanel.RandomizeRequested` → `ModeManager.OnRandomizeScene()`
- `SettingsPanel.RandomizeDogRequested` → `ModeManager.OnRandomizeDog()`
- `SettingsPanel.RandomAcquireItemRequested` → `ModeManager.OnRandomAcquireItem()`
- `SettingsPanel.DogReactionRequested` → 当前模式的小狗执行对应 `EDogReactionTrigger`
- `InfoPanel.SettingsRequested` → `ModeManager.ToggleSettingsPanel()`
- `InfoPanel.BlindBoxRequested` → `ModeManager.OnBlindBoxRequested()`
- `GameManager.BlindBoxRewardClaimRequested` → `ModeManager.OnBlindBoxRewardClaimRequested()`

`GameData` 持有全局筹码和 `PlayerInventory`，并发出：
- `ChipsChanged`
- `EquipmentChanged`
- `InventoryChanged`
- `HandResolved`
- `NewHandStarted`
- `BlindBoxStateChanged`

## 场景工作流

- 新建场景用 `scene_create`（根节点类型+路径），生成的 .tscn 可直接用
- 添加/修改节点用 `Write` 直接改 .tscn 文本（`scene_add_node` 和 `scene_save` 不可靠）
- UI 结构定义在 .tscn 里，脚本不 `new` 节点建 UI 树
- 独立预制体：需要被多处引用或逻辑独立时 → 提取为独立 .tscn
- 测试场景：新技术方案先用独立场景验证，不直接嵌入主场景

## 节点引用规范

**优先用 `[Export]` 而非 `GetNode` 来引用场景节点。**

原因：`GetNode` 依赖节点树路径，场景结构调整（如嵌套容器、重命名）会直接导致运行时崩溃。`[Export]` 在场景文件（.tscn）中绑定的是节点引用，节点树结构调整后引用仍然有效。

做法：
- 在脚本中声明 `[Export] private NodeType _nodeName = null!;`
- 声明后**先编译**，编辑器才能识别新的 Export 字段
- 在 .tscn 文件中用 `_nodeName = NodePath("Target/Path")` 赋值，或在编辑器中拖拽绑定
- **必须确保每个 `[Export]` 字段都在 .tscn 中有对应的 NodePath 赋值**，否则运行时为 null
- `GetNode` 仅在以下场景保留使用：动态创建的子节点（如 `new Button()` 后需要引用），或临时查找（如 `GetParent()`）

## MCP 工作流

- Wick/Godot MCP 通用流程见 `.agents/codex-godot-wick-mcp/SKILL.md`；本项目补充见同目录 `LUCKY_DOG_RISE.md`。
- 常规 `.cs/.tscn` 修改仍优先直接读写文件并 `dotnet build`；`scene_get_tree` 主要用于校验场景树、节点类型和路径。
- `scene_add_node`、`scene_save` 不可靠，改 `.tscn` 时直接写文本；`.godot` 损坏时删 `.godot/` 后重开编辑器并重新 build。

## 构建与发布

- 构建渠道固定为 `Dev / Playtest / Release`。Dev 用于编辑器日常开发；Playtest/Release 使用渠道隔离、加密 PCK、存档 HMAC 和 C# 混淆。
- Codex 隔离终端构建时，先指定 Godot SDK 的 NuGet 缓存目录：
  ```powershell
  $env:NUGET_PACKAGES = "C:\Users\carlo\.nuget\packages"
  dotnet build .\lucky-dog-rise\LuckyDogRise.csproj --no-restore
  ```
- 可外发的 Playtest/Release 包必须使用 `lucky-dog-rise/Build/Build-WindowsPackage.ps1` 生成，不能把 Godot“项目 > 导出”的手工产物发给测试者或玩家。
- 当前本机 Godot 4.6.3 源码、自定义 Windows .NET Release 模板和密钥位于被忽略的 `.local-build/`；日常 Playtest 打包不重新编译模板。
- `.local-build/secrets.psd1` 不得提交、打印或发送。换电脑时恢复原密钥；不要随意重新生成，否则旧 HMAC 存档会失效。
- 新增 Godot 节点类时必须通过混淆保留列表检查；不能通过关闭混淆绕过。Godot 节点类的方法名需要保留，避免信号回调和 `CallDeferred` 失效。
- 通过字符串动态加载的新资源不会自动可靠进入导出包；必须同步维护导出 `include_filter` 或增加运行时构建检查。
- `data_LuckyDogRise_windows_x86_64` 是 Godot .NET 运行目录，必须与 EXE 一起分发并保持目录结构。
- Playtest 截止到香港时间 2026-09-08 结束，过期检查只用于限制普通转发；Dev/Release 不得继承该到期限制。
- 详细实现、命令、验收结果和未完成项见 `docs/steamworks/playtest-build-protection.md`；主人操作指南见 `docs/guide/playtest-packaging-for-owner.md`。

## 协作规则

- **先聊再写** — 主人提需求 → AI 调研 → 讨论 → 确认方向 → 写计划
- **复杂功能走 Plan Mode** — 审核通过才开代码

- **测试场景优先** — 新技术用独立场景验证，再嵌入主场景
- **不硬编码项目设置** — 窗口模式等通过 Godot 设置控制，不在代码里写死

- **UI 结构定义在 .tscn 里** — 脚本不 `new` 节点建 UI 树，而是引用预制好的场景或节点。程序化 new 出来的节点在编辑器中不可见，会导致主人无法手动调整位置和样式
- **用 Theme.tres 统一管理样式** — 非必要不推荐直接在节点上调整 Theme Overrides，方便后续整体换肤

## 音效工作流

- AI 在 `lucky-dog-rise/Audio/SFX/` 或 `lucky-dog-rise/Audio/BGM/` 创建 `.txt` 占位文件
- 代码优先用 `AudioManager.Instance.PlaySfx("CueName")` 播放逻辑 cue，不在调用处写具体变体号
- SFX 资源按 `CueName_1.ogg`、`CueName_2.ogg` 命名；AudioManager 在可用变体中随机选择，并优先 OGG
- 加密导出包不能依赖 `DirAccess` 枚举音频源文件名；变体通过 `ResourceLoader.Exists` 探测，修改时必须兼容导出环境
- 文件不存在时自动打印 `[SFX Missing] CueName` 或占位文件提示到控制台
- 用户看到输出后找音效文件替换 .txt
- cue 和文件名使用大驼峰或项目现有的下划线分段约定（如 `Card_PokerHandDeal_1.ogg`）
