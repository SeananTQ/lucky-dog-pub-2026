from __future__ import annotations

import csv
import re
import sys
from pathlib import Path


PROJECT_ROOT = Path(__file__).resolve().parents[2]
CSV_PATH = PROJECT_ROOT / "Data" / "Localization" / "LocalizationText.csv"
KEYS_PATH = PROJECT_ROOT / "Scripts" / "Desktop" / "L10nKey.cs"
KEY_RE = re.compile(r"^[A-Z][A-Za-z0-9]*(?:_[A-Z][A-Za-z0-9]*)*$")
CONST_RE = re.compile(r"public const string ([A-Za-z_][A-Za-z0-9_]*) = nameof\(\1\);")


def fail(message: str) -> None:
    print(f"[localization] ERROR: {message}")
    sys.exit(1)


def main() -> None:
    if not CSV_PATH.exists():
        fail(f"missing CSV: {CSV_PATH}")
    if not KEYS_PATH.exists():
        fail(f"missing L10nKey file: {KEYS_PATH}")

    with CSV_PATH.open("r", encoding="utf-8", newline="") as f:
        rows = list(csv.reader(f))

    if not rows:
        fail("LocalizationText.csv is empty")

    header = rows[0]
    if header[:3] != ["keys", "en", "zh_CN"]:
        fail("LocalizationText.csv header must start with: keys,en,zh_CN")

    language_columns = [(i, name) for i, name in enumerate(header[1:], start=1) if not name.startswith("_")]
    metadata_columns = {name: i for i, name in enumerate(header) if name.startswith("_")}
    id_column = metadata_columns.get("_id")

    seen: set[str] = set()
    seen_ids: dict[str, str] = {}
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
            if not row[col].strip():
                fail(f"key {key} is missing {locale} text at line {line_no}")
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

    const_text = KEYS_PATH.read_text(encoding="utf-8")
    const_keys = CONST_RE.findall(const_text)
    missing_consts = sorted(set(keys) - set(const_keys))
    stale_consts = sorted(set(const_keys) - set(keys))
    if missing_consts:
        fail("L10nKey.cs is missing constants: " + ", ".join(missing_consts))
    if stale_consts:
        fail("L10nKey.cs has stale constants: " + ", ".join(stale_consts))

    locales = [name for _, name in language_columns]
    print(f"[localization] OK: {len(keys)} keys, locales: {', '.join(locales)}")


if __name__ == "__main__":
    main()
