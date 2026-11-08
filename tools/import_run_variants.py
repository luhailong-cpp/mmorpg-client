"""导入经独立批准的跑步版本；默认只执行只读预检。

支持可移植道童 legacy 交付、与源文件绑定的四正向验收回执，以及
qdao.run-variant-delivery/v1。标准交付包含 sourceRoot（相对素材仓库）、
characterId、variant 和 clips；每个 clip 包含 direction、
selection {file, sha256}、approval {file, sha256} 及有序帧
{frame, file, sha256, durationSeconds}。current 版本的批准记录必须在
orderedSourceHashes 绑定八个有序源哈希，并明确设置 accepted=true、
variant=current。已确认的 legacy 片段要求 identityConfirmed=true、
originalTiming=true；已明确分类的候选版本则须验证源文件保留、
顺序和时长，并保留其尚未确认的历史身份。

发布操作以事务方式替换单个角色目录，读取者不会看到新旧文件混合；
替换期间目录可能短暂不存在，因此须停止 Unity 编辑器后执行。
绝不修改原生图片。

可选的游戏显示校准自动从所选客户端的
tools/run-variant-game-calibrations.json 加载。每条 current8 规则
均绑定严格有序的源哈希列表；源文件变化时须明确复核规则。
仅修改 manifest 的 clip 字段，这不代表游戏验收或图片修复。
"""
from __future__ import annotations

import argparse
from dataclasses import dataclass
import hashlib
from io import BytesIO
import json
import math
import os
from pathlib import Path
import re
import shutil
import tempfile
import uuid

from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
FAMILY = Path('Assets/Resources/World/Characters/QdaoRunVariants20261105')
GAME_CALIBRATIONS = Path('tools/run-variant-game-calibrations.json')
DIRECTIONS = ('N', 'NE', 'E', 'SE', 'S', 'SW', 'W', 'NW')
CHARACTERS = frozenset(('00_reference_topright_boy', '01_ice_sword_girl',
    '02_fire_talisman_boy', '03_lotus_healer_girl', '04_mountain_guardian_boy',
    '05_celestial_musician_girl', '06_thunder_caster_boy', '08_alchemy_prodigy_boy',
    '09_bamboo_archer_girl', '10_crimson_spear_girl', '15_water_dragon_scholar_boy',
    '17_ghost_script_calligrapher_boy'))
SHA = re.compile(r'^[0-9a-f]{64}$')
ACCEPTED = {'explicit_character_and_source_bound',
            'latest_user_cardinal_confirmation_bound_to_last_shown_player'}
LEGACY_CANDIDATE_LABEL = '旧版候选（双椭圆身份待核）'
LEGACY_CANDIDATE_IDENTITIES = {
    'legacy_candidate_unclassified': LEGACY_CANDIDATE_LABEL,
    'legacy_later_natural16_candidate': '旧版候选（16帧阶段版；双椭圆身份待核）',
}


def require(condition, message):
    if not condition:
        raise ValueError(message)


def digest(data):
    return hashlib.sha256(data).hexdigest()


def encoded(value):
    return (json.dumps(value, ensure_ascii=False, sort_keys=True, indent=2,
                       allow_nan=False) + '\n').encode('utf-8')


def read_json(path):
    return json.loads(path.read_text(encoding='utf-8-sig'),
                      parse_constant=lambda value: (_ for _ in ()).throw(ValueError('Nonfinite JSON: ' + value)))


def linked(path):
    return path.is_symlink() or (hasattr(path, 'is_junction') and path.is_junction())


def scoped(root, value):
    """仅允许指定根目录内的路径；相对路径以该根目录为基准，拒绝链接及父级穿越。"""
    root = Path(root).absolute()
    value = Path(value)
    require('..' not in value.parts, f'Parent traversal is forbidden: {value}')
    path = value if value.is_absolute() else root / value
    path = path.absolute()
    require(path.is_relative_to(root), f'Path outside scope: {path}')
    cursor = root
    require(not linked(root), f'Linked root forbidden: {root}')
    for part in path.relative_to(root).parts:
        cursor /= part
        require(not linked(cursor), f'Linked path forbidden: {cursor}')
    require(path.resolve().is_relative_to(root.resolve()), f'Resolved path outside scope: {path}')
    return path


