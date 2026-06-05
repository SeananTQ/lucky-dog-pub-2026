# 美术资源管理

## 目录结构

```
Assets/
├── Background/      # 背景图（1200x1200 或 1200x706）
├── Table/           # 牌桌（1200x594）
├── Card/            # 52 张牌（120x168）
│   ├── Club1~13     # 梅花 A~K
│   ├── Diamond1~13  # 方块
│   ├── Heart1~13    # 红桃
│   └── Spade1~13    # 黑桃
├── Shiba/           # 小狗素材
│   ├── Red/         # 红色变体
│   ├── Black/       # 黑色变体
│   ├── Cream/       # 奶油色变体
│   └── Sesame/      # 芝麻色变体
├── Chip/            # 筹码（单个 + 堆叠）
├── Hand/            # 手臂（MaleA~D + Thumb）
├── Clothes/         # 衣服
├── Eyewear/         # 眼镜
├── Headwear/        # 帽子
├── Accessory/       # 饰品
├── Item/            # 物品（饮料等）
└── layer_index.json # PSD 导出坐标数据
```

## 命名规范

### 文件名：PascalCase

```
Club1.png          ✅
club_1.png         ❌
spade_ace.png      ❌
Spade1.png         ✅
```

### 带变体的素材：BaseAssetName_Variant

```
Head_Chubby.png       # 头部 - 胖版
Eyes_Happy.png        # 眼睛 - 开心
Ears_Plane.png        # 耳朵 - 平
BaseballJacket_Black  # 棒球夹克 - 黑色
```

### 卡牌命名：Suit + Rank(数字)

```
Card_Spade1     = 黑桃 A
Card_Heart11    = 红桃 J
Card_Diamond12  = 方块 Q
Card_Club13     = 梅花 K
```

内部编码：`rank = card % 13`（0=Ace, 1=2, ..., 10=Jack, 11=Queen, 12=King）
文件名：`rank + 1`（1=Ace, 2=2, ..., 11=Jack, 12=Queen, 13=King）

## PSD 导出流程

### 工具

`tools/psd_export/export_layers.py` — Python 脚本，使用 psd-tools 库

### 导出规则

- 画布：1200x1200
- 坐标系：PSD 原始坐标，1:1 映射到游戏场景
- 输出：`assets/output/LuckyDogPub/` + `layer_index.json`
- 隐藏的 SmartObjectLayer 用 `layer.topil()` 而非 `layer.composite()`
- pk2-7 牌组跳过（只需要 pk1 的 52 张牌）

### layer_index.json

```json
{
  "psd": "assets/LuckyDogPub.psd",
  "canvas_width": 1200,
  "canvas_height": 1200,
  "total_layers": 200,
  "layers": [
    {
      "name": "Background/RedA",
      "file": "Background/RedA.png",
      "x": 0, "y": -3, "w": 1200, "h": 1203
    }
  ]
}
```

场景布局时直接用这些坐标定位节点。

## 音效工作流

### 约定

1. AI 在 `Audio/SFX/` 创建 `.txt` 占位文件，说明音效用途
2. 代码用 `AudioManager.Instance.PlaySfxByName("Xxx.wav")` 播放
3. 文件不存在时自动打印 `[SFX] xxx` 到控制台
4. 用户看到输出后找音效文件替换 .txt
5. **文件名必须大驼峰**（如 `Knock.wav`）

### 目录

```
Audio/
├── BGM/           # 背景音乐（OGG 格式）
│   └── MainTheme.ogg
└── SFX/           # 音效（WAV 格式）
    ├── Knock.wav
    ├── CardClick.wav
    └── ChipCollect.wav
```

### AudioManager

- 全局单例（autoload）
- 8 个 SFX 播放器的对象池
- 独立 BGM 播放器
- 音量控制：`SfxVolume` / `BgmVolume`（0.0~1.0 线性值）
- 音频总线：SFX → "SFX" 总线，BGM → "BGM" 总线

### 音频总线设置

在 Godot 编辑器底部 **Audio** 面板里添加 "BGM" 和 "SFX" 两条总线。
代码里已经设好了 `Bus = "BGM"` / `Bus = "SFX"`。

## AtlasTexture 使用

当一个 PNG 包含多个 sprite 时，用 AtlasTexture 裁切：

1. 右键图片 → 新建 AtlasTexture
2. 设置 Region (x, y, w, h) 定义裁切区域
3. 赋给 Sprite2D 使用
4. 可以在 Inspector 里实时调整 Region

.tres 文件格式：
```gdscene
[gd_resource type="AtlasTexture" format=3]

[ext_resource type="Texture2D" path="res://Assets/Xxx.png" id="1_src"]

[resource]
atlas = ExtResource("1_src")
region = Rect2(0, 0, 100, 100)
```

## ZIndex 层级管理

| 元素 | ZIndex | 说明 |
|------|--------|------|
| Background | 0 | 最底层 |
| 狗身体 | 1 | Head/Eyes/Ears |
| 爪子（手心） | 1 | 被桌子挡住 |
| 桌子 | 2 | Table |
| 爪子（手背） | 3 | 挡住桌子 |
| 卡牌/筹码堆 | 4 | 在桌子上 |
| 手臂 | 5 | 最顶层 |

爪子需要设 `z_as_relative = false` 使用绝对 ZIndex。

## 随机换装

代码运行时扫描目录里所有 `.png` 文件，自动生效：
- `Assets/Background/` — 背景
- `Assets/Table/` — 牌桌
- `Assets/Clothes/` — 衣服
- `Assets/Accessory/` — 饰品

加减素材文件不需要改代码。
