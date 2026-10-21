"""Deliver audited native map tiles without changing pixels or requiring the art checkout.

Import requires a frozen audit and its source artwork. --verify only uses delivered
client files, including normalized QA snapshots whose hashes are kept separate from
the original QA document hashes. Gaps continue to use the existing layout image.
"""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path, PurePosixPath
import re
import shutil
import struct
import uuid
import zlib

from PIL import Image

import import_current_maps as base_maps

PROJECT = Path(__file__).resolve().parents[1]
DELIVERY = base_maps.DELIVERY
RESOURCE_ROOT = 'World/CurrentMaps20261009'
SOURCE_REPOSITORY = 'https://github.com/luhailong-cpp/image'
MAPS = {'main': (1, 16), 'village': (3, 14), 'island': (2, 14)}
TILE_PIXELS = 4096
MAX_GIT_BYTES = 100 * 1024 * 1024
SHA_RE = re.compile(r'^[0-9a-f]{64}$')


def require(condition, message):
    if not condition:
        raise ValueError(message)


def digest(data):
    return hashlib.sha256(data).hexdigest()


def file_digest(path):
    value = hashlib.sha256()
    with path.open('rb') as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b''):
            value.update(block)
    return value.hexdigest()


def json_bytes(value):
    return (json.dumps(value, ensure_ascii=False, indent=2) + '\n').encode('utf-8')


def read_json(path):
    return json.loads(path.read_bytes())


def portable_path(value):
    return (isinstance(value, str) and bool(value) and '\\' not in value
            and ':' not in value and not value.startswith('/')
            and all(part not in ('', '.', '..') for part in value.split('/')))


def source_relative(path, art_root):
    resolved = Path(path).resolve()
    try:
        result = resolved.relative_to(art_root.resolve()).as_posix()
    except ValueError as error:
        raise ValueError(f'Source is outside the specified artwork repository: {path}') from error
    require(portable_path(result), f'Unsafe source path: {result}')
    return result


def normalize_source_references(value, art_root):
    """Keep evidence readable while removing workstation-root dependencies.

    The returned document is a normalized snapshot, never represented as having
    the original document's SHA. Source repository paths remain provenance only.
    """
    if isinstance(value, dict):
        return {k: normalize_source_references(v, art_root) for k, v in value.items()}
    if isinstance(value, list):
        return [normalize_source_references(v, art_root) for v in value]
    if isinstance(value, str):
        # These are source JSON references, not paths consumed by the verifier.
        # Convert their separators so the archived document is readable anywhere.
        value = value.replace('\\', '/')
        # Windows TEMP can use an 8.3 spelling (RUNNER~1) while resolve() expands
        # it. Keep both the original absolute spelling and the resolved root.
        roots = {str(root).replace('\\', '/').rstrip('/')
                 for root in (art_root.absolute(), art_root.resolve())}
        for root in sorted(roots, key=len, reverse=True):
            value = re.sub(re.escape(root + '/'), '', value, flags=re.IGNORECASE)
        # Some QA records mention the generator's original host file, outside
        # the art repository. Label it explicitly as historical provenance;
        # the client does not consume or require that workstation path.
        value = re.sub(r'(?<![A-Za-z])([A-Za-z]):/', r'source-host/\1/', value)
    return value


def meta_text(path, project):
    relative = path.relative_to(project).as_posix()
    guid = uuid.uuid5(uuid.NAMESPACE_URL, 'mmorpg-client/current-maps/' + relative).hex
    if path.is_dir():
        body = 'folderAsset: yes\nDefaultImporter:\n  externalObjects: {}\n'
    elif path.suffix == '.png':
        body = ('TextureImporter:\n  externalObjects: {}\n  serializedVersion: 13\n'
                '  mipmaps:\n    enableMipMap: 0\n    sRGBTexture: 1\n'
                '  isReadable: 0\n  textureType: 0\n  textureShape: 1\n'
                '  maxTextureSize: 4096\n  nPOTScale: 0\n'
                '  textureSettings:\n    serializedVersion: 2\n    filterMode: 1\n'
                '    aniso: 1\n    wrapU: 1\n    wrapV: 1\n    wrapW: 1\n'
                '  textureCompression: 0\n  alphaIsTransparency: 0\n'
                '  platformSettings:\n  - serializedVersion: 3\n    buildTarget: DefaultTexturePlatform\n'
                '    maxTextureSize: 4096\n    resizeAlgorithm: 0\n    textureFormat: 3\n'
                '    textureCompression: 0\n    compressionQuality: 100\n    overridden: 0\n')
    else:
        body = 'TextScriptImporter:\n  externalObjects: {}\n'
    return f'fileFormatVersion: 2\nguid: {guid}\n' + body


