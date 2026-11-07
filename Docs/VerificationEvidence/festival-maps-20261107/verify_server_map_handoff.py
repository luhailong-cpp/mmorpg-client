"""Verify the portable map handoff without images, credentials or third-party packages."""
import argparse
import base64
import hashlib
import json
import math
from pathlib import Path


def checked_path(root, relative):
    path = (root / relative).resolve()
    if not path.is_relative_to(root.resolve()):
        raise ValueError('Path escapes root: ' + relative)
    return path


def verify(root, data_path):
    data = json.loads(data_path.read_text(encoding='utf-8-sig'))
    assert data['counts'] == dict(mapIds=9, appearances=15, runtimeTiles=90, uniqueNavigationMasks=9)
    for entry in data['requiredClientFiles']:
        raw = checked_path(root, entry['relativePath']).read_bytes()
        assert len(raw) == entry['bytes'], entry['relativePath']
        assert hashlib.sha256(raw).hexdigest() == entry['sha256'], entry['relativePath']
    maps = []
    for m in data['maps']:
        assert m['clientOnly'] and not m['authoritativeServerRegistered']
        assert not m['automaticOnlineActivationAllowed']
        assert int(m['paddedMapId']) == m['numericSceneId']
        n = m['navigation']
        raw = base64.b64decode(n['base64'], validate=True)
        assert len(raw) == n['packedBytes'] == (n['totalCells'] + 7) // 8
        assert hashlib.sha256(raw).hexdigest() == n['packedSha256']
        assert n['totalCells'] == n['columns'] * n['rows']
        assert all(not raw[i >> 3] & (0x80 >> (i & 7)) for i in range(n['totalCells'], len(raw) * 8))
        cells = {i for i in range(n['totalCells']) if raw[i >> 3] & (0x80 >> (i & 7))}
        assert len(cells) == n['walkableCells']
        actual = base64.b64decode(checked_path(root, n['canonicalWalkmaskRelativePath']).read_text().strip(), validate=True)
        assert actual == raw
        r, spawn, size = m['worldRect'], m['spawnWorld'], n['cellSize']
        col = math.floor((spawn['x'] - r['x']) / size)
        row = n['rows'] - 1 - math.floor((spawn['z'] - r['y']) / size)
        assert row * n['columns'] + col == n['spawnStorageIndex']
        assert n['spawnStorageIndex'] in cells
        remaining, components = set(cells), []
        while remaining:
            queue = [remaining.pop()]
            count = 0
            while queue:
                current = queue.pop()
                count += 1
                y, x = divmod(current, n['columns'])
                for dy, dx in ((0, 1), (0, -1), (1, 0), (-1, 0), (1, 1), (1, -1), (-1, 1), (-1, -1)):
                    xx, yy = x + dx, y + dy
                    if not (0 <= xx < n['columns'] and 0 <= yy < n['rows']):
                        continue
                    target = yy * n['columns'] + xx
                    if target not in remaining:
                        continue
                    if dx and dy and (y * n['columns'] + xx not in cells or yy * n['columns'] + x not in cells):
                        continue
                    remaining.remove(target)
                    queue.append(target)
            components.append(count)
        assert len(components) == n['componentCount']
        assert sorted(components) == sorted(n['componentSizes'])
        maps.append(dict(id=m['paddedMapId'], walkableCells=len(cells), components=len(components)))
    manifest = root.parent / 'bundle-manifest.json'
    bundle_files = 0
    if manifest.is_file():
        for f in json.loads(manifest.read_text(encoding='utf-8'))['files']:
            raw = checked_path(root.parent, f['path']).read_bytes()
            assert len(raw) == f['bytes'] and hashlib.sha256(raw).hexdigest() == f['sha256'], f['path']
            bundle_files += 1
    return dict(ok=True, clientFiles=len(data['requiredClientFiles']), maps=maps, bundleFiles=bundle_files,
                serverRegistered=False, serverDeployed=False, note='File and client-mask integrity only; no server bake or live acceptance.')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--client-root', type=Path, default=Path('client') if Path('client').is_dir() else Path('.'))
    parser.add_argument('--data', type=Path)
    args = parser.parse_args()
    data_path = args.data or args.client_root / 'Docs/VerificationEvidence/festival-maps-20261107/server-handoff-data.json'
    print(json.dumps(verify(args.client_root, data_path), ensure_ascii=False, indent=2))
