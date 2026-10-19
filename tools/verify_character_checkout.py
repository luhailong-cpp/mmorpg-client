"""Read-only character delivery checks; no art repository or Unity installation required."""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import re

ROOT = Path(__file__).resolve().parents[1]
FAMILY = Path('Assets/Resources/World/Characters')
DIRECTIONS = ('N', 'NE', 'E', 'SE', 'S', 'SW', 'W', 'NW')


def digest(data):
    return hashlib.sha256(data).hexdigest()


def verify_contract(folder, identity, version, pinned=None):
    """Preserve the runtime's strict raw-byte hashes, including delivered line endings."""
    errors = []
    try:
        activation_bytes = (folder / 'appearance.json').read_bytes()
        manifest_bytes = (folder / 'manifest.json').read_bytes()
        validation_bytes = (folder / 'validation.json').read_bytes()
        activation = json.loads(activation_bytes)
        manifest = json.loads(manifest_bytes)
    except (OSError, ValueError) as exc:
        return [f'{identity}: missing or invalid contract: {exc}'], None
    if activation.get('characterId') != identity or activation.get('version') != version:
        errors.append(f'{identity}: activation identity/version mismatch')
    if manifest.get('character_id') != identity or manifest.get('version') != version:
        errors.append(f'{identity}: manifest identity/version mismatch')
    for name, data in (('manifest', manifest_bytes), ('validation', validation_bytes)):
        if digest(data) != activation.get(name + '_sha256'):
            errors.append(f'{identity}: {name}.json SHA256 mismatch (check exact bytes and line endings)')
    if pinned is not None:
        if digest(manifest_bytes) != pinned[0] or digest(activation_bytes) != pinned[1]:
            errors.append(f'{identity}: pinned development-delivery hash mismatch')
    return errors, manifest


def pinned_deliveries(project):
    source = (project / 'Assets/Scripts/World/QdaoLocalPlaytestContract.cs').read_text(encoding='utf-8-sig')
    found = re.findall(r'case "([^"]+)":\s+manifestHash = "([0-9a-f]{64})";\s+'
                       r'activationHash = "([0-9a-f]{64})";', source)
    pins = {identity: (manifest, activation) for identity, manifest, activation in found}
    if set(pins) != {'07_moon_shadow_assassin_girl', '15_water_dragon_scholar_boy'}:
        raise ValueError('Pinned delivery declarations changed; review the checkout verifier.')
    return pins


def verify(project, hash_images=False, contracts_only=False):
    delivery = json.loads((project / 'tools/character_deliveries/archived_actions_20261005.json')
                          .read_text(encoding='utf-8-sig'))
    pins = pinned_deliveries(project)
    errors = []
    pngs = 0
    strips = 0
    for character in delivery['characters']:
        identity = character['characterId']
        version = 13 if identity[:2] in ('00', '01', '02', '03') else 14
        folder = project / FAMILY / f'QdaoOriginalRosterV{version}' / identity
        issues, manifest = verify_contract(folder, identity, version, pins.get(identity))
        errors.extend(issues)
        if contracts_only or manifest is None:
            continue
        required = {'portrait.png'} | {f'idle/{d}.png' for d in DIRECTIONS} | {
            f'walk/{d}/{frame:02d}.png' for d in DIRECTIONS for frame in range(1, 17)}
        rows = manifest.get('files', [])
        files = {row['path']: row['sha256'] for row in rows}
        # V13 also signs eight legacy strip textures; V14 ships only individual frames.
        legacy_strips = {f'walk/{d}/strip.png' for d in DIRECTIONS} if version == 13 else set()
        expected_inventory = required | legacy_strips
        if len(rows) != len(expected_inventory) or set(files) != expected_inventory:
            errors.append(f'{identity}: baseline inventory does not match its V{version} PNG contract')
            continue
        for relative in sorted(expected_inventory):
            path = folder / relative
            if not path.is_file() or not Path(str(path) + '.meta').is_file():
                errors.append(f'{identity}: missing PNG or metadata: {relative}')
            elif hash_images and digest(path.read_bytes()) != files[relative]:
                errors.append(f'{identity}: PNG SHA256 mismatch: {relative}')
            else:
                if relative in legacy_strips:
                    strips += 1
                else:
                    pngs += 1
        for name in ('appearance.json', 'manifest.json', 'validation.json'):
            if not (folder / (name + '.meta')).is_file():
                errors.append(f'{identity}: missing metadata: {name}.meta')
        if version == 14 and (not (folder / 'runtime-index.asset').is_file() or
                              not (folder / 'runtime-index.asset.meta').is_file()):
            errors.append(f'{identity}: missing HD runtime index or metadata')
    return dict(characters=len(delivery['characters']), baselinePngsChecked=pngs, legacyStripPngsChecked=strips,
                imageHashesChecked=hash_images and not contracts_only,
                contractsOnly=contracts_only, passed=not errors, errors=errors)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--project', type=Path, default=ROOT)
    parser.add_argument('--hash-images', action='store_true', help='Verify 2055 baseline PNGs and 32 legacy V13 strips.')
    parser.add_argument('--contracts-only', action='store_true', help='Check JSON/pinned contracts without images.')
    args = parser.parse_args()
    result = verify(args.project.resolve(), args.hash_images, args.contracts_only)
    print(json.dumps(result, ensure_ascii=False, indent=2))
    raise SystemExit(0 if result['passed'] else 1)


if __name__ == '__main__':
    main()