def asset_root(manifest):
    for ancestor in manifest.parents:
        if (ancestor / 'AGENTS.md').is_file():
            return ancestor
    return manifest.parent


def evidence(root, item):
    require(isinstance(item, dict) and isinstance(item.get('file'), str), 'Missing evidence file')
    expected = item.get('sha256', '')
    require(isinstance(expected, str) and SHA.fullmatch(expected), 'Missing evidence SHA256')
    path = scoped(root, item['file'])
    require(path.is_file(), f'Missing evidence: {path}')
    require(digest(path.read_bytes()) == expected, f'Evidence changed: {path}')
    return read_json(path)


def duration(value):
    require(isinstance(value, (int, float)) and not isinstance(value, bool)
            and math.isfinite(value) and 0 < value <= 60, f'Invalid frame duration: {value}')
    return float(value)


def game_calibrations(client_root):
    """加载与源文件绑定的可移植 clip 设置，不影响未匹配的输出。"""
    path = scoped(client_root, GAME_CALIBRATIONS)
    if not path.exists():
        return {}
    require(path.is_file(), f'Game calibration rules must be a file: {path}')
    data = read_json(path)
    require(isinstance(data, dict)
            and data.get('schema') == 'qdao.run-variant-game-calibrations/v1'
            and isinstance(data.get('rules'), list), 'Invalid game calibration schema/rules')
    rules, ids = {}, set()
    for rule in data['rules']:
        require(isinstance(rule, dict), 'Game calibration rule must be an object')
        key = (rule.get('characterId'), rule.get('variant'), rule.get('direction'))
        require(isinstance(key[0], str) and key[0] in CHARACTERS
                and key[1] == 'current' and key[2] in DIRECTIONS,
                'Game calibration requires a retained character, current variant and explicit direction')
        rule_id = rule.get('id')
        require(isinstance(rule_id, str) and re.fullmatch(r'[A-Za-z0-9._-]{1,128}', rule_id),
                'Game calibration requires a portable rule id')
        require(key not in rules and rule_id not in ids, 'Duplicate game calibration scope or id')
        hashes = rule.get('orderedSourceHashes')
        require(isinstance(hashes, list) and len(hashes) == 8
                and all(isinstance(value, str) and SHA.fullmatch(value) for value in hashes)
                and len(set(hashes)) == 8, 'Game calibration must bind eight unique ordered source hashes')
        settings = rule.get('runtimeClip')
        require(isinstance(settings, dict)
                and set(settings) == {'pixelsPerUnit', 'overridePivot', 'pivotX', 'pivotY'}
                and settings.get('overridePivot') is True,
                'Game calibration requires exact clip PPU and explicit pivot override fields')
        for field in ('pixelsPerUnit', 'pivotX', 'pivotY'):
            value = settings[field]
            require(isinstance(value, (int, float)) and not isinstance(value, bool)
                    and math.isfinite(value)
                    and (value > 0 if field == 'pixelsPerUnit' else 0 <= value <= 1),
                    f'Invalid game calibration {field}: {value}')
        rules[key] = rule
        ids.add(rule_id)
    return rules


