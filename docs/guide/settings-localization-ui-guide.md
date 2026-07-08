# 设置面板多语言 UI 处理指南

## 背景

设置面板在多语言环境下会遇到两类特殊 UI 问题：

- 设置项标题在英文、日文等语言中可能显著变长，单行显示容易挤压右侧控件。
- Godot 原生 `OptionButton` 展开后的 `PopupMenu` 会受 Theme、滚动容器和自身内部最小宽度影响，不能简单按按钮文字宽度推断最终显示效果。

本指南记录当前项目中已经验证可用的处理方式。后续维护设置页、多语言文本或 Theme 时，优先沿用这些约定，避免重复试错。

## 设置项标题允许双行

玩家可见的设置项标题使用普通 `Label`，并允许在需要时自动折成两行。

当前采用的核心配置：

```text
custom_minimum_size = Vector2(300, 0)
size_flags_horizontal = 3
autowrap_mode = 3
clip_text = false
text_overrun_behavior = 0
```

含义：

- `custom_minimum_size.x = 300` 给 Label 一个稳定的换行参考宽度。
- `custom_minimum_size.y = 0` 不写死行高，短文本仍保持单行高度。
- `size_flags_horizontal = 3` 让 Label 吃掉行内剩余宽度，避免左侧出现无意义缩进。
- `autowrap_mode = 3` 使用智能换行，英文尽量按单词换行，中文和日文也能正常折行。
- `clip_text = false` 避免第二行被裁掉。
- `text_overrun_behavior = 0` 避免长文本优先显示省略号而不是换行。

右侧 `CheckButton` 使用：

```text
size_flags_vertical = 4
```

这样一行文本和两行文本混排时，开关能保持垂直居中。

## 不推荐的做法

不要给设置项标题使用固定两行高度，例如：

```text
custom_minimum_size = Vector2(300, 80)
```

这种做法能让长文本显示两行，但短文本也会占用两行高度，导致列表变松散。

不要在设置项标题上使用：

```text
text_overrun_behavior = 4
```

它会让文本走省略号策略，阻止期望中的第二行显示。

不要只依赖 `HBoxContainer` 的默认宽度计算。多语言长文本会让 Label 的原始最小宽度变得很大，进而挤压或裁切右侧控件。

## 下拉菜单样式

设置页下拉菜单继续使用 Godot 原生 `OptionButton` 和内部 `PopupMenu`。

当前样式沉淀在：

```text
lucky-dog-rise/Themes/DefaultTheme.tres
```

涉及的 Theme 类型：

```text
OptionButton
PopupMenu
```

当前约定：

- `OptionButton` 字号使用 `15`。
- `OptionButton` 本体颜色使用 `#1c464f`。
- `OptionButton` hover / focus / pressed 使用 `#235763`。
- 本体圆角较小，当前为 `5`。
- 本体上下边距较小，避免按钮高度明显高于同一行 Label。
- `PopupMenu` 背景使用 `#1c464f`。
- `PopupMenu` hover 使用 `#235763`。
- `PopupMenu` 的 `item_start_padding` / `item_end_padding` 当前为 `8`。

这些配置应优先放在 Theme 中，而不是散落在具体场景节点的 Theme Override 中。

## 下拉菜单尺寸

真实设置面板中当前使用的尺寸：

```text
LanguageOption:
custom_minimum_size = Vector2(172, 28)

DisplayOption:
custom_minimum_size = Vector2(112, 28)
```

语言下拉需要容纳较长语言名，因此比普通短选项下拉更宽。类似 `DisplayOption` 这种短选项下拉不应盲目使用语言下拉的宽度。

## ScrollContainer 中的右侧安全占位

设置页内容在 `ScrollContainer` 中显示。滚动条会占据右侧视觉空间，导致靠右控件和展开后的 `PopupMenu` 看起来贴得过紧。

当前正式设置页中，下拉行右侧保留一个很小的透明占位：

```text
RightScrollbarGap:
custom_minimum_size = Vector2(4, 0)
```

该占位只用于玩家可见设置页的下拉行：

- `LanguageRow`
- `DisplayRow`

实测结论：

- `20px` 能明显避让滚动条，但浪费横向空间。
- `6px` 可用，但略显松。
- `4px` 在正式游戏设置面板中显示较自然。

该值属于视觉微调值，不应扩展为所有行的通用规则。普通开关行暂不需要额外右侧占位。

## 关于原生 OptionButton 的限制

`OptionButton` 的弹出菜单宽度不能完全独立于按钮宽度控制。

实际表现受以下因素共同影响：

- 按钮本体宽度。
- `PopupMenu` 内部最小宽度。
- 选中项 radio 圆点列。
- `PopupMenu` 的 item padding。
- `PopupMenu` panel 和 hover style 的 content margin。
- 所处父容器和滚动条的视觉占位。

原生控件没有提供一个简单可靠的“PopupMenu 右边缘与面板右边缘对齐”配置项。

曾经验证过的做法：

- 用 C# 在 `AboutToPopup` 中移动 `PopupMenu.Position`，效果不稳定，不采用。
- 用 C# 在运行时清除选中圆点，可行，但属于额外运行时干预。当前保留原生圆点，避免增加维护风险。
- 通过加宽按钮间接加宽 popup 可行，但会让短选项下拉也变宽，不适合作为通用方案。

当前推荐做法：

- 保留原生 `OptionButton`。
- 样式放入 `DefaultTheme.tres`。
- 不用 C# 干预选中圆点。
- 对需要的下拉行使用少量右侧安全占位。
- 只在确实无法接受原生限制时，再考虑自制下拉控件。

## 维护建议

新增设置项时：

1. 如果右侧是开关，标题 Label 按“设置项标题允许双行”的配置处理。
2. 如果右侧是下拉菜单，优先使用 Theme 中的 `OptionButton` / `PopupMenu` 样式。
3. 如果下拉菜单在 `ScrollContainer` 内且靠右显示，按实际视觉效果决定是否增加 `RightScrollbarGap`。
4. 不要为了某一种语言把 CSV 翻译写成不自然的缩写。优先让 UI 布局承受合理长度。
5. 若某种语言仍然过长，先考虑改翻译文案，再考虑调整控件宽度。
