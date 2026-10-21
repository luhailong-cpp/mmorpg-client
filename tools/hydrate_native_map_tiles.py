#!/usr/bin/env python3
"""Reassemble exact PNG blobs on one temporary GitHub Actions branch.

No source images are generated or resized. The final Resources tree contains
ordinary PNG Git blobs, never transport chunk pointers. Network errors are
reported without URLs, response bodies, credentials, or redirect locations.
"""
from __future__ import annotations

import argparse
import base64
from concurrent.futures import ThreadPoolExecutor, as_completed
import hashlib
from http.client import IncompleteRead, RemoteDisconnected
import importlib.util
import json
import os
from pathlib import Path, PurePosixPath
import re
import subprocess
import sys
import tempfile
import time
from urllib.error import HTTPError, URLError
from urllib.parse import quote, urlsplit
from urllib.request import HTTPRedirectHandler, Request, build_opener

TARGET_REPOSITORY = 'luhailong-cpp/mmorpg-client'
TARGET_BRANCH = 'codex/native-map-tiles-20261021-transfer'
SOURCE_REPOSITORY = 'luhailong-cpp/image'
SOURCE_COMMIT = '15aadfdc91234d3b0820f40212d72252c410d18f'
RESOURCE_ROOT = 'Assets/Resources/World/CurrentMaps20261009'
MAX_BATCH_BYTES = 400 * 1024 * 1024
SHA1 = re.compile(r'^[0-9a-f]{40}$')
SHA256 = re.compile(r'^[0-9a-f]{64}$')
DOWNLOAD_WORKERS = 4


class TransportError(RuntimeError):
    pass


def require(condition, message):
    if not condition:
        raise TransportError(message)


def alias(value, primary, secondary):
    if primary in value and secondary in value:
        require(value[primary] == value[secondary], f'Conflicting {primary} and {secondary}')
    return value.get(primary, value.get(secondary))


def portable_path(value):
    return (isinstance(value, str) and bool(value) and '\\' not in value and ':' not in value
            and not value.startswith('/') and all(part not in ('', '.', '..') for part in value.split('/')))


def read_json(path):
    try:
        return json.loads(path.read_bytes())
    except (OSError, ValueError) as error:
        raise TransportError(f'Cannot read JSON {path.name}: {type(error).__name__}') from None


