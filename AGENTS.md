# 偏好设置
写计划时不要采用子AGNET

宫格顺序按键盘顺序，而非手机顺序。
```
789
456
123
```



---

# Lucky Dog Pub - Godot 项目

## 项目概况

类《Bongo Cat》桌宠游戏，扑克部分仿电视扑克 Jacks or Better 规则。
使用 Godot 4.6 + C#
美术资产来自 PSD 1200x1200 画布导出，坐标系 1:1 对应。

## 技术栈

- **引擎**: Godot 4.6.3 Mono (C#)
- **IDE**: VS Code + Wick MCP 插件
- **语言**: C# (.NET 8.0)
- **项目工作空间**: `g:/Workspace/godot-project/lucky-dog-pub-2026/`
- **游戏项目路径**: `g:/Workspace/godot-project/lucky-dog-pub-2026/lucky-dog-rise/`

## 游戏目录结构

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

## 卡牌编码

- 0-51 整数编码：`suit = card / 13` (0=Club,1=Diamond,2=Heart,3=Spade)，`rank = card % 13` (0=Ace,...,12=King)
- 文件名：`CardToString()` → `Club1` ~ `Spade13`（rank+1，美术资源从 1 开始）
- **保留/弃牌交互**：默认全保留（正面朝上），点击卡牌翻到背面（弃牌），再点击反悔翻回。补牌时背面的牌翻转变成新牌

## 透明窗口（桌宠）技术说明

**启用透明背景**：
- 项目设置 → Rendering → Viewport → Transparent Background → ON
- 编辑器 F5 运行时不支持透明窗口（显示棋盘格），需切到独立窗口模式运行
- 命令行独立运行：`godot --path lucky-dog-rise/ res://Scenes/TestDesktop.tscn`

**Windows API**（lucky-dog-rise/Scripts/Desktop/WindowNative.cs）：
- `WS_EX_LAYERED` — per-pixel alpha 层叠窗口
- `WS_EX_TRANSPARENT` — 点击穿透（动态开关：鼠标在交互区时移除，移出时恢复）
- 不需要 `DwmExtendFrameIntoClientArea`（D3D12 下导致黑色背景）
- 置顶用 `SetWindowPos(hWnd, HWND_TOPMOST, ...)`

**点击穿透机制**（TestDesktopController）：
- `_Process` 每帧检测鼠标位置
- 鼠标在交互区 → 移除 `WS_EX_TRANSPARENT` → 点击被窗口捕获
- 鼠标在透明区 → 添加 `WS_EX_TRANSPARENT` → 点击穿透到桌面
- 拖拽时强制关闭穿透，松开后恢复

## 场景工作流

- 新建场景用 `scene_create`（根节点类型+路径），生成的 .tscn 可直接用
- 添加/修改节点用 `Write` 直接改 .tscn 文本（`scene_add_node` 和 `scene_save` 不可靠）
- 改节点属性：简单属性用 `scene_set_node_properties`，复杂结构直接改 .tscn
- 独立预制体：当某个节点需要被多处引用或逻辑独立时→提取为独立 .tscn（如 Card.tscn, DogClaw.tscn）
- 测试场景：新技术方案先用独立场景验证（如 TestDesktop.tscn），不直接嵌入主场景

## 节点引用规范

**优先用 `[Export]` 而非 `GetNode` 来引用场景节点。**

原因：`GetNode` 依赖节点树路径，场景结构调整（如嵌套容器、重命名）会直接导致运行时崩溃。`[Export]` 在场景文件（.tscn）中绑定的是节点引用，节点树结构调整后引用仍然有效。

做法：
- 在脚本中声明 `[Export] private NodeType _nodeName = null!;`
- 在 .tscn 文件中用 `_nodeName = NodePath("Target/Path")` 赋值，或在编辑器中拖拽绑定
- **必须确保每个 `[Export]` 字段都在 .tscn 中有对应的 NodePath 赋值**，否则运行时为 null
- `GetNode` 仅在以下场景保留使用：动态创建的子节点（如 `new Button()` 后需要引用），或临时查找（如 `GetParent()`）

## MCP 坑

- `scene_add_node` 不可用 → Write 直接改 .tscn
- `scene_save` 超时 → 同上
- 改 project.godot 窗口设置可能不生效 → 用户在编辑器手动改
- Wick 进程会堆积：`kill-wick.bat` 清理
- `.godot` 损坏：删掉 `.godot/` → 重开编辑器 → 重新 build

## 协作规则

- **先聊再写** — 主人提需求 → AI 调研 → 讨论 → 确认方向 → 写计划
- **复杂功能走 Plan Mode** — 审核通过才开代码
- **一次只做一个** — 不并行堆砌改动
- **测试场景优先** — 新技术用独立场景验证，再嵌入主场景
- **不硬编码项目设置** — 窗口模式等通过 Godot 设置控制，不在代码里写死
- **迭代反馈** — 每次改动后主人 F5 测试 → 反馈效果 → AI 调整
- **UI 结构定义在 .tscn 里** — 脚本不 `new` 节点建 UI 树，而是引用预制好的场景或节点。程序化 new 出来的节点在编辑器中不可见，会导致主人无法手动调整位置和样式
- **用 Theme.tres 统一管理样式** — 不直接在节点上调整 Theme Overrides，方便后续整体换肤

## 音效工作流

- AI 在 `lucky-dog-rise/Audio/SFX/` 或 `lucky-dog-rise/Audio/BGM/` 创建 `.txt` 占位文件
- 代码用 `AudioManager.Instance.PlaySfxByName("Xxx.wav")` 播放
- 文件不存在时自动打印 `[SFX] xxx` 到控制台
- 用户看到输出后找音效文件替换 .txt
- **文件名必须大驼峰**（如 `Knock.wav`）
