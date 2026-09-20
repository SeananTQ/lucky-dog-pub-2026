"""
一键导出普通道具图标。

从 PSD 按道具表逐图层导出，缩放到统一尺寸。

用法:
    python export_icons.py [--config export_config.json]
"""

import argparse, json, os, sys
from psd_tools import PSDImage
from psd_tools.constants import Tag
from PIL import Image, ImageDraw
import numpy as np


SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
DEFAULT_BACKGROUND_TEMPLATE = os.path.normpath(os.path.join(
    SCRIPT_DIR, "..", "..", "assets", "psd", "BackgroundItemDemo.psd"))
BACKGROUND_GROUP = "Background"
BACKGROUND_SOURCE_SIZE = (1200, 1200)
BACKGROUND_CROP_BOX = (0, 0, 1200, 834)
BACKGROUND_TEMPLATE_SIZE = (1200, 1200)
BACKGROUND_FRAME_LAYER = "_Frame"
BACKGROUND_SHADOW_LAYER = "_Shadow"
BACKGROUND_POSITION_LAYERS = ("_Postion", "_Position")


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


# ─── 单图层渲染 ──────────────────────────────────────────

def render_layer(lo):
    img = lo.topil()
    if img is None:
        return None
    bbox = img.getbbox()
    return img.crop(bbox) if bbox else None


def render_layer_on_canvas(layer, canvas_size):
    """按 PSD 原始坐标把单个图层放回完整透明画布。"""
    img = layer.topil()
    if img is None:
        return None
    canvas = Image.new("RGBA", canvas_size, (0, 0, 0, 0))
    canvas.alpha_composite(img.convert("RGBA"), (layer.left, layer.top))
    return canvas


def render_template_layer(layer, canvas_size):
    """渲染模板图层，保留智能对象变换、效果和图层不透明度。"""
    img = layer.composite()
    if img is None:
        raise ValueError(f"模板图层无法渲染: {layer.name}")

    canvas = Image.new("RGBA", canvas_size, (0, 0, 0, 0))
    canvas.alpha_composite(img.convert("RGBA"), (layer.left, layer.top))
    if layer.opacity < 255:
        alpha = canvas.getchannel("A").point(
            lambda value: round(value * layer.opacity / 255))
        canvas.putalpha(alpha)
    return canvas


def find_top_level_layer(psd, names):
    expected = set(names)
    return next((layer for layer in psd if layer.name in expected), None)


def clear_outer_border(image, content_size):
    """清空内容安全区之外的像素；256/240 对应四周各 8 像素。"""
    width, height = image.size
    safe_width = min(width, content_size)
    safe_height = min(height, content_size)
    left = (width - safe_width) // 2
    top = (height - safe_height) // 2
    right = left + safe_width
    bottom = top + safe_height

    image.paste((0, 0, 0, 0), (0, 0, width, top))
    image.paste((0, 0, 0, 0), (0, bottom, width, height))
    image.paste((0, 0, 0, 0), (0, top, left, bottom))
    image.paste((0, 0, 0, 0), (right, top, width, bottom))
    return image


