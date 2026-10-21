"""Partial native tile delivery: portability, provenance and corrupt asset checks."""
import io
import json
from pathlib import Path, PureWindowsPath
import struct
import sys
import tempfile
import unittest
from unittest.mock import MagicMock, patch
import zlib

from PIL import Image

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import import_completed_map_tiles as tiles


class CompletedMapTileTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        buffer = io.BytesIO()
        Image.new('RGB', (4096, 4096), (73, 112, 99)).save(buffer, format='PNG')
        cls.png = buffer.getvalue()

    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.project = Path(self.temporary.name)
        self.make_delivery()

    def make_delivery(self):
        for key, (scene, grid) in tiles.MAPS.items():
            folder = self.project / tiles.DELIVERY / key
            for directory in (folder, folder / 'tiles', folder / 'tile-evidence'):
                directory.mkdir(parents=True, exist_ok=True)
                tiles.write_meta(directory, self.project)
            layout_sha = tiles.base_maps.MAPS[key][3]
            tiles.write_json(folder / 'source.json', dict(layoutSha256=layout_sha), self.project)
            image = folder / 'tiles/r01_c01.png'
            image.write_bytes(self.png)
            tiles.write_meta(image, self.project)
            image_sha, source_hash = tiles.digest(self.png), 'a' * 64
            contract = dict(schemaVersion=1, sourceRepository=tiles.SOURCE_REPOSITORY,
                            sourcePath='production/contract.json', sourceSha256=source_hash,
                            normalizedDocument=dict(layoutSha256=layout_sha))
            contract_sha = tiles.write_json(folder / 'tile-evidence/contract.json', contract, self.project)
            evidence = dict(schemaVersion=1, sourceRepository=tiles.SOURCE_REPOSITORY,
                            key=key, sceneId=scene, row=1, column=1, layoutSha256=layout_sha,
                            imageSha256=image_sha, imageBytes=len(self.png),
                            sourcePath='production/r01_c01.png', sourceAuditSha256=source_hash,
                            sourceLedgerPath='production/progress.json', sourceLedgerSha256=source_hash,
                            internalQaPassed=True, formalAccepted=False,
                            acceptanceStatus='internal_passed_external_pending', externalStatus='neighbors_pending',
                            sourceRecord=dict(internalQAPassed=True),
                            qa=dict(sourcePath='production/qa.json', sourceSha256=source_hash,
                                    normalizedDocument=dict(internalPassed=True)))
            evidence_sha = tiles.write_json(folder / 'tile-evidence/r01_c01.json', evidence, self.project)
            record = dict(row=1, column=1, resourcePath=f'{tiles.RESOURCE_ROOT}/{key}/tiles/r01_c01',
                          sha256=image_sha, bytes=len(self.png), sourcePath=evidence['sourcePath'],
                          sourceQaPath=evidence['qa']['sourcePath'], sourceQaSha256=source_hash,
                          evidencePath='tile-evidence/r01_c01.json', evidenceSha256=evidence_sha,
                          internalQaPassed=True, formalAccepted=False,
                          acceptanceStatus=evidence['acceptanceStatus'], externalStatus=evidence['externalStatus'])
            manifest = dict(schemaVersion=1, deliveryStatus='partial_native_tiles', key=key, sceneId=scene,
                            layoutSha256=layout_sha, columns=grid, rows=grid, tilePixels=4096,
                            worldRect=tiles.base_maps.WORLD_RECT, wholeMapAccepted=False,
                            fallback='current_layout_image', sourceRepository=tiles.SOURCE_REPOSITORY,
                            sourceAuditSha256=source_hash,
                            productionContract=dict(sourcePath=contract['sourcePath'], sourceSha256=source_hash,
                                                    evidencePath='tile-evidence/contract.json',
                                                    evidenceSha256=contract_sha),
                            deliveredTileCount=1, deliveredImageBytes=len(self.png),
                            excludedTiles=[], tiles=[record])
            tiles.write_json(folder / 'tiles.json', manifest, self.project)

    def mutate_manifest(self, change):
        path = self.project / tiles.DELIVERY / 'main/tiles.json'
        value = tiles.read_json(path)
        change(value)
        tiles.write_json(path, value, self.project)

    def assert_structural_failure(self, change, pattern):
        self.mutate_manifest(change)
        # These tests target manifest constraints, not the PNG decoder tested
        # separately below. Avoid repeatedly decompressing three full 4K PNGs.
        with patch.object(tiles, 'inspect_png'):
            with self.assertRaisesRegex(ValueError, pattern):
                tiles.verify(self.project, verify_base=False)

    def test_sparse_delivery_verifies_without_art_checkout(self):
        result = tiles.verify(self.project, verify_base=False)
        self.assertEqual([m['deliveredTiles'] for m in result], [1, 1, 1])
        self.assertEqual([m['totalSlots'] for m in result], [256, 196, 196])
        self.assertFalse(any(m['wholeMapAccepted'] for m in result))

    def test_duplicate_slot_rejected(self):
        self.assert_structural_failure(lambda m: m['tiles'].append(m['tiles'][0].copy()), 'duplicate tile slot')

    def test_wrong_current_layout_rejected(self):
        self.assert_structural_failure(lambda m: m.update(layoutSha256='b' * 64), 'stale layout')

    def test_out_of_grid_coordinates_rejected(self):
        self.assert_structural_failure(lambda m: m['tiles'][0].update(row=17), 'outside current grid')

    def test_path_traversal_rejected(self):
        self.assert_structural_failure(lambda m: m['tiles'][0].update(resourcePath='../tiles/r01_c01'),
                                       'unsafe/duplicate Resources path')

    def test_absolute_source_provenance_rejected(self):
        self.assert_structural_failure(lambda m: m['tiles'][0].update(sourcePath='D:/work/image/tile.png'),
                                       'nonportable provenance')

    def test_partial_map_cannot_claim_complete_acceptance(self):
        self.assert_structural_failure(lambda m: m.update(wholeMapAccepted=True), 'partial delivery misrepresented')

    def test_external_acceptance_false_remains_pending_and_is_preserved(self):
        folder = self.project / tiles.DELIVERY / 'main'
        evidence_path = folder / 'tile-evidence/r01_c01.json'
        evidence = tiles.read_json(evidence_path)
        evidence['externalStatus'] = False
        evidence_sha = tiles.write_json(evidence_path, evidence, self.project)
        self.mutate_manifest(lambda m: m['tiles'][0].update(externalStatus=False, evidenceSha256=evidence_sha))
        with patch.object(tiles, 'inspect_png'):
            tiles.verify(self.project, verify_base=False)

    def test_tampered_qa_evidence_rejected(self):
        path = self.project / tiles.DELIVERY / 'main/tile-evidence/r01_c01.json'
        path.write_bytes(path.read_bytes().replace(b'neighbors_pending', b'all_edges_accepted'))
        with self.assertRaisesRegex(ValueError, 'evidence SHA mismatch'):
            tiles.verify(self.project, verify_base=False)

    def test_texture_downsampling_settings_rejected(self):
        meta = self.project / tiles.DELIVERY / 'main/tiles/r01_c01.png.meta'
        meta.write_text(meta.read_text().replace('maxTextureSize: 4096', 'maxTextureSize: 2048'))
        with self.assertRaisesRegex(ValueError, 'metadata/import settings'):
            tiles.verify(self.project, verify_base=False)

    def test_png_crc_checked_even_when_manifest_sha_matches(self):
        path = self.project / 'corrupt.png'
        data = bytearray(self.png)
        data[-1] ^= 1  # IEND CRC only; still recognizable as a PNG.
        path.write_bytes(data)
        with self.assertRaisesRegex(ValueError, 'PNG chunk CRC mismatch'):
            tiles.inspect_png(path, tiles.digest(data), len(data))

    def test_valid_crc_but_truncated_pixel_stream_rejected(self):
        chunks, at = [], 8
        while at < len(self.png):
            length = struct.unpack('>I', self.png[at:at + 4])[0]
            kind = self.png[at + 4:at + 8]
            payload = self.png[at + 8:at + 8 + length]
            if kind == b'IDAT':
                payload = payload[:max(1, len(payload) // 2)]
            chunks.append(struct.pack('>I', len(payload)) + kind + payload
                          + struct.pack('>I', zlib.crc32(kind + payload)))
            at += 12 + length
        data = self.png[:8] + b''.join(chunks)
        path = self.project / 'truncated.png'
        path.write_bytes(data)
        with self.assertRaises(OSError):
            tiles.inspect_png(path, tiles.digest(data), len(data))

    def test_byte_identity_detects_changed_art(self):
        path = self.project / tiles.DELIVERY / 'main/tiles/r01_c01.png'
        with self.assertRaisesRegex(ValueError, 'image SHA mismatch'):
            tiles.inspect_png(path, '0' * 64, len(self.png))

    def test_qa_normalization_keeps_source_hash_separate(self):
        art = self.project / 'art'
        art.mkdir()
        qa_path = art / 'qa.json'
        qa_path.write_bytes(json.dumps(dict(imagePath=str(art / 'tile.png'), internalPassed=True,
                                           sourceHostFile='C:/Users/artist/generated/tile.png')).encode())
        source_sha = tiles.file_digest(qa_path)
        evidence = tiles.read_source_document(qa_path, source_sha, art)
        self.assertEqual(evidence['sourceSha256'], source_sha)
        self.assertEqual(evidence['sourcePath'], 'qa.json')
        self.assertEqual(evidence['normalizedDocument']['imagePath'], 'tile.png')
        self.assertEqual(evidence['normalizedDocument']['sourceHostFile'],
                         'source-host/C/Users/artist/generated/tile.png')
        self.assertNotEqual(tiles.digest(tiles.json_bytes(evidence)), source_sha)

    def test_qa_normalization_handles_windows_short_and_resolved_root_spellings(self):
        short = PureWindowsPath('C:/Users/RUNNER~1/AppData/Local/Temp/map-test/art')
        resolved = PureWindowsPath('C:/Users/runneradmin/AppData/Local/Temp/map-test/art')
        art_root = MagicMock(spec=Path)
        art_root.absolute.return_value = short
        art_root.resolve.return_value = resolved
        source = {
            'shortBackslashes': str(short / 'tile.png'),
            'shortForwardSlashes': (short / 'sub/tile.png').as_posix(),
            'resolvedBackslashes': str(resolved / 'tile.png'),
            'resolvedForwardSlashes': (resolved / 'tile.png').as_posix(),
            'shortDifferentCase': str(short / 'tile.png').lower(),
            'outsideRoot': str(PureWindowsPath(str(short) + '-other') / 'tile.png'),
        }
        self.assertEqual(tiles.normalize_source_references(source, art_root), {
            'shortBackslashes': 'tile.png',
            'shortForwardSlashes': 'sub/tile.png',
            'resolvedBackslashes': 'tile.png',
            'resolvedForwardSlashes': 'tile.png',
            'shortDifferentCase': 'tile.png',
            'outsideRoot': 'source-host/C/Users/RUNNER~1/AppData/Local/Temp/map-test/art-other/tile.png',
        })


if __name__ == '__main__':
    unittest.main()
