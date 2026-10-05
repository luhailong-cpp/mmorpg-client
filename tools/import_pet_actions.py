"""导入锁定宠物快照：完整动作启用，不完整正式帧保留于 pending；默认仅检查。"""
from __future__ import annotations

import argparse
import hashlib
import json
import math
from pathlib import Path
import re
import shutil
import struct
import uuid

ROOT = Path(__file__).resolve().parents[1]
DELIVERY = ROOT / 'tools/pet_deliveries/pets_20261005.json'
FAMILY = Path('Assets/Resources/World/Pets/QdaoPets20261005')
COUNTS = {'hit': 6, 'attack': 12, 'cast': 16}
DIRECTIONS = ('E', 'W')


def require(ok, message):
    if not ok:
        raise ValueError(message)


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def json_bytes(value):
    return (json.dumps(value, ensure_ascii=False, indent=2) + '\n').encode('utf-8')


def safe_path(root, relative):
    require(isinstance(relative, str) and not Path(relative).is_absolute(), f'非法相对路径: {relative}')
    path = (root / relative).resolve()
    require(path.is_relative_to(root.resolve()), f'路径越界: {relative}')
    return path


def check_png(path, frame):
    require(path.is_file() and not path.is_symlink(), f'缺少文件或不允许的链接: {path}')
    data = path.read_bytes()
    require(hashlib.sha256(data).hexdigest() == frame['sha256'], f'快照图片SHA已变动: {path}')
    require(data[:8] == b'\x89PNG\r\n\x1a\n' and data[12:16] == b'IHDR', f'不是PNG: {path}')
    require(struct.unpack('>II', data[16:24]) == (frame['width'], frame['height']), f'快照尺寸不匹配: {path}')
    require(data[25] == 6, f'需要RGBA透明PNG: {path}')


def geometry(frame):
    width, height = frame['width'], frame['height']
    require(isinstance(width, int) and isinstance(height, int) and 0 < width <= 2048 and 0 < height <= 2048,
            '宠物画布尺寸无效')
    pivot = frame.get('pivot') or [.5, .08]
    require(len(pivot) == 2 and all(isinstance(v, (int, float)) and math.isfinite(v) and 0 <= v <= 1 for v in pivot),
            '宠物锚点无效')
    # Preserve the whole authored canvas at6.4 world units, including1254px originals.
    return dict(frameWidth=width, frameHeight=height, pixelsPerUnit=width / 6.4,
                overridePivot=True, pivotX=pivot[0], pivotY=pivot[1])


def clip(action, direction, frames, event=-1):
    count = 1 if action == 'idle' else COUNTS[action]
    require([f['frame'] for f in frames] == list(range(1, count + 1)), f'不能启用缺帧/重复片段: {action}/{direction}')
    geometries = [geometry(f) for f in frames]
    require(all(g == geometries[0] for g in geometries), f'同片段画布/锚点不同: {action}/{direction}')
    times = [f.get('durationMs', f.get('frameDurationMs')) for f in frames]
    require(all(isinstance(t, (int, float)) and math.isfinite(t) and 0 < t <= 1000 for t in times), '帧时长无效')
    require(isinstance(event, int) and -1 <= event < count, '事件帧超出片段')
    return dict(action=action, direction=direction, frameCount=count, frameDurationMs=times[0],
                frameDurationsMs=times, eventFrame=event, **geometries[0])