class BackgroundIconComposer:
    """复用 BackgroundItemDemo.psd 批量合成相纸式背景图标。"""

    def __init__(self, template_path):
        self.template_path = template_path
        self.psd = PSDImage.open(template_path)
        if self.psd.size != BACKGROUND_TEMPLATE_SIZE:
            raise ValueError(
                "背景图标模板画布必须是 "
                f"{BACKGROUND_TEMPLATE_SIZE[0]}x{BACKGROUND_TEMPLATE_SIZE[1]}，"
                f"当前为 {self.psd.width}x{self.psd.height}: {template_path}")

        frame = find_top_level_layer(self.psd, (BACKGROUND_FRAME_LAYER,))
        shadow = find_top_level_layer(self.psd, (BACKGROUND_SHADOW_LAYER,))
        position = find_top_level_layer(self.psd, BACKGROUND_POSITION_LAYERS)
        missing = []
        if frame is None:
            missing.append(BACKGROUND_FRAME_LAYER)
        if shadow is None:
            missing.append(BACKGROUND_SHADOW_LAYER)
        if position is None:
            missing.append("/".join(BACKGROUND_POSITION_LAYERS))
        if missing:
            raise ValueError(f"背景图标模板缺少顶层图层: {', '.join(missing)}")

        self.frame = render_template_layer(frame, self.psd.size)
        self.shadow = render_template_layer(shadow, self.psd.size)
        self.affine = self._read_position_affine(position)

    @staticmethod
    def _read_position_affine(position):
        try:
            placed = position._record.tagged_blocks.get_data(Tag.PLACED_LAYER2)
            smart_data = position._record.tagged_blocks.get_data(
                Tag.SMART_OBJECT_LAYER_DATA1).data
            source_size = smart_data[b"Sz  "]
            source_width = float(source_size[b"Wdth"])
            source_height = float(source_size[b"Hght"])
            transform = placed.transform
        except Exception as exc:
            raise ValueError(
                f"{position.name} 必须是包含有效变换数据的智能对象") from exc

        if source_width <= 0 or source_height <= 0 or len(transform) != 8:
            raise ValueError(f"{position.name} 的智能对象尺寸或变换数据无效")

        top_left = np.array(transform[0:2], dtype=float)
        top_right = np.array(transform[2:4], dtype=float)
        bottom_right = np.array(transform[4:6], dtype=float)
        bottom_left = np.array(transform[6:8], dtype=float)
        if not np.allclose(
                bottom_right, top_right + bottom_left - top_left, atol=0.5):
            raise ValueError(f"{position.name} 只能使用旋转/缩放/位移，不能包含透视变形")

        axis_x = (top_right - top_left) / source_width
        axis_y = (bottom_left - top_left) / source_height
        forward = np.array([
            [axis_x[0], axis_y[0], top_left[0]],
            [axis_x[1], axis_y[1], top_left[1]],
            [0.0, 0.0, 1.0],
        ])
        try:
            inverse = np.linalg.inv(forward)
        except np.linalg.LinAlgError as exc:
            raise ValueError(f"{position.name} 的变换矩阵不可逆") from exc
        return tuple(inverse[:2, :].reshape(-1))

    def compose(self, source_layer, source_psd_size, output_size, content_size):
        if tuple(source_psd_size) != BACKGROUND_SOURCE_SIZE:
            raise ValueError(
                "Background 特殊图标流程要求源 PSD 画布为 "
                f"{BACKGROUND_SOURCE_SIZE[0]}x{BACKGROUND_SOURCE_SIZE[1]}，"
                f"当前为 {source_psd_size[0]}x{source_psd_size[1]}")

        source = render_layer_on_canvas(source_layer, source_psd_size)
        if source is None:
            raise ValueError(f"背景图层无法渲染: {source_layer.name}")
        cropped = source.crop(BACKGROUND_CROP_BOX)
        positioned = cropped.transform(
            self.psd.size,
            Image.Transform.AFFINE,
            self.affine,
            resample=Image.Resampling.BICUBIC,
            fillcolor=(0, 0, 0, 0),
        )

        composed = Image.new("RGBA", self.psd.size, (0, 0, 0, 0))
        composed.alpha_composite(self.shadow)
        composed.alpha_composite(positioned)
        composed.alpha_composite(self.frame)
        composed = composed.resize((output_size, output_size), Image.Resampling.LANCZOS)
        return clear_outer_border(composed, content_size)


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
    out_dir = resolve(cfg["输出目录"])
    background_template_value = cfg.get("背景图标模板PSD", DEFAULT_BACKGROUND_TEMPLATE)
    background_template_path = resolve(background_template_value)

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
    background_ok = 0
    background_composer = None

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
            if actual_group == BACKGROUND_GROUP:
                if background_composer is None:
                    if not os.path.exists(background_template_path):
                        raise FileNotFoundError(
                            f"背景图标模板 PSD 不存在: {background_template_path}")
                    background_composer = BackgroundIconComposer(background_template_path)
                    print(f"背景图标模板: {background_template_path}")
                canvas = background_composer.compose(lo, psd.size, size, content)
                background_ok += 1
            else:
                use_short = actual_group in short_side_groups
                img = render_layer(lo)
                if not img:
                    print(f"  [FAIL] [{item_id}] {item_name}")
                    break
                canvas = postprocess(img, use_short, size, content, margin)
            canvas.save(out_path)
            ok += 1
            break

    print(f"\n完成: 共导出 {ok} 张图标")
    if background_ok:
        print(f"  相纸背景图标: {background_ok}")


if __name__ == "__main__":
    main()