def read_plan(project, plan_path):
    plan = read_json(plan_path)
    require(plan.get('schemaVersion') == 1, 'Unsupported transport plan version')
    require(plan.get('sourceRepository') in (SOURCE_REPOSITORY, 'https://github.com/' + SOURCE_REPOSITORY),
            'Transport source repository does not match the pinned artwork repository')
    require(alias(plan, 'pinnedSourceCommit', 'sourceCommit') == SOURCE_COMMIT, 'Artwork commit is not the pinned source')
    require(plan.get('targetRepository', TARGET_REPOSITORY) == TARGET_REPOSITORY, 'Wrong target repository')
    require(plan.get('targetBranch', TARGET_BRANCH) == TARGET_BRANCH, 'Wrong temporary transport branch')
    audit_sha = plan.get('frozenAuditSha256')
    require(isinstance(audit_sha, str) and SHA256.fullmatch(audit_sha), 'Missing frozen artwork audit SHA')
    require(isinstance(plan.get('tiles'), list) and plan['tiles'], 'Transport plan has no tiles')

    expected = {}
    for key, grid in (('main', 16), ('village', 14), ('island', 14)):
        manifest = read_json(project / RESOURCE_ROOT / key / 'tiles.json')
        require(manifest.get('key') == key and manifest.get('deliveryStatus') == 'partial_native_tiles',
                f'{key}: wrong delivered map identity/status')
        require(manifest.get('sourceAuditSha256') == audit_sha, f'{key}: frozen audit does not match final manifest')
        require(manifest.get('columns') == grid and manifest.get('rows') == grid, f'{key}: wrong grid')
        require(isinstance(manifest.get('tiles'), list), f'{key}: missing final tile list')
        for tile in manifest['tiles']:
            row, column = tile.get('row'), tile.get('column')
            require(type(row) is int and type(column) is int and 1 <= row <= grid and 1 <= column <= grid,
                    f'{key}: invalid delivered coordinates')
            slot = (key, row, column)
            require(slot not in expected, f'{key}: duplicate delivered coordinates')
            resource = f'World/CurrentMaps20261009/{key}/tiles/r{row:02d}_c{column:02d}'
            require(tile.get('resourcePath') == resource, f'{key}: unsafe final resource path')
            expected[slot] = tile

    prepared = []
    seen = set()
    for supplied in plan['tiles']:
        require(isinstance(supplied, dict), 'Invalid tile transport entry')
        require(type(supplied.get('row')) is int and type(supplied.get('column')) is int,
                'Transport coordinates must be integers')
        slot = (supplied.get('mapKey'), supplied.get('row'), supplied.get('column'))
        require(slot in expected and slot not in seen, 'Transport tile missing from final manifest or duplicated')
        seen.add(slot)
        final = expected[slot]
        image_sha = alias(supplied, 'imageSha256', 'sha256')
        git_sha = alias(supplied, 'localGitBlobSha1', 'gitBlobSha1')
        require(isinstance(image_sha, str) and SHA256.fullmatch(image_sha), f'{slot}: invalid PNG SHA256')
        require(isinstance(git_sha, str) and SHA1.fullmatch(git_sha), f'{slot}: invalid PNG Git blob SHA1')
        require(image_sha == final.get('sha256') and supplied.get('bytes') == final.get('bytes'),
                f'{slot}: PNG checksum/bytes differ from final manifest')
        byte_count = supplied.get('bytes')
        require(type(byte_count) is int and 0 < byte_count < 100 * 1024 * 1024, f'{slot}: invalid PNG byte count')
        require(supplied.get('sourcePath') == final.get('sourcePath') and portable_path(supplied.get('sourcePath')),
                f'{slot}: artwork provenance path differs from final manifest')
        require(supplied.get('resourcePath', final['resourcePath']) == final['resourcePath'], f'{slot}: destination mismatch')
        chunks = supplied.get('chunks', [])
        if supplied.get('source') == 'public_media':
            require(chunks == [], f'{slot}: public-media entry must not contain chunk pointers')
        else:
            require(supplied.get('source') == 'git_blob_chunks' and isinstance(chunks, list) and chunks,
                    f'{slot}: missing chunk source or chunk list')
            total = 0
            for chunk in chunks:
                require(isinstance(chunk, dict) and isinstance(chunk.get('sha'), str) and SHA1.fullmatch(chunk['sha']),
                        f'{slot}: invalid chunk Git blob SHA1')
                require(type(chunk.get('bytes')) is int and 0 < chunk['bytes'] < 100 * 1024 * 1024,
                        f'{slot}: invalid chunk byte count')
                require(type(chunk.get('offset', total)) is int and chunk.get('offset', total) == total,
                        f'{slot}: chunk offsets are not contiguous')
                total += chunk['bytes']
            require(total == byte_count, f'{slot}: chunks do not sum to exact PNG length')
        destination = project / ('Assets/Resources/' + final['resourcePath'] + '.png')
        require((project / RESOURCE_ROOT).resolve().is_relative_to(project), 'Map resource root escaped checkout')
        require(destination.resolve().is_relative_to((project / RESOURCE_ROOT).resolve()), 'Destination escaped map resources')
        prepared.append(dict(supplied, imageSha256=image_sha, localGitBlobSha1=git_sha,
                             destination=destination, relative=destination.relative_to(project).as_posix(),
                             label=f'{slot[0]}/r{slot[1]:02d}_c{slot[2]:02d}'))
    require(seen == set(expected), 'Transport plan does not cover every final delivered tile exactly once')
    prepared.sort(key=lambda value: value['relative'])
    return plan, prepared