def build_pet(pet):
    pid = pet['id']
    require(re.fullmatch(r'[a-z0-9][a-z0-9-]*', pid) is not None, '宠物ID无效')
    frames = pet['frames']
    keys = [(f['action'], f['direction'], f['frame']) for f in frames]
    require(len(set(keys)) == len(keys), f'重复宠物帧: {pid}')
    require(all(a in COUNTS and d in DIRECTIONS and isinstance(n, int) and 1 <= n <= COUNTS[a]
                for a, d, n in keys), f'宠物动作/方向/帧号无效: {pid}')
    groups = {(c['action'], c['direction']): c for c in pet['clips']}
    require(len(groups) == len(pet['clips']) == 6 and set(groups) == {(a, d) for a in COUNTS for d in DIRECTIONS},
            f'须逐一记录六组宠物动作: {pid}')
    idle = pet['staticAssets']
    require(len(idle) == 2 and {f['direction'] for f in idle} == set(DIRECTIONS), f'静态东西方向未齐: {pid}')
    clips, copies, pending = [], [], []
    for f in sorted(idle, key=lambda f: f['direction']):
        require(f['action'] == 'idle' and f['frame'] == 1, '静态宠物帧定义无效')
        clips.append(clip('idle', f['direction'], [f]))
        copies.append((f"idle/{f['direction']}/01.png", f))
    copies.append(('portrait.png', pet['portrait']))
    for action, count in COUNTS.items():
        for direction in DIRECTIONS:
            entry = groups[action, direction]
            selected = sorted((f for f in frames if f['action'] == action and f['direction'] == direction),
                              key=lambda f: f['frame'])
            numbers = {f['frame'] for f in selected}
            missing = [n for n in range(1, count + 1) if n not in numbers]
            require(entry['expectedFrameCount'] == count and entry['presentFrameCount'] == len(selected),
                    f'快照片段数量不一致: {pid}/{action}/{direction}')
            active = entry['eligibleCompleteClip']
            if active:
                require(not missing and entry['completeFrameSet'] and all(f.get('technicalValid', False) for f in selected),
                        f'不完整或不合格片段不能启用: {pid}/{action}/{direction}')
                events = entry.get('events', [])
                # Audit events are authored one-based frame labels, unlike runtime zero-based indices.
                event = events[0]['frame'] - 1 if events else -1
                clips.append(clip(action, direction, selected, event))
            else:
                pending.append(dict(action=action, direction=direction, presentFrames=len(selected),
                                    expectedFrames=count, missingFrames=missing,
                                    reason='incomplete_or_not_eligible_in_source_audit', activated=False))
            for f in selected:
                prefix = '' if active else 'pending/'
                copies.append((f"{prefix}{action}/{direction}/{f['frame']:02d}.png", f))
    manifest = dict(schemaVersion=1, characterId=pid, frameWidth=1024, frameHeight=1024,
                    pixelsPerUnit=160, pivotX=.5, pivotY=.08, clips=clips,
                    geometryStatus='fixed_source_canvas; missing source anchors use provisional placement, client calibration pending',
                    visualValidation='client_runtime_pending')
    return manifest, copies, pending


def meta(path, project):
    target = Path(str(path) + '.meta')
    prior = target.read_text(encoding='utf-8') if target.exists() else None
    if prior is not None and (path.suffix != '.png' or 'userData: qdao-pets-20261005' not in prior):
        return
    guid = uuid.uuid5(uuid.NAMESPACE_URL, 'mmorpg-client/' + path.relative_to(project).as_posix()).hex
    if prior is not None:
        guid = next(line[6:].strip() for line in prior.splitlines() if line.startswith('guid: '))
    if path.is_dir():
        body = 'folderAsset: yes\nDefaultImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n'
    elif path.suffix == '.png':
        body = ('TextureImporter:\n  externalObjects: {}\n  serializedVersion: 13\n'
                '  mipmaps:\n    enableMipMap: 0\n    sRGBTexture: 1\n'
                '  isReadable: 0\n  textureType: 0\n  textureShape: 1\n'
                '  maxTextureSize: 2048\n  textureSettings:\n    serializedVersion: 2\n'
                '    filterMode: 1\n    aniso: 1\n    mipBias: 0\n    wrapU: 1\n    wrapV: 1\n    wrapW: 1\n'
                '  alphaUsage: 1\n  alphaIsTransparency: 1\n  nPOTScale: 0\n'
                '  compressionQuality: 100\n  platformSettings:\n'
                '  - serializedVersion: 4\n    buildTarget: DefaultTexturePlatform\n'
                '    maxTextureSize: 2048\n    resizeAlgorithm: 0\n    textureFormat: -1\n'
                '    textureCompression: 0\n    compressionQuality: 100\n    overridden: 0\n'
                '  - serializedVersion: 4\n    buildTarget: Standalone\n'
                '    maxTextureSize: 2048\n    resizeAlgorithm: 0\n    textureFormat: -1\n'
                '    textureCompression: 0\n    compressionQuality: 100\n    overridden: 1\n'
                '  userData: qdao-pets-20261005\n  assetBundleName: \n  assetBundleVariant: \n')
    else:
        body = 'TextScriptImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n'
    target.write_text('\n'.join(line.rstrip() for line in (f'fileFormatVersion: 2\nguid: {guid}\n' + body).splitlines()) + '\n', encoding='utf-8')


def publish_json(path, value, project):
    pending = path.with_suffix('.json.importing')
    pending.write_bytes(json_bytes(value))
    pending.replace(path)
    meta(path, project)


