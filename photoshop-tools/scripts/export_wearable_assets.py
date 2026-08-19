"""
LuckyDogPub 装扮资源一体化导出工具。

一次打开 PSD，同时导出：
- 非辅助、非狗皮肤的装扮 PNG；
- layer_index_<版本号>.json；
- tbitem_asset_manifest.json；
- 普通装扮道具图标 PNG。

JSON 清单中的 AssetPathList / IconPath 使用 tbitem.json 的字段名，
方便后续直接用于填写 Items.xlsx。
"""

import argparse
import json
import os
import re
import sys
from fnmatch import fnmatchcase

from psd_tools import PSDImage

from export_all_layers import get_group_path, has_content, layer_index_filename, should_skip
from export_icons import postprocess


# 当前项目中非狗 ItemType 与图标前缀的稳定映射。
ITEM_TYPE_BY_ICON_PREFIX = {
    "Headwear": 2,
    "Eyewear": 3,
    "Arm": 4,
    "Clothes": 5,
    "Table": 6,
    "Background": 7,
    "Accessory": 8,
    "Treat": 9,
    "CardBack": 10,
    "CardFace": 11,
    "BodyDecoration": 12,
}


def clean_component(value: str, fallback: str = "Layer") -> str:
    """将 PSD 名称转换为可安全用于 Windows 文件名的单个路径片段。"""
    text = str(value or "").strip()
    text = re.sub(r'[<>:"/\\|?*\x00-\x1f]', "_", text)
    text = re.sub(r"\s+", "_", text)
    text = text.strip(" .")
    return text or fallback


def clean_version(value: str) -> str:
    version = clean_component(value, "")
    if not version or version in {".", ".."}:
        raise ValueError("美术资源版本号不能为空，且不能是 . 或 ..")
    return version


def clean_icon_folder(value: str) -> str:
    folder = str(value or "").strip()
    if not folder:
        raise ValueError("道具图标文件夹名不能为空")
    if folder in {".", ".."} or "/" in folder or "\\" in folder:
        raise ValueError("道具图标文件夹名只能填写单级文件夹名")
    return clean_component(folder)


def parse_short_rules(value) -> list[str]:
    """支持 JSON 数组、逗号分隔和类似 gitignore 的逐行名单。"""
    if isinstance(value, list):
        raw_values = value
    else:
        raw_values = re.split(r"[,\r\n]+", str(value or ""))

    rules = []
    for raw in raw_values:
        rule = str(raw).strip().replace("\\", "/")
        if not rule or rule.startswith("#"):
            continue
        rules.append(rule.strip("/"))
    return rules


def is_dog_layer(group_path: list[str]) -> bool:
    return any(part.lstrip("_").casefold() == "shiba" for part in group_path)


def icon_prefix(group_path: list[str]) -> str:
    """从 PSD 组路径得到 tbitem 图标名前缀。"""
    parts = [part for part in group_path if part and not part.startswith("_")]
    if not parts:
        return ""
    if "CardFace" in parts:
        return "CardFace"
    return parts[-1] if parts[-1] != "Player" else ""


def path_matches(pattern: str, group_path: list[str], layer_name: str) -> bool:
    pattern = pattern.strip("/")
    group_text = "/".join(group_path)
    layer_text = f"{group_text}/{layer_name}" if group_text else layer_name
    candidates = [group_text, layer_text, layer_name, *group_path]
    return any(fnmatchcase(candidate, pattern) for candidate in candidates)


def use_short_side(rules: list[str], group_path: list[str], layer_name: str) -> bool:
    return any(path_matches(rule, group_path, layer_name) for rule in rules)


def unique_filename(used: set[str], base_name: str) -> str:
    """为同组重名或图标重名提供稳定后缀，不覆盖先前输出。"""
    if base_name not in used:
        used.add(base_name)
        return base_name

    stem, extension = os.path.splitext(base_name)
    index = 2
    while f"{stem}_{index}{extension}" in used:
        index += 1
    result = f"{stem}_{index}{extension}"
    used.add(result)
    return result


def windows_path(*parts: str) -> str:
    return "\\".join(str(part).strip("\\/") for part in parts if str(part).strip("\\/"))


