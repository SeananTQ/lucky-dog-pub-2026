# 偏好设置

---

# Lucky Dog Pub - Godot 项目

## 项目概况

类《Bongo Cat》桌宠游戏，扑克部分仿电视扑克 Jacks or Better 规则。
使用 Godot 4.6 + C#
美术资产来自 PSD 1200x1200 画布导出，坐标系 1:1 对应。

## 当前实现状态

- 桌宠模式：已支持打字/点击统计、输入加筹码、舌头反馈、根据 `DesktopActivityState` 切换小狗表情、启动 Rise 动画、桌宠气泡盲盒提示与开盒表演。
- 小狗视觉：已迁移到 v1 资源和 `DogReaction` 数据驱动，旧硬编码入口仅保留 TODO 待清理。
- 背包：已支持分页、空分页提示、数量堆叠、New 标记、装备/卸下、可空闲装备位。
- 存档：已支持本地 JSON 存档、版本号、缺字段兜底、损坏备份、重置存档确认。
- 调试：Debug 页支持随机狗、随机场景、随机获得道具、播放狗反应、切换背包数据来源、盲盒状态调试与时间/成本倍率。
- 盲盒：已支持资格生成与本地展示节奏分离、12 个新手盲盒顺序领取、双循环轨道积压与优先级、开盒升品表演、桌宠/扑克两种开盒外壳、中断恢复与自动领奖。
- 待开发：LinkTree 免费领取、重复补偿/分解、正式 Steam 库存 API 接入等。

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

盲盒资格生成和本地展示节奏是两层逻辑。`BlindBoxSchedule.StartSeconds` 是调度生效下限；循环行的 `IntervalSeconds` 是单条轨道的资格间隔，不是全局领取 CD，非循环行的 `IntervalSeconds` 则是相对上一次实际领取的最短本地间隔。12 个新手盲盒未领完时，本地只显示新手顺序，但正式循环轨道仍在后台生成并按表中上限保留资格。第 12 个新手奖励实际进入背包后，使用 `GameDevelopConfig.BlindBoxPostNewbieDelaySeconds` 控制首个正式盲盒；领取正式盲盒后仍有积压时，使用 `BlindBoxBacklogClaimDelaySeconds` 控制下一次展示。所有基础秒数统一受 `BlindBoxWaitDurationMultiplier` 缩放，多资格积压时按 `BlindBoxSchedule.Priority` 选择。玩家真实游玩时间与盲盒调度时钟分离；倍率只改变调度时钟之后的流速，不能用 `TotalPlaySeconds / 当前倍率` 反推整个历史时间轴。

## 背包与存档

- 背包数据在 `PlayerInventory` 中维护，拥有状态使用 `Dictionary<int, int>` 表示 `itemId -> count`，支持重复道具堆叠显示。
- `ItemCell.tscn` 显示品质框、图标、`MarkNew`、装备中标记和数量角标。数量为 1 时不显示数量。
- `NewItemIds` 会写入存档。点击带 New 标记的已装备道具时，只清除 New，不立刻卸下装备。
- 装备位规则来自 `EquipmentSlotConfig`。`CanUnequip=False` 的槽位需要默认装备玩家已拥有道具；`CanUnequip=True` 的槽位允许空闲。
- 新建/重置本地存档时，默认只拥有 `Item.AcquisitionType == Initial` 的道具。`调试全道具` 模式仍然拥有全部道具，不写入真实存档。
- 获得道具时，如果该道具所属槽位当前为空，会自动装备本次获得的道具；不会顺手补齐其它可空闲槽位。
- `IsUnique=True` 的道具已拥有后不应重复获得；`IsUnique=False` 可重复获得并堆叠数量。
- 本地存档由 `SaveManager` 写入 `user://saves/profile_0.json`，同时维护 `profile_0.backup.json` 和损坏档 `profile_0.corrupt.json`。
- 存档含 `Version`、`Chips`、`TotalPlaySeconds`、`OwnedItemCounts`、兼容旧档的 `OwnedItemIds`、`EquippedItemIdsByType`、`NewItemIds`、`BlindBoxRuntimeState`、`PendingBlindBoxReward`、`CreatedAt`、`UpdatedAt`。
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

面板避让优先级在 `ModeManager.cs` 顶部拆成两套数组：
- `BossKeyPanelSlotPriority`：伪装模式系统功能面板优先级
- `PlayPanelSlotPriority`：扑克模式系统功能面板优先级

默认优先级链：
```
6(右) → 8(上中) → 9(上右) → 7(上左) → 4(左) → 2(下中) → 3(下右) → 1(下左)
```
改优先级只改对应数组顺序，不改 `GetPanelSlotPosition()`。兜底策略：所有槽位不可用时覆盖在 A 区中央。

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

## 胖窗口设计

每种模式初始化时设定一次宿主窗口尺寸：
- **伪装模式**：265×235 A 区 + 2×面板尺寸缓冲
- **游玩模式**：840×600（信息面板+游戏面板）+ 2×面板尺寸缓冲
- **沉浸模式**：1200×1200 全屏

面板展开/收起不改变窗口大小。模式切换时窗口 resize，保留屏幕位置（不重定位）。

从伪装模式切到扑克模式时，优先原地展开；若 840×600 的扑克内容区会超出屏幕，则移动宿主窗口，保证扑克内容区留在屏幕内。无需强行保证整个胖窗口都在屏幕内。

**桌宠 Counter 布局**：
- `BossKeyContent.tscn` 的 `ContentA/TaskBar` 标记表示 Windows 任务栏上沿参考线。任务栏高度根据屏幕尺寸和可用工作区计算，不写死固定像素。
- 设置页 Display 组里的 `Center Counter on Taskbar` 控制 Counter 是否按 Windows 任务栏高度居中；关闭时使用场景中手调的原始位置/高度。
- 居中后如果 Counter 会挡住狗舌头，优先把 Counter 的 Y 上限压到舌头下方；剩余空间不足时缩小 Counter 高度。
- 缩小 Counter 高度时必须同时减少 `Panel` 样式的 `ContentMarginTop` 和 `ContentMarginBottom`，否则容器最小高度会把面板撑回去。
- 爪子和舌头视觉上应压在 Counter 前方；当前实现通过 Counter 自身避让舌头，而不是额外复制一层 DogArea 盖在状态栏上。

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
- 可外发的 Playtest/Release 包必须使用 `lucky-dog-rise/Build/Build-WindowsPackage.ps1` 生成，不能把 Godot“项目 > 导出”的手工产物发给测试者或玩家。
- 当前本机 Godot 4.6.3 源码、自定义 Windows .NET Release 模板和密钥位于被忽略的 `.local-build/`；日常 Playtest 打包不重新编译模板。
- `.local-build/secrets.psd1` 不得提交、打印或发送。换电脑时恢复原密钥；不要随意重新生成，否则旧 HMAC 存档会失效。
- 新增 Godot 节点类时必须通过混淆保留列表检查；不能通过关闭混淆绕过。Godot 节点类的方法名需要保留，避免信号回调和 `CallDeferred` 失效。
- 通过字符串动态加载的新资源不会自动可靠进入导出包；必须同步维护导出 `include_filter` 或增加运行时构建检查。
- `data_LuckyDogRise_windows_x86_64` 是 Godot .NET 运行目录，必须与 EXE 一起分发并保持目录结构。
- Playtest 截止到香港时间 2026-08-11 结束，过期检查只用于限制普通转发；Dev/Release 不得继承该到期限制。
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