def write_meta(path, project):
    Path(str(path) + '.meta').write_text(meta_text(path, project), encoding='utf-8', newline='\n')


def verify_meta(path, project):
    target = Path(str(path) + '.meta')
    require(target.is_file() and target.read_text(encoding='utf-8') == meta_text(path, project),
            f'{path.name}: deterministic metadata/import settings changed')


def verify_png_crc(path):
    # Pillow deliberately stops its CRC walk before IEND. Check every chunk,
    # including IEND, and reject trailing/truncated bytes ourselves.
    with path.open('rb') as stream:
        require(stream.read(8) == b'\x89PNG\r\n\x1a\n', f'{path.name}: invalid PNG signature')
        while True:
            header = stream.read(8)
            require(len(header) == 8, f'{path.name}: truncated PNG chunk header')
            length, kind = struct.unpack('>I4s', header)
            require(length <= path.stat().st_size - stream.tell() - 4,
                    f'{path.name}: truncated PNG chunk payload')
            checksum = zlib.crc32(kind)
            remaining = length
            while remaining:
                block = stream.read(min(remaining, 1024 * 1024))
                require(bool(block), f'{path.name}: truncated PNG data')
                checksum = zlib.crc32(block, checksum)
                remaining -= len(block)
            stored = stream.read(4)
            require(len(stored) == 4 and struct.unpack('>I', stored)[0] == checksum,
                    f'{path.name}: PNG chunk CRC mismatch')
            if kind == b'IEND':
                require(length == 0 and not stream.read(1), f'{path.name}: invalid PNG end/trailing bytes')
                break


def inspect_png(path, expected_sha, expected_bytes):
    require(isinstance(expected_sha, str) and bool(SHA_RE.fullmatch(expected_sha)),
            f'{path.name}: invalid image SHA')
    require(type(expected_bytes) is int and 0 < expected_bytes < MAX_GIT_BYTES,
            f'{path.name}: image must fit GitHub individual-file limit')
    require(path.is_file() and path.stat().st_size == expected_bytes,
            f'{path.name}: image byte count mismatch')
    require(file_digest(path) == expected_sha, f'{path.name}: image SHA mismatch')
    verify_png_crc(path)
    # A second open/load checks full zlib and pixel decoding. Merely inspecting
    # IHDR or validating chunk CRCs would miss truncated IDAT pixel streams.
    with Image.open(path) as image:
        require(image.format == 'PNG' and image.size == (TILE_PIXELS, TILE_PIXELS)
                and image.mode == 'RGB', f'{path.name}: expected native 4096x4096 RGB PNG')
        image.verify()
    with Image.open(path) as image:
        image.load()


def write_json(path, value, project):
    data = json_bytes(value)
    path.write_bytes(data)
    write_meta(path, project)
    return digest(data)


def read_source_document(path, expected_sha, art_root):
    relative = source_relative(path, art_root)
    data = Path(path).read_bytes()
    require(digest(data) == expected_sha, f'Source evidence changed since audit: {path}')
    return dict(sourcePath=relative, sourceSha256=expected_sha,
                normalizedDocument=normalize_source_references(json.loads(data), art_root))


def validate_slot(tile, key, grid):
    row, column = tile['row'], tile['column']
    require(type(row) is int and type(column) is int and 1 <= row <= grid and 1 <= column <= grid,
            f'{key}: tile coordinates outside current grid')
    return f'r{row:02d}_c{column:02d}'