def render_layer(layer):
    image = layer.topil() if hasattr(layer, "topil") else layer.composite()
    if image is None:
        return None
    content_bbox = image.getbbox()
    if not content_bbox:
        return None
    return image, image.crop(content_bbox), content_bbox


def export_wearable_assets(
    psd_path: str,
    output_root: str,
    version: str,
    icon_folder: str = "ItemIcon",
    crop_blank: bool = True,
    canvas_size: int = 256,
    content_size: int = 240,
    margin: int = 16,
    short_rules=None,
):
    resource_version = clean_version(version)
    icon_folder = clean_icon_folder(icon_folder)
    rules = parse_short_rules(short_rules)

    psd = PSDImage.open(psd_path)
    version_dir = os.path.join(output_root, resource_version)
    os.makedirs(version_dir, exist_ok=True)

    print(f"打开 PSD: {psd_path} ({psd.width}x{psd.height})")
    print(f"资源目录: {version_dir}")
    if rules:
        print(f"短边规则: {', '.join(rules)}")

    all_layers = list(psd.descendants())
    index_layers = []
    manifest_items = []
    used_art_names: dict[str, set[str]] = {}
    used_icon_names: set[str] = set()
    exported_count = 0
    icon_count = 0
    skipped_count = 0
    warning_count = 0

    for layer in all_layers:
        if layer.is_group():
            continue

        group_path = get_group_path(layer)
        if should_skip(layer) or is_dog_layer(group_path):
            skipped_count += 1
            continue
        if not has_content(layer):
            skipped_count += 1
            continue

        rendered = render_layer(layer)
        if rendered is None:
            skipped_count += 1
            print(f"  [WARN] 图层没有可见像素，跳过: {'/'.join(group_path)}/{layer.name}")
            warning_count += 1
            continue

        image, visible_image, content_bbox = rendered
        bbox = layer.bbox
        doc_x, doc_y = bbox[0], bbox[1]
        actual_x = doc_x + content_bbox[0]
        actual_y = doc_y + content_bbox[1]
        actual_w = content_bbox[2] - content_bbox[0]
        actual_h = content_bbox[3] - content_bbox[1]

        folder_parts = [clean_component(part) for part in group_path] or ["_root"]
        folder_key = "/".join(folder_parts)
        used_in_folder = used_art_names.setdefault(folder_key, set())
        layer_base = f"{clean_component(layer.name)}.png"
        filename = unique_filename(used_in_folder, layer_base)
        file_rel_os = os.path.join(*folder_parts, filename)
        file_rel_json = "/".join(folder_parts + [filename])
        output_path = os.path.join(version_dir, file_rel_os)
        os.makedirs(os.path.dirname(output_path), exist_ok=True)

        try:
            (visible_image if crop_blank else image).save(output_path)
        except Exception as exc:
            print(f"  [WARN] 装扮 PNG 保存失败 [{layer.name}]: {exc}")
            warning_count += 1
            skipped_count += 1
            continue

        index_layers.append({
            "name": layer.name,
            "file": file_rel_json,
            "group": "/".join(group_path),
            "x": actual_x,
            "y": actual_y,
            "width": actual_w,
            "height": actual_h,
            "doc_x": doc_x,
            "doc_y": doc_y,
        })
        exported_count += 1

        prefix = icon_prefix(group_path)
        if not prefix or prefix not in ITEM_TYPE_BY_ICON_PREFIX:
            print(f"  [WARN] 无图标类型映射，仅导出装扮 PNG: {'/'.join(group_path)}/{layer.name}")
            warning_count += 1
            continue

        if re.match(r"^(新建图层|图层|Layer)(?:[ _-]*(?:副本|copy|\d+))*$", layer.name, re.IGNORECASE):
            print(f"  [WARN] PSD 图层名疑似临时名称，仍继续导出: {'/'.join(group_path)}/{layer.name}")
            warning_count += 1

        short_side = use_short_side(rules, group_path, layer.name)
        icon_base = f"{clean_component(prefix)}_{clean_component(layer.name)}.png"
        icon_name = unique_filename(used_icon_names, icon_base)
        icon_path = os.path.join(version_dir, icon_folder, icon_name)
        os.makedirs(os.path.dirname(icon_path), exist_ok=True)
        try:
            postprocess(visible_image, short_side, canvas_size, content_size, margin).save(icon_path)
        except Exception as exc:
            print(f"  [WARN] 道具图标保存失败 [{layer.name}]: {exc}")
            warning_count += 1
            continue

        asset_path = windows_path(resource_version, *folder_parts, filename)
        item_icon_path = windows_path(resource_version, icon_folder, icon_name)
        manifest_items.append({
            "AssetPathList": [asset_path],
            "IconPath": item_icon_path,
            "IconName": icon_name,
            "SourceGroup": "/".join(group_path),
            "SourceLayer": layer.name,
            "ItemType": ITEM_TYPE_BY_ICON_PREFIX[prefix],
            "IconExportRule": "ShortSide" if short_side else "LongSide",
        })
        icon_count += 1

    index_path = os.path.join(version_dir, layer_index_filename(resource_version))
    index_data = {
        "psd": psd_path,
        "version": str(version).strip(),
        "canvas_width": psd.width,
        "canvas_height": psd.height,
        "total_layers": len(index_layers),
        "layers": index_layers,
    }
    with open(index_path, "w", encoding="utf-8") as handle:
        json.dump(index_data, handle, ensure_ascii=False, indent=2)

    manifest_path = os.path.join(version_dir, "tbitem_asset_manifest.json")
    manifest_data = {
        "_说明": "供 Items.xlsx / tbitem.json 填写的装扮资源清单",
        "Version": str(version).strip(),
        "ItemIconFolder": icon_folder,
        "Items": manifest_items,
    }
    with open(manifest_path, "w", encoding="utf-8") as handle:
        json.dump(manifest_data, handle, ensure_ascii=False, indent=2)

    print("\n完成!")
    print(f"  装扮 PNG: {exported_count}")
    print(f"  道具图标: {icon_count}")
    print(f"  跳过图层: {skipped_count}")
    print(f"  警告数量: {warning_count}")
    print(f"  坐标文件: {index_path}")
    print(f"  填表清单: {manifest_path}")


