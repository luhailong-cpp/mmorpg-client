"""按锁定交付清单接入归档动作；默认只读，--execute 写入，--verify 检查客户端成品。"""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import shutil
import struct
import uuid

ROOT = Path(__file__).resolve().parents[1]
DELIVERY = ROOT / 'tools/character_deliveries/archived_actions_20261005.json'
FAMILY = Path('Assets/Resources/World/Characters/QdaoArchivedActions20261005')
DIRECTIONS = ('N', 'NE', 'E', 'SE', 'S', 'SW', 'W', 'NW')
COUNTS = {'run': 16, 'attack': 12, 'hit': 6, 'cast': 16}


def require(ok, message):
    if not ok:
        raise ValueError(message)


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def json_bytes(value):
    return (json.dumps(value, ensure_ascii=False, indent=2) + '\n').encode('utf-8')


def safe_path(root, relative):
    path = (root / relative).resolve()
    require(path.is_relative_to(root.resolve()), f'路径越界: {relative}')
    return path


def check_png(path, expected):
    require(path.is_file() and not path.is_symlink(), f'缺少文件或不允许的链接: {path}')
    data = path.read_bytes()
    require(hashlib.sha256(data).hexdigest() == expected, f'图片SHA不匹配: {path}')
    require(data[:8] == b'\x89PNG\r\n\x1a\n' and data[12:16] == b'IHDR', f'不是PNG: {path}')
    require(struct.unpack('>II', data[16:24]) == (1024, 1024), f'图片尺寸应为1024: {path}')
    require(data[25] == 6, f'图片需要RGBA透明通道: {path}')


def runtime_manifest(character):
    clips = []
    for action, count in COUNTS.items():
        for direction in DIRECTIONS if action == 'run' else ('E', 'W'):
            frames = sorted((f for f in character['frames'] if f['action'] == action and f['direction'] == direction),
                            key=lambda f: f['frame'])
            require([f['frame'] for f in frames] == list(range(1, count + 1)),
                    f"帧序缺失/重复: {character['characterId']}/{action}/{direction}")
            times = [f['durationMs'] for f in frames]
            require(all(isinstance(t, (float, int)) and 0 < t <= 1000 for t in times), '非法帧时长')
            event = character.get('eventFrames', {}).get(action, {}).get(direction, -1)
            require(-1 <= event < count, '非法事件帧')
            clips.append(dict(action=action, direction=direction, frameCount=count,
                              frameDurationMs=times[0], frameDurationsMs=times, eventFrame=event))
    require(len(character['frames']) == 196, '单角色必须完整196帧')
    pivot = character['unityPivot']
    require(len(pivot) == 2 and all(0 <= v <= 1 for v in pivot), '非法锚点')
    return dict(schemaVersion=1, characterId=character['characterId'], pixelsPerUnit=104,
                pivotX=pivot[0], pivotY=pivot[1], clips=clips,
                sourceManifestSha256=character['manifestSha256'], rootStatus=character['rootStatus'],
                visualValidation='client_runtime_pending')


def meta(path, project):
    target = Path(str(path) + '.meta')
    prior = target.read_text(encoding='utf-8') if target.exists() else None
    if prior is not None and (path.suffix != '.png' or 'userData: archived-actions-20261005' not in prior):
        return
    relative = path.relative_to(project).as_posix()
    guid = uuid.uuid5(uuid.NAMESPACE_URL, 'mmorpg-client/' + relative).hex
    if prior is not None:
        guid = next(line[6:].strip() for line in prior.splitlines() if line.startswith('guid: '))
    if path.is_dir():
        body = 'folderAsset: yes\nDefaultImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n'
    elif path.suffix == '.png':
        body = ('TextureImporter:\n  externalObjects: {}\n  serializedVersion: 13\n'
                '  mipmaps:\n    enableMipMap: 0\n    sRGBTexture: 1\n'
                '  isReadable: 0\n  textureType: 0\n  textureShape: 1\n'
                '  maxTextureSize: 1024\n  textureSettings:\n    serializedVersion: 2\n'
                '    filterMode: 1\n    aniso: 1\n    mipBias: 0\n    wrapU: 1\n    wrapV: 1\n    wrapW: 1\n'
                '  alphaUsage: 1\n  alphaIsTransparency: 1\n  nPOTScale: 0\n'
                '  compressionQuality: 100\n  platformSettings:\n'
                '  - serializedVersion: 4\n    buildTarget: DefaultTexturePlatform\n'
                '    maxTextureSize: 1024\n    resizeAlgorithm: 0\n    textureFormat: -1\n'
                '    textureCompression: 0\n    compressionQuality: 100\n    overridden: 0\n'
                '  - serializedVersion: 4\n    buildTarget: Standalone\n'
                '    maxTextureSize: 1024\n    resizeAlgorithm: 0\n    textureFormat: -1\n'
                '    textureCompression: 0\n    compressionQuality: 100\n    overridden: 1\n'
                '  userData: archived-actions-20261005\n  assetBundleName: \n  assetBundleVariant: \n')
    else:
        body = 'TextScriptImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n'
    contents = f'fileFormatVersion: 2\nguid: {guid}\n' + body
    target.write_text('\n'.join(line.rstrip() for line in contents.splitlines()) + '\n', encoding='utf-8')