def canonical_clips(data, root, character, variant):
    require(data.get('characterId') == character and data.get('variant') == variant,
            'Character/variant differs from delivery; fallback is forbidden')
    scope = scoped(root, data.get('sourceRoot', '.'))
    clips = []
    for clip in data.get('clips', []):
        selection = evidence(scope, clip['selection'])
        source_hashes = [f['sha256'] for f in clip['frames']]
        require([f.get('sha256') for f in selection.get('frames', [])] == source_hashes,
                'Delivery frame order differs from independently selected source')
        require([f.get('durationSeconds') for f in selection['frames']] ==
                [f.get('durationSeconds') for f in clip['frames']],
                'Delivery timing differs from the original selected source')
        approval = evidence(scope, clip['approval'])
        require(approval.get('characterId') == character
                and approval.get('direction') == clip['direction']
                and approval.get('variant') == variant
                and approval.get('orderedSourceHashes') == source_hashes,
                'Independent approval does not bind this character, direction, variant and source order')
        identity = clip.get('sourceIdentity')
        extra = {}
        if identity is not None:
            # 保留旧源用于对比，不代表美术验收通过，
            # 也不能证明它就是用户所指的双椭圆版本。
            require(variant == 'legacy' and isinstance(identity, dict)
                    and set(identity) == {'classification', 'doubleEllipseConfirmed', 'displayLabel'}
                    and identity.get('classification') in LEGACY_CANDIDATE_IDENTITIES
                    and identity.get('doubleEllipseConfirmed') is False
                    and identity.get('displayLabel') == LEGACY_CANDIDATE_IDENTITIES[identity['classification']],
                    'Invalid or misleading unclassified legacy source identity')
            require(approval.get('sourceRetentionVerified') is True
                    and approval.get('originalTiming') is True
                    and approval.get('originalOrder') is True
                    and approval.get('identityConfirmed') is False
                    and approval.get('sourceIdentity', identity) == identity,
                    'Legacy candidate requires verified retention, original timing/order and unconfirmed identity')
            canvas = clip.get('nativeCanvas')
            require(canvas in ([1024, 1024], [1254, 1254])
                    and all(type(side) is int for side in canvas),
                    'Legacy candidate requires explicit nativeCanvas 1024 or1254 square')
            extra = {'sourceIdentity': dict(identity), 'nativeCanvas': list(canvas)}
        else:
            require(approval.get('accepted') is True,
                    'Independent approval must accept the selected source')
            if variant == 'legacy':
                require(approval.get('identityConfirmed') is True and approval.get('originalTiming') is True,
                        'Legacy identity/original timing not confirmed')
            require(clip.get('nativeCanvas', [1254, 1254]) == [1254, 1254],
                    'Confirmed legacy/current sources require native1254 canvas')
        frames = []
        for frame in clip['frames']:
            frames.append({**frame, 'path': scoped(scope, frame['file'])})
        clips.append({'direction': clip['direction'], 'frames': frames,
                      'approvalSha256': clip['approval']['sha256'], **extra})
    return clips


def boy_clips(data, manifest, root, character, variant):
    require(character == '00_reference_topright_boy' and variant == 'legacy',
            'Portable sixteen-frame boy delivery is legacy only; never current8')
    require(data.get('allEightAccepted') is True and data.get('acceptedFrames') == 128
            and data.get('acceptedDirections') == 8 and data.get('frameOrder') == list(range(1, 17)),
            'Boy delivery acceptance/order is incomplete')
    require(data.get('fps') == 15 and data.get('canvas') == [1254, 1254],
            'Legacy boy requires original 16 frames at 15 fps on native 1254 canvas')
    clips = []
    for clip in data['directions']:
        require(clip.get('direction') in DIRECTIONS, 'Invalid portable direction')
        # 可移植包保留字节一致的证据；原工作站路径可能已删除，
        # 不得仅为导入而恢复这些路径。
        records = Path('records') / clip['direction']
        evidence(manifest.parent, {'file': str(records / 'selection.json'), 'sha256': clip['selectionSha256']})
        evidence(manifest.parent, {'file': str(records / 'acceptance.json'), 'sha256': clip['authoritySha256']})
        frames = [{**f, 'path': scoped(manifest.parent, f['file']), 'durationSeconds': 1 / 15}
                  for f in clip['frames']]
        require(len(frames) == 16, 'Legacy boy direction must retain all 16 poses')
        clips.append({'direction': clip['direction'], 'frames': frames,
                      'approvalSha256': clip['authoritySha256']})
    require({c['direction'] for c in clips} == set(DIRECTIONS), 'Legacy boy requires all eight directions')
    return clips


def cardinal_clips(data, root, character, variant):
    require(variant == 'current', 'Cardinal current8 acceptance cannot supply legacy')
    clips = []
    for row in data['records']:
        if row['characterId'] != character:
            continue
        accepted = row.get('userVisualAcceptance', {})
        if accepted.get('status') not in ACCEPTED:
            continue
        require(row.get('variantId') == 'current_natural_8'
                and row.get('completeEightSourcesExist') is True
                and row.get('shownPlayerExactSourcesMatch') is True,
                'Approved current8 receipt does not bind a complete unchanged displayed selection')
        selection = evidence(root, row['selection'])
        frames = selection.get('frames', [])
        require([f.get('sha256') for f in frames] == row['orderedSourceHashes']
                == row['shownPlayerBinding']['frameSha256s'], 'Selected/displayed hashes differ')
        # 用户验收回执是独立权威依据，与源文件选定记录区分。
        frames = [{**f, 'path': scoped(root, f['file']), 'durationSeconds': 2 / 15}
                  for f in frames]
        clips.append({'direction': row['direction'], 'frames': frames,
                      'approvalSha256': digest(encoded(accepted)),
                      'acceptance': 'user_visual_source_bound'})
    return clips


