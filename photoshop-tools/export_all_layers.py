"""
PSD 全量导出工具

按 PSD 图层组结构导出所有非辅助图层为 PNG，
同时生成 layer_index.json 坐标信息。

用法:
    python export_all_layers.py --psd <PSD路径> --out <导出目录>
"""

import os, sys, json, re, argparse
from pathlib import Path
from psd_tools import PSDImage


def get_group_path(layer, root_name="Root"):
    """从图层向上遍历，返回组路径列表（顶层组在前）"""
    path = []
    p = layer.parent
    while p is not None and hasattr(p, 'is_group') and p.is_group():
        if p.name != root_name:
            path.append(p.name)
        p = p.parent if hasattr(p, 'parent') else None
    path.reverse()
    return path


def has_skip_ancestor(layer):
    """检查图层是否有以 _ 开头的祖先组"""
    p = layer.parent
    while p is not None and hasattr(p, 'is_group') and p.is_group():
        if p.name.startswith("_"):
            return True
        p = p.parent if hasattr(p, 'parent') else None
    return False


def should_skip(layer):
    """跳过辅助图层：自身或祖先组以 _ 开头"""
    if layer.name.startswith("_"):
        return True
    return has_skip_ancestor(layer)


def has_content(layer) -> bool:
    bbox = layer.bbox
    if bbox is None:
        return False
    w = bbox[2] - bbox[0] if not hasattr(bbox, 'width') else bbox.width
    h = bbox[3] - bbox[1] if not hasattr(bbox, 'height') else bbox.height
    return w > 0 and h > 0


def safe_filename(name: str) -> str:
    return re.sub(r'[:/\\ ]', '_', name)


def export_all_layers(psd_path: str, out_dir: str, crop_blank: bool = True, use_composite: bool = False):
    psd_name = Path(psd_path).stem
    psd = PSDImage.open(psd_path)

    os.makedirs(out_dir, exist_ok=True)
    print(f"打开 PSD: {psd_path} ({psd.width}x{psd.height})")

    all_layers = list(psd.descendants())
    exported = []
    exported_count = 0
    skipped_count = 0

    for layer in all_layers:
        if layer.is_group():
            continue
        if not has_content(layer):
            skipped_count += 1
            continue
        if should_skip(layer):
            skipped_count += 1
            continue

        # 构建组路径
        group_path = get_group_path(layer)
        # 如果组路径为空，放到根层级
        folder = os.path.join(*group_path) if group_path else "_root"

        # 文件名
        filename = f"{safe_filename(layer.name)}.png"
        file_rel = os.path.join(folder, filename)

        # 渲染
        try:
            if use_composite:
                # 合成渲染：只显示目标图层，确保图层效果正确
                for l_hide in all_layers:
                    try:
                        l_hide.visible = False
                    except AttributeError:
                        pass
                # 显示目标图层的父组链
                p = layer.parent
                while p is not None and hasattr(p, 'is_group') and p.is_group():
                    try:
                        p.visible = True
                    except AttributeError:
                        pass
                    p = p.parent if hasattr(p, 'parent') else None
                layer.visible = True
                img = psd.composite()
            else:
                img = layer.topil() if hasattr(layer, 'topil') else layer.composite()
            if img is None:
                skipped_count += 1
                continue
        except Exception as e:
            print(f"  [!] 渲染失败 [{layer.name}]: {e}")
            skipped_count += 1
            continue

        # 裁剪到实际内容，同时记录精确坐标
        bbox = layer.bbox
        doc_x, doc_y = bbox[0], bbox[1]

        if crop_blank:
            content_bbox = img.getbbox()
            if content_bbox:
                img_cropped = img.crop(content_bbox)
                if use_composite:
                    # composite 已是画布坐标，getbbox 即绝对位置
                    actual_x = content_bbox[0]
                    actual_y = content_bbox[1]
                else:
                    # topil 是图层自身像素，需加上图层在画布中的位置
                    actual_x = doc_x + content_bbox[0]
                    actual_y = doc_y + content_bbox[1]
                actual_w = content_bbox[2] - content_bbox[0]
                actual_h = content_bbox[3] - content_bbox[1]
            else:
                actual_x, actual_y = doc_x, doc_y
                actual_w = bbox[2] - bbox[0]
                actual_h = bbox[3] - bbox[1]
                img_cropped = img
        else:
            # 不裁剪，保持画布原尺寸
            img_cropped = img
            content_bbox = img.getbbox()
            if content_bbox:
                if use_composite:
                    actual_x = content_bbox[0]
                    actual_y = content_bbox[1]
                else:
                    actual_x = doc_x + content_bbox[0]
                    actual_y = doc_y + content_bbox[1]
                actual_w = content_bbox[2] - content_bbox[0]
                actual_h = content_bbox[3] - content_bbox[1]
            else:
                actual_x, actual_y = 0, 0
                actual_w, actual_h = psd.width, psd.height

        # 保存
        save_dir = os.path.join(out_dir, folder)
        os.makedirs(save_dir, exist_ok=True)
        filepath = os.path.join(save_dir, filename)

        try:
            img_cropped.save(filepath)
        except Exception as e:
            print(f"  [!] 保存失败 [{layer.name}]: {e}")
            continue

        exported.append({
            "name": layer.name,
            "file": file_rel.replace("\\", "/"),
            "group": "/".join(group_path) if group_path else "",
            "x": actual_x,
            "y": actual_y,
            "width": actual_w,
            "height": actual_h,
            "doc_x": doc_x,
            "doc_y": doc_y,
        })
        exported_count += 1

        if exported_count % 100 == 0:
            print(f"  进度: {exported_count} 图层已导出...")

    # JSON
    json_path = os.path.join(out_dir, "layer_index.json")
    json_data = {
        "psd": psd_path,
        "canvas_width": psd.width,
        "canvas_height": psd.height,
        "total_layers": exported_count,
        "layers": exported,
    }
    with open(json_path, "w", encoding="utf-8") as f:
        json.dump(json_data, f, ensure_ascii=False, indent=2)

    print(f"\n完成!")
    print(f"  导出图层: {exported_count}")
    print(f"  跳过图层: {skipped_count}")
    print(f"  输出目录: {out_dir}")
    print(f"  坐标文件: {json_path}")


def load_config(path):
    with open(path, encoding="utf-8") as f:
        cfg = json.load(f)
    return cfg


def resolve_path(base, p):
    if os.path.isabs(p):
        return p
    return os.path.normpath(os.path.join(base, p))


def main():
    parser = argparse.ArgumentParser(description="PSD 全量图层导出工具")
    parser.add_argument("--config", help="配置文件路径")
    parser.add_argument("--psd", help="PSD 文件路径")
    parser.add_argument("--out", default="output", help="导出目录")
    args = parser.parse_args()

    script_dir = os.path.dirname(os.path.abspath(__file__))

    if args.config:
        cfg = load_config(args.config)
        psd_path = resolve_path(script_dir, cfg["psd路径"])
        out_dir = resolve_path(script_dir, cfg["输出目录"])
        crop_blank = cfg.get("裁剪空白", True)
        use_composite = cfg.get("使用合成渲染", False)
    else:
        if not args.psd:
            print("错误: 需要 --psd 或 --config")
            sys.exit(1)
        psd_path = resolve_path(script_dir, args.psd)
        out_dir = resolve_path(script_dir, args.out)
        crop_blank = True
        use_composite = False

    if not os.path.exists(psd_path):
        print(f"错误: PSD 文件不存在 - {psd_path}")
        sys.exit(1)

    export_all_layers(psd_path, out_dir, crop_blank, use_composite)


if __name__ == "__main__":
    main()
