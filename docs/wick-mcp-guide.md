# Wick MCP 经验手册

> 基于 Godot 4.6.3 + C# + Wick MCP 的实际开发经验编写。供不同 AI 模型在新项目中快速上手参考。

---

## 1. Wick MCP 是什么

Wick 是一个为 Godot 引擎设计的 MCP（Model Context Protocol）桥接工具，让 AI 能够与 Godot 编辑器和运行时交互。

### 架构

```
AI 客户端（Claude Code / Cursor / etc.）
    ↕ MCP 协议（stdio）
Wick MCP Server（.NET 10.0 进程）
    ↕ TCP JSON-RPC（端口 6505）
Godot 编辑器插件（GDScript）
    ↕ TCP（端口 7777）
游戏运行时（可选，需安装 Wick.Runtime）
```

### 三个组件

| 组件 | 位置 | 作用 |
|------|------|------|
| Godot 插件 | `项目目录/addons/wick/` | 在编辑器内运行 TCP 服务，暴露场景树和节点操作 |
| MCP Server | 独立 .NET 项目 | 接收 AI 的 MCP 调用，转发给 Godot 插件 |
| AI 客户端 | Claude Code / Cursor 等 | 通过 MCP 协议调用 Wick 提供的工具 |

---

## 2. 安装配置

### 2.1 Godot 插件

将 `addons/wick/` 目录复制到你的 Godot 项目下，然后在编辑器中启用：

**Project → Project Settings → Plugins → Wick → ✅ Enable**

插件启用后会在端口 6505 和 7777 启动 TCP 服务。

### 2.2 构建 MCP Server

```bash
git clone https://github.com/buildepicshit/Wick.git
cd Wick

# 构建（不能有警告，项目启用了 TreatWarningsAsErrors）
dotnet build Wick.slnx --configuration Release

# 如果有 NU1902 NuGet 漏洞警告导致构建失败：
# 在 Directory.Build.props 中添加 <NoWarn>$(NoWarn);NU1902</NoWarn>
```
**如果有 NU1902 NuGet 漏洞警告导致构建失败：在 Directory.Build.props 中添加 <NoWarn>$(NoWarn);NU1902</NoWarn>**


### 2.3 配置 AI 客户端

在项目根目录创建 `.mcp.json`：

```json
{
  "mcpServers": {
    "wick": {
      "command": "dotnet",
      "args": ["run", "--project", "绝对路径/Wick/src/Wick.Server"],
      "env": {
        "WICK_GROUPS": "all",
        "WICK_GODOT_BIN": "绝对路径/Godot_v4.6.3-stable_mono_win64.exe",
        "WICK_PROJECT_PATH": "绝对路径/你的Godot项目目录"
      }
    }
  }
}
```

**关键配置项：**

| 变量 | 说明 |
|------|------|
| `WICK_GROUPS` | 激活的工具组。`all` 激活全部，也可指定 `core,csharp,build,scene,runtime` |
| `WICK_GODOT_BIN` | Godot 可执行文件的绝对路径。`runtime` 组需要 |
| `WICK_PROJECT_PATH` | Godot 项目目录（包含 project.godot 的目录） |

配置完成后重启 AI 客户端。输入 `/mcp` 检查 wick 是否在列表中。

---

## 3. 工具清单

### Core 组（基础）

| 工具 | 说明 |
|------|------|
| `editor_status` | 检查编辑器和运行时连接状态 |
| `editor_scene_tree` | 获取编辑器当前打开的场景树（需要场景已打开） |
| `editor_node_properties` | 获取节点属性 |
| `editor_set_property` | 设置节点属性 |
| `editor_call_method` | 调用节点方法 |
| `editor_connect` | 强制重连编辑器 |

### Scene 组（场景操作）

| 工具 | 说明 |
|------|------|
| `scene_create` | 创建新 .tscn 场景文件 |
| `scene_add_node` | 向场景添加子节点 |
| `scene_set_node_properties` | 设置节点属性并保存 |
| `scene_save` | 保存场景 |
| `scene_load_resource` | 加载资源（纹理、材质、脚本等）并赋值给节点属性 |
| `scene_get_tree` | 读取 .tscn 文件的场景树结构（纯文件读取，不需要 Godot 运行） |
| `scene_get_node_properties` | 读取节点属性（纯文件读取） |

### Build 组（编译）

| 工具 | 说明 |
|------|------|
| `dot_net_build` | 执行 `dotnet build`，返回 Roslyn 增强的错误/警告诊断 |
| `dot_net_clean` | 清理构建产物 |
| `dot_net_test` | 运行测试 |

### C# 组（代码分析）

