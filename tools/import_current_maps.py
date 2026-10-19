"""Import the three approved layout images and conservative navigation for online play.

Images are copied byte-for-byte; these 1254px layouts are not finished 4K tiles.
Verification uses the delivered polygon evidence and needs no art checkout.
"""
from __future__ import annotations

import argparse
import base64
from collections import deque
import hashlib
import json
import math
from pathlib import Path
import uuid

from PIL import Image, ImageDraw

PROJECT = Path(__file__).resolve().parents[1]
DELIVERY = Path('Assets/Resources/World/CurrentMaps20261009')
PRODUCTION = Path('qdao_city_tiles_4k_20260916/builtin_q64_production')
PREVIEWS = PRODUCTION / 'parallel_20261005/daylight-only-20261009'
ANNOTATIONS = PRODUCTION / 'capacity-layout-validation-20261010/maps'
RESOLUTION = 150
WORLD_RECT = dict(x=50, y=0, width=300, height=300)
MAPS = {
    'main': (1, '主城', 'tianyong/tianyong-spring-natural-stone-v9.png',
             '47485b823db7dbe4e63883f9ae6d759f3f2fbb5897c72f16fa64e921bde6ccd7', (199, 0, 133)),
    'village': (3, '渔村', 'donghai-natural-stone-clear-routes-v10.png',
                'cf450f0afe784b4700f2dfc19b3af35867765403e8f1d81741e78b3116583d42', (201, 0, 147)),
    'island': (2, '八仙岛', 'penglai/penglai-natural-stone-wall-trees-v6.png',
               'e7a06f547274a29a01cc4e20223637e99c0fab534d4982361e824147c800b34f', (195, 0, 173)),
}


def sha(data):
    return hashlib.sha256(data).hexdigest()


def json_bytes(value):
    return (json.dumps(value, ensure_ascii=False, indent=2) + '\n').encode('utf-8')


def spawn_cell(spawn, resolution=RESOLUTION):
    x, _, z = spawn
    return (math.floor((x - WORLD_RECT['x']) / WORLD_RECT['width'] * resolution),
            resolution - 1 - math.floor(z / WORLD_RECT['height'] * resolution))


def connected_component(cells, width, height, spawn):
    sx, sy = spawn
    if not (0 <= sx < width and 0 <= sy < height) or not cells[sy * width + sx]:
        raise ValueError('Spawn is outside conservative visible ground')
    kept = [False] * (width * height)
    kept[sy * width + sx] = True
    queue = deque([(sx, sy)])
    while queue:
        x, y = queue.popleft()
        for nx, ny in ((x - 1, y), (x + 1, y), (x, y - 1), (x, y + 1)):
            if 0 <= nx < width and 0 <= ny < height:
                i = ny * width + nx
                if cells[i] and not kept[i]:
                    kept[i] = True
                    queue.append((nx, ny))
    return kept