def import_tiles(project, art_root, audit_path):
    project, art_root = project.resolve(), art_root.resolve()
    base_maps.verify(project)
    audit_data = audit_path.read_bytes()
    audit = json.loads(audit_data)
    require(audit.get('schemaVersion') == 1 and audit.get('frozen') is True and isinstance(audit.get('selected'), list)
            and isinstance(audit.get('excluded'), list), 'Import requires the frozen selected/excluded audit')
    before = {p: file_digest(p) for key in MAPS for p in (project / DELIVERY / key).iterdir()
              if p.is_file() and p.name in ('layout.png', 'source.json', 'navigation.json', 'walkmask.txt')}
    require(all(t.get('mapKey') in MAPS for t in audit['selected']), 'Unknown map in audit')
    for key, (scene, grid) in MAPS.items():
        folder = project / DELIVERY / key
        source = read_json(folder / 'source.json')
        contract = audit['contracts'][key]
        require(contract['layoutSha256'] == source['layoutSha256']
                and contract['rows'] == grid and contract['columns'] == grid,
                f'{key}: production contract does not match current layout')
        for dirname in ('tiles', 'tile-evidence'):
            (folder / dirname).mkdir(exist_ok=True)
            write_meta(folder / dirname, project)
        contract_snapshot = dict(schemaVersion=1, sourceRepository=SOURCE_REPOSITORY,
                                 **read_source_document(contract['path'], contract['sha256'], art_root))
        contract_sha = write_json(folder / 'tile-evidence/contract.json', contract_snapshot, project)
        records, seen = [], set()
        selected = sorted((t for t in audit['selected'] if t['mapKey'] == key),
                          key=lambda t: (t['row'], t['column']))
        for tile in selected:
            name = validate_slot(tile, key, grid)
            require(name not in seen, f'{key}/{name}: duplicate audit slot')
            seen.add(name)
            require(tile.get('eligible') is True and tile['sceneId'] == scene
                    and tile['layoutSha256'] == source['layoutSha256']
                    and tile['dimensions'] == [TILE_PIXELS, TILE_PIXELS],
                    f'{key}/{name}: ineligible, stale, or incorrectly sized audit candidate')
            source_path = Path(tile['sourcePath'])
            image_sha, image_bytes = tile['imageSha256'], tile['bytes']
            source_path_relative = source_relative(source_path, art_root)
            require(type(image_bytes) is int and 0 < image_bytes < MAX_GIT_BYTES
                    and source_path.stat().st_size == image_bytes and file_digest(source_path) == image_sha,
                    f'{key}/{name}: source image changed since audit')
            qa = read_source_document(tile['qaPath'], tile['qaSha256'], art_root)
            external_status = normalize_source_references(tile['externalStatus'], art_root)
            # A ledger can continue changing as other tiles are produced. Its
            # audited hash and selected record are retained without requiring a
            # later unrelated ledger byte sequence to match that frozen audit.
            snapshot = dict(schemaVersion=1, sourceRepository=SOURCE_REPOSITORY,
                            key=key, sceneId=scene, row=tile['row'], column=tile['column'],
                            layoutSha256=source['layoutSha256'], imageSha256=image_sha,
                            imageBytes=image_bytes, sourcePath=source_path_relative,
                            sourceAuditSha256=digest(audit_data),
                            sourceLedgerPath=source_relative(tile['ledgerPath'], art_root),
                            sourceLedgerSha256=tile['ledgerSha256'],
                            internalQaPassed=True, formalAccepted=bool(tile['formalAccepted']),
                            acceptanceStatus=tile['acceptanceStatus'], externalStatus=external_status,
                            normalization='art repository paths are relative; source-host references are historical only',
                            sourceRecord=normalize_source_references(tile['currentRecord'], art_root),
                            qa=qa)
            evidence_relative = f'tile-evidence/{name}.json'
            evidence_sha = write_json(folder / evidence_relative, snapshot, project)
            destination = folder / 'tiles' / (name + '.png')
            if not destination.exists() or file_digest(destination) != image_sha:
                shutil.copyfile(source_path, destination)
            write_meta(destination, project)
            records.append(dict(row=tile['row'], column=tile['column'],
                                resourcePath=f'{RESOURCE_ROOT}/{key}/tiles/{name}',
                                sha256=image_sha, bytes=image_bytes, sourcePath=source_path_relative,
                                sourceQaPath=qa['sourcePath'], sourceQaSha256=qa['sourceSha256'],
                                evidencePath=evidence_relative, evidenceSha256=evidence_sha,
                                internalQaPassed=True, formalAccepted=bool(tile['formalAccepted']),
                                acceptanceStatus=tile['acceptanceStatus'], externalStatus=external_status))
        excluded = [dict(row=t['row'], column=t['column'], reason=t.get('reason'),
                         acceptanceStatus=t.get('acceptanceStatus'),
                         externalStatus=normalize_source_references(t.get('externalStatus'), art_root))
                    for t in audit['excluded'] if t['mapKey'] == key]
        manifest = dict(schemaVersion=1, deliveryStatus='partial_native_tiles', key=key, sceneId=scene,
                        layoutSha256=source['layoutSha256'], columns=grid, rows=grid, tilePixels=TILE_PIXELS,
                        worldRect=base_maps.WORLD_RECT, wholeMapAccepted=False,
                        coverageStatus='internal_qa_passed_tiles_only_external_seams_incomplete',
                        fallback='current_layout_image', sourceRepository=SOURCE_REPOSITORY,
                        sourceAuditSha256=digest(audit_data),
                        productionContract=dict(sourcePath=contract_snapshot['sourcePath'],
                                                sourceSha256=contract['sha256'],
                                                evidencePath='tile-evidence/contract.json',
                                                evidenceSha256=contract_sha),
                        deliveredTileCount=len(records), deliveredImageBytes=sum(t['bytes'] for t in records),
                        excludedTiles=excluded, tiles=records)
        write_json(folder / 'tiles.json', manifest, project)
    require(all(file_digest(p) == value for p, value in before.items()), 'Existing layout/navigation changed')
    return verify(project)