| 工具 | 说明 |
|------|------|
| `c_sharp_analyze` | 分析 C# 文件结构（类、方法、属性、字段） |
| `c_sharp_find_symbol` | 按名称搜索 C# 符号 |
| `c_sharp_find_references` | 查找符号的所有引用 |
| `c_sharp_get_member_signatures` | 获取类型的成员签名 |

### Runtime 组（运行时）

| 工具 | 说明 |
|------|------|
| `runtime_launch_game` | 启动游戏进程 |
| `runtime_stop_game` | 停止游戏 |
| `runtime_diagnose` | 获取运行时状态 + 异常 + 日志 |
| `runtime_query_scene_tree` | 查询运行时场景树（需安装 Wick.Runtime） |
| `runtime_call_method` | 调用运行时节点方法 |
| `runtime_get_exceptions` | 获取异常缓冲 |
| `runtime_get_log_tail` | 获取最近日志 |

---

## 4. C# 开发工作流

### 4.1 编写/修改 C# 脚本

AI 直接使用文件操作工具（Write/Edit）创建和修改 `.cs` 文件。Wick MCP 的 `dot_net_build` 用于编译验证。

**完整流程：**
1. AI 用 Write/Edit 工具创建/修改 `.cs` 文件
2. AI 调用 `dot_net_build` 编译
3. 如果有编译错误，AI 读取诊断信息并修复
4. 循环直到编译通过
5. 用户在 Godot 编辑器中 F5 运行

### 4.2 挂载 C# 脚本到场景

**方法一：编辑 .tscn 文件（推荐）**

在 .tscn 文件中添加脚本引用：

```
[ext_resource type="Script" path="res://scripts/MyScript.cs" id="1_script"]

[node name="MyNode" type="Control"]
script = ExtResource("1_script")
```

Godot 通常能自动识别手动编辑的 .tscn 文件。如果不行，需要用户在编辑器中手动挂载。

**方法二：通过 MCP 工具**

使用 `scene_load_resource` 工具将脚本赋值给节点的 `script` 属性：
```
scene_load_resource(scenePath, nodePath, "script", "res://scripts/MyScript.cs")
```

**注意：** MCP 的 `scene_save` 会重写整个 .tscn 文件。如果之前手动添加了 ext_resource 引用，可能会被覆盖。建议先用 MCP 创建场景，最后再手动挂载脚本。

### 4.3 脚本修改后无需重新挂载

修改 `.cs` 文件内容后，不需要重新挂载脚本。Godot 会在下次编译时自动使用新内容。

---

## 5. 场景操作

### 创建场景

```python
# 创建场景文件
scene_create(path="res://scenes/game.tscn", rootType="Control")

# 添加子节点
scene_add_node(scenePath="res://scenes/game.tscn", parentPath=".", type="Label", name="Title")

# 设置属性
scene_set_node_properties(scenePath, "Title", {"text": "Hello", "horizontal_alignment": "1"})

# 保存
scene_save(scenePath="res://scenes/game.tscn")
```

### 读取场景

```python
# 获取完整场景树
scene_get_tree(scenePath="res://scenes/game.tscn", includeProperties=True)

# 获取特定节点属性
scene_get_node_properties(scenePath="res://scenes/game.tscn", nodePath="Title")
```

### 注意事项

- `scene_get_tree` 和 `scene_get_node_properties` 是纯文件读取，不需要 Godot 运行
- `scene_create`、`scene_add_node`、`scene_save` 需要 Godot 插件运行
- 节点路径使用 Godot 风格：根节点为 `"."`，直接子节点为 `"NodeName"`，嵌套为 `"Parent/Child"`

---

## 6. 已知限制

### 6.1 不能做的事

| 限制 | 说明 |
|------|------|
| 不能直接看游戏画面 | 没有截图/视口捕获功能。需要用户手动截图 + AI 读取分析 |
| 不能运行游戏 | `editor_run_scene` 和 `runtime_launch_game` 可能阻断 AI 客户端会话。建议用户手动运行 |
| 不能模拟鼠标点击 | 只能通过 `emit_signal` 或 `call_method` 触发，不能模拟真实输入事件 |
| .tscn 手动编辑可能被覆盖 | `scene_save` 会重写整个文件 |
| StyleBoxFlat 的 GlowColor/GlowSize 不可用 | C# API 中不存在这些属性，只在 .tscn 资源格式中有效 |

### 6.2 绕过方案

| 问题 | 方案 |
|------|------|
| 需要看游戏画面 | 用户按 F12 截图保存到 inbox，AI 读取截图文件分析 |
| 需要运行游戏 | 用户手动在 Godot 编辑器中 F5 运行 |
| 需要点击按钮 | AI 用 `editor_call_method` 调用 `emit_signal("pressed")` |
| 需要编辑 .tscn 又不想被覆盖 | 先用 MCP 创建场景，最后再手动编辑 .tscn 挂载脚本 |

