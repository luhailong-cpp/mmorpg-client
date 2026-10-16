"""Use real Git checkouts and archives to protect byte-signed character contracts."""
import hashlib
import importlib.util
import io
import json
from pathlib import Path
import shutil
import subprocess
import tarfile
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location('checkout_verifier', ROOT / 'tools/verify_character_checkout.py')
CHECKER = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(CHECKER)


def encode(value, crlf=False):
    data = (json.dumps(value, indent=2) + '\n').encode()
    return data.replace(b'\n', b'\r\n') if crlf else data


class CharacterCheckoutTests(unittest.TestCase):
    def fixture(self, root, version):
        identity = '00_reference_topright_boy' if version == 13 else '07_moon_shadow_assassin_girl'
        folder = root / CHECKER.FAMILY / f'QdaoOriginalRosterV{version}' / identity
        folder.mkdir(parents=True)
        manifest = encode(dict(character_id=identity, version=version), version == 13)
        validation = encode(dict(status='passed'), version == 13)
        activation = encode(dict(characterId=identity, version=version,
                                 manifest_sha256=hashlib.sha256(manifest).hexdigest(),
                                 validation_sha256=hashlib.sha256(validation).hexdigest()))
        expected = {'manifest.json': manifest, 'validation.json': validation, 'appearance.json': activation}
        for name, data in expected.items():
            (folder / name).write_bytes(data)
        pinned = None if version == 13 else (CHECKER.digest(manifest), CHECKER.digest(activation))
        return folder, identity, expected, pinned

    def test_strict_contracts_reject_newline_or_content_changes(self):
        with tempfile.TemporaryDirectory() as temp:
            for version in (13, 14):
                folder, identity, expected, pinned = self.fixture(Path(temp), version)
                self.assertEqual([], CHECKER.verify_contract(folder, identity, version, pinned)[0])
                manifest = expected['manifest.json']
                changed = manifest.replace(b'\r\n', b'\n') if version == 13 else manifest.replace(b'\n', b'\r\n')
                (folder / 'manifest.json').write_bytes(changed)
                self.assertTrue(CHECKER.verify_contract(folder, identity, version, pinned)[0])
                (folder / 'manifest.json').write_bytes(manifest.replace(b'"version":', b'"changed":'))
                self.assertTrue(CHECKER.verify_contract(folder, identity, version, pinned)[0])
                (folder / 'manifest.json').write_bytes(manifest)
                (folder / 'validation.json').write_bytes(expected['validation.json'] + b' ')
                self.assertTrue(CHECKER.verify_contract(folder, identity, version, pinned)[0])

    @unittest.skipUnless(shutil.which('git'), 'Git is required to verify real checkout behavior')
    def test_git_autocrlf_modes_and_archive_preserve_signed_bytes(self):
        with tempfile.TemporaryDirectory() as temp:
            base = Path(temp)
            source = base / 'source'
            source.mkdir()
            (source / '.gitattributes').write_bytes((ROOT / '.gitattributes').read_bytes())
            fixtures = [self.fixture(source, v) for v in (13, 14)]
            archive_path = CHECKER.FAMILY / 'QdaoArchivedActions20261005/00_reference_topright_boy/manifest.json'
            (source / archive_path).parent.mkdir(parents=True)
            (source / archive_path).write_bytes(b'{"clips": []}\n')
            def git(*args):
                return subprocess.check_output(['git', '-C', str(source), *args], stderr=subprocess.PIPE)
            git('init', '-q')
            git('config', 'core.autocrlf', 'true')
            git('add', '.')
            git('-c', 'user.name=Checkout Test', '-c', 'user.email=checkout-test@example.invalid',
                'commit', '-qm', 'Byte contract fixtures')
            expected_files = {str((folder / name).relative_to(source)).replace('\\', '/'): data
                              for folder, _, expected, _ in fixtures for name, data in expected.items()}
            expected_files[archive_path.as_posix()] = b'{"clips": []}\n'
            for mode in ('true', 'false', 'input'):
                with self.subTest(autocrlf=mode):
                    target = base / ('clone-' + mode)
                    subprocess.check_call(['git', '-c', 'core.autocrlf=' + mode, 'clone', '-q',
                                           '--no-hardlinks', str(source), str(target)],
                                          stdout=subprocess.DEVNULL, stderr=subprocess.PIPE)
                    for relative, data in expected_files.items():
                        self.assertEqual(data, (target / relative).read_bytes(), (mode, relative))
                    for folder, identity, _, pinned in fixtures:
                        version = 13 if identity.startswith('00_') else 14
                        self.assertEqual([], CHECKER.verify_contract(target / folder.relative_to(source),
                                                                    identity, version, pinned)[0])
            with tarfile.open(fileobj=io.BytesIO(git('archive', '--format=tar', 'HEAD')), mode='r:') as archive:
                for relative, data in expected_files.items():
                    self.assertEqual(data, archive.extractfile(relative).read(), relative)


if __name__ == '__main__':
    unittest.main()
