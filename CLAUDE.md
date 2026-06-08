# Lucky Dog Pub - Godot 项目

## 项目概况

电视扑克（Jacks or Better）小游戏，使用 Godot 4.6 + C#。验证 Godot MCP 工作流。
美术资产来自 PSD 1200x1200 画布导出，坐标系 1:1 对应。

## 技术栈

- **引擎**: Godot 4.6.3 Mono (C#)
- **IDE**: VS Code + Wick MCP 插件
- **语言**: C# (.NET 8.0)
- **项目路径**: `g:/Workspace/godot-project/lucky-dog-pub-2026/lucky-dog-rise/`

## 目录结构

```
lucky-dog-rise/
├── Assets/              # PSD 导出的 PNG 素材（不要重命名）
│   ├── Card/            # 52 张牌 (Club1-13, Diamond1-13, ...)
│   ├── Shiba/Red/       # 小狗素材（Head, Eyes, Ears, Claw, Eyewear, Headwear）
│   ├── Chip/            # 筹码
│   ├── Table/           # 牌桌
│   ├── Background/      # 背景
│   ├── Hand/            # 手臂
│   ├── Clothes/         # 衣服
│   ├── Eyewear/         # 眼镜
│   ├── Headwear/        # 帽子
│   ├── Accessory/       # 饰品
│   └── layer_index.json # PSD 层坐标数据
├── Audio/
│   ├── BGM/             # 背景音乐（OGG 格式）
│   └── SFX/             # 音效文件（大驼峰命名，如 Knock.wav）
├── Scenes/
│   ├── Main.tscn        # 主场景（扑克游戏）
│   ├── TestDesktop.tscn # 透明窗口测试场景（狗头缩小25%）
│   ├── DogArea.tscn     # 小狗场景（表情系统）
│   ├── HandArea.tscn    # 手臂场景（敲桌交互）
│   ├── ChipStack.tscn   # 筹码堆场景（下注交互）
│   ├── ChipReward.tscn  # 奖励筹码场景（收集动画）
│   └── Prefabs/         # 小组件预制体
│       └── DogClaw.tscn # 狗爪子（手心/手背切换）
├── Scripts/
│   ├── Desktop/             # 桌宠宿主窗口系统
│   │   ├── WindowNative.cs        # Windows API P/Invoke（user32.dll, dwmapi.dll）
│   │   ├── WindowManager.cs       # 宿主窗口管理器（透明/置顶/层叠窗口）
│   │   ├── DragHandler.cs         # 窗口拖拽控制器
│   │   ├── PanelPositioner.cs     # 智能定位计算器（面板左>右>上>下）
│   │   ├── SettingsPanelController.cs  # 设置面板控制器
│   │   ├── SettingsManager.cs     # 音效设置持久化（ConfigFile）
│   │   └── TestDesktopController.cs    # 透明窗口测试场景控制器
│   ├── GameManager.cs       # 游戏状态机 + 逻辑编排
│   ├── DeckManager.cs       # 牌组管理（CheatDeck）
│   ├── CardEvaluator.cs     # 牌型判定（纯静态）
│   ├── DogHintSystem.cs     # 小狗提示逻辑
│   ├── DogVisual.cs         # 小狗视觉表现
│   ├── HandAreaController.cs    # 手臂交互+敲桌动画
│   ├── ChipStackController.cs   # 筹码堆交互
│   ├── ChipRewardController.cs  # 奖励筹码收集
│   ├── ProgressionManager.cs    # 成长系统（段位）
│   ├── AudioManager.cs      # 音效管理（autoload 单例）
│   ├── DogProverbs.cs       # Game Over 格言
│   ├── HUDController.cs     # UI 管理（按钮/标签/消息/Overlay）
│   ├── DebugHUDController.cs # Debug 面板（种子显示/随机换装）
│   ├── TutorialManager.cs   # 新手引导弹跳
│   └── CardTableController.cs # 卡牌显示/保留/动画
├── Themes/
│   └── DefaultTheme.tres # Duolingo 风格主题
├── project.godot
└── LuckyDogRise.csproj
```

## 术语约定

| 术语 | 说明 |
|------|------|
| **宿主窗口** | 唯一的 OS 级窗口，透明+无边框+置顶+鼠标穿透。DisplayServer + Windows API 操作 |
| **游戏面板** | 宿主窗口内的扑克内容区域，始终可见，是宿主窗口的"锚点" |
| **设置面板** | 宿主窗口内的 UI 控件，从游戏面板边缘弹出，智能定位（左>右>上>下） |

关系：**宿主窗口 > { 游戏面板, 设置面板, 商店面板, ... }**。面板不是独立 OS 窗口，而是宿主窗口场景树中的 Control 节点。

## 游戏流程

```
WaitingForBet → 点 ChipStack → -5 筹码 → 发牌
Dealt → 点卡牌保留/弃牌 → Holding
Holding → 点狗给提示（一次性）→ 改保留牌 → 狗自动戴墨镜
Dealt/Holding → 点手臂（敲桌）/ DRAW 按钮 → 补牌动画
Drawing → 结算
  ├─ 赢了 → 出现奖励筹码 → 点筹码收集 → +payout → 自动发下一局
  └─ 没赢 → ChipStack 提示再点 → 回到 WaitingForBet
GameOver → 筹码归零 → 黑屏格言 → RESTART
```

## 状态机

`GameManager.State`:
- `WaitingForBet` — 等待玩家下注
- `Dealt` — 已发牌，等待选牌
- `Holding` — 玩家已选保留
- `Drawing` — 补牌中
- `Settled` — 结算中（赢了等收集筹码）
- `GameOver` — 游戏结束

## ZIndex 层级

| 元素 | ZIndex | 说明 |
|------|--------|------|
| Background | 0 | 最底层 |
| 狗身体 | 1 | Head/Eyes/Ears |
| 爪子手心 | 1 | 被桌子挡住 |
| 桌子 | 2 | Table |
| 爪子手背 | 3 | 挡住桌子 |
| 卡牌/筹码堆 | 4 | 在桌子上 |
| 手臂 | 5 | 最顶层 |

爪子设 `z_as_relative = false` 用绝对 ZIndex。

## 透明窗口（桌宠）技术说明

**启用透明背景的方法**（需要用户在编辑器手动设置）：
1. 项目设置 → Rendering → Viewport → Transparent Background → ON
2. 编辑器 F5 运行时不支持透明窗口（显示棋盘格+提示），需切到独立窗口模式运行
3. 或在命令行独立运行 `godot --path lucky-dog-rise/ res://Scenes/TestDesktop.tscn`

**Windows API 关键样式**（lucky-dog-rise/Scripts/Desktop/WindowNative.cs）：
- `WS_EX_LAYERED` — 启用 per-pixel alpha 层叠窗口
- `WS_EX_TRANSPARENT` — 点击穿透（动态开关：鼠标在交互区时移除，移出时恢复）
- 不需要 `DwmExtendFrameIntoClientArea`（在 D3D12 下导致黑色背景）
- 置顶用 `SetWindowPos(hWnd, HWND_TOPMOST, ...)`

**点击穿透机制**（lucky-dog-rise/Scripts/Desktop/TestDesktopController.cs）：
- `_Process` 每帧检测鼠标位置（`DisplayServer.MouseGetPosition()`）
- 鼠标在交互区（狗/按钮）→ 移除 `WS_EX_TRANSPARENT` → 点击被窗口捕获
- 鼠标在透明区 → 添加 `WS_EX_TRANSPARENT` → 点击穿透到桌面
- 拖拽时强制关闭穿透，松开后根据鼠标位置恢复

## MCP 工作流注意事项

- `scene_create` 可用，`scene_add_node` 不可用
- `scene_save` 超时，用 Write 直接写 `lucky-dog-rise/Scenes/*.tscn` 文件
- 改 `lucky-dog-rise/project.godot` 的窗口设置可能不生效，需用户在编辑器里手动改
- 每次 Rewind 可能产生多余 Wick 进程，用 `kill-wick.bat` 清理
- `lucky-dog-rise/.godot` 文件夹损坏时：删除 `.godot/`，重新打开编辑器，然后重新 build

## AI 协作工作流

1. **先讨论再写计划** — 主人提出需求 → AI 调研/分析 → 双方讨论方案 → 确认方向后写实施计划
2. **复杂功能走 Plan Mode** — 读代码 → 出计划 → 主人审核 → 批准后才开代码
3. **一个问题一个任务** — 不并行堆叠多个改动，逐个完成再推进下一个
4. **测试场景优先** — 新技术方案（如透明窗口）先用独立测试场景验证，不直接嵌入主场景
5. **不硬编码项目设置** — 窗口模式（无边框/全屏等）通过项目设置控制，不在代码里写死，方便调试
6. **TSCN 手动编辑** — MCP 场景工具（scene_save/scene_add_node）不可靠，改场景直接写 .tscn 文本
7. **迭代反馈** — 每次改动后主人 F5 测试 → 反馈效果 → AI 调整

## 音效工作流

1. AI 在 `lucky-dog-rise/Audio/SFX/` 或 `lucky-dog-rise/Audio/BGM/` 创建 `.txt` 占位文件
2. 代码用 `AudioManager.Instance.PlaySfxByName("Xxx.wav")` 播放
3. 文件不存在时自动打印 `[SFX] xxx` 到控制台
4. 用户看到输出后找音效文件替换 .txt
5. **文件名必须大驼峰**（如 `Knock.wav`）

## 卡牌编码

- 0-51 整数编码：`suit = card / 13` (0=Club,1=Diamond,2=Heart,3=Spade)，`rank = card % 13` (0=Ace,...,12=King)
- 文件名：`CardToString()` → `Club1` ~ `Spade13`（rank+1，美术资源从 1 开始）
- **保留/弃牌交互**：默认全保留（正面朝上），点击卡牌翻到背面（弃牌），再点击反悔翻回。补牌时背面的牌翻转变成新牌

## 小狗系统

- 4 档信号：Bored / Happy / LuckyEye / TopTier
- 狗根据**补牌后的实际结果**给提示（不是预设等级）
- 给提示后改保留牌 → 自动戴墨镜锁定
- 默认表情：Eyes_Cute + Ears_Happy + 双手手背
- 爪子切换：`ShowClawPalm()`（手心）/ `ShowClawBack()`（手背）
- 摇手拒绝：`ShakePaw()`（锁定后再点狗触发）
