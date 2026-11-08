"""失败和保留测试使用合成夹具，不使用正式原生素材。"""
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
import sys

from PIL import Image

SPEC = importlib.util.spec_from_file_location('import_run_variants', Path(__file__).parents[1] / 'import_run_variants.py')
M = importlib.util.module_from_spec(SPEC)
sys.modules[SPEC.name] = M
SPEC.loader.exec_module(M)
CHARACTER = '01_ice_sword_girl'
FIRE_BOY = '02_fire_talisman_boy'


def unity_expanded_meta(payload):
    """记录 Unity6000.3.11f1 在 real128 导入中的序列化差异。"""
    text = payload.decode()
    text = text.replace('  isReadable: 0\n',
        '  isReadable: 0\n  streamingMipmaps: 0\n  grayScaleToAlpha: 0\n'
        '  textureFormat: 1\n  spriteMode: 0\n  spriteMeshType: 1\n  alignment: 0\n'
        '  spritePivot: {x: 0.5, y: 0.5}\n  spritePixelsToUnits: 100\n'
        '  flipbookRows: 1\n  flipbookColumns: 1\n  swizzle: 50462976\n')
    before, platforms = text.split('  platformSettings:\n', 1)
    platforms = platforms.replace('    maxTextureSize: 512\n', '    maxTextureSize: 4096\n', 1)
    platforms = platforms.replace('  userData:',
        '  - serializedVersion: 4\n    buildTarget: Standalone\n    maxTextureSize: 512\n'
        '    resizeAlgorithm: 0\n    textureFormat: -1\n    textureCompression: 0\n'
        '    compressionQuality: 50\n    crunchedCompression: 0\n'
        '    allowsAlphaSplitting: 0\n    overridden: 0\n'
        '  spriteSheet:\n    serializedVersion: 2\n    sprites: []\n    outline: []\n'
        '  userData:')
    return (before + '  platformSettings:\n' + platforms +
            '  assetBundleName: \n  assetBundleVariant: \n').replace('\n', '\r\n').encode()


class ImportRunVariantsTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.source = self.root / 'source'
        self.client = self.root / 'client'
        self.source.mkdir()
        self.client.mkdir()
        (self.source / 'AGENTS.md').write_text('Test asset root', encoding='utf-8')

    def put(self, path, value):
        path.write_bytes(M.encoded(value))
        return {'file': path.relative_to(self.source).as_posix(), 'sha256': M.digest(path.read_bytes())}

    def delivery(self, variant='current', count=None, direction='E', suffix='', canvas=1254,
                 character=CHARACTER):
        count = count or (8 if variant == 'current' else 16)
        folder = self.source / (variant + suffix)
        folder.mkdir()
        frames = []
        for i in range(1, count + 1):
            file = folder / f'{i:02d}.png'
            image = Image.new('RGBA', (canvas, canvas), (i * 11 % 256, 90, 150, 128))
            image.save(file)
            frames.append(dict(frame=i, file=file.relative_to(self.source).as_posix(),
                sha256=M.digest(file.read_bytes()), durationSeconds=2 / 15 if variant == 'current' else 1 / 15))
        selection = self.put(folder / 'selection.json', {'frames': frames})
        approval = self.put(folder / 'approval.json', dict(accepted=True, characterId=character,
            direction=direction, variant=variant, orderedSourceHashes=[f['sha256'] for f in frames],
            identityConfirmed=variant == 'legacy', originalTiming=variant == 'legacy'))
        manifest = self.source / f'{variant}{suffix}.json'
        data = dict(schema='qdao.run-variant-delivery/v1', sourceRoot='.', characterId=character,
            variant=variant, clips=[dict(direction=direction, frames=frames, selection=selection, approval=approval)])
        self.put(manifest, data)
        return manifest, data

    def legacy_candidate(self, canvas=1254):
        manifest, data = self.delivery('legacy', canvas=canvas)
        clip = data['clips'][0]
        clip['sourceIdentity'] = dict(classification='legacy_candidate_unclassified',
            doubleEllipseConfirmed=False, displayLabel=M.LEGACY_CANDIDATE_LABEL)
        clip['nativeCanvas'] = [canvas, canvas]
        for frame in clip['frames']:
            frame['durationSeconds'] = .08
        clip['selection'] = self.put(self.source / clip['selection']['file'], {'frames': clip['frames']})
        approval = M.read_json(self.source / clip['approval']['file'])
        approval.update(accepted=False, identityConfirmed=False, sourceRetentionVerified=True,
                        originalTiming=True, originalOrder=True)
        clip['approval'] = self.put(self.source / clip['approval']['file'], approval)
        self.put(manifest, data)
        return manifest, data

    def run_import(self, manifest, variant='current', **kwargs):
        return M.run(manifest, CHARACTER, variant, self.client, **kwargs)

    def tree(self):
        return {p.relative_to(self.client).as_posix(): p.read_bytes()
                for p in self.client.rglob('*') if p.is_file()}

    def calibration_rule(self, data):
        return dict(id='fixture-current-fire-sw', characterId=FIRE_BOY, variant='current',
            direction='SW', orderedSourceHashes=[f['sha256'] for f in data['clips'][0]['frames']],
            runtimeClip=dict(pixelsPerUnit=30, overridePivot=True, pivotX=.5, pivotY=.22))

    def write_calibrations(self, rules):
        path = self.client / 'tools/run-variant-game-calibrations.json'
        path.parent.mkdir(exist_ok=True)
        path.write_bytes(M.encoded(dict(schema='qdao.run-variant-game-calibrations/v1', rules=rules)))
        return path

    def test_source_bound_clip_calibration_changes_only_manifest_proof_and_fingerprint(self):
        manifest, data = self.delivery(direction='SW', character=FIRE_BOY)
        _, east = self.delivery(direction='E', suffix='-east', character=FIRE_BOY)
        data['clips'].extend(east['clips'])
        self.put(manifest, data)
        baseline = M.preflight(manifest, FIRE_BOY, 'current', self.client)
        M.run(manifest, FIRE_BOY, 'current', self.client, execute=True)
        for path in baseline.destination.rglob('*.png.meta'):
            path.write_bytes(unity_expanded_meta(path.read_bytes()))
        retained = {p.relative_to(baseline.destination): p.read_bytes()
                    for p in baseline.destination.rglob('*')
                    if p.is_file() and (p.suffix == '.meta' or p.suffix == '.png')}
        rule = self.calibration_rule(data)
        self.write_calibrations([rule])
        plan = M.preflight(manifest, FIRE_BOY, 'current', self.client)
        sw = next(c for c in plan.runtime['clips'] if c['direction'] == 'SW')
        for key, value in rule['runtimeClip'].items():
            self.assertEqual(sw.get(key), value)
        self.assertEqual(plan.runtime['pixelsPerUnit'], 39)
        self.assertEqual((plan.runtime['pivotX'], plan.runtime['pivotY']), (.5, .08))
        self.assertEqual(next(c for c in plan.runtime['clips'] if c['direction'] == 'E'),
                         next(c for c in baseline.runtime['clips'] if c['direction'] == 'E'))
        self.assertNotEqual(plan.fingerprint, baseline.fingerprint)
        self.assertEqual(set(plan.files), set(baseline.files))
        self.assertEqual({p for p in plan.files if plan.files[p] != baseline.files[p]},
                         {Path('manifest.json'), Path('import-proof.json')})
        proof = json.loads(plan.files[Path('import-proof.json')])
        binding = proof['gameCalibrations'][0]
        self.assertEqual(binding['ruleSource'], 'tools/run-variant-game-calibrations.json')
        self.assertEqual(binding['ruleSha256'], M.digest(M.encoded(rule)))
        self.assertEqual(binding['orderedSourceHashes'], rule['orderedSourceHashes'])
        self.assertEqual(binding['configuredRuntimeClip'], rule['runtimeClip'])
        self.assertEqual(binding['emittedRuntimeClip'], rule['runtimeClip'])
        self.assertEqual(binding['gameAcceptance'], 'pending_independent_runtime_closure')
        self.assertEqual(proof['calibration'], 'pending real Unity game validation')
        self.assertEqual(plan.runtime['visualValidation'], 'game_scale_ground_anchor_pending')
        M.run(manifest, FIRE_BOY, 'current', self.client, execute=True, replace=True)
        self.assertEqual(retained, {p: (plan.destination / p).read_bytes() for p in retained})
        self.assertEqual(M.run(manifest, FIRE_BOY, 'current', self.client,
                              verify_output=True)['mode'], 'verified')

    def test_calibration_rejects_reapproved_changed_source_hash_or_order_without_writes(self):
        for change in ('hash', 'order'):
            with self.subTest(change=change):
                manifest, data = self.delivery(direction='SW', character=FIRE_BOY, suffix='-' + change)
                rule = self.calibration_rule(data)
                self.write_calibrations([rule])
                clip = data['clips'][0]
                if change == 'hash':
                    frame = clip['frames'][0]
                    source = self.source / frame['file']
                    Image.new('RGBA', (1254, 1254), (250, 10, 20, 255)).save(source)
                    frame['sha256'] = M.digest(source.read_bytes())
                else:
                    clip['frames'][0], clip['frames'][1] = clip['frames'][1], clip['frames'][0]
                    for index, frame in enumerate(clip['frames'], 1):
                        frame['frame'] = index
                # 新的源美术批准不能静默沿用旧的游戏校准。
                clip['selection'] = self.put(self.source / clip['selection']['file'], {'frames': clip['frames']})
                approval = M.read_json(self.source / clip['approval']['file'])
                approval['orderedSourceHashes'] = [f['sha256'] for f in clip['frames']]
                clip['approval'] = self.put(self.source / clip['approval']['file'], approval)
                self.put(manifest, data)
                before = self.tree()
                with self.assertRaisesRegex(ValueError, 'calibration source hash/order mismatch'):
                    M.run(manifest, FIRE_BOY, 'current', self.client, execute=True)
                self.assertEqual(self.tree(), before)

    def test_unmatched_calibrations_preserve_all_previous_plan_bytes(self):
        target, target_data = self.delivery(direction='SW', character=FIRE_BOY, suffix='-target')
        east, east_data = self.delivery(direction='E', character=FIRE_BOY, suffix='-east')
        target_data['clips'].extend(east_data['clips'])
        self.put(target, target_data)
        other, _ = self.delivery(direction='SW', suffix='-other-character')
        legacy, _ = self.delivery('legacy', direction='SW', character=FIRE_BOY, suffix='-legacy')
        cases = [(other, CHARACTER, 'current', None), (east, FIRE_BOY, 'current', None),
                 (legacy, FIRE_BOY, 'legacy', None), (target, FIRE_BOY, 'current', ['E'])]
        baselines = [M.preflight(path, character, variant, self.client, directions)
                     for path, character, variant, directions in cases]
        rule = self.calibration_rule(target_data)
        # 只选择 E 的导入不得检查未选 SW 的源文件锁定。
        rule['orderedSourceHashes'].reverse()
        self.write_calibrations([rule])
        for case, baseline in zip(cases, baselines):
            path, character, variant, directions = case
            with self.subTest(character=character, variant=variant, directions=directions):
                plan = M.preflight(path, character, variant, self.client, directions)
                self.assertEqual(plan.fingerprint, baseline.fingerprint)
                self.assertEqual(plan.runtime, baseline.runtime)
                self.assertEqual(plan.files, baseline.files)

    def test_fingerprint_binds_applied_settings_not_unrelated_rules(self):
        manifest, data = self.delivery(direction='SW', character=FIRE_BOY)
        rule = self.calibration_rule(data)
        self.write_calibrations([rule])
        original = M.preflight(manifest, FIRE_BOY, 'current', self.client)
        unrelated = {**rule, 'id': 'unrelated', 'characterId': CHARACTER}
        self.write_calibrations([unrelated, rule])
        same = M.preflight(manifest, FIRE_BOY, 'current', self.client)
        self.assertEqual(same.files, original.files)
        self.assertEqual(same.fingerprint, original.fingerprint)
        rule['runtimeClip']['pixelsPerUnit'] = 31
        self.write_calibrations([unrelated, rule])
        changed = M.preflight(manifest, FIRE_BOY, 'current', self.client)
        self.assertNotEqual(changed.fingerprint, original.fingerprint)
        self.assertEqual({p for p in changed.files if changed.files[p] != original.files[p]},
                         {Path('manifest.json'), Path('import-proof.json')})
        binding = json.loads(changed.files[Path('import-proof.json')])['gameCalibrations'][0]
        self.assertEqual(binding['configuredRuntimeClip']['pixelsPerUnit'], 31)
        self.assertEqual(binding['emittedRuntimeClip']['pixelsPerUnit'], 31)

    def test_checked_in_calibration_is_exact_fixed72_fire_sw_source_lock(self):
        document = M.read_json(Path(__file__).parents[1] / 'run-variant-game-calibrations.json')
        self.assertEqual(document['schema'], 'qdao.run-variant-game-calibrations/v1')
        self.assertEqual(document['gameAcceptance'], 'pending_independent_runtime_closure')
        self.assertEqual(len(document['rules']), 1)
        rule = document['rules'][0]
        self.assertEqual((rule['characterId'], rule['variant'], rule['direction']),
                         (FIRE_BOY, 'current', 'SW'))
        self.assertEqual(rule['runtimeClip'],
                         dict(pixelsPerUnit=30, overridePivot=True, pivotX=.5, pivotY=.22))
        self.assertEqual(rule['orderedSourceHashes'], [
            'f6c11b64a44c62146562b89e31fef06c7f09791a32e87d9ebb4fdf214c3d57e7',
            'd4b6a0e45c0fd3adbe61858d0b1b76c98255385dab8143f524c816f8f4e4bf32',
            'd11657daafc88b57300a4bd4f999aac0e52b17df8b73132cc312c7d8e4d82778',
            '248c128f8b203f706f30704816948411c1386b93bac32a791442849c380437a1',
            '09b84029bca1182259b4b46c47b984ab922efd2cbc4e551d32ff5296e0197423',
            '70aa0d1ca482ad4c4d2b7347bcb26eac946c8722899a17eefe848fb83004c3d1',
            '11b9ee0e91a6cef37df83232d2c770cf24dd6584df17d8e851a30c7439264cf3',
            '11f29c2fce1ec35f5807fe91d6b0784d8899592a0155c883707b21c44a74a3a7'])

    def test_default_preflight_is_read_only_and_single_direction_is_explicit(self):
        manifest, _ = self.delivery()
        result = self.run_import(manifest)
        self.assertEqual(result['mode'], 'preflight')
        self.assertEqual(result['directions'], ['E'])
        self.assertFalse(result['allEightDirectionsAvailable'])
        self.assertEqual(len(result['unavailableDirections']), 7)
        self.assertEqual(list(self.client.iterdir()), [])

    def test_legacy16_and_current8_preserve_timing_and_isolate_roots(self):
        legacy, _ = self.delivery('legacy')
        current, _ = self.delivery('current')
        for path, variant, count, time in [(legacy, 'legacy', 16, 1000 / 15),
                                         (current, 'current', 8, 2000 / 15)]:
            result = self.run_import(path, variant, execute=True)
            target = Path(result['destination'])
            runtime = M.read_json(target / 'manifest.json')
            clip = runtime['clips'][0]
            self.assertEqual(clip['frameCount'], count)
            self.assertEqual(len(clip['frameDurationsMs']), count)
            self.assertAlmostEqual(clip['frameDurationsMs'][0], time)
            self.assertAlmostEqual(sum(clip['frameDurationsMs']), 16000 / 15)
            self.assertEqual(runtime['frameWidth'], 384)
            self.assertEqual(runtime['pixelsPerUnit'], 39)
            with Image.open(target / 'run/E/01.png') as image:
                self.assertEqual(image.size, (384, 384))
            self.assertEqual(self.run_import(path, variant, verify_output=True)['mode'], 'verified')
        self.assertTrue((self.client / M.FAMILY / 'legacy' / CHARACTER).is_dir())
        self.assertTrue((self.client / M.FAMILY / 'current' / CHARACTER).is_dir())

    def test_current16_is_not_silently_decimated(self):
        manifest, _ = self.delivery('current', 16)
        with self.assertRaisesRegex(ValueError, 'exactly eight'):
            self.run_import(manifest, execute=True)
        self.assertEqual(list(self.client.iterdir()), [])

    def test_cross_variant_and_unknown_direction_never_fall_back(self):
        manifest, _ = self.delivery()
        with self.assertRaisesRegex(ValueError, 'fallback'):
            self.run_import(manifest, 'legacy', execute=True)
        with self.assertRaisesRegex(ValueError, 'not independently accepted'):
            self.run_import(manifest, directions=['NW'], execute=True)
        self.assertEqual(list(self.client.iterdir()), [])

    def test_cancelled_and_unknown_characters_are_rejected(self):
        manifest, _ = self.delivery()
        for cid in ['07_moon_shadow_assassin_girl', '14_short_hair_snow_summoner_girl',
                    '20_star_formation_master_girl', '01_ice_sword_girl/../../other']:
            with self.subTest(cid=cid), self.assertRaisesRegex(ValueError, 'allowlist'):
                M.run(manifest, cid, 'current', self.client, execute=True)

    def test_last_frame_corruption_does_not_publish_earlier_frames(self):
        manifest, data = self.delivery()
        final = self.source / data['clips'][0]['frames'][-1]['file']
        final.write_bytes(b'not a png')
        with self.assertRaisesRegex(ValueError, 'SHA256 mismatch'):
            self.run_import(manifest, execute=True)
        self.assertEqual(list(self.client.iterdir()), [])

    def test_path_escape_rejected_without_writes(self):
        manifest, data = self.delivery()
        data['clips'][0]['frames'][0]['file'] = '../outside.png'
        self.put(manifest, data)
        with self.assertRaisesRegex(ValueError, 'traversal'):
            self.run_import(manifest, execute=True)
        self.assertEqual(list(self.client.iterdir()), [])

    def test_approval_and_original_timing_cannot_be_overridden(self):
        manifest, data = self.delivery('legacy')
        data['clips'][0]['frames'][0]['durationSeconds'] = .1
        self.put(manifest, data)
        with self.assertRaisesRegex(ValueError, 'timing differs'):
            self.run_import(manifest, 'legacy')
        manifest, data = self.delivery('current')
        approval = self.source / data['clips'][0]['approval']['file']
        value = M.read_json(approval)
        value['direction'] = 'W'
        data['clips'][0]['approval'] = self.put(approval, value)
        self.put(manifest, data)
        with self.assertRaisesRegex(ValueError, 'Independent approval'):
            self.run_import(manifest)

    def test_duplicate_hash_poses_are_rejected(self):
        manifest, data = self.delivery()
        clip = data['clips'][0]
        clip['frames'][1]['sha256'] = clip['frames'][0]['sha256']
        clip['selection'] = self.put(self.source / clip['selection']['file'], {'frames': clip['frames']})
        app = M.read_json(self.source / clip['approval']['file'])
        app['orderedSourceHashes'] = [f['sha256'] for f in clip['frames']]
        clip['approval'] = self.put(self.source / clip['approval']['file'], app)
        self.put(manifest, data)
        with self.assertRaisesRegex(ValueError, 'Repeated native frame hashes'):
            self.run_import(manifest)

    def test_nonfinite_timing_rejected(self):
        manifest, data = self.delivery()
        manifest.write_text(json.dumps(data).replace('0.13333333333333333', 'NaN'), encoding='utf-8')
        with self.assertRaisesRegex(ValueError, 'Nonfinite'):
            self.run_import(manifest, execute=True)
        self.assertEqual(list(self.client.iterdir()), [])

    def test_same_input_is_idempotent_and_different_source_requires_replace(self):
        manifest, data = self.delivery()
        result = self.run_import(manifest, execute=True)
        target = Path(result['destination']) / 'manifest.json'
        before = target.stat().st_mtime_ns
        self.assertEqual(self.run_import(manifest, execute=True)['mode'], 'unchanged')
        self.assertEqual(target.stat().st_mtime_ns, before)
        data['note'] = 'different locked input'
        self.put(manifest, data)
        snapshot = self.tree()
        with self.assertRaisesRegex(ValueError, '--replace'):
            self.run_import(manifest, execute=True)
        self.assertEqual(self.tree(), snapshot)
        self.assertEqual(self.run_import(manifest, execute=True, replace=True)['mode'], 'imported')

    def test_publication_failure_rolls_back_previous_tree(self):
        manifest, data = self.delivery()
        self.run_import(manifest, execute=True)
        snapshot = self.tree()
        data['note'] = 'new manifest'
        self.put(manifest, data)
        original = M.os.replace
        count = 0
        def fail_install(source, destination):
            nonlocal count
            count += 1
            if count == 2:
                raise OSError('simulated directory publication failure')
            return original(source, destination)
        with patch.object(M.os, 'replace', side_effect=fail_install), self.assertRaisesRegex(OSError, 'simulated'):
            self.run_import(manifest, execute=True, replace=True)
        self.assertEqual(self.tree(), snapshot)
        self.assertFalse(any(p.name.startswith('.run-variant-stage') for p in self.client.iterdir()))

    def test_new_publication_failure_cleans_new_parents(self):
        manifest, _ = self.delivery()
        with patch.object(M, 'verify', side_effect=OSError('simulated verification failure')):
            with self.assertRaisesRegex(OSError, 'simulated'):
                self.run_import(manifest, execute=True)
        self.assertEqual(list(self.client.iterdir()), [])

    def test_verify_catches_derived_tampering(self):
        manifest, _ = self.delivery()
        result = self.run_import(manifest, execute=True)
        (Path(result['destination']) / 'run/E/08.png').write_bytes(b'changed')
        with self.assertRaisesRegex(ValueError, 'Published bytes differ'):
            self.run_import(manifest, verify_output=True)

    def test_unity_meta_serialization_is_verified_and_not_rewritten(self):
        manifest, _ = self.delivery()
        result = self.run_import(manifest, execute=True)
        metas = list(Path(result['destination']).rglob('*.png.meta'))
        for path in metas:
            path.write_bytes(unity_expanded_meta(path.read_bytes()))
        before = {path: (path.read_bytes(), path.stat().st_mtime_ns) for path in metas}
        self.assertEqual(self.run_import(manifest, verify_output=True)['mode'], 'verified')
        self.assertEqual(self.run_import(manifest, execute=True)['mode'], 'unchanged')
        self.assertEqual(before, {path: (path.read_bytes(), path.stat().st_mtime_ns) for path in metas})

    def test_malicious_guid_ppu_size_type_and_other_import_semantics_fail(self):
        expected = M.meta_bytes(M.FAMILY / 'current' / CHARACTER / 'run/E/01.png', 'png')
        actual = unity_expanded_meta(expected)
        guid = M.meta_fields(actual)[('guid',)]
        changes = [(guid, '0' * 32), ('spritePixelsToUnits: 100', 'spritePixelsToUnits: 39'),
            ('  maxTextureSize: 512', '  maxTextureSize: 128'), ('textureType: 0', 'textureType: 8'),
            ('spriteMode: 0', 'spriteMode: 1'), ('alphaUsage: 1', 'alphaUsage: 0'),
            ('enableMipMap: 0', 'enableMipMap: 1'), ('filterMode: 1', 'filterMode: 0'),
            ('textureCompression: 0', 'textureCompression: 1'), ('x: 0.5, y: 0.5', 'x: 0.5, y: 0.08'),
            ('maxTextureSize: 4096', 'maxTextureSize: 128'), ('overridden: 0', 'overridden: 1')]
        for old, new in changes:
            with self.subTest(field=old), self.assertRaises(ValueError):
                M.verify_meta(actual.replace(old.encode(), new.encode(), 1), expected, 'fixture')

    def test_meta_duplicate_fields_and_other_importer_are_rejected(self):
        expected = M.meta_bytes(Path('fixture.png'), 'png')
        actual = unity_expanded_meta(expected)
        with self.assertRaisesRegex(ValueError, 'Duplicate'):
            M.verify_meta(actual + b'  textureType: 0\n', expected, 'fixture')
        with self.assertRaisesRegex(ValueError, 'importer type'):
            M.verify_meta(actual.replace(b'TextureImporter:', b'ScriptedImporter:'), expected, 'fixture')

    def test_text_and_folder_meta_extra_unity_fields_keep_guid_checks(self):
        for kind in ['folder', 'text']:
            expected = M.meta_bytes(Path('fixture'), kind)
            expanded = expected + b'  assetBundleName: \n  assetBundleVariant: \n'
            M.verify_meta(expanded, expected, 'fixture')
            guid = M.meta_fields(expected)[('guid',)]
            with self.assertRaises(ValueError):
                M.verify_meta(expanded.replace(guid.encode(), b'0' * 32), expected, 'fixture')

    def test_unclassified_candidate_retains_identity_order_timing_and_is_idempotent(self):
        manifest, data = self.legacy_candidate()
        result = self.run_import(manifest, 'legacy', execute=True)
        target = Path(result['destination'])
        runtime = M.read_json(target / 'manifest.json')
        clip = runtime['clips'][0]
        self.assertEqual(clip['sourceIdentity'], data['clips'][0]['sourceIdentity'])
        self.assertFalse(clip['sourceIdentity']['doubleEllipseConfirmed'])
        self.assertEqual(clip['frameCount'], 16)
        self.assertEqual(clip['frameDurationsMs'], [80] * 16)
        self.assertEqual(clip['sourceCanvas'], [1254, 1254])
        proof = M.read_json(target / 'import-proof.json')
        self.assertEqual([f['sourceSha256'] for f in proof['frames']],
                         [f['sha256'] for f in data['clips'][0]['frames']])
        self.assertEqual(self.run_import(manifest, 'legacy', verify_output=True)['mode'], 'verified')
        self.assertEqual(self.run_import(manifest, 'legacy', execute=True)['mode'], 'unchanged')
        clip['sourceIdentity']['displayLabel'] = '双椭圆已确认'
        (target / 'manifest.json').write_bytes(M.encoded(runtime))
        with self.assertRaisesRegex(ValueError, 'Published bytes differ'):
            self.run_import(manifest, 'legacy', verify_output=True)

    def test_later_natural16_candidate_preserves_unconfirmed_identity(self):
        manifest, data = self.legacy_candidate()
        clip = data['clips'][0]
        identity = dict(classification='legacy_later_natural16_candidate',
            doubleEllipseConfirmed=False,
            displayLabel=M.LEGACY_CANDIDATE_IDENTITIES['legacy_later_natural16_candidate'])
        clip['sourceIdentity'] = identity
        path = self.source / clip['approval']['file']
        approval = M.read_json(path)
        approval['sourceIdentity'] = identity
        self.assertFalse(approval['accepted'])
        clip['approval'] = self.put(path, approval)
        self.put(manifest, data)
        result = self.run_import(manifest, 'legacy', execute=True)
        runtime = M.read_json(Path(result['destination']) / 'manifest.json')
        self.assertEqual(runtime['clips'][0]['sourceIdentity'], identity)
        self.assertEqual(runtime['clips'][0]['frameCount'], 16)
        self.assertEqual(runtime['clips'][0]['frameDurationsMs'], [80] * 16)
        self.assertEqual(self.run_import(manifest, 'legacy', verify_output=True)['mode'], 'verified')

    def test_candidate_classification_label_pairs_cannot_be_mixed(self):
        manifest, data = self.legacy_candidate()
        clip = data['clips'][0]
        identities = M.LEGACY_CANDIDATE_IDENTITIES
        for classification, correct_label in identities.items():
            wrong_label = next(label for label in identities.values() if label != correct_label)
            with self.subTest(classification=classification):
                clip['sourceIdentity'] = dict(classification=classification,
                    doubleEllipseConfirmed=False, displayLabel=wrong_label)
                self.put(manifest, data)
                with self.assertRaisesRegex(ValueError, 'source identity'):
                    self.run_import(manifest, 'legacy', execute=True)
                self.assertEqual(list(self.client.iterdir()), [])

    def test_candidate_1024_canvas_is_native_and_proof_never_claims1254(self):
        manifest, _ = self.legacy_candidate(1024)
        result = self.run_import(manifest, 'legacy', execute=True)
        target = Path(result['destination'])
        proof = M.read_json(target / 'import-proof.json')
        self.assertEqual(proof['nativeCanvas'], [1024, 1024])
        self.assertEqual(proof['sourceCanvases'], [{'direction': 'E', 'nativeCanvas': [1024, 1024]}])
        self.assertTrue(all(f['sourceCanvas'] == [1024, 1024] and
            f['transform'] == 'fixed-canvas-1024-to-384-rgba-lanczos-v1' for f in proof['frames']))
        with Image.open(target / 'run/E/01.png') as image:
            self.assertEqual(image.size, (384, 384))

    def test_candidate_missing_or_dishonest_retention_identity_timing_is_rejected(self):
        manifest, data = self.legacy_candidate()
        clip = data['clips'][0]
        approval_path = self.source / clip['approval']['file']
        original = M.read_json(approval_path)
        for field, value in [('identityConfirmed', True), ('originalTiming', False),
                             ('originalOrder', None), ('sourceRetentionVerified', False),
                             ('characterId', '02_fire_talisman_boy'), ('direction', 'N'),
                             ('variant', 'current'), ('orderedSourceHashes', []),
                             ('sourceIdentity', {'doubleEllipseConfirmed': True})]:
            with self.subTest(field=field):
                approval = {**original, field: value}
                clip['approval'] = self.put(approval_path, approval)
                self.put(manifest, data)
                with self.assertRaises(ValueError):
                    self.run_import(manifest, 'legacy', execute=True)
                self.assertEqual(list(self.client.iterdir()), [])

    def test_candidate_cannot_forge_confirmed_classification_or_label(self):
        manifest, data = self.legacy_candidate()
        original = dict(data['clips'][0]['sourceIdentity'])
        for field, value in [('classification', 'legacy_double_ellipse'),
                             ('doubleEllipseConfirmed', True), ('displayLabel', '双椭圆已确认')]:
            with self.subTest(field=field):
                data['clips'][0]['sourceIdentity'] = {**original, field: value}
                self.put(manifest, data)
                with self.assertRaisesRegex(ValueError, 'source identity'):
                    self.run_import(manifest, 'legacy')

    def test_candidate_cannot_lower_current_or_confirmed_legacy_approval_gate(self):
        manifest, data = self.legacy_candidate()
        data['variant'] = 'current'
        clip = data['clips'][0]
        path = self.source / clip['approval']['file']
        approval = M.read_json(path)
        approval['variant'] = 'current'
        clip['approval'] = self.put(path, approval)
        self.put(manifest, data)
        with self.assertRaisesRegex(ValueError, 'source identity'):
            self.run_import(manifest, 'current')
        data['variant'] = approval['variant'] = 'legacy'
        del clip['sourceIdentity']
        clip['approval'] = self.put(path, approval)
        self.put(manifest, data)
        with self.assertRaisesRegex(ValueError, 'Independent approval'):
            self.run_import(manifest, 'legacy')

    def test_candidate_requires_correct_explicit_uniform_canvas(self):
        manifest, data = self.legacy_candidate(1024)
        clip = data['clips'][0]
        for canvas in [None, [2048, 2048], [1024, 1254], [1024.0, 1024.0], [1254, 1254]]:
            with self.subTest(canvas=canvas):
                clip['nativeCanvas'] = canvas
                self.put(manifest, data)
                with self.assertRaises(ValueError):
                    self.run_import(manifest, 'legacy', execute=True)
                self.assertEqual(list(self.client.iterdir()), [])

    def test_existing_paths_emit_no_candidate_metadata_and_keep_fingerprint_contract(self):
        for variant in ['legacy', 'current']:
            manifest, _ = self.delivery(variant)
            plan = M.preflight(manifest, CHARACTER, variant, self.client)
            proof = json.loads(plan.files[Path('import-proof.json')])
            self.assertNotIn('sourceIdentity', plan.runtime['clips'][0])
            self.assertNotIn('sourceCanvas', plan.runtime['clips'][0])
            self.assertNotIn('sourceCanvases', proof)
            self.assertEqual(proof['nativeCanvas'], [1254, 1254])
            self.assertTrue(all('sourceCanvas' not in f for f in proof['frames']))
            expected = M.digest(M.encoded({'character': CHARACTER, 'variant': variant,
                'manifestSha256': M.digest(manifest.read_bytes()), 'frames': proof['frames'],
                'transform': 'fixed-canvas-1254-to-384-rgba-lanczos-v1'}))
            self.assertEqual(plan.fingerprint, expected)


if __name__ == '__main__':
    unittest.main()
