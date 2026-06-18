---
last_editor: Codex
last_edit: 2026-06-18
status: draft
---

# PSD JSON 资源偏移计算指南

## 背景

本项目的美术资源来自 1200x1200 PSD 画布。美术在 PSD 中把各类物体摆到视觉正确的位置后，导出 PNG 资源，同时导出 `layer_index.json`。

PNG 会裁切透明像素，因此每张 PNG 的尺寸可能不同。`layer_index.json` 中记录的 `x`、`y` 是该 PNG 左上角在 PSD 画布中的坐标，`width`、`height` 是裁切后的 PNG 尺寸。

Godot 中常用 `Sprite2D` 显示这些 PNG。默认情况下，`Sprite2D.Centered = true`，因此 `Sprite2D.Position` 控制的是图片中心点，而不是左上角。

这意味着程序不能直接把 JSON 的 `x`、`y` 当作 `Sprite2D.Position` 使用，也不能只用左上角差值做偏移。正确做法是：用 JSON 还原每张 PNG 在 PSD 中的图片中心点，再计算它相对于参考物的中心点差值。

## 已验证案例

已验证对象为主游戏场景中的酒类道具。

- 场景：`lucky-dog-rise/Scenes/Main.tscn`
- 道具实例：`ItemArea`
- 道具子场景：`lucky-dog-rise/Scenes/ItemArea.tscn`
- 显示节点：`Treat`
- 参考图片：`Assets/v1/Treat/Whisky.png`
- 坐标数据：`Assets/v1/layer_index.json`
- 控制脚本：`lucky-dog-rise/Scripts/ItemAreaController.cs`

在 `ItemArea.tscn` 中，策划或美术先把 `Treat` 节点加载为 `Whisky.png`，并手动调整 `Treat.Position`，让 Whisky 在 `ItemArea` 原点附近达到视觉正确的位置。

随后在 `Main.tscn` 中调整 `ItemArea.Position`，让 Whisky 在主场景中落到正确位置。

这两个手调结果分别承担不同职责：

- `Treat.Position`：参考物在子场景内部的基础偏移。
- `ItemArea.Position`：整类道具在主场景中的整体落点。

程序运行时只应该动态调整 `Treat.Position`，不应该根据具体酒类改动 `ItemArea.Position`。

## 正确公式

对同一类可替换物体，先选一个参考物。参考物必须满足以下条件：

- 在 PSD 中位置正确。
- 在对应 Godot 子场景中已经人工调到视觉正确。
- 文件名稳定，不会随意改名。

酒类道具当前使用 `Whisky.png` 作为参考物。

计算步骤如下：

1. 读取参考物在子场景中的手调位置。

```csharp
var referenceLocalPosition = treatSprite.Position;
```

2. 从 `layer_index.json` 中读取参考物的 PSD 矩形。

```text
referenceX
referenceY
referenceWidth
referenceHeight
```

3. 计算参考物在 PSD 中的图片中心点。

```text
referenceCenter =
  (
    referenceX + referenceWidth / 2,
    referenceY + referenceHeight / 2
  )
```

4. 对每一个同类物体，读取它自己的 PSD 矩形并计算图片中心点。

```text
itemCenter =
  (
    itemX + itemWidth / 2,
    itemY + itemHeight / 2
  )
```

5. 计算该物体相对参考物的 PSD 中心差值。

```text
centerDelta = itemCenter - referenceCenter
```

6. 计算运行时 `Sprite2D.Position`。

```text
runtimePosition = referenceLocalPosition + centerDelta
```

完整公式：

```text
Sprite2D.Position =
  参考物在子场景中的手调位置
  + 当前物体在 PSD 中的图片中心点
  - 参考物在 PSD 中的图片中心点
```

## 为什么使用图片中心点

JSON 的 `x`、`y` 是 PNG 左上角在 PSD 中的位置，但 Godot 默认 `Sprite2D.Position` 控制的是 PNG 中心点。

当不同 PNG 尺寸不同时，只使用左上角差值会漏掉尺寸差异。例如高酒瓶和矮酒杯的左上角 Y 坐标差异很大，但真正需要交给 `Sprite2D.Position` 的是“中心点应该移动多少”。

因此同类物体的运行时偏移必须以图片中心点为基准：

```text
center = topLeft + size / 2
```

如果后续某个场景把 `Sprite2D.Centered` 改为 `false`，本公式不再适用。那种情况下 `Sprite2D.Position` 会控制左上角，公式应改为左上角差值：

```text
Sprite2D.Position =
  参考物手调位置
  + 当前物体 PSD 左上角
  - 参考物 PSD 左上角
```

当前项目默认应保持 `Sprite2D.Centered = true`。

## 酒类道具的验证数据

以 `Whisky.png` 为参考物，`ItemArea.tscn` 中的 `Treat.Position` 为：

```text
(0, -60)
```

根据中心点公式，部分酒类道具的运行时位置应为：

```text
Whisky      -> (0, -60)
WhiskyCagar -> (-0.5, -116)
Bordeaux    -> (-17, -102.5)
Chardonnay  -> (-22, -102)
Tulip       -> (2, -101)
Tumbler     -> (-11, -61)
Vintage     -> (-9, -86)
ShotGlass   -> (-51.5, -25.5)
```