def texture_bytes(path, expected, native_canvas=(1254, 1254)):
    require(isinstance(expected, str) and SHA.fullmatch(expected), f'Invalid PNG SHA256: {path}')
    require(path.is_file(), f'Missing native PNG: {path}')
    data = path.read_bytes()
    require(digest(data) == expected, f'Native SHA256 mismatch: {path}')
    with Image.open(BytesIO(data)) as image:
        require(image.format == 'PNG' and image.mode == 'RGBA' and image.size == tuple(native_canvas),
                f'Expected native {native_canvas[0]}x{native_canvas[1]} RGBA PNG: {path}')
        image.load()
        # 只处理完整固定画布，不逐帧进行包围盒处理、位移或姿态合成。
        output = image.resize((384, 384), Image.Resampling.LANCZOS)
        stream = BytesIO()
        output.save(stream, format='PNG', compress_level=9)
        return stream.getvalue()


def meta_bytes(relative, kind):
    guid = uuid.uuid5(uuid.NAMESPACE_URL, 'mmorpg-client/' + relative.as_posix()).hex
    if kind == 'folder':
        body = 'folderAsset: yes\nDefaultImporter:\n  externalObjects: {}\n  userData: run-variants-20261105\n'
    elif kind == 'png':
        body = ('TextureImporter:\n  externalObjects: {}\n  serializedVersion: 13\n'
                '  mipmaps:\n    enableMipMap: 0\n    sRGBTexture: 1\n'
                '  isReadable: 0\n  textureType: 0\n  textureShape: 1\n'
                '  maxTextureSize: 512\n  textureSettings:\n    serializedVersion: 2\n'
                '    filterMode: 1\n    aniso: 1\n    mipBias: 0\n    wrapU: 1\n    wrapV: 1\n    wrapW: 1\n'
                '  alphaUsage: 1\n  alphaIsTransparency: 1\n  nPOTScale: 0\n'
                '  platformSettings:\n  - serializedVersion: 4\n    buildTarget: DefaultTexturePlatform\n'
                '    maxTextureSize: 512\n    textureFormat: -1\n    textureCompression: 0\n'
                '    overridden: 0\n  userData: run-variants-20261105\n')
    else:
        body = 'TextScriptImporter:\n  externalObjects: {}\n  userData: run-variants-20261105\n'
    return (f'fileFormatVersion: 2\nguid: {guid}\n' + body).encode()


def meta_fields(payload):
    """无需 YAML 依赖，读取 Unity 受限的标量／映射 .meta 语法。

    保留完整缩进路径（含平台列表索引），拒绝重复键，
    保留 Unity 添加的无关默认值；这不是通用 YAML 加载器。
    """
    fields, stack, list_counts = {}, [], {}
    def put(key, value):
        require(key not in fields, f'Duplicate .meta field: {"/".join(key)}')
        fields[key] = value
    for line in payload.decode('utf-8-sig').splitlines():
        if not line.strip() or line.lstrip().startswith('#'):
            continue
        match = re.fullmatch(r'( *)(- )?([A-Za-z0-9_]+):(?: *(.*))?', line)
        require(match is not None, f'Unsupported .meta syntax: {line}')
        indent, sequence, key, value = len(match[1]), bool(match[2]), match[3], match[4] or ''
        if sequence:
            while stack and (stack[-1][0] > indent or
                             (stack[-1][0] == indent and stack[-1][1].startswith('['))):
                stack.pop()
            require(stack and stack[-1][0] == indent, 'Malformed .meta list')
            parent = tuple(v for _, v in stack)
            index = list_counts.get(parent, 0)
            list_counts[parent] = index + 1
            stack.append((indent, f'[{index}]'))
            indent += 2
        else:
            while stack and stack[-1][0] >= indent:
                stack.pop()
        path = tuple(v for _, v in stack) + (key,)
        value = value.strip()
        put(path, value)
        if not value:
            stack.append((indent, key))
        elif value.startswith('{') and value.endswith('}') and value != '{}':
            for pair in value[1:-1].split(','):
                part = pair.strip().split(':', 1)
                require(len(part) == 2, 'Malformed inline .meta mapping')
                put(path + (part[0].strip(),), part[1].strip())
    return fields