def verify_evidence(folder, relative, expected_sha, project):
    require(portable_path(relative) and relative.startswith('tile-evidence/') and relative.endswith('.json'),
            'Unsafe evidence path')
    path = folder / PurePosixPath(relative)
    data = path.read_bytes()
    require(digest(data) == expected_sha, f'{relative}: evidence SHA mismatch')
    verify_meta(path, project)
    value = json.loads(data)
    require(value['schemaVersion'] == 1 and value['sourceRepository'] == SOURCE_REPOSITORY,
            f'{relative}: invalid evidence identity')
    return value


def verify(project, *, verify_base=True):
    project = project.resolve()
    if verify_base:
        base_maps.verify(project)
    results = []
    all_paths, all_guids = set(), set()
    for key, (scene, grid) in MAPS.items():
        folder = project / DELIVERY / key
        source = read_json(folder / 'source.json')
        manifest_path = folder / 'tiles.json'
        manifest = read_json(manifest_path)
        require(manifest['schemaVersion'] == 1 and manifest['deliveryStatus'] == 'partial_native_tiles'
                and manifest['key'] == key and manifest['sceneId'] == scene, f'{key}: manifest identity/status')
        require(manifest['layoutSha256'] == source['layoutSha256'] == base_maps.MAPS[key][3]
                and manifest['worldRect'] == base_maps.WORLD_RECT, f'{key}: stale layout/geometry')
        require(manifest['rows'] == grid and manifest['columns'] == grid
                and manifest['tilePixels'] == TILE_PIXELS, f'{key}: native tile grid changed')
        require(manifest['wholeMapAccepted'] is False and manifest['fallback'] == 'current_layout_image'
                and manifest['sourceRepository'] == SOURCE_REPOSITORY, f'{key}: partial delivery misrepresented')
        require(bool(SHA_RE.fullmatch(manifest['sourceAuditSha256'])), f'{key}: missing audit provenance')
        for path in (manifest_path, folder / 'tiles', folder / 'tile-evidence'):
            verify_meta(path, project)
        production = manifest['productionContract']
        contract = verify_evidence(folder, production['evidencePath'], production['evidenceSha256'], project)
        require(contract['sourcePath'] == production['sourcePath']
                and contract['sourceSha256'] == production['sourceSha256']
                and portable_path(contract['sourcePath'])
                and bool(SHA_RE.fullmatch(contract['sourceSha256'])), f'{key}: contract provenance mismatch')
        require(contract['normalizedDocument'].get('layoutSha256') == manifest['layoutSha256'],
                f'{key}: archived production contract uses another layout')
        seen, expected_files, expected_evidence = set(), set(), {'contract.json'}
        byte_count = 0
        for tile in manifest['tiles']:
            name = validate_slot(tile, key, grid)
            require(name not in seen, f'{key}/{name}: duplicate tile slot')
            seen.add(name)
            resource_path = f'{RESOURCE_ROOT}/{key}/tiles/{name}'
            require(tile['resourcePath'] == resource_path and resource_path not in all_paths,
                    f'{key}/{name}: unsafe/duplicate Resources path')
            all_paths.add(resource_path)
            require(portable_path(tile['sourcePath']) and portable_path(tile['sourceQaPath'])
                    and bool(SHA_RE.fullmatch(tile['sourceQaSha256'])), f'{key}/{name}: nonportable provenance')
            require(tile['evidencePath'] == f'tile-evidence/{name}.json', f'{key}/{name}: wrong evidence path')
            evidence = verify_evidence(folder, tile['evidencePath'], tile['evidenceSha256'], project)
            for field, expected in (('key', key), ('sceneId', scene), ('row', tile['row']),
                                    ('column', tile['column']), ('imageSha256', tile['sha256']),
                                    ('imageBytes', tile['bytes']), ('layoutSha256', manifest['layoutSha256']),
                                    ('sourcePath', tile['sourcePath']), ('internalQaPassed', True),
                                    ('formalAccepted', tile['formalAccepted']),
                                    ('acceptanceStatus', tile['acceptanceStatus']),
                                    ('externalStatus', tile['externalStatus']),
                                    ('sourceAuditSha256', manifest['sourceAuditSha256'])):
                require(evidence.get(field) == expected, f'{key}/{name}: evidence {field} mismatch')
            require(tile['internalQaPassed'] is True and isinstance(tile['formalAccepted'], bool)
                    and bool(tile['acceptanceStatus']) and tile['externalStatus'] is not None
                    and evidence['qa']['sourcePath'] == tile['sourceQaPath']
                    and evidence['qa']['sourceSha256'] == tile['sourceQaSha256']
                    and isinstance(evidence['qa']['normalizedDocument'], dict)
                    and isinstance(evidence['sourceRecord'], dict)
                    and portable_path(evidence['sourceLedgerPath'])
                    and bool(SHA_RE.fullmatch(evidence['sourceLedgerSha256'])),
                    f'{key}/{name}: incomplete QA evidence')
            path = folder / 'tiles' / (name + '.png')
            inspect_png(path, tile['sha256'], tile['bytes'])
            verify_meta(path, project)
            guid = uuid.uuid5(uuid.NAMESPACE_URL, 'mmorpg-client/current-maps/'
                              + path.relative_to(project).as_posix()).hex
            require(guid not in all_guids, f'{key}/{name}: duplicate texture GUID')
            all_guids.add(guid)
            byte_count += tile['bytes']
            expected_files.add(path.name)
            expected_evidence.add(name + '.json')
        require({p.name for p in (folder / 'tiles').iterdir() if p.suffix != '.meta'} == expected_files,
                f'{key}: unlisted tile files')
        require({p.name for p in (folder / 'tile-evidence').iterdir() if p.suffix != '.meta'} == expected_evidence,
                f'{key}: unlisted evidence files')
        require(manifest['deliveredTileCount'] == len(seen) and manifest['deliveredImageBytes'] == byte_count,
                f'{key}: delivery totals mismatch')
        for excluded in manifest['excludedTiles']:
            require(validate_slot(excluded, key, grid) not in seen and bool(excluded['reason']),
                    f'{key}: excluded tile also delivered or reason missing')
        results.append(dict(key=key, sceneId=scene, deliveredTiles=len(seen), totalSlots=grid * grid,
                            imageBytes=byte_count, excludedTiles=len(manifest['excludedTiles']),
                            wholeMapAccepted=False))
    return results


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--project', type=Path, default=PROJECT)
    parser.add_argument('--art-root', type=Path, default=PROJECT.parent / 'image')
    parser.add_argument('--audit', type=Path, help='Frozen machine-readable selected/excluded audit')
    parser.add_argument('--verify', action='store_true', help='Verify delivered files without artwork/audit paths')
    args = parser.parse_args()
    if not args.verify and not args.audit:
        parser.error('--audit is required for import')
    result = verify(args.project) if args.verify else import_tiles(args.project, args.art_root, args.audit)
    print(json.dumps(dict(ok=True, maps=result, imageBytes=sum(m['imageBytes'] for m in result)),
                     ensure_ascii=False, indent=2))


if __name__ == '__main__':
    main()