主人已经在游戏中验证：使用该公式后，所有酒类道具的位置视觉正确。

## 执行程序员 AI 的接入流程

为其他可替换物体接入同类偏移系统时，应按以下流程执行。

1. 确认同类物体来自同一 PSD 构图规则。

同一类物体应在 PSD 中已经由美术摆到视觉正确的位置。JSON 必须记录该类所有 PNG 的 `x`、`y`、`width`、`height`。

2. 在对应子场景中创建或确认显示节点。

显示节点通常是 `Sprite2D`。节点应在 `.tscn` 中存在，避免运行时用 `new` 创建主 UI 或主美术结构。

3. 选择一个参考物。

参考物应加载到显示节点上，并由策划或美术手动调整到子场景原点附近的视觉正确位置。

4. 在主场景中调整父节点位置。

父节点位置代表整类物体在主场景中的整体落点。后续切换同类资源时，程序不应再修改父节点位置。

5. 在代码中读取参考物的手调位置。

代码应在 `_Ready()` 中先读取显示节点的初始 `Position`，作为 `referenceLocalPosition`。

6. 从 JSON 构建位置缓存。

缓存 key 建议使用文件名，例如 `Whisky.png`。运行时切换资源时，根据当前资源文件名取出对应位置。

7. 运行时切换资源。

运行时需要同时设置贴图和位置：

```csharp
sprite.Texture = texture;
sprite.Position = cache.GetValueOrDefault(fileName, referenceLocalPosition);
sprite.Visible = true;
```

8. 验证所有同类资源。

主人或测试者应在游戏内通过背包、随机换装或调试按钮切换该类所有资源，确认每个资源的位置都视觉正确。

## 推荐代码结构

推荐把 JSON 中的矩形读取和中心点计算拆成小函数，避免公式散落在业务代码里。

```csharp
private static Vector2 ReadCenter(Godot.Collections.Dictionary d)
{
    var x = (float)d["x"].AsDouble();
    var y = (float)d["y"].AsDouble();
    var w = ReadDim(d, "w", "width");
    var h = ReadDim(d, "h", "height");
    return new Vector2(x + w / 2f, y + h / 2f);
}

private static float ReadDim(Godot.Collections.Dictionary d, string shortKey, string longKey)
{
    return d.ContainsKey(shortKey)
        ? (float)d[shortKey].AsDouble()
        : (float)d[longKey].AsDouble();
}
```

`ReadDim` 同时兼容旧 JSON 的 `w`、`h` 字段和新 JSON 的 `width`、`height` 字段。

## 常见错误

### 直接使用 JSON 左上角

错误公式：

```text
Sprite2D.Position =
  参考物手调位置
  + 当前物体 PSD 左上角
  - 参考物 PSD 左上角
```

该公式只适用于 `Sprite2D.Centered = false` 的情况。当前项目默认 `Sprite2D.Centered = true`，使用该公式会导致不同尺寸 PNG 的垂直或水平位置错误。

### 只使用底部中心

错误公式：

```text
Sprite2D.Position =
  参考物手调位置
  + 当前物体 PSD 底部中心
  - 参考物 PSD 底部中心
```

该公式可能让某些物体的水平位置看起来正确，但会忽略 `Sprite2D.Position` 实际控制图片中心点的问题。酒类道具测试中，`WhiskyCagar` 与 `Whisky` 水平正确但垂直错误，就是该公式导致的。

### 把参考物手调位置当作通用视觉锚点

参考物手调位置只说明“参考物这张 PNG 如何对准子场景原点”。其他 PNG 尺寸不同，不能直接复用同一个位置。必须叠加 PSD 中心点差值。

### 修改父节点位置来修单个资源

父节点位置代表整类物体的整体落点。如果某个资源位置不正确，应检查 JSON 坐标、PNG 尺寸、中心点公式或单品微调数据，不应为单个资源移动父节点。

## 单品微调

原则上，如果 PSD 中已经摆放正确，并且 JSON 导出正确，则同类资源不需要单品微调。

只有在以下情况才建议加入单品微调：

- 美术资源本身视觉锚点特殊，无法用同类公式表达。
- PSD 中该图层摆放有意偏离通用规则。
- 主人明确验收后认为某个单品需要额外调整。

单品微调应作为最后一层叠加：

```text
Sprite2D.Position =
  参考物手调位置
  + 当前物体 PSD 中心点
  - 参考物 PSD 中心点
  + 单品微调
```

单品微调应集中管理，避免散落在多处代码中。

## 验收标准

同类物体的偏移系统接入完成后，应满足以下条件：

- 参考物在子场景和主场景中视觉正确。
- 切换到同类其他资源时，物体落点与 PSD 构图一致。
- 不同 PNG 尺寸不会导致位置漂移。
- 父节点位置只负责整类物体整体落点。
- 显示节点位置由公式统一计算。
- 新增同类 PNG 和 JSON 记录后，不需要再次调整场景位置。

