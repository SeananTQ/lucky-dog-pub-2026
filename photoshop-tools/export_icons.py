"""
一键导出道具图标。

从 PSD 按道具表逐图层导出，缩放到统一尺寸。
狗皮肤从狗皮肤表读取显隐规则，用 PSD 合成渲染。

用法:
    python export_icons.py [--config export_config.json]
"""

import json, os, sys
from psd_tools import PSDImage
from PIL import Image, ImageDraw
import numpy as np


def load_config(path):
    with open(path, encoding="utf-8") as f:
        cfg = json.load(f)
    return cfg


def resolve_path(base, p):
    if os.path.isabs(p):
        return p
    return os.path.normpath(os.path.join(base, p))


# ─── 索引 ────────────────────────────────────────────────

def build_item_index(psd, folder_map):
    """建立 {组名: {图层名: 图层对象}} 索引"""
    idx = {}
    for layer in psd.descendants():
        if layer.is_group() and layer.name in folder_map.values():
            idx[layer.name] = {c.name: c for c in layer.descendants() if not c.is_group()}
    return idx


def build_shiba_index(psd):
    """建立 {颜色: {图层名: 图层对象}} 索引"""
    for layer in psd.descendants():
        if layer.is_group() and layer.name == "Shiba":
            return {sub.name: {c.name: c for c in sub.descendants() if not c.is_group()}
                    for sub in layer if hasattr(sub, 'is_group') and sub.is_group()}
    return {}


# ─── 导出单个图层（普通道具） ─────────────────────────────

def export_layer(lo, use_short, canvas_size, content_size, radius):
    img = lo.topil()
    if img is None:
        return None
    bbox = img.getbbox()
    if not bbox:
        return None
    img = img.crop(bbox)
    return postprocess(img, use_short, canvas_size, content_size, radius)


# ─── 导出狗皮肤 ──────────────────────────────────────────

def _hide_all(container):
    for l in container:
        if hasattr(l, 'visible'):
            l.visible = False
        if hasattr(l, '__iter__'):
            _hide_all(l)


def export_dog(psd, color_name, head_file, default_ears, default_eyes,
               canvas_size, content_size, radius):
    _hide_all(psd)

    # 显示 Shiba > {Color} + 需要的部件
    for layer in psd.descendants():
        if layer.is_group() and layer.name == "Shiba":
            layer.visible = True
            for sub in layer:
                if hasattr(sub, 'is_group') and sub.is_group() and sub.name == color_name:
                    sub.visible = True
                    for child in sub:
                        if child.is_group():
                            continue
                        n = child.name
                        if (n.startswith("Skin_") or n.startswith("Claw_")
                                or n == head_file
                                or n == default_ears
                                or n == default_eyes):
                            child.visible = True

    full = psd.composite()
    if full is None:
        return None
    bbox = full.getbbox()
    if not bbox:
        return None
    return postprocess(full.crop(bbox), False, canvas_size, content_size, radius)


# ─── 后处理（裁剪→缩放→居中→圆角） ─────────────────────

def postprocess(img, use_short, canvas_size, content_size, radius):
    """统一后处理流水线"""
    w, h = img.size
    if use_short:
        scale = content_size / min(w, h)
        nw, nh = int(w * scale), int(h * scale)
        img = img.resize((nw, nh), Image.LANCZOS)
        left = (nw - content_size) // 2
        top = (nh - content_size) // 2
        img = img.crop((left, top, left + content_size, top + content_size))
    else:
        scale = content_size / max(w, h)
        nw, nh = int(w * scale), int(h * scale)
        img = img.resize((nw, nh), Image.LANCZOS)

    canvas = Image.new("RGBA", (canvas_size, canvas_size), (0, 0, 0, 0))
    cx, cy = (canvas_size - img.width) // 2, (canvas_size - img.height) // 2
    canvas.paste(img, (cx, cy), img)

    # 圆角：alpha 相乘，不替换
    mask = Image.new("L", (canvas_size, canvas_size), 255)
    ImageDraw.Draw(mask).rounded_rectangle(
        [0, 0, canvas_size - 1, canvas_size - 1], radius=radius, fill=0)
    arr = np.array(canvas)
    arr[:, :, 3] = np.minimum(arr[:, :, 3], 255 - np.array(mask))
    return Image.fromarray(arr, "RGBA")


