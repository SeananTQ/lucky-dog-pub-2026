# 美术资源规范（通用）

## 命名规范

### 总则

- **PascalCase**：所有文件名和文件夹名
- **全称优先**：`Headwear` 不用 `Hat`，`Accessory` 不用 `Acc`
- **Prefix_BaseAssetName_Variant_State**

### 示例

```
# 角色素材
Head_Chubby.png        # 头部 - 胖版
Head_Thin.png          # 头部 - 瘦版
Eyes_Happy.png         # 眼睛 - 开心
Eyes_Bored.png         # 眼睛 - 无聊
Ears_Plane.png         # 耳朵 - 平

# 服装
BaseballJacket_Black   # 棒球夹克 - 黑色
WhiteShirt_White       # 白衬衫 - 白色
BlueStripedShirt_RegattaStripe  # 条纹衫

# 饰品
TextWristband_Green    # 手腕带 - 绿
Monocle_golden         # 单片眼镜 - 金

# 扑克牌
Club1 ~ Club13         # 梅花 A~K
Diamond1 ~ Diamond13   # 方块
Heart1 ~ Heart13       # 红桃
Spade1 ~ Spade13       # 黑桃
```

### 卡牌编码

| 内部编码 | 文件名 | 牌面 |
|----------|--------|------|
| rank 0 | 1 | A |
| rank 1~9 | 2~10 | 2~10 |
| rank 10 | 11 | J |
| rank 11 | 12 | Q |
| rank 12 | 13 | K |

转换：`fileName = rank + 1`

## 目录结构

```
Assets/
├── Background/      # 背景
├── Table/           # 牌桌
├── Card/            # 扑克牌
├── Character/       # 角色素材
│   ├── Head/
│   ├── Eyes/
│   ├── Ears/
│   └── ...
├── Clothes/         # 服装
├── Eyewear/         # 眼镜
├── Headwear/        # 帽子
├── Accessory/       # 饰品
├── Item/            # 物品
├── Chip/            # 筹码
└── layer_index.json # PSD 导出坐标
```

## PSD 导出规范

### 画布尺寸

- 基准画布：1200x1200（正方形）
- 坐标系 1:1 映射到游戏场景

### 层命名规则

```
类别_具体名称_变体_状态
```

- 每个有意义的部件独立一层
- 同一部件的变体放在同一文件夹下
- 不需要导出的层加前缀 `_` 或放入 `_skip` 文件夹

### 导出工具

Python + psd-tools 库：

```python
from psd_tools import PSDImage
psd = PSDImage.open('file.psd')
for layer in psd:
    image = layer.topil()  # 用 topil 不用 composite（隐藏层也能导出）
    image.save(f'{layer.name}.png')
```

### layer_index.json

```json
{
  "psd": "源文件路径",
  "canvas_width": 1200,
  "canvas_height": 1200,
  "total_layers": 200,
  "layers": [
    {
      "name": "文件夹/文件名",
      "file": "文件夹/文件名.png",
      "x": 0, "y": 0, "w": 100, "h": 100
    }
  ]
}
```

场景布局时直接用这些坐标。

## 游戏中的资源使用

### 纹理加载

```csharp
// 直接加载
var tex = GD.Load<Texture2D>("res://Assets/Card/Spade1.png");

// 动态路径
string path = $"res://Assets/Card/{suit}{rank}.png";
var tex = GD.Load<Texture2D>(path);
```

### AtlasTexture（一个 PNG 多个 sprite）

当一张图包含多个元素时：

1. 右键图片 → New AtlasTexture
2. 设置 Region (x, y, w, h)
3. 赋给 Sprite2D

```gdscene
[gd_resource type="AtlasTexture" format=3"]
[ext_resource type="Texture2D" path="res://Assets/SpriteSheet.png" id="1"]
[resource]
atlas = ExtResource("1")
region = Rect2(0, 0, 64, 64)
```

### ZIndex 层级管理

项目不使用一张覆盖所有场景和装备位的通用绝对 Z 表。Godot 默认 `z_as_relative = true`，子节点的最终层级由父节点层级与自身 ZIndex 共同决定；不同装备位位于不同父节点下，不能把同一个数值理解为相同的全局层级。

Item 表中的 `VisualZOffset` 是物品相对于所属装备位基础层级的本地偏移：`0` 保持默认，正数向前，负数向后。字段只由已经明确接入的显示控制器使用，不得在通用资源加载代码中直接作为绝对 ZIndex 应用。当前仅帽子接入：基础 Z=1，`0` 挡住固定眼镜，`-1` 被固定眼镜和鼻子挡住。

美术素材文件名和 `layer_index.json` 只负责资源身份与 PSD 坐标，不编码视觉层级。一个物品需要拆成前后多个绘制层时，应输出独立素材并使用复合场景或逐资源配置，不能依赖一个整体 Z 偏移实现交叉遮挡。

爪子等必须跨父节点参与全场景排序的特殊节点可以设置 `z_as_relative = false`，但需要由具体场景规范明确其绝对层级。Item 字段说明见 [Item 表字段说明](../numerical/Item表字段说明.md)。

## 音效资源

### 格式

- BGM：OGG Vorbis（循环友好）
- SFX：WAV 16-bit 44100Hz

### 命名

```
Audio/
├── BGM/
│   ├── MainTheme.ogg
│   └── BattleTheme.ogg
└── SFX/
    ├── CardClick.wav
    ├── ChipStack.wav
    ├── Knock.wav
    └── WinFanfare.wav
```

### 工作流

1. AI 创建 .txt 占位文件说明用途
2. 代码打印 `[SFX] 文件名` 提示
3. 用户替换为真实音频文件
4. 文件名必须 PascalCase

## 动态扫描目录

代码运行时扫描目录，加减文件自动生效：

```csharp
var dir = DirAccess.Open("res://Assets/Clothes/");
var files = new List<string>();
dir.ListDirBegin();
string name = dir.GetNext();
while (name != "")
{
    if (name.EndsWith(".png")) files.Add(name);
    name = dir.GetNext();
}
dir.ListDirEnd();
// 随机选一个
var tex = GD.Load<Texture2D>("res://Assets/Clothes/" + files[rng.Next(files.Count)]);
```

适用场景：随机换装、随机背景、随机道具等。
