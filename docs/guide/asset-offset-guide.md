# 饰品坐标定位指南

## 做法总结

所有饰品贴图（眼镜、帽子等）来自 PSD 的单独图层导出。每张 PNG 保留了它在 1200×1200 画布上的位置信息，记录在 `Assets/layer_index.json` 中：

```json
{
  "name": "Eyewear/Sunglasses_Blade",
  "file": "Eyewear/Sunglasses_Blade.png",
  "x": 365, "y": 365, "w": 442, "h": 100
}
```

### 核心问题

小狗场景已居中到 (0,0)，PSD 坐标不能直接使用。需要将 PSD 坐标转换为场景坐标：

1. 计算 PSD 中心点：`centerX = x + w/2`，`centerY = y + h/2`
2. 用已对齐的参考物推导出偏移量，应用到所有同类饰品上

### 偏移量推导公式

以已对齐的饰品为参考：

| 类别 | 参考饰品 | PSD 中心 | 场景坐标 | 偏移量 |
|------|---------|----------|---------|-------|
| 眼镜 | Sunglasses_Blade | (586, 415) | (0, -262) | `cx - 586`, `cy - 677` |
| 帽子 | FedoraHat_Maroon | (585, 239) | (0, -444) | `cx - 585`, `cy - 683` |

```csharp
// 代码实现（GameManager.cs 的 AssetEntry.FromJson）
var cx = (float)d["x"] + (float)d["w"] / 2f;
var cy = (float)d["y"] + (float)d["h"] / 2f;
var scenePos = isHeadwear
    ? new Vector2(cx - 585f, cy - 683f)
    : new Vector2(cx - 586f, cy - 677f);
```

## 工作流（新增饰品部位）

新增一个饰品类别（如衣服、项链等）的完整流程：

### 设计师（你）负责

1. 在 `DogArea.tscn` 根节点下新建 `Sprite2D`，命名如 `Clothing`
2. 放入一个已知位置的物品贴图
3. 手动调 `Position` 视觉对齐小狗
4. 记录该位置（如 `(10, -100)`）和物品名称

### AI（开发者）负责

5. 从 `layer_index.json` 读该物品的 PSD `(x, y, w, h)`
6. 算 PSD 中心：`(x + w/2, y + h/2)`
7. 偏移量 = 场景坐标 - PSD 中心
8. 在 `AssetEntry.FromJson` 中添加新类别分支
9. 在 `OnRandomizeDog` 中添加设置逻辑

```
你：DogArea.tscn 里调好 Sprite 位置 → 告诉我场景坐标和物品名
AI：读 JSON → 算偏移 → 写代码
```

### 一次性的对接成本

新类别的对接成本只在第一次——确定偏移量之后，后续同类饰品只需 PNG + JSON 记录，不需要再动场景和代码。

## 添加已有类别的饰品

对新眼镜、新帽子这种已有类别的饰品：

1. PNG 放进 `Assets/Eyewear/`（或 `Assets/Headwear/`）
2. 在 `layer_index.json` 中添加记录
3. 完成——不需要改代码和场景

## 验证方法

1. 运行游戏 → 调试面板 → "Randomize Dog" 按钮
2. 多次点击，确认所有饰品位置正确、贴图显示正常
3. 如果某个饰品位置不对，检查 `layer_index.json` 中它的 `(x, y, w, h)` 是否与 PSD 导出一致

## 注意事项

- JSON 中的 `x, y` 是 PSD 图层在画布上的**左上角坐标**（不是中心点）
- `w, h` 是图层导出 PNG 的**实际像素尺寸**（可能小于 1200×1200）
- `.tscn` 场景文件中的饰品节点位置是参考品的对齐位置，程序运行时会用 JSON 数据覆盖
- 同上，调整 DogArea.tscn 中参考品的位置后，要同步更新代码中的偏移量公式
