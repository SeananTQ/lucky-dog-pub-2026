# 开发工作流：Claude Code + Wick MCP + C#

## 架构概览

```
Claude Code (AI 程序员)
    ↕ MCP 协议 (stdio)
Wick MCP Server (.NET 进程)
    ↕ TCP JSON-RPC (端口 6505)
Godot 编辑器插件 (GDScript)
    ↕ TCP (端口 7777)
游戏运行时 (可选)
```

## 日常开发循环

### 典型的一次功能开发

1. **描述需求** — 用自然语言告诉 Claude Code 要做什么
2. **Claude 写代码** — 直接用 Write/Edit 工具创建/修改 .cs 文件
3. **编译验证** — Claude 调用 `dot_net_build` 检查编译错误
4. **修错循环** — 有错误则读取诊断信息，修复，重新编译
5. **场景修改** — 直接用 Write 工具编辑 .tscn 文件
6. **用户测试** — 在 Godot 编辑器里 F5 运行
7. **截图反馈** — 用户截图发给 Claude，用 local-vision 技能分析

### 关键工具使用

| 操作 | 工具 | 说明 |
|------|------|------|
| 创建/修改 C# 脚本 | Write / Edit | 直接操作文件系统 |
| 编译检查 | `dot_net_build` | 返回 Roslyn 增强的错误诊断 |
| 创建场景 | `scene_create` | 创建 .tscn 骨架 |
| 读取场景树 | `scene_get_tree` | 纯文件读取，不需要 Godot 运行 |
| 编辑器连接 | `editor_status` / `editor_connect` | 检查/恢复 MCP 连接 |
| 截图分析 | local-vision 技能 | 调用本地 LM Studio 视觉模型 |

### MCP 工具限制与绕过

| 工具 | 状态 | 绕过方案 |
|------|------|---------|
| `scene_create` | ✅ 可用 | — |
| `scene_add_node` | ❌ 不可用 | 用 Write 直接写 .tscn |
| `scene_save` | ❌ 超时 | 用 Write 直接写 .tscn |
| `scene_get_tree` | ✅ 可用 | — |
| `editor_status/connect` | ✅ 可用 | — |
| `dot_net_build` | ✅ 可用 | — |

## .tscn 文件手动编写要点

### 基本结构

```gdscene
[gd_scene load_steps=N format=3 uid="uid://xxx"]

[ext_resource type="Script" path="res://Scripts/Xxx.cs" id="1_script"]
[ext_resource type="Texture2D" path="res://Assets/Xxx.png" id="2_tex"]
[ext_resource type="PackedScene" path="res://Scenes/Xxx.tscn" id="3_scene"]

[sub_resource type="StyleBoxFlat" id="StyleBoxFlat_xxx"]
bg_color = Color(1, 1, 1, 1)

[node name="Root" type="Node2D"]
script = ExtResource("1_script")

[node name="Child" type="Sprite2D" parent="."]
position = Vector2(100, 200)
texture = ExtResource("2_tex")
```

### 场景实例化

```gdscene
[ext_resource type="PackedScene" path="res://Scenes/DogArea.tscn" id="4_dog"]

[node name="DogArea" parent="." instance=ExtResource("4_dog")]
```

### 注意事项

- `load_steps` 是 ext_resource + sub_resource 总数（+1 也行，不严格）
- `uid` 字段 Godot 编辑器会自动生成，手写时可以省略
- `unique_id` 同上，编辑器会自动补
- 修改 .tscn 后需要在编辑器里重新打开场景才能看到变化

## 常见问题排查

### MCP 连接断开

```bash
# 检查进程
tasklist | grep -i wick

# 杀掉所有 Wick 进程
taskkill /F /IM Wick.Server.exe

# 在 Claude Code 里重启
/mcp
```

### 编译失败

1. 读取 `dot_net_build` 返回的诊断信息
2. 定位文件路径和行号
3. 修复代码，重新编译
4. 常见错误：
   - CS0117：API 名称错误（如 `GD.LinearToDb` 不存在）
   - CS1061：信号连接方式错误（Node2D 用 `Connect` 而非 `+=`）
   - CS0246：命名空间或类型找不到

### 运行时崩溃

1. 查看 Godot 编辑器底部 **Output** 面板
2. 找到错误信息和调用栈
3. 常见原因：
   - GetNode 路径错误（节点不存在）
   - 空引用（资源加载失败）
   - 信号连接的目标方法签名不匹配

### 窗口大小不生效

`project.godot` 里改窗口设置可能不生效。需要在编辑器里手动改：
**Project → Project Settings → General → Window → Size**

### 多个 Wick 进程

每次 Rewind 可能产生多余的 Wick 进程。用 `kill-wick.bat` 清理。

## 代码组织原则

### 模块化拆分

| 控制器 | 职责 |
|--------|------|
| GameManager | 游戏状态机 + 逻辑编排（不直接操作 UI） |
| HUDController | 按钮/标签/消息/Overlay |
| DebugHUDController | 种子面板、debug 模式切换 |
| TutorialManager | 新手引导弹跳 |
| CardTableController | 卡牌显示/保留/动画 |
| AudioManager | 音效/BGM 播放（autoload 单例） |

### 场景拆分

| 场景 | 内容 |
|------|------|
| Main.tscn | 主场景，实例化所有子场景 |
| DogArea.tscn | 小狗（Head/Eyes/Ears/Claw/Eyewear/Headwear） |
| HandArea.tscn | 手臂（Hand/Clothes/Accessory/HitButton） |
| ChipStack.tscn | 筹码堆（Sprite + 点击按钮） |
| ChipReward.tscn | 奖励筹码（收集动画） |

### 信号连接模式

```csharp
// 方式 1：C# 事件（推荐，用于自定义信号）
_dogVisual.DogClicked += OnDogClicked;

// 方式 2：Godot Connect（用于引擎信号或跨节点调用）
_hud.ConnectDeal(this, nameof(OnDealPressed));

// 方式 3：Lambda
_button.Pressed += () => DoSomething();
```
