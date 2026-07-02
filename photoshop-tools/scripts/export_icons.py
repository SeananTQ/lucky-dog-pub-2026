"""
一键导出道具图标。

从 PSD 按道具表逐图层导出，缩放到统一尺寸。
狗皮肤从狗皮肤表读取显隐规则，用 PSD 合成渲染。

用法:
    python export_icons.py [--config export_config.json]
"""

import argparse, json, os, sys
from psd_tools import PSDImage
from PIL import Image, ImageDraw
import numpy as np


# ─── 索引 ────────────────────────────────────────────────

def build_index(psd):
    """
    建立 {图层名: [(图层对象, 组名), ...]} 索引。
    同一图层名可能出现在多个组（如各颜色组下的 Ears_Happy），记录全部。
    """
    index = {}
    for layer in psd.descendants():
        if layer.is_group():
            group_name = layer.name
            for child in layer:
                if not child.is_group():
                    index.setdefault(child.name, []).append((child, group_name))
    return index


# ─── 图层查找 ────────────────────────────────────────────

def find_layer(index, fname, hint_group=None):
    """
    在索引中按名查找图层。
    唯一匹配直接返回；多个同名时用 hint_group 消歧。
    """
    entries = index.get(fname, [])
    if not entries:
        return None
    if len(entries) == 1:
        return entries[0]  # (layer_obj, group_name)
    if hint_group:
        for lo, grp in entries:
            if grp == hint_group:
                return (lo, grp)
    # 多个匹配且消歧失败 → 报告
    groups = [g for _, g in entries]
    print(f"    [WARN] '{fname}' 重名: {groups}, 请确认路径中的组名")
    return entries[0]


# ─── 路径解析 ────────────────────────────────────────────

def parse_path(ap):
    """从后往前解析路径，返回 (组名, 文件名无扩展)"""
    parts = ap.replace("\\\\", "\\").strip("\\").split("\\")
    if len(parts) < 2:
        return None, None
    file = parts[-1]
    if not file:
        return None, None
    fname = os.path.splitext(file)[0]
    # 组名在文件名前面一层；对于 Shiba/Red/ 这种路径，组名在倒数第二层
    group = parts[-2] if len(parts) >= 2 else None
    return group, fname


# ─── 狗皮肤导出 ──────────────────────────────────────────

def _hide_all(container):
    for l in container:
        if hasattr(l, 'visible'):
            l.visible = False
        if hasattr(l, '__iter__'):
            _hide_all(l)


def export_dog(psd, color_name, head_file, default_ears, default_eyes):
    _hide_all(psd)
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
                                or n == head_file or n == default_ears or n == default_eyes):
                            child.visible = True
    full = psd.composite()
    if full is None:
        return None
    bbox = full.getbbox()
    return full.crop(bbox) if bbox else None


# ─── 单图层渲染 ──────────────────────────────────────────

def render_layer(lo):
    img = lo.topil()
    if img is None:
        return None
    bbox = img.getbbox()
    return img.crop(bbox) if bbox else None


# ─── 后处理 ──────────────────────────────────────────────

def postprocess(img, use_short, canvas_size, content_size, margin):
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

    # 边框留白：alpha 相乘，不替换
    if margin > 0:
        mask = Image.new("L", (canvas_size, canvas_size), 255)
        ImageDraw.Draw(mask).rounded_rectangle(
            [0, 0, canvas_size - 1, canvas_size - 1], radius=margin, fill=0)
        arr = np.array(canvas)
        arr[:, :, 3] = np.minimum(arr[:, :, 3], 255 - np.array(mask))
        canvas = Image.fromarray(arr, "RGBA")

    return canvas


# ─── 主流程 ──────────────────────────────────────────────

def main():
    parser = argparse.ArgumentParser(description="道具图标导出工具")
    parser.add_argument("config_path", nargs="?", help="配置文件路径")
    parser.add_argument("--config", dest="config_option", help="配置文件路径")
    args = parser.parse_args()

    cfg_path = args.config_option or args.config_path or os.path.join("configs", "export_config.json")
    with open(cfg_path, encoding="utf-8") as f:
        cfg = json.load(f)

    base_dir = os.getcwd()

    def resolve(p):
        return p if os.path.isabs(p) else os.path.normpath(os.path.join(base_dir, p))

    psd_path = resolve(cfg["psd路径"])
    item_json = resolve(cfg["道具表路径"])
    dog_json = resolve(cfg.get("狗皮肤表路径", ""))
    out_dir = resolve(cfg["输出目录"])

    for p in [psd_path, item_json]:
        if not os.path.exists(p):
            print(f"错误: 文件不存在 - {p}")
            sys.exit(1)

    size = cfg.get("画布尺寸", 256)
    content = cfg.get("内容尺寸", 240)
    margin = cfg.get("边框留白", 16)
    short_side_groups = set(cfg.get("短边缩放组", []))

    psd = PSDImage.open(psd_path)
    index = build_index(psd)

    with open(item_json, encoding="utf-8") as f:
        items = json.load(f)

    os.makedirs(out_dir, exist_ok=True)
    ok = 0

    for item in items:
        item_id = item["Id"]
        item_name = item["Name"]
        icon_name = os.path.basename(item.get("IconPath", f"item_{item_id}.png"))
        out_path = os.path.join(out_dir, icon_name)

        if item["ItemType"] == 1:
            continue

        for ap in item["AssetPathList"]:
            hint_group, fname = parse_path(ap)
            if not fname:
                break

            entry = find_layer(index, fname, hint_group)
            if not entry:
                print(f"  [MISS] [{item_id}] {item_name}: '{fname}'")
                break

            lo, actual_group = entry
            use_short = actual_group in short_side_groups

            img = render_layer(lo)
            if not img:
                print(f"  [FAIL] [{item_id}] {item_name}")
                break

            canvas = postprocess(img, use_short, size, content, margin)
            canvas.save(out_path)
            ok += 1
            break

    # 狗皮肤
    if dog_json and os.path.exists(dog_json):
        with open(dog_json, encoding="utf-8") as f:
            skins = json.load(f)

        for skin in skins:
            color_name = os.path.basename(skin["FolderPath"])
            head_file = skin["Head"].replace(".png", "")
            default_ears = skin.get("DefaultEars", "Ears_Happy.png").replace(".png", "")
            default_eyes = skin.get("DefaultEyes", "Eyes_Happy.png").replace(".png", "")
            icon_name = skin.get("IconName", f"dog_{skin['Id']}.png")
            out_path = os.path.join(out_dir, icon_name)

            img = export_dog(psd, color_name, head_file, default_ears, default_eyes)
            if img:
                postprocess(img, False, size, content, margin).save(out_path)
                ok += 1
                print(f"  [OK]   [{skin['Id']}] {color_name}/{head_file} {icon_name}")
            else:
                print(f"  [FAIL] [{skin['Id']}] {color_name}")

    print(f"\n完成: 共导出 {ok} 张图标")


if __name__ == "__main__":
    main()