def verify_meta(actual, expected, label):
    """比较导入语义，允许 Unity 展开序列化字段和默认值。

    这些素材以 Texture2D 加载。Sprite.Create 使用 manifest 默认值
    PPU39/pivot(.5,.08)，或使用明确绑定源文件的 clip 覆盖值。
    未启用的 TextureImporter sprite 默认值仍为 PPU100/pivot(.5,.5)；
    分别核验这些契约，不将其混为一谈。
    """
    got, want = meta_fields(actual), meta_fields(expected)
    def value(path, default=None):
        return got.get(tuple(path.split('/')), default)
    def same(path, expected_value, optional=False):
        found = value(path, str(expected_value) if optional else None)
        if isinstance(expected_value, (int, float)):
            try:
                valid = found is not None and math.isfinite(float(found)) and float(found) == expected_value
            except (ValueError, TypeError):
                valid = False
        else:
            valid = found == expected_value
        require(valid, f'.meta semantic mismatch {label}: {path}={found!r}, expected {expected_value!r}')
    same('fileFormatVersion', 2)
    same('guid', want[('guid',)])
    importer = next(k[0] for k in want if len(k) == 1 and k[0].endswith('Importer'))
    roots = {k[0] for k in got if len(k) == 1 and k[0].endswith('Importer')}
    require(roots == {importer}, f'.meta importer type differs: {label}')
    if importer == 'DefaultImporter':
        same('folderAsset', 'yes')
    else:
        require(value('folderAsset') is None, f'File unexpectedly marked folder: {label}')
    same(importer + '/externalObjects', '{}')
    same(importer + '/userData', 'run-variants-20261105')
    same(importer + '/assetBundleName', '', optional=True)
    same(importer + '/assetBundleVariant', '', optional=True)
    if importer != 'TextureImporter':
        return
    prefix = 'TextureImporter/'
    required = {'isReadable': 0, 'textureType': 0, 'textureShape': 1,
        'maxTextureSize': 512, 'mipmaps/enableMipMap': 0, 'mipmaps/sRGBTexture': 1,
        'textureSettings/filterMode': 1, 'textureSettings/aniso': 1,
        'textureSettings/mipBias': 0, 'textureSettings/wrapU': 1,
        'textureSettings/wrapV': 1, 'textureSettings/wrapW': 1,
        'alphaUsage': 1, 'alphaIsTransparency': 1, 'nPOTScale': 0}
    for name, expected_value in required.items():
        same(prefix + name, expected_value)
    defaults = {'spriteMode': 0, 'spritePixelsToUnits': 100,
        'spritePivot/x': .5, 'spritePivot/y': .5, 'spriteMeshType': 1,
        'alignment': 0, 'streamingMipmaps': 0, 'grayScaleToAlpha': 0,
        'bumpmap/convertToNormalMap': 0, 'flipbookRows': 1, 'flipbookColumns': 1,
        'swizzle': 50462976, 'spriteSheet/sprites': '[]'}
    for name, expected_value in defaults.items():
        same(prefix + name, expected_value, optional=True)
    platform_root = ('TextureImporter', 'platformSettings')
    groups = sorted({k[2] for k in got if k[:2] == platform_root and len(k) >= 4})
    targets = set()
    for group in groups:
        base = prefix + 'platformSettings/' + group + '/'
        target = value(base + 'buildTarget')
        require(target and target not in targets, f'Duplicate/missing platform target: {label}')
        targets.add(target)
        same(base + 'textureFormat', -1)
        same(base + 'textureCompression', 0)
        same(base + 'overridden', 0)
        same(base + 'crunchedCompression', 0, optional=True)
        same(base + 'allowsAlphaSplitting', 0, optional=True)
        same(base + 'resizeAlgorithm', 0, optional=True)
        try:
            maximum = int(value(base + 'maxTextureSize', '0'))
        except ValueError:
            maximum = 0
        # Unity 会将未启用的 DefaultTexturePlatform 上限展开为4096，
        # 实际顶层上限仍为512；两者都不会缩小384像素纹理。
        require(maximum in (512, 1024, 2048, 4096, 8192, 16384),
                f'.meta platform would alter intended canvas: {label}/{target}')
    require('DefaultTexturePlatform' in targets, f'Missing default texture platform: {label}')


