from __future__ import annotations

import csv
import re
import sys
from pathlib import Path


PROJECT_ROOT = Path(__file__).resolve().parents[2]
CSV_PATH = PROJECT_ROOT / "Data" / "Localization" / "LocalizationText.csv"
ROOMS_CSV_PATH = PROJECT_ROOT / "Scenes" / "Rooms" / "InGameRoomText.csv"
KEYS_PATH = PROJECT_ROOT / "Scripts" / "Desktop" / "L10nKey.cs"
KEY_RE = re.compile(r"^[A-Z][A-Za-z0-9]*(?:_[A-Z][A-Za-z0-9]*)*$")
CONST_RE = re.compile(r"public const string ([A-Za-z_][A-Za-z0-9_]*) = nameof\(\1\);")
EMPTY_TEXT_MARKER = "@empty"
# These subtrees are development UI, not player-facing localized content.
DEBUG_TOOLTIP_ROOTS = {
    "App/SystemPanel.tscn": ("Panel/RootVBox/Scroll/ContentVBox/DebugContent",),
    "Prefabs/CollectionEventPage.tscn": ("DebugToolsModule",),
}


def validate_scene_tooltips(keys: set[str], room_keys: set[str]) -> None:
    scenes = PROJECT_ROOT / "Scenes"
    checked = 0
    skipped_debug = 0
    for scene in sorted(scenes.rglob("*.tscn")):
        relative = scene.relative_to(scenes).as_posix()
        if relative.startswith("Dev/"):
            continue
        scene_keys = keys | room_keys if relative.startswith("Rooms/") else keys
        node_path = ""
        for line_no, line in enumerate(scene.read_text(encoding="utf-8").splitlines(), start=1):
            node = re.match(r'\[node name="([^"]+)"(.*)\]', line)
            if node:
                parent = re.search(r'parent="([^"]+)"', node[2])
                prefix = parent[1] + "/" if parent and parent[1] != "." else ""
                node_path = prefix + node[1]
            tooltip = re.fullmatch(r'tooltip_text\s*=\s*"((?:[^"\\]|\\.)*)"', line)
            if not tooltip or not tooltip[1]:
                continue
            if any(node_path == root or node_path.startswith(root + "/")
                   for root in DEBUG_TOOLTIP_ROOTS.get(relative, ())):
                skipped_debug += 1
                continue
            if tooltip[1] not in scene_keys:
                fail(f"{relative}:{line_no} ({node_path}) tooltip must use a localization key: {tooltip[1]}")
            checked += 1
    print(f"[localization] OK: {checked} player scene tooltips use valid keys; {skipped_debug} Debug tooltips excluded")


def fail(message: str) -> None:
    print(f"[localization] ERROR: {message}")
    sys.exit(1)


def validate_catalog(path: Path, seen: set[str], seen_ids: dict[str, str]) -> tuple[set[str], list[str]]:
    if not path.exists():
        fail(f"missing CSV: {path}")
    with path.open("r", encoding="utf-8", newline="") as f:
        rows = list(csv.reader(f))

    if not rows:
        fail(f"{path.name} is empty")

    header = rows[0]
    if header[:3] != ["keys", "en", "zh_CN"]:
        fail(f"{path.name} header must start with: keys,en,zh_CN")

    language_columns = [(i, name) for i, name in enumerate(header[1:], start=1) if not name.startswith("_")]
    metadata_columns = {name: i for i, name in enumerate(header) if name.startswith("_")}
    id_column = metadata_columns.get("_id")

    keys: list[str] = []
    for line_no, row in enumerate(rows[1:], start=2):
        if not row or not row[0].strip():
            fail(f"empty key at line {line_no}")
        key = row[0].strip()
        if key in seen:
            fail(f"duplicate key {key} at line {line_no}")
        if not KEY_RE.match(key):
            fail(f"key {key} at line {line_no} does not match Category_Name style")
        if len(row) < len(header):
            fail(f"key {key} at line {line_no} has too few columns")
        for col, locale in language_columns:
            value = row[col].strip()
            if not value:
                fail(f"key {key} is missing {locale} text at line {line_no}")
            if value.startswith("@") and value != EMPTY_TEXT_MARKER:
                fail(f"key {key} has unknown localization marker {value} for {locale} at line {line_no}")
            if path == ROOMS_CSV_PATH:
                placeholders = lambda text: sorted(re.findall(r"\{[^{}]+\}", text))
                if placeholders(value) != placeholders(row[1]):
                    fail(f"key {key} has mismatched format placeholders for {locale}")
                if key.startswith("Rooms_Companion") and any(c in value for c in ("\n", "\r", "\\n", "\\r")):
                    fail(f"companion name {key} must stay on one line for {locale}")
        if id_column is not None:
            id_value = row[id_column].strip()
            if not id_value:
                fail(f"key {key} is missing _id at line {line_no}")
            if not id_value.isdigit():
                fail(f"key {key} has non-numeric _id {id_value} at line {line_no}")
            if id_value in seen_ids:
                fail(f"duplicate _id {id_value} for {key} and {seen_ids[id_value]}")
            seen_ids[id_value] = key
        seen.add(key)
        keys.append(key)

    return set(keys), [name for _, name in language_columns]


def main() -> None:
    seen: set[str] = set()
    seen_ids: dict[str, str] = {}
    keys, locales = validate_catalog(CSV_PATH, seen, seen_ids)
    room_keys: set[str] = set()
    if ROOMS_CSV_PATH.exists():
        room_keys, room_locales = validate_catalog(ROOMS_CSV_PATH, seen, seen_ids)
        if room_locales != locales:
            fail("room and main catalogs must have the same locale columns")
    if not KEYS_PATH.exists():
        fail(f"missing L10nKey file: {KEYS_PATH}")

    const_text = KEYS_PATH.read_text(encoding="utf-8")
    const_keys = CONST_RE.findall(const_text)
    missing_consts = sorted(set(keys) - set(const_keys))
    stale_consts = sorted(set(const_keys) - set(keys))
    if missing_consts:
        fail("L10nKey.cs is missing constants: " + ", ".join(missing_consts))
    if stale_consts:
        fail("L10nKey.cs has stale constants: " + ", ".join(stale_consts))

    validate_scene_tooltips(keys, room_keys)
    if room_keys:
        print(f"[localization] OK: {len(room_keys)} room keys; keys and metadata IDs are unique across both catalogs")
    print(f"[localization] OK: {len(keys)} keys, locales: {', '.join(locales)}")


if __name__ == "__main__":
    main()
