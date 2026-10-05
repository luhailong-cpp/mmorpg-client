"""宠物快照的启用边界、完整性及来源漂移检查。"""
import copy
import hashlib
import importlib.util
import json
from pathlib import Path
import struct
import tempfile
import unittest
import zlib

SCRIPT = Path(__file__).resolve().parents[1] / 'import_pet_actions.py'
SPEC = importlib.util.spec_from_file_location('pet_importer', SCRIPT)
IMPORTER = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(IMPORTER)


def rgba_png():
    def chunk(kind, data):
        return struct.pack('>I', len(data)) + kind + data + struct.pack('>I', zlib.crc32(kind + data))
    return (b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('>IIBBBBB', 2, 2, 8, 6, 0, 0, 0)) +
            chunk(b'IDAT', zlib.compress((b'\0' + b'\xff\0\0\xff' * 2) * 2)) + chunk(b'IEND', b''))


class PetDeliveryTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.delivery = json.loads(IMPORTER.DELIVERY.read_text(encoding='utf-8'))

    def test_only_complete_snapshot_clips_activate(self):
        active, total_frames = 0, 0
        self.assertEqual(20, len(self.delivery['pets']))
        for pet in self.delivery['pets']:
            manifest, copies, pending = IMPORTER.build_pet(pet)
            active_keys = {(c['action'], c['direction']) for c in manifest['clips']}
            self.assertTrue({('idle', 'E'), ('idle', 'W')}.issubset(active_keys))
            for group in pet['clips']:
                self.assertEqual(group['eligibleCompleteClip'], (group['action'], group['direction']) in active_keys)
            for relative, frame in copies:
                if relative.startswith('pending/'):
                    self.assertNotIn((frame['action'], frame['direction']), active_keys)
            self.assertTrue(all(not p['activated'] for p in pending))
            active += len(manifest['clips']) - 2
            total_frames += len(pet['frames'])
        self.assertEqual(27, active)
        self.assertEqual(560, total_frames)

    def test_missing_frame_cannot_be_enabled_by_flag(self):
        pet = copy.deepcopy(self.delivery['pets'][0])
        partial = next(c for c in pet['clips'] if not c['completeFrameSet'])
        partial['eligibleCompleteClip'] = partial['completeFrameSet'] = True
        with self.assertRaisesRegex(ValueError, '不完整'):
            IMPORTER.build_pet(pet)

    def test_duplicate_frame_does_not_fill_a_missing_slot(self):
        pet = copy.deepcopy(self.delivery['pets'][0])
        pet['frames'][1] = pet['frames'][0]
        with self.assertRaisesRegex(ValueError, '重复'):
            IMPORTER.build_pet(pet)

    def test_native_1254_canvas_has_no_runtime_downscale(self):
        pet = next(p for p in self.delivery['pets'] if p['id'] == 'legacy-yun-jiu-jiu')
        manifest, _, _ = IMPORTER.build_pet(pet)
        for clip in manifest['clips']:
            self.assertEqual((1254, 1254), (clip['frameWidth'], clip['frameHeight']))
            self.assertEqual(1254 / 6.4, clip['pixelsPerUnit'])
            self.assertEqual((.5, .08), (clip['pivotX'], clip['pivotY']))

    def test_exact_bindings_do_not_assume_similar_species(self):
        bindings = {p['id']: (p['petTableIds'], p['modelIds']) for p in self.delivery['pets'] if p['petTableIds'] or p['modelIds']}
        self.assertEqual({'legacy-ling-yue': ([1], [1001]), 'legacy-yun-jiu-jiu': ([4], [1004])}, bindings)

    def test_source_hash_drift_fails_before_publishing_anything(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            source, project = root / 'source', root / 'project'
            source.mkdir()
            project.mkdir()
            delivery = copy.deepcopy(self.delivery)
            delivery['pets'] = delivery['pets'][:1]
            pet = delivery['pets'][0]
            frames = pet['staticAssets'] + [pet['portrait']] + pet['frames']
            payload = rgba_png()
            for n, frame in enumerate(frames):
                frame.update(sourceRelative=f'{n}.png', width=2, height=2, sha256=hashlib.sha256(payload).hexdigest())
                (source / frame['sourceRelative']).write_bytes(payload)
            (source / frames[-1]['sourceRelative']).write_bytes(b'changed during production')
            with self.assertRaisesRegex(ValueError, 'SHA已变动'):
                IMPORTER.run(delivery, source, project, execute=True)
            self.assertFalse((project / IMPORTER.FAMILY).exists())

    def test_path_escape_and_invalid_identity_fail(self):
        with tempfile.TemporaryDirectory() as folder:
            with self.assertRaisesRegex(ValueError, '越界'):
                IMPORTER.safe_path(Path(folder), '../outside.png')
        pet = copy.deepcopy(self.delivery['pets'][0])
        pet['id'] = '../elsewhere'
        with self.assertRaisesRegex(ValueError, 'ID无效'):
            IMPORTER.build_pet(pet)

    def test_png_meta_preserves_original_size_and_guid(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            png = root / '01.png'
            png.write_bytes(rgba_png())
            IMPORTER.meta(png, root)
            first = Path(str(png) + '.meta').read_text()
            self.assertIn('maxTextureSize: 2048', first)
            self.assertIn('nPOTScale: 0', first)
            IMPORTER.meta(png, root)
            self.assertEqual(first, Path(str(png) + '.meta').read_text())


if __name__ == '__main__':
    unittest.main()