@dataclass
class Plan:
    character: str
    variant: str
    destination: Path
    files: dict
    fingerprint: str
    runtime: dict
    png_count: int


def preflight(manifest, character, variant, client_root, directions=None):
    require(character in CHARACTERS, 'Character is not in the exact retained 12-character allowlist')
    require(variant in ('legacy', 'current'), 'Unknown variant; fallback is forbidden')
    client_root = Path(client_root).absolute()
    require(client_root.is_dir() and not linked(client_root), 'Client root must be an existing real directory')
    manifest = Path(manifest).absolute()
    root = asset_root(manifest)
    manifest = scoped(root, manifest)
    data = read_json(manifest)
    schema = data.get('schema')
    if schema == 'boy-run.portable-delivery/v1':
        clips = boy_clips(data, manifest, root, character, variant)
    elif schema == 'run-art.root-cardinal-user-acceptance/v1':
        clips = cardinal_clips(data, root, character, variant)
    elif schema == 'qdao.run-variant-delivery/v1':
        clips = canonical_clips(data, root, character, variant)
    else:
        raise ValueError('Unsupported delivery schema; raw arbitrary sixteen-frame selections are not current8')
    keys = [c['direction'] for c in clips]
    require(all(d in DIRECTIONS for d in keys) and len(set(keys)) == len(keys), 'Invalid or duplicate direction')
    if directions:
        require(set(directions).issubset(DIRECTIONS) and set(directions).issubset(keys),
                'Requested direction is not independently accepted and available')
        clips = [c for c in clips if c['direction'] in directions]
    require(clips, 'No independently approved clips available for this character/variant')
    clips.sort(key=lambda c: DIRECTIONS.index(c['direction']))
    destination = scoped(client_root, FAMILY / variant / character)
    files, runtime_clips, provenance = {}, [], []
    calibrations, applied_calibrations = game_calibrations(client_root), []
    candidate_canvases = []
    for clip in clips:
        frames = clip['frames']
        count = len(frames)
        require(count == 8 if variant == 'current' else 1 <= count <= 256,
                'Current requires exactly eight independent poses; legacy preserves its authored count')
        require([f.get('frame') for f in frames] == list(range(1, count + 1)), 'Missing/duplicate/out-of-order frame indices')
        require(len({f['sha256'] for f in frames}) == count, 'Repeated native frame hashes in one cycle')
        require(len({str(f['path']).casefold() for f in frames}) == count, 'Repeated native source paths in one cycle')
        times = [duration(f['durationSeconds']) for f in frames]
        require(math.isfinite(sum(times)), 'Nonfinite cycle duration')
        direction = clip['direction']
        runtime_clips.append(dict(action='run', direction=direction, frameCount=count,
            frameDurationMs=times[0] * 1000, frameDurationsMs=[t * 1000 for t in times], eventFrame=-1))
        rule = calibrations.get((character, variant, direction))
        if rule is not None:
            hashes = [frame['sha256'] for frame in frames]
            require(hashes == rule['orderedSourceHashes'],
                    f'Game calibration source hash/order mismatch for {variant}/{character}/{direction}; '
                    f'rule {rule["id"]} must be reviewed for the selected eight sources')
            runtime_clips[-1].update(rule['runtimeClip'])
            # 只计算匹配的规范规则哈希，不包含无关文件条目。
            # 输出字段仅记录导入器产物，不能视为运行时验收。
            applied_calibrations.append(dict(ruleId=rule['id'],
                ruleSource=GAME_CALIBRATIONS.as_posix(), ruleSha256=digest(encoded(rule)),
                characterId=character, variant=variant, direction=direction,
                orderedSourceHashes=hashes, configuredRuntimeClip=dict(rule['runtimeClip']),
                emittedRuntimeClip={key: runtime_clips[-1][key] for key in rule['runtimeClip']},
                gameAcceptance='pending_independent_runtime_closure'))
        canvas = clip.get('nativeCanvas', [1254, 1254])
        if 'sourceIdentity' in clip:
            runtime_clips[-1]['sourceIdentity'] = clip['sourceIdentity']
            runtime_clips[-1]['sourceCanvas'] = canvas
            candidate_canvases.append(dict(direction=direction, nativeCanvas=canvas))
        for index, frame in enumerate(frames, 1):
            relative = Path('run') / direction / f'{index:02d}.png'
            source = scoped(root, frame['path'])
            payload = texture_bytes(source, frame['sha256'], canvas)
            files[relative] = payload
            provenance.append(dict(direction=direction, frame=index, source=str(source),
                sourceSha256=frame['sha256'], derived=relative.as_posix(), derivedSha256=digest(payload),
                durationSeconds=times[index - 1], approvalSha256=clip['approvalSha256']))
            if 'sourceIdentity' in clip:
                provenance[-1].update(sourceCanvas=canvas,
                    transform=f'fixed-canvas-{canvas[0]}-to-384-rgba-lanczos-v1',
                    sourceIdentity=clip['sourceIdentity'])
    transform = ('explicit-native-canvas-to-384-rgba-lanczos-v1' if candidate_canvases else
                 'fixed-canvas-1254-to-384-rgba-lanczos-v1')
    fingerprint_input = {'character': character, 'variant': variant,
        'manifestSha256': digest(manifest.read_bytes()), 'frames': provenance,
        'transform': transform}
    if applied_calibrations:
        fingerprint_input['gameCalibrations'] = applied_calibrations
    fingerprint = digest(encoded(fingerprint_input))
    runtime = dict(schemaVersion=1, characterId=character, variant=variant,
        pixelsPerUnit=39, pivotX=.5, pivotY=.08, frameWidth=384, frameHeight=384,
        clips=runtime_clips, unavailableDirections=[d for d in DIRECTIONS if d not in {c['direction'] for c in clips}],
        allEightDirectionsAvailable=len(clips) == 8, sourceManifestSha256=digest(manifest.read_bytes()),
        inputFingerprint=fingerprint, visualValidation='game_scale_ground_anchor_pending',
        sourceFramesPreserved=True, crossVariantFallback=False)
    files[Path('manifest.json')] = encoded(runtime)
    proof = dict(schema='qdao.run-variant-import-proof/v1',
        inputFingerprint=fingerprint, sourceManifest=str(manifest), frames=provenance,
        nativeCanvas=[1254, 1254], gameCanvas=[384, 384], transform='uniform full-canvas LANCZOS',
        pixelsPerUnit=39, pivot=[.5, .08], calibration='pending real Unity game validation')
    if applied_calibrations:
        proof['gameCalibrations'] = applied_calibrations
    if candidate_canvases:
        canvases = [c.get('nativeCanvas', [1254, 1254]) for c in clips]
        proof['nativeCanvas'] = canvases[0] if all(c == canvases[0] for c in canvases) else None
        proof['sourceCanvases'] = [dict(direction=c['direction'], nativeCanvas=canvas)
                                  for c, canvas in zip(clips, canvases)]
    files[Path('import-proof.json')] = encoded(proof)
    for relative in list(files):
        files[Path(str(relative) + '.meta')] = meta_bytes(FAMILY / variant / character / relative,
                                                       'png' if relative.suffix == '.png' else 'text')
    folders = {Path('run')} | {Path('run') / c['direction'] for c in clips}
    for relative in folders:
        files[Path(str(relative) + '.meta')] = meta_bytes(FAMILY / variant / character / relative, 'folder')
    return Plan(character, variant, destination, files, fingerprint, runtime, len(provenance))