# ─── 主流程 ──────────────────────────────────────────────

FOLDER_TO_GROUP = {
    "Headwear": "Headwear", "Eyewear": "Eyewear",
    "Hand": "Arm", "Clothes": "Clothes",
    "Table": "Table", "Background": "Background",
    "Accessory": "Accessory", "Item": "Item",
}


def main():
    cfg_path = sys.argv[1] if len(sys.argv) > 1 else "export_config.json"
    cfg = load_config(cfg_path)
    script_dir = os.path.dirname(os.path.abspath(__file__))

    psd_path = resolve_path(script_dir, cfg["psd路径"])
    item_json_path = resolve_path(script_dir, cfg["道具表路径"])
    dogskin_json_path = resolve_path(script_dir, cfg["狗皮肤表路径"])
    out_dir = resolve_path(script_dir, cfg["输出目录"])

    for p in [psd_path, item_json_path, dogskin_json_path]:
        if not os.path.exists(p):
            print(f"错误: 文件不存在 - {p}")
            sys.exit(1)

    canvas_size = cfg.get("画布尺寸", 256)
    content_size = cfg.get("内容尺寸", 240)
    radius = cfg.get("圆角半径", 16)
    short_side_types = set(cfg.get("短边物品类型", [6, 7]))

    psd = PSDImage.open(psd_path)

    with open(item_json_path, encoding="utf-8") as f:
        items = json.load(f)

    # 建立索引
    item_index = build_item_index(psd, FOLDER_TO_GROUP)
    shiba_index = build_shiba_index(psd)  # 用于验证

    os.makedirs(out_dir, exist_ok=True)

    total = len(items)
    ok = 0

    for item in items:
        item_type = item["ItemType"]
        item_id = item["Id"]
        item_name = item["Name"]
        icon_name = os.path.basename(item.get("IconPath", f"item_{item_id}.png"))
        out_path = os.path.join(out_dir, icon_name)

        if item_type == 1:  # 狗皮肤
            # 从狗皮肤表查找
            continue  # 在下面单独处理
        else:
            use_short = item_type in short_side_types
            for ap in item["AssetPathList"]:
                norm = ap.replace("\\\\", "\\")
                parts = norm.split("\\")
                ai = next(i for i, p in enumerate(parts) if p.lower() == "assets")
                rel = parts[ai + 1:]
                folder, file = rel[0], rel[-1]
                fname = os.path.splitext(file)[0]
                pg = FOLDER_TO_GROUP.get(folder)
                if not pg:
                    break
                lo = item_index.get(pg, {}).get(fname)
                if not lo:
                    print(f"  [MISS] [{item_id}] {item_name}: '{fname}' in '{pg}'")
                    break
                result = export_layer(lo, use_short, canvas_size, content_size, radius)
                if result:
                    result.save(out_path)
                    ok += 1
                else:
                    print(f"  [FAIL] [{item_id}] {item_name}")
                break

    # 单独处理狗皮肤
    if os.path.exists(dogskin_json_path):
        with open(dogskin_json_path, encoding="utf-8") as f:
            skins = json.load(f)

        for skin in skins:
            color_name = os.path.basename(skin["FolderPath"])
            head_file = skin["Head"].replace(".png", "")
            default_ears = skin.get("DefaultEars", "Ears_Happy.png").replace(".png", "")
            default_eyes = skin.get("DefaultEyes", "Eyes_Happy.png").replace(".png", "")
            icon_name = skin.get("IconName", f"dog_{skin['Id']}.png")
            out_path = os.path.join(out_dir, icon_name)

            result = export_dog(psd, color_name, head_file, default_ears, default_eyes,
                                canvas_size, content_size, radius)
            if result:
                result.save(out_path)
                ok += 1
                print(f"  [OK]   [{skin['Id']}] {color_name}/{head_file} {icon_name}")
            else:
                print(f"  [FAIL] [{skin['Id']}] {color_name}")

    print(f"\n完成: 共导出 {ok} 张图标")


if __name__ == "__main__":
    main()