def run(delivery, source_root, project, execute=False, verify=False):
    require(delivery.get('schemaVersion') == 1, '不支持的宠物交付清单')
    pets = delivery['pets']
    require(pets and len({p['id'] for p in pets}) == len(pets), '宠物清单须非空且身份唯一')
    plans = {p['id']: build_pet(p) for p in pets}
    catalog = dict(schemaVersion=1, pets=[{k: p[k] for k in ('id', 'displayName', 'petTableIds', 'modelIds')} for p in pets])
    for field in ('petTableIds', 'modelIds'):
        bound = [v for p in pets for v in p[field]]
        require(len(set(bound)) == len(bound) and all(isinstance(v, int) and v > 0 for v in bound), '宠物数值身份绑定冲突')
    family = project / FAMILY
    # Verify every locked source first; a changing production frame must not publish half a snapshot.
    for pid, (manifest, copies, _) in plans.items():
        for relative, frame in copies:
            path = safe_path(family / pid, relative) if verify else safe_path(source_root, frame['sourceRelative'])
            check_png(path, frame)
            if verify:
                texture_meta = Path(str(path) + '.meta').read_text(encoding='utf-8')
                require('maxTextureSize: 2048' in texture_meta and 'nPOTScale: 0' in texture_meta,
                        f'宠物图片导入设置会缩图: {path}')
        if verify:
            require((family / pid / 'manifest.json').read_bytes() == json_bytes(manifest), f'宠物运行合同不匹配: {pid}')
    complete_clips = sum(sum(c['action'] != 'idle' for c in m['clips']) for m, _, _ in plans.values())
    pending_frames = sum(sum(relative.startswith('pending/') for relative, _ in files) for _, files, _ in plans.values())
    combat_frames = sum(len(p['frames']) for p in pets)
    report = dict(schemaVersion=1, sourceAuditSha256=delivery['sourceAuditSha256'], snapshotFinishedUtc=delivery['snapshotFinishedUtc'],
                  petCount=len(pets), staticIdleFrames=len(pets) * 2, portraitCount=len(pets),
                  activeCombatClips=complete_clips, activeCombatFrames=combat_frames - pending_frames,
                  pendingCombatFrames=pending_frames, totalCombatFrames=combat_frames,
                  missingCombatFrames=len(pets) * 68 - combat_frames, expectedCombatFrames=len(pets) * 68,
                  runAnimation='not_delivered; no idle duplicate or interpolation fabricated as run',
                  completion='partial_source_snapshot; complete clips only activated; client_runtime_pending',
                  pending=[dict(petId=pid, clips=parts) for pid, (_, _, parts) in plans.items()])
    if verify:
        require((family / 'catalog.json').read_bytes() == json_bytes(catalog), '宠物catalog不匹配')
        require((family / 'import-report.json').read_bytes() == json_bytes(report), '宠物导入报告不匹配')
    if execute:
        for pid, (manifest, copies, _) in plans.items():
            destination = family / pid
            for relative, frame in copies:
                source = safe_path(source_root, frame['sourceRelative'])
                path = safe_path(destination, relative)
                path.parent.mkdir(parents=True, exist_ok=True)
                if not path.exists() or digest(path) != frame['sha256']:
                    staged = path.with_suffix('.png.importing')
                    shutil.copyfile(source, staged)
                    # A source edit after preflight must never overwrite a previously published PNG.
                    check_png(staged, frame)
                    staged.replace(path)
                check_png(path, frame)
                meta(path, project)
            publish_json(destination / 'manifest.json', manifest, project)
        publish_json(family / 'catalog.json', catalog, project)
        publish_json(family / 'import-report.json', report, project)
        for folder in sorted(p for p in family.rglob('*') if p.is_dir()):
            meta(folder, project)
        meta(family, project)
        meta(family.parent, project)
        run(delivery, source_root, project, verify=True)
    return {k: report[k] for k in ('petCount', 'staticIdleFrames', 'portraitCount', 'activeCombatClips', 'activeCombatFrames',
                                  'pendingCombatFrames', 'totalCombatFrames', 'missingCombatFrames')} | {
                                      'mode': 'verified' if verify else 'imported' if execute else 'dry_run'}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--delivery', type=Path, default=DELIVERY)
    parser.add_argument('--source-root', type=Path, default=ROOT.parent / 'image')
    parser.add_argument('--project', type=Path, default=ROOT)
    modes = parser.add_mutually_exclusive_group()
    modes.add_argument('--execute', action='store_true')
    modes.add_argument('--verify', action='store_true')
    args = parser.parse_args()
    delivery = json.loads(args.delivery.read_text(encoding='utf-8-sig'))
    print(json.dumps(run(delivery, args.source_root.resolve(), args.project.resolve(), args.execute, args.verify), ensure_ascii=False))


if __name__ == '__main__':
    main()