def pack_mask(cells):
    packed = bytearray((len(cells) + 7) // 8)
    for i, walkable in enumerate(cells):
        if walkable:
            packed[i // 8] |= 1 << (7 - i % 8)
    return bytes(packed)


def build_mask(annotation, spawn, resolution=RESOLUTION):
    width, height = annotation['annotationWidth'], annotation['annotationHeight']
    image = Image.new('L', (width, height), 0)
    draw = ImageDraw.Draw(image)
    for polygon in annotation['walkablePolygons']:
        draw.polygon([tuple(p) for p in polygon['points']], fill=255)
    for polygon in annotation['obstaclePolygons']:
        draw.polygon([tuple(p) for p in polygon['points']], fill=0)
    # A cell is admitted only when every source pixel intersecting its area is
    # visible ground. Narrow or occluded links stay blocked instead of guessed.
    cells = []
    for row in range(resolution):
        for col in range(resolution):
            box = (math.floor(col * width / resolution), math.floor(row * height / resolution),
                   math.ceil((col + 1) * width / resolution), math.ceil((row + 1) * height / resolution))
            cells.append(image.crop(box).getextrema()[0] == 255)
    kept = connected_component(cells, resolution, resolution, spawn_cell(spawn, resolution))
    return pack_mask(kept), sum(kept), sum(cells)


def write_meta(path, project):
    relative = path.relative_to(project).as_posix()
    guid = uuid.uuid5(uuid.NAMESPACE_URL, 'mmorpg-client/current-maps/' + relative).hex
    if path.is_dir():
        body = 'folderAsset: yes\nDefaultImporter:\n  externalObjects: {}\n'
    elif path.suffix == '.png':
        body = ('TextureImporter:\n  externalObjects: {}\n  serializedVersion: 13\n'
                '  mipmaps:\n    enableMipMap: 0\n    sRGBTexture: 1\n'
                '  isReadable: 0\n  textureType: 0\n  textureShape: 1\n'
                '  maxTextureSize: 2048\n  nPOTScale: 0\n'
                '  textureSettings:\n    serializedVersion: 2\n    filterMode: 1\n'
                '    aniso: 1\n    wrapU: 1\n    wrapV: 1\n    wrapW: 1\n'
                '  textureCompression: 0\n  alphaIsTransparency: 0\n'
                '  platformSettings:\n  - serializedVersion: 3\n    buildTarget: DefaultTexturePlatform\n'
                '    maxTextureSize: 2048\n    resizeAlgorithm: 0\n    textureFormat: -1\n'
                '    textureCompression: 0\n    compressionQuality: 100\n    overridden: 0\n')
    else:
        body = 'TextScriptImporter:\n  externalObjects: {}\n'
    target = Path(str(path) + '.meta')
    if not target.exists():
        target.write_text(f'fileFormatVersion: 2\nguid: {guid}\n' + body,
                          encoding='utf-8', newline='\n')


def import_maps(project, art_root):
    root = project / DELIVERY
    root.mkdir(parents=True, exist_ok=True)
    write_meta(root, project)
    for key, (scene, name, relative, expected, spawn) in MAPS.items():
        image_path = art_root / PREVIEWS / relative
        annotation_relative = ANNOTATIONS / (key + '.json')
        original_annotation = (art_root / annotation_relative).read_bytes()
        annotation = json.loads(original_annotation)
        image_bytes = image_path.read_bytes()
        if sha(image_bytes) != expected or annotation['imageSha256'] != expected:
            raise ValueError(f'{key}: approved artwork/annotation SHA mismatch')
        with Image.open(image_path) as image:
            if image.size != (1254, 1254):
                raise ValueError(f'{key}: unexpected layout size')
        # Keep the evidence portable; source.json retains original repo paths.
        annotation.pop('imagePath', None)
        nav_bytes = json_bytes(annotation)
        mask, count, before_count = build_mask(annotation, spawn)
        mask_text = base64.b64encode(mask)
        source = dict(schemaVersion=1, key=key, sceneId=scene, name=name,
                      artStatus='layout_preview', navigationStatus='conservative_visible_ground',
                      worldRect=WORLD_RECT, layoutPixels=dict(width=1254, height=1254),
                      walkmaskResolution=RESOLUTION, cellSize=2,
                      spawnWorld=dict(zip(('x', 'y', 'z'), spawn)),
                      layoutSha256=expected, walkmaskSha256=sha(mask_text),
                      walkmaskPackedSha256=sha(mask), navigationSha256=sha(nav_bytes),
                      walkableCellCount=count, conservativeCellCountBeforeConnectivity=before_count,
                      maskEncoding='base64-msb-first-row-major-top-to-bottom',
                      sampling='all intersecting source pixels walkable; spawn four-neighbour component',
                      worldScaleStatus='temporary_integration_scale_not_capacity_approved',
                      sourceRepository='https://github.com/luhailong-cpp/image',
                      sourceLayoutPath=(PREVIEWS / relative).as_posix(),
                      sourceAnnotationPath=annotation_relative.as_posix(),
                      sourceAnnotationSha256=sha(original_annotation))
        folder = root / key
        folder.mkdir(exist_ok=True)
        write_meta(folder, project)
        for filename, data in (('layout.png', image_bytes), ('navigation.json', nav_bytes),
                               ('walkmask.txt', mask_text), ('source.json', json_bytes(source))):
            path = folder / filename
            path.write_bytes(data)
            write_meta(path, project)
    return verify(project)


def verify(project):
    results = []
    for key, (scene, _, _, expected, spawn) in MAPS.items():
        folder = project / DELIVERY / key
        source = json.loads((folder / 'source.json').read_bytes())
        nav_bytes = (folder / 'navigation.json').read_bytes()
        mask_text = (folder / 'walkmask.txt').read_bytes()
        packed = base64.b64decode(mask_text, validate=True)
        mask, count, before_count = build_mask(json.loads(nav_bytes), spawn)
        requirements = {
            'identity': source['schemaVersion'] == 1 and source['key'] == key and source['sceneId'] == scene,
            'world rectangle': source['worldRect'] == WORLD_RECT,
            'spawn': source['spawnWorld'] == dict(zip(('x', 'y', 'z'), spawn)),
            'grid': source['walkmaskResolution'] == RESOLUTION and source['cellSize'] == 2,
            'status': source['artStatus'] == 'layout_preview' and source['navigationStatus'] == 'conservative_visible_ground',
            'image bytes': sha((folder / 'layout.png').read_bytes()) == expected == source['layoutSha256'],
            'polygon bytes': sha(nav_bytes) == source['navigationSha256'],
            'mask text bytes': sha(mask_text) == source['walkmaskSha256'],
            'mask packed bytes': sha(packed) == source['walkmaskPackedSha256'],
            'reproducible mask': packed == mask and len(packed) == 2813 and (packed[-1] & 15) == 0,
            'cell counts': count == source['walkableCellCount'] and before_count == source['conservativeCellCountBeforeConnectivity'],
            'portable source': not Path(source['sourceLayoutPath']).is_absolute() and not Path(source['sourceAnnotationPath']).is_absolute(),
        }
        for check, passed in requirements.items():
            if not passed:
                raise ValueError(f'{key}: {check} failed')
        with Image.open(folder / 'layout.png') as image:
            if image.size != (1254, 1254) or source['layoutPixels'] != dict(width=1254, height=1254):
                raise ValueError(f'{key}: layout dimensions changed')
        results.append(dict(key=key, sceneId=scene, spawnWorld=source['spawnWorld'],
                            walkableCells=count, packedSha256=sha(packed)))
    return results


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--project', type=Path, default=PROJECT)
    parser.add_argument('--art-root', type=Path, default=PROJECT.parent / 'image')
    parser.add_argument('--verify', action='store_true', help='Check delivered resources without the image repository')
    args = parser.parse_args()
    results = verify(args.project) if args.verify else import_maps(args.project, args.art_root)
    print(json.dumps(dict(ok=True, maps=results), ensure_ascii=False, indent=2))


if __name__ == '__main__':
    main()