---

## 7. 调试流程

### 7.1 截图调试（推荐）

1. 在项目中创建一个 ScreenshotTool 脚本，按 F12 保存截图到指定目录
2. 用户运行游戏，遇到问题时按 F12
3. AI 读取截图文件进行视觉分析

**ScreenshotTool 脚本模板：**

```csharp
using Godot;

public partial class ScreenshotTool : Node
{
    private const string SavePath = "绝对路径/inbox";

    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventKey key && key.Pressed && key.Keycode == Key.F12)
        {
            var image = GetViewport().GetTexture().GetImage();
            var timestamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
            image.SavePng($"{SavePath}/screenshot_{timestamp}.png");
        }
    }
}
```

### 7.2 编译错误调试

AI 调用 `dot_net_build` 后会获得 Roslyn 增强的错误信息，包含：
- 错误代码和消息
- 文件路径和行号
- 源代码上下文

AI 可以直接定位并修复错误。

### 7.3 运行时异常调试

```python
# 获取运行时状态和最近异常
runtime_diagnose()

# 获取详细异常信息（带 Roslyn 源码上下文）
runtime_get_exceptions(limit=10)
```

### 7.4 节点状态检查

```python
# 在编辑器中检查节点属性
editor_node_properties(target="editor", nodePath="/root/Scene/MyNode")

# 在运行时检查（需游戏运行）
runtime_query_node_properties(nodePath="/root/Scene/MyNode")
```

---

## 8. 踩坑记录

### 8.1 Godot 项目不能包含 MCP Server 源码

**问题：** Wick 的 MCP Server 源码（src/、tests/）和 Godot 项目放在同一目录下，Godot 编译时会扫描所有 .cs 文件，导致 900+ 编译错误。

**解决：** 将 Wick 源码放在独立目录，Godot 项目只保留 `addons/wick/` 插件目录。

### 8.2 .tscn 节点路径要与代码一致

**问题：** 在 .tscn 中添加了中间层节点（如 HBoxContainer 包裹 Label），但 C# 代码中的 GetNode 路径没有更新。

**解决：** 修改 .tscn 结构后，同步更新 C# 代码中的所有 GetNode 路径。

### 8.3 CallDeferred 布局计算可能尺寸为零

**问题：** 在 `_Ready()` 中用 `CallDeferred` 计算布局时，控件尺寸可能尚未确定（为零）。

**解决：** 在 `UpdateFanLayout` 中检查尺寸是否有效，并添加 `_Notification(NotificationResized)` 监听尺寸变化重新计算。

### 8.4 StyleBoxFlat C# API 限制

**问题：** `GlowColor`、`GlowSize` 等属性在 .tscn 资源格式中可用，但 C# API 中不存在。

**解决：** 只使用 C# API 中确认存在的属性（BgColor, BorderColor, SetBorderWidthAll, SetCornerRadiusAll, SetContentMarginAll, ShadowColor, ShadowSize, ShadowOffset）。

### 8.5 editor_run_scene 会阻断 AI 客户端

**问题：** 调用 `editor_run_scene` 或 `runtime_launch_game` 后，AI 客户端会话可能中断。

**解决：** 不通过 MCP 运行游戏，由用户手动在 Godot 编辑器中 F5 运行。

### 8.6 MCP Server 启动需要先构建

**问题：** `dotnet run` 首次运行时会自动构建，如果构建失败（如 NU1902 漏洞警告），MCP Server 无法启动。

**解决：** 在 `Directory.Build.props` 中添加 `<NoWarn>$(NoWarn);NU1902</NoWarn>` 忽略漏洞警告。

---

## 9. 对比 Godot-MCP-Native 的改进

| 能力 | Godot-MCP-Native v1.0.6 | Wick MCP |
|------|--------------------------|----------|
| C# 脚本创建/编辑 | ❌ 只支持 .gd | ✅ AI 直接操作 .cs 文件 |
| C# 脚本挂载 | ❌ 需手动编辑器操作 | ⚠️ .tscn 编辑通常有效 |
| 编译 | ❌ | ✅ dot_net_build |
| C# 代码分析 | ❌ | ✅ Roslyn 分析 |
| 场景创建/编辑 | ✅ | ✅ |
| 场景切换 | ❌ 不生效 | ✅ |
| 运行时异常 | ❌ | ✅ Roslyn 增强 |
| 查看游戏画面 | ❌ | ❌（同样不支持） |
| 运行游戏 | ✅ | ⚠️ 可能阻断会话 |
