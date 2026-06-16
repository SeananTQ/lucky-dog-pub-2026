# PSD 道具图标导出工作流

## 工具链

- **psd_tools 1.17.1** — 读取 PSD 结构、重命名图层、获取像素
- **Pillow** — 图像处理（裁剪、缩放、圆角）
- **numpy** — alpha 通道运算
- **aggdraw** — 矢量形状图层渲染（可选，部分 ShapeLayer 需要）

## 一键导出

```bash
# 修改 export_config.json 里的路径后直接运行：
python export_icons.py

# 或指定其他配置：
python export_icons.py --config my_config.json
```

### 配置项

```json
{
    "psd路径": "LuckyDogPub-Test02.psd",
    "道具表路径": "tbitem.json",
    "狗皮肤表路径": "tbdogskin.json",
    "输出目录": "output",
    "画布尺寸": 256,
    "内容尺寸": 240,
    "圆角半径": 16,
    "短边物品类型": [6, 7]
}
```

- `短边物品类型`：以短边为准缩放后居中裁剪（桌布、背景等需要填满画面的物品）
- 其他物品以长边为准缩放，短边居中留白

## 核心流程

```
tbitem.json ──→ 遍历每个物品
                     │
                     ├─ ItemType=1 (狗)
                     │   └─ tbdogskin.json 读取 DefaultEars/DefaultEyes
                     │   └─ psd.composite() 全图渲染（狗需要多图层合成）
                     │
                     └─ 其他 → 从 AssetPathList 提取 {文件夹}/{文件名}
                                │
                                ├─ 映射到 PSD 组名 → 找到图层对象
                                │
                                ├─ layer.topil() 获取像素
                                │
                                ├─ 裁剪实际内容 (getbbox)
                                │
                                ├─ 缩放
                                │   ├─ 普通物品：以长边为准缩放到 content_size
                                │   └─ 短边物品：以短边为准缩放后居中裁剪到 content_size²
                                │
                                ├─ 居中粘贴到 canvas_size² 画布
                                │
                                └─ alpha 相乘实现圆角（不是 putalpha 替换！）
                                    保存 PNG
```

## 关键注意事项

### 1. 普通道具用 `topil()`，狗用 `composite()`

```python
# ✅ 普通道具：直接获取图层原始像素，颜色准确
img = layer.topil()

# ✅ 狗皮肤：全图合成才能正确渲染多个图层的叠加
full = psd.composite()  # 只显示需要的狗部位，其他全部隐藏
```

**原因**：普通道具是单个图层，`topil()` 直接读取嵌入像素数据，颜色准确。狗皮肤需要多图层合成（皮肤+头+耳朵+眼睛+爪子），必须在 PSD 上下文里用 `composite()` 渲染。如果对狗用 `topil()` 再手动叠加，边缘会虚。

### 2. 圆角用 alpha 相乘，不是替换

```python
# ✅ 正确：遮罩与现有 alpha 取最小值（只切四角）
mask = Image.new("L", (CANVAS, CANVAS), 255)
ImageDraw.Draw(mask).rounded_rectangle(
    [0, 0, CANVAS-1, CANVAS-1], radius=RADIUS, fill=0)
arr = np.array(canvas)
arr[:, :, 3] = np.minimum(arr[:, :, 3], 255 - np.array(mask))
canvas = Image.fromarray(arr, "RGBA")

# ❌ 错误：putalpha 替换整个 alpha，会把透明背景变成黑色不透明
canvas.putalpha(mask)
```

`putalpha(mask)` 将画布上**所有**位于圆角矩形内的像素设为不透明。画布上物品周围的区域是 `(0,0,0,0)`，变成不透明后就是 `(0,0,0,255)`——纯黑。alpha 相乘则只切掉四个角，其他区域保持原透明度。

### 3. 缩放策略

```python
if use_short_side:
    # 短边 = CONTENT → 适合桌布/背景这种需要填满画面的
    scale = CONTENT / min(w, h)
    img = img.resize((int(w*scale), int(h*scale)), Image.LANCZOS)
    # 居中裁剪到正方形
    left, top = (nw-CONTENT)//2, (nh-CONTENT)//2
    img = img.crop((left, top, left+CONTENT, top+CONTENT))
else:
    # 长边 = CONTENT → 适合头饰/眼镜等有固定形状的
    scale = CONTENT / max(w, h)
    img = img.resize((int(w*scale), int(h*scale)), Image.LANCZOS)
```

### 4. 依赖

```bash
pip install psd-tools Pillow numpy
pip install aggdraw          # ShapeLayer 矢量渲染（可选）
```

## 文件结构

```
photoshop-tools-test/
├── export_config.json            # 导出配置（路径、尺寸）
├── export_icons.py               # 一键导出脚本
├── export_icons_workflow.md      # 本文档
├── tbitem.json                   # 道具表（含 AssetPathList）
├── tbdogskin.json                # 狗皮肤表（含默认表情）
├── LuckyDogPub-Test02.psd        # PSD 源文件（经重命名）
└── output/                       # 导出的 PNG 图标
```