class SafeRedirect(HTTPRedirectHandler):
    def redirect_request(self, request, fp, code, msg, headers, new_url):
        require(urlsplit(new_url).scheme == 'https', 'Refused non-HTTPS download redirect')
        if request.has_header('Authorization'):
            require(urlsplit(new_url).netloc == 'api.github.com', 'Refused authenticated redirect outside GitHub API')
        return super().redirect_request(request, fp, code, msg, headers, new_url)


def retry_fetch(request, consume, label):
    for attempt in range(5):
        try:
            with build_opener(SafeRedirect()).open(request, timeout=90) as response:
                require(response.status == 200, f'{label}: unexpected download status')
                return consume(response)
        except HTTPError as error:
            retryable = error.code in (403, 408, 429, 500, 502, 503, 504)
            reason = f'HTTP {error.code}'
        except (URLError, TimeoutError, ConnectionError, OSError, IncompleteRead, RemoteDisconnected) as error:
            retryable, reason = True, type(error).__name__
        if not retryable or attempt == 4:
            raise TransportError(f'{label}: download failed ({reason})') from None
        time.sleep(min(30, 2 ** (attempt + 1)))
    raise TransportError(f'{label}: exhausted download attempts')


def read_chunk(chunk, token, label):
    request = Request(f'https://api.github.com/repos/{TARGET_REPOSITORY}/git/blobs/{chunk["sha"]}', headers={
        'Authorization': 'Bearer ' + token, 'Accept': 'application/vnd.github+json',
        'X-GitHub-Api-Version': '2022-11-28', 'User-Agent': 'native-map-tile-transport'})

    def consume(response):
        # Base64 JSON overhead is bounded; never accept an unbounded API payload.
        encoded = 4 * ((chunk['bytes'] + 2) // 3)
        # GitHub wraps base64 at 60 columns. Each newline becomes two bytes
        # (backslash + n) inside JSON; count that escaping before bounding reads.
        limit = encoded + 2 * ((encoded + 59) // 60) + 65536
        body = response.read(limit + 1)
        require(len(body) <= limit, f'{label}: oversized chunk response')
        try:
            record = json.loads(body)
            require(record.get('encoding') == 'base64' and record.get('sha') == chunk['sha']
                    and record.get('size') == chunk['bytes'], f'{label}: chunk response metadata differs')
            raw = base64.b64decode(''.join(record['content'].split()), validate=True)
        except (ValueError, KeyError, TypeError):
            raise TransportError(f'{label}: invalid base64 chunk response') from None
        require(len(raw) == chunk['bytes'], f'{label}: decoded chunk length differs')
        digest = hashlib.sha1(f'blob {len(raw)}\0'.encode('ascii') + raw).hexdigest()
        require(digest == chunk['sha'], f'{label}: chunk Git blob checksum differs')
        return raw

    return retry_fetch(request, consume, label)


def git_blob_hash(path):
    digest = hashlib.sha1(f'blob {path.stat().st_size}\0'.encode('ascii'))
    with path.open('rb') as handle:
        for block in iter(lambda: handle.read(1024 * 1024), b''):
            digest.update(block)
    return digest.hexdigest()


def verifier_module(project):
    sys.path.insert(0, str(project / 'tools'))
    spec = importlib.util.spec_from_file_location('native_tile_delivery_verifier', project / 'tools/import_completed_map_tiles.py')
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def hydrate_tile(tile, token, verifier):
    destination = tile['destination']
    destination.parent.mkdir(parents=True, exist_ok=True)
    if destination.exists():
        verifier.inspect_png(destination, tile['imageSha256'], tile['bytes'])
        require(git_blob_hash(destination) == tile['localGitBlobSha1'], f'{tile["label"]}: existing PNG Git blob differs')
        return 'verified existing'
    temporary = destination.with_name(destination.name + '.transport-part')
    try:
        if tile['source'] == 'public_media':
            url = f'https://media.githubusercontent.com/media/{SOURCE_REPOSITORY}/{SOURCE_COMMIT}/' + quote(tile['sourcePath'], safe='/')

            def consume(response):
                count = 0
                with temporary.open('wb') as handle:
                    while True:
                        block = response.read(1024 * 1024)
                        if not block:
                            break
                        count += len(block)
                        require(count <= tile['bytes'], f'{tile["label"]}: media response exceeds expected PNG length')
                        handle.write(block)
                require(count == tile['bytes'], f'{tile["label"]}: truncated media PNG')

            retry_fetch(Request(url, headers={'User-Agent': 'native-map-tile-transport'}), consume, tile['label'])
        else:
            with temporary.open('wb') as handle:
                for index, chunk in enumerate(tile['chunks']):
                    handle.write(read_chunk(chunk, token, f'{tile["label"]} chunk {index + 1}'))
        # This checks exact bytes + SHA256, every PNG chunk CRC, and full 4096 RGB decoding.
        verifier.inspect_png(temporary, tile['imageSha256'], tile['bytes'])
        require(git_blob_hash(temporary) == tile['localGitBlobSha1'], f'{tile["label"]}: assembled PNG Git blob differs')
        temporary.replace(destination)
        return 'downloaded and verified'
    finally:
        if temporary.exists():
            temporary.unlink()


def command(project, arguments, *, capture=False):
    result = subprocess.run(arguments, cwd=project, capture_output=True, text=True, encoding='utf-8',
                            env=dict(os.environ, GIT_TERMINAL_PROMPT='0'))
    # Verification commands cannot print tokens; Git transport output is intentionally suppressed.
    if arguments[0] == sys.executable:
        output = result.stdout + result.stderr
        token = os.environ.get('GITHUB_TOKEN')
        if token:
            output = output.replace(token, '[redacted]')
        print(output, end='', flush=True)
    require(result.returncode == 0, f'{arguments[0]} {arguments[1]} failed with exit code {result.returncode}')
    if capture:
        return result.stdout.strip()


def enforce_checkout(project):
    require(os.environ.get('GITHUB_REPOSITORY') == TARGET_REPOSITORY, 'Run only in the intended repository Actions context')
    require(os.environ.get('GITHUB_REF') == 'refs/heads/' + TARGET_BRANCH, 'Run only on the temporary transport branch')
    top = Path(command(project, ['git', 'rev-parse', '--show-toplevel'], capture=True)).resolve()
    require(top == project, 'Project is not the checkout root')
    branch = command(project, ['git', 'symbolic-ref', '--short', 'HEAD'], capture=True)
    require(branch == TARGET_BRANCH, 'Refusing to write any branch other than the temporary transport branch')
    origin = command(project, ['git', 'remote', 'get-url', 'origin'], capture=True)
    require(origin in (f'https://github.com/{TARGET_REPOSITORY}', f'https://github.com/{TARGET_REPOSITORY}.git'),
            'Origin is not the intended GitHub repository')
    require(not command(project, ['git', 'diff', '--name-only'], capture=True), 'Checkout has modified tracked files before hydration')
    require(not command(project, ['git', 'diff', '--cached', '--name-only'], capture=True), 'Checkout has pre-existing staged changes')


def batches(tiles):
    batch, size = [], 0
    for tile in tiles:
        if batch and size + tile['bytes'] > MAX_BATCH_BYTES:
            yield batch
            batch, size = [], 0
        require(tile['bytes'] <= MAX_BATCH_BYTES, 'One image exceeds the maximum commit batch')
        batch.append(tile)
        size += tile['bytes']
    if batch:
        yield batch


def publish_batches(project, tiles):
    sequence = list(batches(tiles))
    for number, batch in enumerate(sequence, 1):
        # Recheck the branch immediately before every mutation, even after a long download.
        require(command(project, ['git', 'symbolic-ref', '--short', 'HEAD'], capture=True) == TARGET_BRANCH,
                'Temporary branch changed during transport')
        with tempfile.NamedTemporaryFile(prefix='native-tile-paths-', delete=False) as paths:
            paths.write(b''.join(tile['relative'].encode('utf-8') + b'\0' for tile in batch))
            path_file = paths.name
        try:
            command(project, ['git', 'add', '--pathspec-from-file=' + path_file, '--pathspec-file-nul'])
        finally:
            Path(path_file).unlink(missing_ok=True)
        staged = set(command(project, ['git', 'diff', '--cached', '--name-only'], capture=True).splitlines())
        require(staged <= {tile['relative'] for tile in batch}, 'Unexpected staged files; refusing commit')
        if not staged:
            print(f'Batch {number}/{len(sequence)} already present', flush=True)
            continue
        # Confirm filters did not replace a PNG with a Git LFS pointer or transform its bytes.
        for tile in batch:
            actual = command(project, ['git', 'rev-parse', ':' + tile['relative']], capture=True)
            require(actual == tile['localGitBlobSha1'], f'{tile["label"]}: staged blob is not the exact PNG')
        message = f'补齐原生地图分片 {number}/{len(sequence)} [skip ci]'
        command(project, ['git', '-c', 'user.name=github-actions[bot]', '-c',
                          'user.email=41898282+github-actions[bot]@users.noreply.github.com', 'commit', '-m', message])
        for attempt in range(3):
            try:
                command(project, ['git', '-c', 'pack.threads=2', '-c', 'pack.windowMemory=64m',
                                  'push', 'origin', 'HEAD:refs/heads/' + TARGET_BRANCH])
                break
            except TransportError:
                if attempt == 2:
                    raise
                time.sleep(10 * (attempt + 1))
        head = command(project, ['git', 'rev-parse', 'HEAD'], capture=True)
        print(f'Pushed batch {number}/{len(sequence)}: {len(staged)} PNGs, <= {sum(tile["bytes"] for tile in batch)} bytes, {head}', flush=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--project', type=Path, default=Path.cwd())
    parser.add_argument('--plan', type=Path, default=Path('tools/native-map-tiles-transport-plan.json'))
    parser.add_argument('--validate-only', action='store_true', help='Read plan/manifests only; no Git mutations or network')
    args = parser.parse_args()
    project = args.project.resolve()
    plan_path = args.plan if args.plan.is_absolute() else project / args.plan
    _, tiles = read_plan(project, plan_path)
    print(f'Validated plan: {len(tiles)} PNGs, {sum(tile["bytes"] for tile in tiles)} bytes, {len(list(batches(tiles)))} push batches', flush=True)
    if args.validate_only:
        return 0
    enforce_checkout(project)
    token = os.environ.get('GITHUB_TOKEN', '')
    require(bool(token), 'GITHUB_TOKEN is required for same-repository chunk reads')
    verifier = verifier_module(project)
    with ThreadPoolExecutor(max_workers=DOWNLOAD_WORKERS) as pool:
        futures = {pool.submit(hydrate_tile, tile, token, verifier): tile for tile in tiles}
        try:
            for done, future in enumerate(as_completed(futures), 1):
                tile = futures[future]
                status = future.result()
                print(f'{done}/{len(tiles)} {tile["label"]}: {status}', flush=True)
        except Exception:
            for future in futures:
                future.cancel()
            raise
    command(project, [sys.executable, 'tools/import_completed_map_tiles.py', '--verify'])
    command(project, [sys.executable, '-m', 'unittest', 'discover', '-s', 'tools/tests', '-p', 'test_import_current_maps.py', '-v'])
    command(project, [sys.executable, '-m', 'unittest', 'discover', '-s', 'tools/tests', '-p', 'test_import_completed_map_tiles.py', '-v'])
    publish_batches(project, tiles)
    print('Exact PNG delivery committed only to the temporary transport branch; main is untouched.', flush=True)
    return 0


if __name__ == '__main__':
    try:
        raise SystemExit(main())
    except Exception as error:
        # TransportError messages are deliberately sanitized; unknown exceptions report type only.
        message = str(error) if isinstance(error, TransportError) else type(error).__name__
        print('Transport failed: ' + message, file=sys.stderr)
        raise SystemExit(1)