def verify(plan, client_root):
    actual = {p.relative_to(plan.destination) for p in plan.destination.rglob('*') if p.is_file()}
    require(actual == set(plan.files), 'Published character contains missing or unexpected files')
    for relative, expected in plan.files.items():
        path = scoped(client_root, plan.destination / relative)
        require(path.is_file(), f'Published file missing: {path}')
        if relative.suffix == '.meta':
            verify_meta(path.read_bytes(), expected, str(path))
        else:
            require(path.read_bytes() == expected, f'Published bytes differ: {path}')
    character_meta = scoped(client_root, str(plan.destination) + '.meta')
    require(character_meta.is_file(), 'Character .meta missing')
    verify_meta(character_meta.read_bytes(), meta_bytes(FAMILY / plan.variant / plan.character, 'folder'),
                str(character_meta))


def publish(plan, client_root, replace=False):
    """执行前须完成完整预检；失败时回滚资源树和新增父目录。"""
    client_root = Path(client_root).absolute()
    destination = scoped(client_root, plan.destination)
    character_meta = scoped(client_root, str(destination) + '.meta')
    expected_meta = meta_bytes(FAMILY / plan.variant / plan.character, 'folder')
    if character_meta.exists():
        verify_meta(character_meta.read_bytes(), expected_meta, str(character_meta))
    if destination.exists():
        require(destination.is_dir(), 'Destination is not a directory')
        prior = scoped(client_root, destination / 'import-proof.json')
        require(prior.is_file(), 'Refusing to overwrite an unowned resource directory')
        old = read_json(prior)
        if old.get('inputFingerprint') == plan.fingerprint:
            verify(plan, client_root)
            return 'unchanged'
        require(replace, 'Different source already published; explicit --replace is required')
    created_dirs, created_metas = [], []
    # 临时目录与目标位于同一卷，以保证目录重命名语义。
    with tempfile.TemporaryDirectory(prefix='.run-variant-stage-', dir=client_root) as temporary:
        staging = Path(temporary) / 'new'
        staging.mkdir()
        for relative, payload in plan.files.items():
            prior_meta = scoped(client_root, destination / relative)
            if relative.suffix == '.meta' and prior_meta.exists():
                verify_meta(prior_meta.read_bytes(), payload, str(prior_meta))
                payload = prior_meta.read_bytes()  # 保留 Unity 合法的规范化结果。
            p = staging / relative
            p.parent.mkdir(parents=True, exist_ok=True)
            p.write_bytes(payload)
        backup = Path(temporary) / 'previous'
        moved_old = installed = False
        try:
            parents = []
            cursor = destination.parent
            while cursor != client_root:
                parents.append(cursor)
                cursor = cursor.parent
            for parent in reversed(parents):
                scoped(client_root, parent)
                if not parent.exists():
                    parent.mkdir()
                    created_dirs.append(parent)
            if destination.exists():
                os.replace(destination, backup)
                moved_old = True
            os.replace(staging, destination)
            installed = True
            for folder in [client_root / FAMILY, client_root / FAMILY / plan.variant, destination]:
                path = scoped(client_root, str(folder) + '.meta')
                if not path.exists():
                    path.write_bytes(meta_bytes(folder.relative_to(client_root), 'folder'))
                    created_metas.append(path)
            verify(plan, client_root)
        except BaseException:
            if installed:
                shutil.rmtree(scoped(client_root, destination))
            if moved_old:
                os.replace(backup, destination)
            for path in reversed(created_metas):
                path.unlink(missing_ok=True)
            for path in reversed(created_dirs):
                path.rmdir()
            raise
    return 'imported'


