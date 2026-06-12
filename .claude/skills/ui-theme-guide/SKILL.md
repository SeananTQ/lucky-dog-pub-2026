---
name: ui-theme-guide
description: 当需要创建UI节点时，先阅读此守则
---

# Lucky Dog Rise — Theme Guide

## 项目中的资源

```
Themes/
├── DefaultTheme.tres              # 全局 Theme（Button/Label/PanelContainer/CheckButton 等）
├── DefaultTheme_V1Bak.tres        # V1 备份
├── Buttons/
│   ├── Primary_{Normal,Hover,Pressed,Disabled}.tres        # 绿色主按钮
│   ├── Secretary_{Normal,Hover,Pressed,Disabled}.tres      # 蓝色次要按钮
│   ├── Action_{Normal,Hover,Pressed,Disabled}.tres         # 橙色强调按钮
│   └── Destructive_{Normal,Hover,Pressed,Disabled}.tres    # 玫红危险按钮
├── Label_KeyText.tres             # 键文字样式（小号、白、描边阴影）
├── Label_ValueText.tres           # 值文字样式（大号、亮色、描边阴影）
├── Panel_ValuePanel.tres          # 信息面板内层容器背景
└── Style_BoxFlat_*.tres           # 各按钮颜色状态独立文件

Scenes/Prefabs/UIPrefabs/          # 信息面板预制件（6种）
├── H_FixedWidth.tscn              # 水平固定宽度，左键右值（含 Spacer）
├── H_AutoWidth.tscn               # 水平自动宽度，居中对齐
├── V_Standard.tscn                # 垂直标准，键在上值在下
├── V_RichText.tscn                # 垂直 + RichTextLabel（分数/混排）
├── O_NestedValue.tscn             # 纯值，带内层叠层面板
└── O_DirectValue.tscn             # 纯值，直接 Label

Scenes/UIThemePreview.tscn         # UI 预览场景（运行时查看所有控件效果）
```

## 控件 → Theme 路径

| 控件 | Theme 路径 | 说明 |
|------|-----------|------|
| Button | `Button/styles/{normal,hover,pressed,disabled}` | 默认绿色。变体用 theme_override 指向 Buttons/ 下的文件 |
| Label | `Label/colors/font_color`, `Label/fonts/font` | 正文 26px。键值对通过 LabelSettings 单独控制 |
| CheckButton | `CheckButton/icons/{checked,unchecked,...}` | 内联 SVG 滑块，绿轨道/白色滑块 |
| CheckBox | `CheckBox/icons/checked`, `unchecked` | 绿筹码/黑筹码 |
| PanelContainer | `PanelContainer/styles/panel` | 暗底圆角。内层 ValuePanel 用 theme_override |
| HSlider | `HSlider/styles/{slider,grabber,...}` | 滑动条 |
| LineEdit | `LineEdit/styles/normal` | 文本输入框 |
| TextEdit | 同上（多行版） | 文本区域 |
| LabelSettings | 直接引用 .tres | 键（KeyText）和值（ValueText）分别设置 |

## 按钮变体用法

场景里需要其他颜色的按钮时：

```gdscript
var btn = GetNode<Button>("MyButton")
btn.AddThemeStyleboxOverride("normal", GD.Load<StyleBox>("res://Themes/Buttons/Action_Normal.tres"))
btn.AddThemeStyleboxOverride("hover", GD.Load<StyleBox>("res://Themes/Buttons/Action_Hover.tres"))
btn.AddThemeStyleboxOverride("pressed", GD.Load<StyleBox>("res://Themes/Buttons/Action_Pressed.tres"))
btn.AddThemeStyleboxOverride("disabled", GD.Load<StyleBox>("res://Themes/Buttons/Action_Disabled.tres"))
```

或在编辑器中直接拖拽 ext_resource + theme_override。

## 信息面板预制件用法

1. 场景中右键 → `Instance Child Scene` → 选择 `Scenes/Prefabs/UIPrefabs/*.tscn`
2. 改 Key / Value Label 的 `text` 属性
3. 如需调整宽度，改外层 PanelContainer 的 `custom_minimum_size`

## UIThemePreview 场景

`Scenes/UIThemePreview.tscn` 包含完整的控件预览：

| 区域 | 包含内容 |
|------|---------|
| Labels | 14/18/22/26/32 号字体 + LineEdit |
| Buttons | Primary / 中文 / Flat / Disabled / Action / Destructive / Secretary |
| Toggles | CheckButton × 2 + CheckBox × 2 |
| Containers | PanelContainer 嵌套示例 |
| Other | ScrollContainer、TextureRect、TextEdit |
| Info Panels | H/FixedWidth、H/AutoWidth、V/Standard、V/RichText、O/Nested、O/Direct |

## 设计规则

- **字体**：Inter（英文主字体）→ Mi Sans（中文 fallback）
- **按钮**：12px 圆角，底部 6px 深色台阶，pressed 时文字下沉 6px
- **信息面板**：外层 PanelContainer（暗底）+ 内层 ValuePanel（稍亮 `#33454A`）
- **颜色**：主绿 `#58CC02` / 深绿 `#338000` / 橙 `#FF9800` / 蓝 `#0093FF` / 玫红 `#C90060`
- **文字**：白色，`outline_size=2` + 半透投影，小尺寸可读
- **面板底色**：`#1B2838`（深灰蓝）