def load_config(path: str) -> dict:
    with open(path, encoding="utf-8") as handle:
        return json.load(handle)


def resolve_path(base: str, value: str) -> str:
    return value if os.path.isabs(value) else os.path.normpath(os.path.join(base, value))


def main():
    parser = argparse.ArgumentParser(description="LuckyDogPub 装扮资源一体化导出工具")
    parser.add_argument("--config", help="配置文件路径")
    parser.add_argument("--psd", help="PSD 文件路径")
    parser.add_argument("--out", default="output", help="输出根目录")
    parser.add_argument("--version", default="", help="美术资源版本号")
    parser.add_argument("--icon-folder", default="ItemIcon", help="道具图标文件夹名")
    args = parser.parse_args()

    if args.config:
        config_path = os.path.abspath(args.config)
        cfg = load_config(config_path)
        base_dir = os.getcwd()
        psd_path = resolve_path(base_dir, cfg["psd路径"])
        output_root = resolve_path(base_dir, cfg["输出根目录"])
        version = cfg.get("美术资源版本号", "")
        icon_folder = cfg.get("道具图标文件夹", "ItemIcon")
        crop_blank = cfg.get("裁剪空白", True)
        canvas_size = int(cfg.get("画布尺寸", 256))
        content_size = int(cfg.get("内容尺寸", 240))
        margin = int(cfg.get("边框留白", 16))
        short_rules = cfg.get("短边规则名单", [])
    else:
        if not args.psd:
            print("错误: 需要 --psd 或 --config")
            sys.exit(1)
        base_dir = os.getcwd()
        psd_path = resolve_path(base_dir, args.psd)
        output_root = resolve_path(base_dir, args.out)
        version = args.version
        icon_folder = args.icon_folder
        crop_blank = True
        canvas_size = 256
        content_size = 240
        margin = 16
        short_rules = []

    if not os.path.exists(psd_path):
        print(f"错误: PSD 文件不存在 - {psd_path}")
        sys.exit(1)

    try:
        export_wearable_assets(
            psd_path,
            output_root,
            version,
            icon_folder,
            crop_blank,
            canvas_size,
            content_size,
            margin,
            short_rules,
        )
    except Exception as exc:
        print(f"错误: {exc}")
        sys.exit(1)


if __name__ == "__main__":
    main()
