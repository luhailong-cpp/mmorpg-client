"""Partial native tile delivery: portability, provenance and corrupt asset checks."""
import io
import json
from pathlib import Path, PureWindowsPath
import shutil
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

    def make_audit(self, internal_passed, *, policy=tiles.GENERATED_SELECTION, qa_missing=False):
        art = self.project / 'art'
        art.mkdir(exist_ok=True)
        audit = dict(schemaVersion=1, frozen=True, selected=[], excluded=[], contracts={})
        if policy is not None:
            audit['selectionPolicy'] = policy
        for key, (scene, grid) in tiles.MAPS.items():
            folder = art / key
            folder.mkdir(exist_ok=True)
            layout_sha = tiles.base_maps.MAPS[key][3]
            contract = folder / 'contract.json'
            contract.write_bytes(tiles.json_bytes(dict(layoutSha256=layout_sha)))
            audit['contracts'][key] = dict(path=str(contract), sha256=tiles.file_digest(contract),
                                           layoutSha256=layout_sha, rows=grid, columns=grid)
            image = folder / 'r01_c01.png'
            image.write_bytes(self.png)
            qa = folder / 'qa.json'
            qa.write_bytes(tiles.json_bytes(dict(internalPassed=internal_passed,
                                                failures=['seam_not_repaired'] if internal_passed is False else [])))
            ledger = folder / 'progress.json'
            current = dict(internalQAPassed=internal_passed, accepted=False)
            ledger.write_bytes(tiles.json_bytes(current))
            audit['selected'].append(dict(
                mapKey=key, sceneId=scene, row=1, column=1, layoutSha256=layout_sha,
                dimensions=[4096, 4096], eligible=True, sourcePath=str(image),
                imageSha256=tiles.digest(self.png), bytes=len(self.png),
                qaPath=None if qa_missing else str(qa),
                qaSha256=None if qa_missing else tiles.file_digest(qa),
                ledgerPath=str(ledger), ledgerSha256=tiles.file_digest(ledger),
                internalQaPassed=internal_passed, formalAccepted=False,
                acceptanceStatus='generated_qa_missing' if qa_missing else 'generated_seams_unrepaired',
                externalStatus=None if qa_missing else {'passed': False, 'reason': 'seam_not_repaired'},
                currentRecord=current))
        audit_path = self.project / 'audit.json'
        audit_path.write_bytes(tiles.json_bytes(audit))
        return art, audit_path

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

    def test_selected_map_verifies_sparse_payload_and_retains_base_checks(self):
        for key in ('village', 'island'):
            (self.project / tiles.DELIVERY / key / 'tiles/r01_c01.png').unlink()
        with patch.object(tiles.base_maps, 'verify') as verify_base:
            with patch.object(tiles, 'inspect_png', wraps=tiles.inspect_png) as inspect:
                result = tiles.verify(self.project, map_key='main')
            verify_base.assert_called_once_with(self.project.resolve())
        self.assertEqual([item['key'] for item in result], ['main'])
        self.assertEqual(inspect.call_count, 1)
        self.assertEqual(inspect.call_args.args[0],
                         self.project.resolve() / tiles.DELIVERY / 'main/tiles/r01_c01.png')
        # Omitting the selector still requires every delivered map payload.
        with self.assertRaisesRegex(ValueError, 'image byte count mismatch'):
            tiles.verify(self.project, verify_base=False)

    def test_selected_map_does_not_skip_image_hash_or_manifest_validation(self):
        path = self.project / tiles.DELIVERY / 'main/tiles/r01_c01.png'
        changed = bytearray(self.png)
        changed[-1] ^= 1
        path.write_bytes(changed)
        with self.assertRaisesRegex(ValueError, 'image SHA mismatch'):
            tiles.verify(self.project, verify_base=False, map_key='main')
        path.write_bytes(self.png)
        self.mutate_manifest(lambda manifest: manifest.update(layoutSha256='b' * 64))
        with self.assertRaisesRegex(ValueError, 'stale layout'):
            tiles.verify(self.project, verify_base=False, map_key='main')

    def test_map_selector_rejects_unknown_map_and_import_mode(self):
        with self.assertRaisesRegex(ValueError, 'Unknown map selected'):
            tiles.verify(self.project, verify_base=False, map_key='missing')
        with patch.object(sys, 'argv', ['import_completed_map_tiles.py', '--map', 'main',
                                       '--audit', 'unused-audit.json']):
            with patch.object(tiles, 'import_tiles') as import_tiles:
                with patch('sys.stderr', new=io.StringIO()) as errors:
                    with self.assertRaises(SystemExit) as raised:
                        tiles.main()
                self.assertEqual(raised.exception.code, 2)
                self.assertIn('--map is only supported with --verify', errors.getvalue())
                import_tiles.assert_not_called()

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

    def test_generated_delivery_preserves_failed_and_missing_qa_without_changing_pixels(self):
        for internal_passed, missing in ((False, False), (None, True)):
            with self.subTest(internalQaPassed=internal_passed, qaMissing=missing):
                art, audit_path = self.make_audit(internal_passed, qa_missing=missing)
                with patch.object(tiles.base_maps, 'verify'):
                    result = tiles.import_tiles(self.project, art, audit_path)
                self.assertEqual([item['selectionPolicy'] for item in result],
                                 [tiles.GENERATED_SELECTION] * 3)
                for key in tiles.MAPS:
                    folder = self.project / tiles.DELIVERY / key
                    manifest = tiles.read_json(folder / 'tiles.json')
                    record = manifest['tiles'][0]
                    evidence = tiles.read_json(folder / record['evidencePath'])
                    self.assertIs(record['internalQaPassed'], internal_passed)
                    self.assertIs(evidence['internalQaPassed'], internal_passed)
                    self.assertFalse(record['formalAccepted'])
                    self.assertFalse(manifest['wholeMapAccepted'])
                    self.assertEqual(manifest['coverageStatus'],
                                     'all_generated_current_layout_seams_not_required')
                    self.assertEqual((folder / 'tiles/r01_c01.png').read_bytes(), self.png)
                    if missing:
                        self.assertIsNone(record['sourceQaPath'])
                        self.assertIsNone(record['sourceQaSha256'])
                        self.assertIsNone(evidence['qa'])
                    else:
                        self.assertEqual(evidence['qa']['normalizedDocument']['failures'],
                                         ['seam_not_repaired'])
                # Offline validation already ran as part of import_tiles, which
                # never asks the art tree for anything inside verify().

    def test_original_strict_import_still_rejects_failed_qa(self):
        art, audit_path = self.make_audit(False, policy=None)
        with patch.object(tiles.base_maps, 'verify'):
            with self.assertRaisesRegex(ValueError, 'strict selection requires passed internal QA'):
                tiles.import_tiles(self.project, art, audit_path)

    def test_generated_audit_cannot_default_unknown_qa_to_passed(self):
        art, audit_path = self.make_audit(None, qa_missing=True)
        audit = tiles.read_json(audit_path)
        del audit['selected'][0]['internalQaPassed']
        audit_path.write_bytes(tiles.json_bytes(audit))
        with patch.object(tiles.base_maps, 'verify'):
            with self.assertRaisesRegex(ValueError, 'must record the actual QA state'):
                tiles.import_tiles(self.project, art, audit_path)

    def test_long_artwork_paths_import_without_leaking_windows_io_prefixes(self):
        art, audit_path = self.make_audit(True)
        long_folder = art.joinpath(*(['native-evidence-' + 'x' * 55] * 4))
        tiles.access_path(long_folder).mkdir(parents=True)
        # The cleanup target is this test's owned art directory, resolved and
        # checked before the extended-path recursive cleanup is registered.
        tiles.canonical_path(art).relative_to(tiles.canonical_path(self.project))
        self.addCleanup(shutil.rmtree, tiles.access_path(art))
        audit = tiles.read_json(audit_path)
        record = next(item for item in audit['selected'] if item['mapKey'] == 'main')
        for field in ('sourcePath', 'qaPath', 'ledgerPath'):
            old = Path(record[field])
            target = long_folder / old.name
            tiles.access_path(target).write_bytes(tiles.access_path(old).read_bytes())
            record[field] = str(target)
        qa = tiles.read_json(Path(record['qaPath']))
        qa['imagePath'] = str(tiles.access_path(record['sourcePath']))
        tiles.access_path(record['qaPath']).write_bytes(tiles.json_bytes(qa))
        record['qaSha256'] = tiles.file_digest(Path(record['qaPath']))
        contract = audit['contracts']['main']
        long_contract = long_folder / 'contract.json'
        tiles.access_path(long_contract).write_bytes(tiles.access_path(contract['path']).read_bytes())
        contract['path'] = str(long_contract)
        long_audit = long_folder / 'frozen-audit.json'
        tiles.access_path(long_audit).write_bytes(tiles.json_bytes(audit))
        self.assertGreater(len(str(long_audit)), 260)
        with patch.object(tiles.base_maps, 'verify'):
            tiles.import_tiles(self.project, art, long_audit)
        folder = self.project / tiles.DELIVERY / 'main'
        evidence = tiles.read_json(folder / 'tile-evidence/r01_c01.json')
        expected = Path(record['sourcePath']).relative_to(art).as_posix()
        self.assertEqual(evidence['sourcePath'], expected)
        self.assertEqual(evidence['qa']['normalizedDocument']['imagePath'], expected)
        self.assertNotIn('\\\\?\\', json.dumps(evidence))
        self.assertNotIn('//?/', json.dumps(evidence))
        self.assertEqual((folder / 'tiles/r01_c01.png').read_bytes(), self.png)
        with self.assertRaisesRegex(ValueError, 'outside the specified artwork repository'):
            tiles.source_relative(tiles.access_path(self.project / 'outside.png'), art)

    def test_missing_qa_cannot_claim_passed_or_formally_accepted(self):
        art, audit_path = self.make_audit(None, qa_missing=True)
        record = tiles.read_json(audit_path)['selected'][0]
        for change in ({'internalQaPassed': True}, {'formalAccepted': True}):
            with self.subTest(change=change):
                with self.assertRaisesRegex(ValueError, 'missing QA cannot claim acceptance|formal acceptance requires'):
                    tiles.audit_qa(dict(record, **change), tiles.GENERATED_SELECTION, art, 'main/r01_c01')

    def test_verify_cannot_opt_out_of_qa_with_an_unknown_policy(self):
        self.assert_structural_failure(lambda m: m.update(selectionPolicy='skip_all_checks'),
                                       'Unknown tile selection policy')

    def test_failed_qa_requires_explicit_generated_policy_in_offline_manifest(self):
        folder = self.project / tiles.DELIVERY / 'main'
        evidence_path = folder / 'tile-evidence/r01_c01.json'
        evidence = tiles.read_json(evidence_path)
        evidence['internalQaPassed'] = False
        evidence_sha = tiles.write_json(evidence_path, evidence, self.project)
        self.assert_structural_failure(
            lambda m: m['tiles'][0].update(internalQaPassed=False, evidenceSha256=evidence_sha),
            'incomplete QA evidence')

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

    def test_opaque_rgba_preserves_original_png_bytes(self):
        path = self.project / 'opaque.png'
        with Image.new('RGBA', (4096, 4096), (73, 112, 99, 255)) as image:
            image.save(path, format='PNG')
        original = path.read_bytes()
        tiles.inspect_png(path, tiles.digest(original), len(original))
        self.assertEqual(path.read_bytes(), original)

    def test_rgba_with_one_nonopaque_pixel_is_rejected(self):
        path = self.project / 'transparent.png'
        with Image.new('RGBA', (4096, 4096), (73, 112, 99, 255)) as image:
            image.putpixel((4095, 4095), (73, 112, 99, 254))
            image.save(path, format='PNG')
        original = path.read_bytes()
        with self.assertRaisesRegex(ValueError, 'must be fully opaque'):
            tiles.inspect_png(path, tiles.digest(original), len(original))

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

    def test_extended_windows_prefixes_are_not_archived(self):
        root = PureWindowsPath('C:/artwork')
        art_root = MagicMock(spec=Path)
        art_root.absolute.return_value = root
        art_root.resolve.return_value = root
        self.assertEqual(tiles.normalize_source_references({
            'image': r'\\?\C:\artwork\native\tile.png',
            'forward': '//?/C:/artwork/qa.json',
            'outside': r'\\?\D:\original-host\tile.png',
        }, art_root), {
            'image': 'native/tile.png', 'forward': 'qa.json',
            'outside': 'source-host/D/original-host/tile.png',
        })


if __name__ == '__main__':
    unittest.main()
