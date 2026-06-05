# 协作模式与经验教训

## 设计师 + AI 程序员的协作模式

### 角色分工

- **设计师（用户）**：提出需求、测试反馈、调整参数、提供美术资源
- **AI 程序员（Claude Code）**：写代码、编译验证、修 bug、重构

### 沟通模式

1. **需求描述** — 设计师用自然语言描述功能，不需要写伪代码
2. **AI 先解释再动手** — 对于复杂改动，AI 先说明计划再执行
3. **截图反馈** — 设计师 F5 运行后截图，AI 用视觉模型分析问题
4. **逐步细化** — 先跑通核心流程，再调细节和美化

### 决策记录

重要设计决策记录在 GDD 的"设计决策日志"里，包含日期和理由。

## 开发节奏

### 推荐顺序

1. 核心玩法（牌组、发牌、换牌、判定）
2. 交互流程（点击、动画、反馈）
3. UI 美化（主题、面板、布局）
4. 音效和视觉细节
5. 打包发布

### 不推荐的做法

- 一开始就追求完美 UI
- 同时做多个大功能
- 不测试就继续开发

## 经验教训

### ✅ 验证有效的做法

**写 .tscn 文件代替 MCP 工具**
`scene_add_node` 和 `scene_save` 不可用时，直接用 Write 写 .tscn 文件是可靠的替代方案。Godot 编辑器会自动识别手动编辑的 .tscn。

**模块化拆分控制器**
GameManager 最初承担了所有职责（UI、Debug、动画、逻辑），后来拆分成 6 个控制器后代码清晰很多。

**AudioManager 对象池**
用 8 个 AudioStreamPlayer 的对象池，解决了"只播后半段"的问题（同一个播放器重复播放时状态没重置）。

**CheatDeck（从答案生成问题）**
先决定结果等级，再反推 5 张牌。让游戏体验更有戏剧性（频繁出现"差一点就赢"的感觉）。

**新手引导用 _UnhandledInput**
点击空白区域时弹跳正确的交互元素，不需要额外 UI，利用了 Godot 的输入事件传播机制。

**Debug 模式开关**
`DebugMode = true/false` 一行代码切换，按钮、消息面板、种子面板全部联动显示/隐藏。

### ❌ 踩过的坑

**project.godot 窗口设置不生效**
直接改 project.godot 的窗口大小可能被编辑器缓存覆盖。需要在编辑器里手动改 Project Settings。

**多个 Wick 进程冲突**
每次 Rewind 游戏会 spawn 新的 Wick 进程，导致端口冲突。解决：`kill-wick.bat` 清理。

**GD.LinearToDb 不存在**
Godot C# API 没有 `GD.LinearToDb`，需要手动计算：`20f * Mathf.Log(linear) / Mathf.Log(10f)`。

**Node2D 的 InputEvent 信号**
Node2D 的 `InputEvent` 信号需要 Area2D + CollisionShape2D 才能触发。改用透明 Button 覆盖区域更简单。

**卡牌命名 J/Q/K vs 11/12/13**
内部编码用 0-based（A=0, K=12），但文件名用 1-based（A=1, K=13）。`CardToString()` 需要 +1 转换。

**顺子检测 A-10-J-Q-K**
排序后 [0,9,10,11,12] 的 `9 ≠ 0+1` 导致直接 return false。需要单独匹配 A-high straight 模式。

**ZIndex 相对 vs 绝对**
默认 `z_as_relative = true`，子节点的 ZIndex 是相对于父节点的。需要设 `z_as_relative = false` 才能用绝对层级。

**音效首帧丢失**
`_Ready()` 里加载音频可能还没导入完成。解决：延迟加载或用 AudioManager 预加载。

## Debug 技巧

### 种子复现

1. 记下 Seed 数字
2. 重启游戏，在输入框填入种子
3. 下一手就是同样的牌

### 截图分析

```bash
python .claude/skills/local-vision/scripts/analyze_image.py \
  --image "inbox/screenshot.png" \
  --prompt-text "描述这个游戏界面的问题"
```

提示词要具体，视觉模型没有对话上下文。

### 控制台输出

关键节点用 `GD.Print()` 输出状态，方便定位问题：
```csharp
GD.Print($"[Deal] Card{i}: {card} -> {path}");
GD.Print("[SFX] 敲击");  // 音效占位
```

## 性能注意事项

- Tween 动画不要创建太多同时运行
- 音效对象池大小（8）根据实际需求调整
- 避免在 `_Process()` 里做重操作
- 场景实例化比动态创建节点更高效
