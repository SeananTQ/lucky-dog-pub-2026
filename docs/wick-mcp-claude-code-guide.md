# Claude Code + Wick MCP + C# 通用开发指南

## 工具链

| 工具 | 角色 |
|------|------|
| Godot 4.6+ Mono | 游戏引擎 |
| Claude Code | AI 程序员（写代码、编译、调试） |
| Wick MCP | 桥接层（AI ↔ Godot 编辑器） |
| VS Code | 编辑器（可选） |

## 架构

```
Claude Code (AI)
    ↕ MCP 协议 (stdio)
Wick MCP Server (.NET 进程)
    ↕ TCP JSON-RPC (端口 6505)
Godot 编辑器插件 (GDScript)
    ↕ TCP (端口 7777)
游戏运行时（可选）
```

## 安装步骤

### 1. Godot 插件

将 `addons/wick/` 复制到 Godot 项目目录，编辑器里启用：
**Project → Project Settings → Plugins → Wick → Enable**

### 2. MCP Server 构建

```bash
cd Wick/src/Wick.Server
dotnet build --configuration Release
```

如有 NU1902 警告：在 `Directory.Build.props` 加 `<NoWarn>$(NoWarn);NU1902</NoWarn>`

### 3. Claude Code 配置

项目根目录创建 `.mcp.json`：

```json
{
  "mcpServers": {
    "wick": {
      "command": "dotnet",
      "args": ["run", "--project", "绝对路径/Wick/src/Wick.Server"],
      "env": {
        "WICK_GROUPS": "all",
        "WICK_GODOT_BIN": "绝对路径/Godot可执行文件",
        "WICK_PROJECT_PATH": "绝对路径/你的Godot项目"
      }
    }
  }
}
```

## MCP 工具能力表

### ✅ 可用

| 工具 | 用途 |
|------|------|
| `editor_status` | 检查编辑器/运行时连接 |
| `editor_connect` | 强制重连 |
| `editor_scene_tree` | 获取当前打开的场景树 |
| `editor_node_properties` | 获取节点属性 |
| `editor_set_property` | 设置节点属性 |
| `editor_call_method` | 调用节点方法 |
| `scene_create` | 创建新 .tscn 文件 |
| `scene_get_tree` | 读取 .tscn 的场景树（纯文件读取） |
| `scene_get_node_properties` | 读取节点属性（纯文件读取） |
| `dot_net_build` | 编译 C# 项目 |
| `dot_net_clean` | 清理构建产物 |
| `c_sharp_analyze` | 分析 C# 文件结构 |
| `c_sharp_find_symbol` | 搜索符号定义 |
| `c_sharp_find_references` | 查找符号引用 |

### ❌ 不可用（需绕过）

| 工具 | 问题 | 绕过方案 |
|------|------|---------|
| `scene_add_node` | 报错 | 用 Write 直接写 .tscn |
| `scene_save` | 超时 | 用 Write 直接写 .tscn |
| `editor_run_scene` | 可能阻断会话 | 用户手动 F5 |
| `runtime_launch_game` | 可能阻断会话 | 用户手动运行 |

## .tscn 文件格式

### 基本结构

```gdscene
[gd_scene load_steps=N format=3]

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
[ext_resource type="PackedScene" path="res://Scenes/Xxx.tscn" id="4_ref"]

[node name="Instance" parent="." instance=ExtResource("4_ref")]
```

### 关键规则

- `load_steps` = ext_resource 数 + sub_resource 数（不严格，多 1 也行）
- `uid` 字段可省略，Godot 编辑器自动生成
- `unique_id` 同上
- 修改 .tscn 后编辑器里关闭再打开场景才能看到变化
- 节点路径：根节点 `"."`，子节点 `"ChildName"`，嵌套 `"Parent/Child"`

## C# 开发流程

### 编写脚本

AI 直接用 Write/Edit 创建/修改 .cs 文件。挂载脚本到场景：

```gdscene
[ext_resource type="Script" path="res://Scripts/MyScript.cs" id="1_script"]

[node name="MyNode" type="Node2D"]
script = ExtResource("1_script")
```

### 编译验证

```bash
dot_net_build(projectPath="项目路径/Project.csproj")
```

返回 Roslyn 增强的诊断信息，包含文件路径、行号、错误上下文。

### 信号连接

```csharp
// C# 自定义信号（推荐）
[Signal]
public delegate void MySignalEventHandler();
// 触发：EmitSignal(SignalName.MySignal)
// 连接：node.MySignal += Handler

// Godot 引擎信号
node.Connect(CanvasItem.SignalName.InputEvent, new Callable(this, nameof(Handler)))

// Lambda
button.Pressed += () => DoSomething();
```

### 资源加载

```csharp
var tex = GD.Load<Texture2D>("res://Assets/Xxx.png");
var scene = GD.Load<PackedScene>("res://Scenes/Xxx.tscn");
var instance = scene.Instantiate<Node2D>();
AddChild(instance);
```

## 常见问题

### MCP 连接断开

```bash
taskkill /F /IM Wick.Server.exe
# Claude Code 里 /mcp 重启
```

### 多个 Wick 进程

每次 Rewind 可能产生多余进程。批量清理：

```bat
@echo off
taskkill /F /IM Wick.Server.exe 2>nul
```

### 编译错误速查

| 错误码 | 原因 | 解决 |
|--------|------|------|
| CS0117 | API 不存在 | 查 Godot C# 文档确认正确名称 |
| CS1061 | 信号/属性访问方式错误 | Node2D 信号用 `Connect`，不用 `+=` |
| CS0246 | 类型找不到 | 检查 using 和命名空间 |
| MSB1003 | 找不到 .csproj | 指定正确的项目路径 |

### project.godot 修改不生效

窗口大小等设置可能被编辑器缓存覆盖。在编辑器里手动改：
**Project → Project Settings → General → Window → Size**

### 音频 API 差异

Godot C# 没有 `GD.LinearToDb()`，手动计算：

```csharp
float LinearToDb(float linear) =>
    linear <= 0.0001f ? -80f : 20f * Mathf.Log(linear) / Mathf.Log(10f);
```

### ZIndex 层级

- 默认 `z_as_relative = true`，子节点 ZIndex 相对于父节点
- 设 `z_as_relative = false` 使用绝对 ZIndex
- 同 ZIndex 时，场景树中越靠后的节点越在上面

## 工作流最佳实践

### 开发节奏

1. 先跑通核心流程，再调细节
2. 每个功能编译通过再继续下一个
3. 用 Debug 模式开关控制开发/发布状态
4. 截图反馈比文字描述更高效

### 代码组织

- 每个独立功能拆成单独的场景 + 控制器脚本
- GameManager 只做逻辑编排，不直接操作 UI 节点
- 用 autoload 单例管理全局系统（音频、存档等）

### .tscn 编辑策略

- 简单修改：直接 Edit .tscn 文件
- 复杂调整：让用户在编辑器里手动操作
- 节点属性改不动时：用 `editor_set_property` MCP 工具

### 音效工作流

1. AI 创建 .txt 占位文件 + 代码打印 `[SFX] xxx`
2. 用户找音效替换 .txt
3. AudioManager 预加载 + 对象池播放

### 截图调试

```bash
python .claude/skills/local-vision/scripts/analyze_image.py \
  --image "截图路径.png" \
  --prompt-text "具体的问题描述"
```

提示词要具体，视觉模型没有对话上下文。
