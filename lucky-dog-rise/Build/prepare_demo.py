"""Create a disposable, cache-free Demo export project. Never edits source assets."""
import argparse
import json
import re
import shutil
from pathlib import Path


def prepare(source, output):
    source, output = source.resolve(), output.resolve()
    expected = source.parent / '.local-build' / 'demo-project'
    if output != expected or output == source:
        raise ValueError(f'Output must be {expected}')
    tables = {p.stem: json.loads(p.read_text(encoding='utf-8-sig'))
              for p in (source / 'Data/Json').glob('*.json')}
    original = tables['tbitem']
    for name in ('tbitem', 'tbdogskin', 'tbblindbox', 'tbblindboxschedule', 'tblinktree'):
        tables[name] = [r for r in tables[name] if r['BuildChannelMask'] & 4]
    items = {r['Id']: r for r in tables['tbitem']}
    skins = {r['Id']: r for r in tables['tbdogskin']}
    boxes = {r['Id'] for r in tables['tbblindbox']}
    schedules = {r['Id'] for r in tables['tbblindboxschedule']}

    def require(value, allowed, context):
        if value and value not in allowed:
            raise ValueError(f'{context}: missing Demo dependency {value}')

    for row in original:
        if row['AcquisitionType'] == 1:
            require(row['Id'], items, 'Initial item')
    for row in items.values():
        require(row['SafeResourceId'], items, f"Item {row['Id']} SafeResourceId")
        require(row['SkinId'], skins, f"Item {row['Id']} SkinId")
    for row in tables['tbequipmentslotconfig']:
        require(row['DefaultItemId'], items, 'Equipment default')
    for row in tables['tbblindboxschedule']:
        require(row['BlindBoxId'], boxes, 'Schedule box')
        require(row['FallbackBlindBoxId'], boxes, 'Schedule fallback')
        require(row['NextScheduleId'], schedules, 'Schedule next')
    for row in tables['tblinktree']:
        require(row['RewardItemId'], items, 'LinkTree reward')
        require(row['RewardBlindBoxId'], boxes, 'LinkTree box')
    for name in ('tbblindboxitemweight', 'tbblindboxrarityrate'):
        tables[name] = [r for r in tables[name] if r['BlindBoxId'] in boxes]
    for row in tables['tbblindboxitemweight']:
        require(row['ItemId'], items, 'Demo pool')
    tables['tbrefreshmentconfig'] = [r for r in tables['tbrefreshmentconfig'] if r['ItemId'] in items]
    # Keep table files for Luban's eager loader, but do not ship disabled platform definitions.
    tables['tbsteamitemdef'] = []
    tables['tbachievement'] = []

    reasons = {}
    def keep(path, reason):
        if not path:
            return
        rel = 'Assets/' + path.replace('\\', '/').rstrip('/')
        target = (source / rel).resolve()
        if not target.is_relative_to(source / 'Assets') or not target.exists():
            raise ValueError(f'{reason}: invalid/missing asset {path}')
        files = target.rglob('*') if target.is_dir() else [target]
        for file in files:
            if file.is_file() and not file.name.endswith('.import'):
                reasons.setdefault(file.relative_to(source).as_posix(), set()).add(reason)

    for row in items.values():
        keep(row['IconPath'], f"Item {row['Id']} icon")
        # Dog directories are controlled solely by retained DogSkin rows.
        if row['ItemType'] != 1:
            for path in row['AssetPathList']:
                keep(path, f"Item {row['Id']} asset")
    for row in skins.values():
        keep(row['FolderPath'], f"DogSkin {row['Id']} folder")
        keep(row['FixedEyewear'], f"DogSkin {row['Id']} eyewear")

    removed = []
    for file in (source / 'Assets').rglob('*'):
        if not file.is_file() or file.name.endswith('.import'):
            continue
        rel = file.relative_to(source).as_posix()
        parts = rel.split('/')
        if len(parts) < 4 or not re.fullmatch(r'v\d+', parts[1]):
            continue
        # All versioned artwork is deny-by-default, including future dog breeds.
        # Position metadata and these non-equipment folders are shared infrastructure.
        if parts[2] in ('ChipStack', 'Fallback') or file.suffix == '.json':
            continue
        if rel not in reasons:
            removed.append(rel)
    removed_set = set(removed)
    scene_edits = {}
    # Remove editor-only texture defaults from the COPY; equipment initializes at runtime.
    for folder in ('Scenes', 'Themes'):
        for file in (source / folder).rglob('*'):
            if file.suffix not in ('.tscn', '.tres') or 'Dev' in file.relative_to(source).parts:
                continue
            content = file.read_text(encoding='utf-8-sig')
            for path in re.findall(r'path="res://([^"\n]+)"', content):
                if path in removed_set:
                    pattern = r'^\[ext_resource type="Texture2D"[^\n]*path="res://' + re.escape(path) + r'"[^\n]*id="([^"]+)"\]\r?\n'
                    match = re.search(pattern, content, re.M)
                    if not match:
                        raise ValueError(f'{file}: unsupported excluded reference {path}')
                    resource_id = match.group(1)
                    content = re.sub(pattern, '', content, flags=re.M)
                    content = re.sub(r'^texture = ExtResource\("' + re.escape(resource_id) + r'"\)\r?\n', '', content, flags=re.M)
                    if f'ExtResource("{resource_id}")' in content:
                        raise ValueError(f'{file}: excluded texture used beyond sprite default: {path}')
                    scene_edits[file.relative_to(source).as_posix()] = content

    if output.exists():
        for child in output.iterdir():
            if child.is_dir() and not child.is_symlink():
                shutil.rmtree(child)
            else:
                child.unlink()
    ignored_patterns = shutil.ignore_patterns(
        '.godot', 'bin', 'obj', 'addons', 'Tools', 'Build', 'docs', 'Dev',
        'steam_appid*.txt', 'export_presets.cfg')

    def ignore_demo_content(directory, names):
        ignored = ignored_patterns(directory, names)
        # Shared room logic is not part of Demo; do not suppress unrelated Rooms folders.
        if Path(directory) == source / 'Scripts':
            ignored.add('Rooms')
        return ignored

    shutil.copytree(source, output, ignore=ignore_demo_content, dirs_exist_ok=True)
    for rel in removed:
        (output / rel).unlink(missing_ok=True)
        (output / (rel + '.import')).unlink(missing_ok=True)
    for rel, content in scene_edits.items():
        (output / rel).write_text(content, encoding='utf-8')
    for name, rows in tables.items():
        (output / 'Data/Json' / (name + '.json')).write_text(
            json.dumps(rows, ensure_ascii=False, indent=2), encoding='utf-8')
    project = output / 'project.godot'
    project.write_text(project.read_text(encoding='utf-8').replace(
        'enabled=PackedStringArray("res://addons/wick/plugin.cfg")', 'enabled=PackedStringArray()'), encoding='utf-8')
    csproj = output / 'LuckyDogRise.csproj'
    csproj.write_text(re.sub(r'<SteamworksNetRoot>.*?</SteamworksNetRoot>',
        lambda _: '<SteamworksNetRoot>' + (source.parent / '.local-build/steamworks/Steamworks.NET-Standalone_2025.163.0/Windows-x64').as_posix() + '</SteamworksNetRoot>',
        csproj.read_text(encoding='utf-8')), encoding='utf-8')
    manifest = dict(item_ids=sorted(items), dog_skin_ids=sorted(skins),
                    dog_folders=sorted({r['FolderPath'] for r in skins.values()}),
                    retained_assets={p: sorted(r) for p, r in sorted(reasons.items())},
                    excluded_assets=sorted(removed),
                    cleaned_scenes=sorted(scene_edits),
                    table_counts={k: len(v) for k, v in tables.items()})
    manifest['excluded_imports'] = sorted({p for rel in removed
        if (source / (rel + '.import')).exists()
        for p in re.findall(r'"res://(\.godot/imported/[^"\n]+)"',
                            (source / (rel + '.import')).read_text(encoding='utf-8'))})
    report = output.parent / 'demo-content-manifest.json'
    report.write_text(json.dumps(manifest, ensure_ascii=False, indent=2), encoding='utf-8')
    print(f"Demo project: {len(items)} items, {len(skins)} skins, {len(removed)} excluded files. Manifest: {report}")
    return manifest


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--source', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    prepare(args.source, args.output)
