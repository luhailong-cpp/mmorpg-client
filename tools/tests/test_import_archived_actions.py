"""归档动作清单的边界测试；不改动正式美术文件。"""
import copy
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

SCRIPT = Path(__file__).resolve().parents[1] / 'import_archived_actions.py'
SPEC = importlib.util.spec_from_file_location('archived_importer', SCRIPT)
IMPORTER = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(IMPORTER)


class ArchivedDeliveryTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.delivery = json.loads(IMPORTER.DELIVERY.read_text(encoding='utf-8'))

    def test_all_seven_deliveries_keep_authored_timing_and_anchors(self):
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


if __name__ == '__main__':
    unittest.main()
