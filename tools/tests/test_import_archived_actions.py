"""归档动作清单的边界测试；不改动正式美术文件。"""
import copy
import hashlib
import importlib.util
import json
from pathlib import Path
import struct
import tempfile
import unittest
import zlib

SCRIPT = Path(__file__).resolve().parents[1] / 'import_archived_actions.py'
SPEC = importlib.util.spec_from_file_location('archived_importer', SCRIPT)
IMPORTER = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(IMPORTER)


class ArchivedDeliveryTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.delivery = json.loads(IMPORTER.DELIVERY.read_text(encoding='utf-8'))

    def test_all_fifteen_deliveries_keep_authored_timing_and_anchors(self):
        self.assertEqual({'00', '01', '02', '03', '04', '05', '06', '07', '08', '09', '10', '14', '15', '17', '20'},
                         {c['characterId'][:2] for c in self.delivery['characters']})
        for character in self.delivery['characters']:
            manifest = IMPORTER.runtime_manifest(character)
            self.assertEqual(14, len(manifest['clips']))
            self.assertEqual(character['unityPivot'][1], manifest['pivotY'])
            for clip in manifest['clips']:
                expected = 1200 if character['characterId'].startswith('09_') else 960
                if clip['action'] == 'run':
                    self.assertEqual(expected, sum(clip['frameDurationsMs']))
                elif clip['action'] == 'attack':
                    self.assertEqual(360, sum(clip['frameDurationsMs']))
                elif clip['action'] == 'cast':
                    self.assertEqual(720, sum(clip['frameDurationsMs']))

    def test_lotus_keeps_each_direction_authored_pivot(self):
        character = next(c for c in self.delivery['characters'] if c['characterId'].startswith('03_'))
        manifest = IMPORTER.runtime_manifest(character)
        for clip in manifest['clips']:
            self.assertTrue(clip['overridePivot'])
            self.assertEqual(character['clipPivots'][clip['action']][clip['direction']],
                             [clip['pivotX'], clip['pivotY']])
        east = next(c for c in manifest['clips'] if c['action'] == 'run' and c['direction'] == 'E')
        self.assertAlmostEqual(800 / 1254, east['pivotX'])

    def test_zero_based_thunder_sources_keep_order_and_events(self):
        character = next(c for c in self.delivery['characters'] if c['characterId'].startswith('06_'))
        for frame in character['frames']:
            self.assertEqual(frame['frame'] - 1, int(Path(frame['source']).stem))
        manifest = IMPORTER.runtime_manifest(character)
        self.assertTrue(all(c['eventFrame'] == 6 for c in manifest['clips'] if c['action'] == 'attack'))
        self.assertTrue(all(c['eventFrame'] == 9 for c in manifest['clips'] if c['action'] == 'cast'))

    def test_invalid_clip_pivot_is_rejected(self):
        character = copy.deepcopy(self.delivery['characters'][0])
        character['clipPivots'] = {'run': {'E': [1.2, .08]}}
        with self.assertRaises(ValueError):
            IMPORTER.runtime_manifest(character)

    def test_duplicate_slot_is_rejected_even_with_expected_count(self):
        character = copy.deepcopy(self.delivery['characters'][0])
        character['frames'][1] = character['frames'][0]
        with self.assertRaises(ValueError):
            IMPORTER.runtime_manifest(character)

    def test_incomplete_delivery_is_rejected(self):
        character = copy.deepcopy(self.delivery['characters'][0])
        character['frames'].pop()
        with self.assertRaises(ValueError):
            IMPORTER.runtime_manifest(character)

    def test_out_of_range_event_is_rejected(self):
        character = copy.deepcopy(self.delivery['characters'][0])
        character['eventFrames'] = {'attack': {'E': 12}}
        with self.assertRaises(ValueError):
            IMPORTER.runtime_manifest(character)

    def test_missing_event_stays_unspecified(self):
        manifest = IMPORTER.runtime_manifest(self.delivery['characters'][0])
        self.assertTrue(all(c['eventFrame'] == -1 for c in manifest['clips']))

    def test_path_cannot_escape_source_or_target(self):
        with tempfile.TemporaryDirectory() as folder:
            with self.assertRaises(ValueError):
                IMPORTER.safe_path(Path(folder), '../outside.png')

    def test_meta_keeps_existing_guid_and_is_deterministic(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            target = root / 'manifest.json'
            target.write_text('{}')
            IMPORTER.meta(target, root)
            meta = target.with_suffix('.json.meta')
            initial = meta.read_bytes()
            IMPORTER.meta(target, root)
            self.assertEqual(initial, meta.read_bytes())
            meta.write_text('fileFormatVersion: 2\nguid: user-existing-guid\n')
            IMPORTER.meta(target, root)
            self.assertIn('user-existing-guid', meta.read_text())

    def make_client_snapshot(self, project):
        def chunk(kind, data):
            return struct.pack('>I', len(data)) + kind + data + struct.pack('>I', zlib.crc32(kind + data))
        png = (b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('>IIBBBBB', 1024, 1024, 8, 6, 0, 0, 0)) +
               chunk(b'IDAT', zlib.compress((b'\0' + b'\0' * 4096) * 1024)) + chunk(b'IEND', b''))
        character = copy.deepcopy(self.delivery['characters'][0])
        destination = project / IMPORTER.FAMILY / character['characterId']
        for frame in character['frames']:
            frame['sha256'] = hashlib.sha256(png).hexdigest()
            path = destination / frame['action'] / frame['direction'] / f"{frame['frame']:02d}.png"
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(png)
        manifest = IMPORTER.runtime_manifest(character)
        manifest_path = destination / 'manifest.json'
        manifest_path.write_bytes(IMPORTER.json_bytes(manifest).replace(b'\n', b'\r\n'))
        return {'schemaVersion': 1, 'characters': [character]}, manifest_path

    def test_client_verification_accepts_windows_crlf_checkout(self):
        with tempfile.TemporaryDirectory() as folder:
            project = Path(folder)
            delivery, _ = self.make_client_snapshot(project)
            result = IMPORTER.run(delivery, project / 'absent-source', project, verify=True)
            self.assertEqual({'characters': 1, 'clips': 14, 'pngs': 196, 'mode': 'verified'}, result)

    def test_client_verification_still_rejects_changed_timing_with_crlf(self):
        with tempfile.TemporaryDirectory() as folder:
            project = Path(folder)
            delivery, manifest_path = self.make_client_snapshot(project)
            manifest = json.loads(manifest_path.read_text(encoding='utf-8'))
            manifest['clips'][0]['frameDurationsMs'][0] += 1
            manifest_path.write_bytes(IMPORTER.json_bytes(manifest).replace(b'\n', b'\r\n'))
            with self.assertRaisesRegex(ValueError, '客户端时序/锚点清单不匹配'):
                IMPORTER.run(delivery, project / 'absent-source', project, verify=True)


if __name__ == '__main__':
    unittest.main()
