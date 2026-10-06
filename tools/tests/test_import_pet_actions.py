"""宠物快照的启用边界、完整性及来源漂移检查。"""
import copy
import hashlib
import importlib.util
import json
from pathlib import Path
import re
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
        self.assertEqual(112, active)
        self.assertEqual(1344, total_frames)

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
            size = 1254 if clip['action'] == 'idle' else 1024
            self.assertEqual((size, size), (clip['frameWidth'], clip['frameHeight']))
            self.assertEqual(size / 6.4, clip['pixelsPerUnit'])
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
            delivery['pendingPromotions'] = []
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

    def test_contact_and_release_do_not_fire_on_windup_or_secondary_contact(self):
        expected = {'11-xiajiaolu': {('attack', 'E'): 6, ('cast', 'W'): 9},
                    '15-landuoxian': {('cast', 'E'): 8},
                    'legacy-fu-xiao-hu': {('cast', 'W'): 9}}
        for pid, events in expected.items():
            pet = next(p for p in self.delivery['pets'] if p['id'] == pid)
            manifest, _, _ = IMPORTER.build_pet(pet)
            actual = {(c['action'], c['direction']): c['eventFrame'] for c in manifest['clips']}
            for key, frame in events.items():
                self.assertEqual(frame, actual[key], (pid, key))

    def test_unresolved_source_evidence_cannot_be_enabled(self):
        pet = copy.deepcopy(self.delivery['pets'][0])
        frame = next(f for f in pet['frames'] if f['action'] == 'hit' and f['direction'] == 'E')
        frame['sourceEvidenceResolved'] = False
        with self.assertRaisesRegex(ValueError, '来源证据'):
            IMPORTER.build_pet(pet)

    def promotion_fixture(self, root):
        source, project = root / 'source', root / 'project'
        source.mkdir()
        project.mkdir()
        delivery = copy.deepcopy(self.delivery)
        delivery['pets'] = delivery['pets'][:1]
        pet = delivery['pets'][0]
        payload = rgba_png()
        sha = hashlib.sha256(payload).hexdigest()
        for n, frame in enumerate(pet['staticAssets'] + [pet['portrait']] + pet['frames']):
            frame.update(sourceRelative=f'{n}.png', width=2, height=2, sha256=sha)
            (source / frame['sourceRelative']).write_bytes(payload)
        delivery['pendingPromotions'] = [dict(petId=pet['id'], action='hit', direction='E', frame=1, previousSha256=sha)]
        old = project / IMPORTER.FAMILY / pet['id'] / 'pending/hit/E/01.png'
        old.parent.mkdir(parents=True)
        old.write_bytes(payload)
        IMPORTER.meta(old, project)
        delivery['pendingPromotions'][0]['previousGuid'] = re.search(
            r'^guid: ([0-9a-f]{32})$', Path(str(old) + '.meta').read_text(), re.MULTILINE).group(1)
        target = project / IMPORTER.FAMILY / pet['id'] / 'hit/E/01.png'
        return delivery, source, project, old, target

    def test_pending_promotion_preserves_guid_and_only_removes_owned_copy(self):
        with tempfile.TemporaryDirectory() as folder:
            delivery, source, project, old, target = self.promotion_fixture(Path(folder))
            old_meta = Path(str(old) + '.meta')
            prior = old_meta.read_text()
            unrelated = old.parent / 'keep.txt'
            unrelated.write_text('not owned')
            IMPORTER.run(delivery, source, project, execute=True)
            self.assertFalse(old.exists())
            self.assertFalse(old_meta.exists())
            self.assertEqual(prior, Path(str(target) + '.meta').read_text())
            self.assertEqual('not owned', unrelated.read_text())
            self.assertEqual('verified', IMPORTER.run(delivery, source, project, verify=True)['mode'])
            IMPORTER.run(delivery, source, project, execute=True)
            self.assertEqual(prior, Path(str(target) + '.meta').read_text())

    def test_clean_reimport_reproduces_migrated_guid_and_cleans_empty_folder_meta(self):
        with tempfile.TemporaryDirectory() as folder:
            delivery, source, project, old, target = self.promotion_fixture(Path(folder))
            old.unlink()
            Path(str(old) + '.meta').unlink()
            IMPORTER.run(delivery, source, project, execute=True)
            self.assertIn('guid: ' + delivery['pendingPromotions'][0]['previousGuid'],
                          Path(str(target) + '.meta').read_text())
            self.assertFalse(old.parent.exists())
            self.assertFalse(Path(str(old.parent) + '.meta').exists())
            self.assertFalse(Path(str(old.parent.parent) + '.meta').exists())

    def test_unknown_folder_guid_is_preserved(self):
        with tempfile.TemporaryDirectory() as folder:
            delivery, source, project, old, target = self.promotion_fixture(Path(folder))
            folder_meta = Path(str(old.parent) + '.meta')
            folder_meta.write_text('fileFormatVersion: 2\nguid: ' + '0' * 32 + '\nfolderAsset: yes\nDefaultImporter:\n')
            prior = folder_meta.read_text()
            IMPORTER.run(delivery, source, project, execute=True)
            self.assertEqual(prior, folder_meta.read_text())

    def test_windows_crlf_checkout_verifies_without_relaxing_json_content(self):
        with tempfile.TemporaryDirectory() as folder:
            delivery, source, project, old, target = self.promotion_fixture(Path(folder))
            IMPORTER.run(delivery, source, project, execute=True)
            family = project / IMPORTER.FAMILY
            generated = list(family.rglob('*.json'))
            self.assertEqual(3, len(generated))
            for path in generated:
                path.write_bytes(path.read_text(encoding='utf-8').replace('\n', '\r\n').encode('utf-8'))
                self.assertIn(b'\r\n', path.read_bytes())
            self.assertEqual('verified', IMPORTER.run(delivery, source, project, verify=True)['mode'])
            catalog = family / 'catalog.json'
            catalog.write_bytes(catalog.read_bytes() + b' ')
            with self.assertRaisesRegex(ValueError, 'catalog不匹配'):
                IMPORTER.run(delivery, source, project, verify=True)

    def test_changed_or_unowned_pending_is_never_removed(self):
        for change in ('sha', 'owner', 'identity'):
            with self.subTest(change=change), tempfile.TemporaryDirectory() as folder:
                delivery, source, project, old, target = self.promotion_fixture(Path(folder))
                if change == 'sha':
                    old.write_bytes(b'user replacement')
                elif change == 'owner':
                    meta = Path(str(old) + '.meta')
                    meta.write_text(meta.read_text().replace('qdao-pets-20261005', 'someone-else'))
                else:
                    delivery['pendingPromotions'][0]['petId'] = '../outside'
                with self.assertRaises(ValueError):
                    IMPORTER.run(delivery, source, project, execute=True)
                self.assertTrue(old.exists())
                self.assertFalse(target.exists())

    def test_bad_new_source_never_deletes_old_pending(self):
        with tempfile.TemporaryDirectory() as folder:
            delivery, source, project, old, target = self.promotion_fixture(Path(folder))
            (source / delivery['pets'][0]['frames'][-1]['sourceRelative']).write_bytes(b'changed')
            with self.assertRaisesRegex(ValueError, 'SHA已变动'):
                IMPORTER.run(delivery, source, project, execute=True)
            self.assertTrue(old.exists())
            self.assertFalse(target.exists())

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
