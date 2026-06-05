# Godot C# 常用模式

## 节点访问

```csharp
// GetNode（运行时查找，路径错误会崩溃）
var label = GetNode<Label>("HUD/InfoPanel/Label");
var child = GetNode<Node2D>("ChildName");

// GetTree().GetNodesInGroup("group_name")
var enemies = GetTree().GetNodesInGroup("enemies");
```

## 信号系统

### 自定义信号

```csharp
public partial class MyNode : Node2D
{
    [Signal]
    public delegate void HealthChangedEventHandler(int newHealth);

    public void TakeDamage(int amount)
    {
        _health -= amount;
        EmitSignal(SignalName.HealthChanged, _health);
    }
}

// 连接
myNode.HealthChanged += (hp) => GD.Print($"HP: {hp}");
```

### 引擎信号

```csharp
// Button
button.Pressed += OnButtonPressed;

// 输入（Control 节点）
control.GuiInput += OnGuiInput;

// 输入（Node2D，需要 Area2D）
node.Connect(CanvasItem.SignalName.InputEvent, new Callable(this, nameof(OnInput)));
```

### 信号参数匹配

```csharp
// GuiInput 签名
private void OnGuiInput(InputEvent e)
{
    if (e is InputEventMouseButton mb && mb.Pressed)
    {
        // 处理点击
    }
}
```

## Tween 动画

```csharp
// 基本用法
var tween = CreateTween();
tween.TweenProperty(node, "position:x", 100f, 0.5f);
tween.TweenProperty(node, "modulate:a", 0f, 0.3f);

// 缓动曲线
tween.TweenProperty(node, "position:y", targetY, 0.2f)
    .SetEase(Tween.EaseType.Out)
    .SetTrans(Tween.TransitionType.Bounce);

// 并行动画
var tween = CreateTween().SetParallel(true);
tween.TweenProperty(nodeA, "rotation", 0.5f, 0.1f);
tween.TweenProperty(nodeB, "rotation", -0.5f, 0.1f);

// 链式（顺序执行）
tween.Chain().TweenProperty(nodeA, "rotation", 0f, 0.1f);

// 延迟
tween.TweenInterval(0.5f);

// 回调
tween.TweenCallback(Callable.From(() => DoSomething()));
```

## 输入处理

```csharp
// 按键
public override void _Input(InputEvent @event)
{
    if (@event is InputEventKey key && key.Pressed)
    {
        if (key.Keycode == Key.Space) DoSomething();
    }
}

// 鼠标
public override void _Input(InputEvent @event)
{
    if (@event is InputEventMouseButton mb && mb.Pressed)
    {
        if (mb.ButtonIndex == MouseButton.Left) OnClick();
    }
}

// 未处理的输入（按钮等 UI 消费后剩下的）
public override void _UnhandledInput(InputEvent @event)
{
    if (@event is InputEventMouseButton mb && mb.Pressed)
    {
        // 点击了空白区域
    }
}

// 消费输入事件（阻止传播）
GetViewport().SetInputAsHandled();
```

## 定时器

```csharp
// 方式 1：SceneTreeTimer（一次性延迟）
GetTree().CreateTimer(1.0).Timeout += () => GD.Print("1秒后");

// 方式 2：Timer 节点
var timer = new Timer();
timer.WaitTime = 2.0f;
timer.OneShot = true;
timer.Timeout += OnTimerDone;
AddChild(timer);
timer.Start();
```

## 资源管理

```csharp
// 加载纹理
var tex = GD.Load<Texture2D>("res://Assets/Card/Spade1.png");

// 加载场景
var scene = GD.Load<PackedScene>("res://Scenes/Enemy.tscn");
var instance = scene.Instantiate<Node2D>();
AddChild(instance);

// 检查资源是否存在
if (ResourceLoader.Exists("res://Audio/SFX/Knock.wav"))
    stream = GD.Load<AudioStream>("res://Audio/SFX/Knock.wav");

// 动态扫描目录
var dir = DirAccess.Open("res://Assets/Clothes/");
dir.ListDirBegin();
string name = dir.GetNext();
while (name != "")
{
    if (name.EndsWith(".png")) files.Add(name);
    name = dir.GetNext();
}
dir.ListDirEnd();
```

## 音频

```csharp
// AudioStreamPlayer（非位置音效）
var player = new AudioStreamPlayer();
player.Stream = GD.Load<AudioStream>("res://Audio/SFX/Click.wav");
player.Bus = "SFX";
AddChild(player);
player.Play();

// 音量控制
player.VolumeDb = -6f;                    // dB
player.VolumeDb = LinearToDb(0.5f);       // 线性转 dB

// BGM
var bgm = new AudioStreamPlayer();
bgm.Stream = GD.Load<AudioStream>("res://Audio/BGM/Main.ogg");
bgm.Bus = "BGM";
bgm.Play();
```

## 存档

```csharp
// ConfigFile（简单键值对）
var config = new ConfigFile();
config.SetValue("save", "high_score", 1000);
config.SetValue("save", "rank", 2);
config.Save("user://save.cfg");

// 读取
var config = new ConfigFile();
if (config.Load("user://save.cfg") == Error.Ok)
{
    int score = (int)(long)config.GetValue("save", "high_score", 0);
}
```

## 场景切换

```csharp
// 切换到另一个场景
GetTree().ChangeSceneToFile("res://Scenes/Menu.tscn");

// 带过渡的切换（用 AnimationPlayer 或 Tween 控制）
```

## 导出属性（Inspector 可调）

```csharp
public partial class Enemy : Node2D
{
    [Export]
    public float Speed { get; set; } = 100f;

    [Export]
    public int Health { get; set; } = 10;

    [Export]
    public Texture2D SpriteTexture { get; set; }
}
```

## 分组

```csharp
// 添加到分组
AddToGroup("enemies");

// 查询分组
var enemies = GetTree().GetNodesInGroup("enemies");

// 条件检查
if (IsInGroup("enemies")) { }
```

## 常用数学

```csharp
Mathf.Clamp(value, min, max);
Mathf.Lerp(a, b, t);
Mathf.DegToRad(degrees);
Mathf.RadToDeg(radians);
Vector2.DirectionTo(other);  // 归一化方向
position.MoveToward(target, delta * speed);
```

## C# 与 GDScript 互操作

```csharp
// 调用 GDScript 方法
node.Call("method_name", arg1, arg2);

// 获取 GDScript 属性
var value = node.Get("property_name");

// 设置 GDScript 属性
node.Set("property_name", value);
```
