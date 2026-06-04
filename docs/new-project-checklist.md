# Godot + Wick MCP 新项目快速启动清单

> 新建 Godot C# 项目时，按这个清单走一遍就能进入开发状态。

---

## 1. 创建 Godot 项目

在 Godot 编辑器里新建项目，然后手动配置 `project.godot`：

```ini
[application]
config/name="项目名"
run/main_scene="res://scenes/main.tscn"
config/features=PackedStringArray("4.6", "C#", "GL Compatibility")

[dotnet]
project/assembly_name="项目名"

[editor_plugins]
enabled=PackedStringArray("res://addons/wick/plugin.cfg")
```

## 2. 安装 Wick 插件

把 Wick 仓库里的 `addons/wick/` 目录复制到项目下，编辑器中启用插件。

## 3. 配置 MCP

在工作空间根目录创建 `.mcp.json`：

```json
{
  "mcpServers": {
    "wick": {
      "command": "dotnet",
      "args": ["run", "--project", "Wick源码绝对路径/src/Wick.Server"],
      "env": {
        "WICK_GROUPS": "all",
        "WICK_GODOT_BIN": "Godot可执行文件绝对路径",
        "WICK_PROJECT_PATH": "Godot项目绝对路径"
      }
    }
  }
}
```

重启 Claude Code，输入 `/mcp` 确认 wick 已连接。

## 4. 项目目录结构

```
项目名/
├── project.godot
├── assets/              ← 美术素材（图标、图片）
├── scenes/
│   ├── main.tscn        ← 主场景
│   ├── ui/              ← UI 场景 (.tscn)
│   └── game/            ← 游戏逻辑场景
├── scripts/
│   ├── autoload/        ← 全局单例（EventBus、GameManager）
│   ├── data/            ← 数据类（枚举、状态、配置）
│   ├── game/            ← 游戏逻辑脚本
│   └── ui/              ← UI 脚本
├── Themes/              ← Theme 资源文件 (.tres)
└── addons/wick/         ← Wick 插件（不要改）
```

## 5. 第一个脚本：ScreenshotTool

创建 `scripts/ScreenshotTool.cs`：

```csharp
using Godot;

public partial class ScreenshotTool : Node
{
    private const string SavePath = "inbox绝对路径";

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

在任意场景根节点挂上这个脚本，F12 截图调试。

## 6. C# 开发规则

### 写代码

- 直接用 Edit/Write 工具操作 `.cs` 文件
- 节点引用用 `[Export]`，Inspector 拖拽绑定
- 信号连接用代码 `node.Signal += Handler`
- **不要用** `GetNode` 硬编码路径（层级变了就断）

### 挂载脚本

在 `.tscn` 文件里手动添加：

```
[ext_resource type="Script" path="res://scripts/MyScript.cs" id="1_script"]

[node name="MyNode" type="Control"]
script = ExtResource("1_script")
```

Godot 通常能自动识别。不行就手动在编辑器里拖拽挂载。

### 编译

- AI 调用 Wick MCP 的 `dot_net_build` 编译
- 用户也可以在 Godot 编辑器里 F5 自动编译
- 改了 `.cs` 文件不需要重新挂载脚本

### 运行和调试

- **用户手动 F5 运行**（不要通过 MCP 运行，会阻断 AI 会话）
- 遇到问题按 F12 截图保存到 inbox
- AI 读取截图分析问题

## 7. UI 开发要点

- Theme 样式抽到 `.tres` 文件，多节点复用
- 显隐切换用 SceneManager 集中管理
- 刷新数据用 `NotificationVisibilityChanged`，不用 `_Input` 判断
- 面板全部常驻内存（Visible 切换），不要动态加载/卸载

## 8. 信号通信模式

```csharp
// EventBus.cs 里声明信号
[Signal] public delegate void XxxEventHandler();

// 发射信号
EventBus.Instance.EmitSignal(EventBus.SignalName.Xxx);

// 监听信号
EventBus.Instance.Xxx += OnXxx;
```

## 9. 注意事项

- Wick MCP Server 只能有一个实例运行，不要同时在 VS Code 和 Claude Code 里启动
- `.tscn` 手动编辑后，不要用 MCP 的 `scene_save`，会覆盖你的修改
- Godot 项目目录下不要放 Wick 的 `src/`、`tests/` 等源码，会跟 Godot 编译冲突
- `[Signal]` 委托名必须以 `EventHandler` 结尾