def run(manifest, character, variant, client_root=ROOT, execute=False, verify_output=False,
        replace=False, directions=None):
    require(not (execute and verify_output), 'Choose execute or verify')
    require(not replace or execute, '--replace requires --execute')
    plan = preflight(manifest, character, variant, client_root, directions)
    mode = 'preflight'
    if verify_output:
        verify(plan, Path(client_root).absolute())
        mode = 'verified'
    elif execute:
        mode = publish(plan, client_root, replace)
    return dict(character=character, variant=variant, mode=mode, pngs=plan.png_count,
        directions=[c['direction'] for c in plan.runtime['clips']],
        unavailableDirections=plan.runtime['unavailableDirections'],
        allEightDirectionsAvailable=plan.runtime['allEightDirectionsAvailable'],
        inputFingerprint=plan.fingerprint, destination=str(plan.destination),
        gameCalibration='pending real Unity game validation')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--manifest', type=Path, required=True)
    parser.add_argument('--character', required=True)
    parser.add_argument('--variant', choices=('legacy', 'current'), required=True)
    parser.add_argument('--client-root', type=Path, default=ROOT)
    parser.add_argument('--direction', choices=DIRECTIONS, action='append')
    parser.add_argument('--replace', action='store_true')
    modes = parser.add_mutually_exclusive_group()
    modes.add_argument('--execute', action='store_true')
    modes.add_argument('--verify', action='store_true')
    args = parser.parse_args()
    try:
        result = run(args.manifest, args.character, args.variant, args.client_root,
                     args.execute, args.verify, args.replace, args.direction)
    except (ValueError, OSError, KeyError, TypeError) as error:
        parser.exit(2, f'Run import rejected: {error}\n')
    print(json.dumps(result, ensure_ascii=False, allow_nan=False))


if __name__ == '__main__':
    main()