def run(delivery, source_root, project, execute=False, verify=False):
    require(delivery.get('schemaVersion') == 1, '不支持的交付清单')
    characters = delivery['characters']
    require(len(characters) == 7 and len({c['characterId'] for c in characters}) == 7, '须为七个不同角色')
    manifests = {}
    # 在写入任何文件之前核验整个批次，避免中途输入错误造成半套启用。
    for c in characters:
        cid = c['characterId']
        manifests[cid] = runtime_manifest(c)
        folder = safe_path(source_root, cid)
        if not verify:
            require(digest(safe_path(folder, c['manifest'])) == c['manifestSha256'], f'源清单已变动: {cid}')
        for f in c['frames']:
            relative = f"{f['action']}/{f['direction']}/{f['frame']:02d}.png"
            path = safe_path(project / FAMILY / cid, relative) if verify else safe_path(folder, f['source'])
            check_png(path, f['sha256'])
        if verify:
            manifest_path = project / FAMILY / cid / 'manifest.json'
            require(manifest_path.read_bytes() == json_bytes(manifests[cid]), f'客户端时序/锚点清单不匹配: {cid}')
    if execute:
        for c in characters:
            cid = c['characterId']
            destination = project / FAMILY / cid
            for f in c['frames']:
                source = safe_path(source_root / cid, f['source'])
                path = destination / f['action'] / f['direction'] / f"{f['frame']:02d}.png"
                path.parent.mkdir(parents=True, exist_ok=True)
                if not path.exists() or digest(path) != f['sha256']:
                    shutil.copyfile(source, path)
                meta(path, project)
            # 最后发布运行清单，读取方只能看到已复制的完整动作集。
            output = destination / 'manifest.json'
            pending = output.with_suffix('.json.importing')
            pending.write_bytes(json_bytes(manifests[cid]))
            pending.replace(output)
            meta(output, project)
        family = project / FAMILY
        for folder in sorted((p for p in family.rglob('*') if p.is_dir())):
            meta(folder, project)
        # 稀疏检出时不要替既有 Git 目录生成不同GUID；这里只给新增动作根生成meta。
        meta(family, project)
        run(delivery, source_root, project, verify=True)
    return {'characters': len(characters), 'clips': sum(len(m['clips']) for m in manifests.values()),
            'pngs': sum(len(c['frames']) for c in characters), 'mode': 'verified' if verify else 'imported' if execute else 'dry_run'}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--delivery', type=Path, default=DELIVERY)
    parser.add_argument('--source-root', type=Path, default=ROOT.parent / 'image/qdao_original_roster_v14_hd/action-remake-20261001/characters')
    parser.add_argument('--project', type=Path, default=ROOT)
    modes = parser.add_mutually_exclusive_group()
    modes.add_argument('--execute', action='store_true')
    modes.add_argument('--verify', action='store_true')
    args = parser.parse_args()
    delivery = json.loads(args.delivery.read_text(encoding='utf-8-sig'))
    print(json.dumps(run(delivery, args.source_root.resolve(), args.project.resolve(), args.execute, args.verify), ensure_ascii=False))


if __name__ == '__main__':
    main()
