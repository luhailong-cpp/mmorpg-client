"""Unit tests for original map dual-festival preview importer and verifier."""

from __future__ import annotations

import hashlib
import json
from pathlib import Path
import sys
import tempfile
import unittest

from PIL import Image

TOOLS_DIR = Path(__file__).resolve().parents[1]
if str(TOOLS_DIR) not in sys.path:
    sys.path.insert(0, str(TOOLS_DIR))

import import_original_map_preview as importer


def sha256_bytes(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def build_synthetic_delivery(source_dir: Path) -> None:
    (source_dir / "art").mkdir(parents=True)
    (source_dir / "records").mkdir(parents=True)
    (source_dir / "tiles" / "spring").mkdir(parents=True)
    (source_dir / "tiles" / "midautumn").mkdir(parents=True)

    (source_dir / "README.md").write_text("# 桂灯集测试交付\n", encoding="utf-8")

    neutral = Image.new("RGB", (4096, 2731), (100, 110, 120))
    spring = neutral.copy()
    mid = neutral.copy()
    # Edit east storefront rectangles across rows 1-3, cols 3-4 so all 6 east tiles differ.
    for x in (2500, 3200):
        for y in (700, 1500, 2060):
            spring.putpixel((x, y), (220, 40, 40))
            mid.putpixel((x, y), (235, 195, 65))

    def save_png(rel: str, img: Image.Image) -> tuple[str, int]:
        path = source_dir / rel
        img.save(path, format="PNG")
        raw = path.read_bytes()
        return sha256_bytes(raw), len(raw)

    def write_gen(rel: str, target_rel: str, target_sha: str, route: str, calls: int) -> str:
        raw = json.dumps({
            "schemaVersion": 1,
            "mapId": "original:guideng-market",
            "file": target_rel,
            "sha256": target_sha,
            "width": 4096,
            "height": 2731,
            "route": route,
            "physicalCalls": calls,
            "actualModel": None,
            "actualQuality": None,
        }, indent=2).encode("utf-8")
        (source_dir / rel).write_bytes(raw)
        return sha256_bytes(raw)

    neutral_sha, neutral_bytes = save_png("art/neutral.png", neutral)
    neutral_gen_sha = write_gen("art/neutral.png.generation.json", "art/neutral.png", neutral_sha, "derived", 0)

    acceptance_raw = json.dumps({"schemaVersion": 1, "mapId": "original:guideng-market"}).encode("utf-8")
    (source_dir / "records/art-acceptance.json").write_bytes(acceptance_raw)
    native_gen_sha = write_gen("records/spring-east-native.png.generation.json", "records/spring-east-native.png", "a" * 64, "host-builtin", 1)

    appearances = {}
    for app_id, whole_img in (("spring", spring), ("midautumn", mid)):
        whole_rel = f"art/{app_id}.png"
        whole_sha, whole_bytes = save_png(whole_rel, whole_img)
        whole_gen_rel = f"art/{app_id}.png.generation.json"
        whole_gen_sha = write_gen(whole_gen_rel, whole_rel, whole_sha, "derived", 0)
        tiles = []
        for row in range(1, 4):
            y = (row - 1) * 1024
            h = 683 if row == 3 else 1024
            for col in range(1, 5):
                x = (col - 1) * 1024
                w = 1024
                tile_rel = f"tiles/{app_id}/r{row:02d}_c{col:02d}.png"
                t_sha, t_bytes = save_png(tile_rel, whole_img.crop((x, y, x + w, y + h)))
                tiles.append({
                    "file": tile_rel,
                    "sha256": t_sha,
                    "bytes": t_bytes,
                    "row": row,
                    "column": col,
                    "x": x,
                    "y": y,
                    "width": w,
                    "height": h,
                    "source": whole_rel,
                    "sourceSha256": whole_sha,
                })
        appearances[app_id] = {
            "source": {
                "file": whole_rel,
                "sha256": whole_sha,
                "bytes": whole_bytes,
                "width": 4096,
                "height": 2731,
                "opaque": True,
                "generationRecord": whole_gen_rel,
                "generationRecordSha256": whole_gen_sha,
            },
            "tileCount": 12,
            "tiles": tiles,
            "verification": {
                "fullCoverage": True,
                "recomposedRgbEqual": True,
                "editedRects": [[2452, 572, 3228, 1058], [2487, 1453, 3985, 2108]],
                "outsideEditedRectsRgbEqual": True,
            },
        }

    manifest = {
        "schemaVersion": 1,
        "mapId": "original:guideng-market",
        "numericMapId": None,
        "numericMapIdStatus": "not_assigned",
        "acceptanceScope": "map_art_and_pixel_coordinates",
        "canvas": {"width": 4096, "height": 2731, "coordinateConvention": "top-left, half-open pixels"},
        "tileGrid": {
            "maxTileWidth": 1024,
            "maxTileHeight": 1024,
            "columns": 4,
            "rows": 3,
            "resampled": False,
            "padded": False,
            "mode": "RGB",
        },
        "sharedNeutral": {
            "file": "art/neutral.png",
            "sha256": neutral_sha,
            "bytes": neutral_bytes,
            "width": 4096,
            "height": 2731,
            "opaque": True,
            "generationRecord": "art/neutral.png.generation.json",
            "generationRecordSha256": neutral_gen_sha,
        },
        "appearances": appearances,
        "additionalAcceptedFiles": [
            {"file": "records/art-acceptance.json", "sha256": sha256_bytes(acceptance_raw)},
            {"file": "records/spring-east-native.png.generation.json", "sha256": native_gen_sha},
        ],
    }
    (source_dir / "manifest.json").write_bytes(importer.json_bytes(manifest))


def seed_legacy_candidate(project_root: Path) -> bytes:
    legacy_root = project_root / importer.DELIVERY_REL / "guideng-market" / importer.LEGACY_PRESERVED_REVISION
    sentinel = b"legacy-candidate-sentinel"
    for app_id in ("spring", "midautumn"):
        app_dir = legacy_root / app_id
        tiles_dir = app_dir / "tiles"
        tiles_dir.mkdir(parents=True)
        (app_dir / "overview.png").write_bytes(sentinel)
        (app_dir / "source.json").write_bytes(b"{}\n")
        tiles = []
        for row in range(1, 4):
            for col in range(1, 5):
                stem = f"r{row:02d}_c{col:02d}"
                (tiles_dir / f"{stem}.png").write_bytes(sentinel)
                tiles.append({
                    "resourcePath": f"{importer.RESOURCE_ROOT}guideng-market/{importer.LEGACY_PRESERVED_REVISION}/{app_id}/tiles/{stem}",
                    "fileExtension": ".png",
                })
        (app_dir / "tiles.json").write_bytes(importer.json_bytes({"tiles": tiles}))
    return sentinel


class ImportOriginalMapPreviewTests(unittest.TestCase):
    def test_import_verify_preserves_legacy_and_rejects_tamper(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            source = root / "source"
            project = root / "client"
            (project / "Assets/Resources/World").mkdir(parents=True)
            build_synthetic_delivery(source)
            sentinel = seed_legacy_candidate(project)

            receipt = importer.import_guideng(source, project)
            self.assertEqual(receipt["revision"], importer.REVISION)
            self.assertEqual(receipt["artStage"], importer.ART_STAGE_FORMAL)
            self.assertEqual(len(receipt["appearances"]), 2)
            for app in receipt["appearances"]:
                self.assertTrue(app["recompositionVerification"]["rgbByteEqualToWholeSource"])

            importer.verify_offline(project, require_preserved_legacy=True)

            # Preserved legacy files must remain byte-untouched.
            legacy_tile = (
                project / importer.DELIVERY_REL / "guideng-market" / importer.LEGACY_PRESERVED_REVISION
                / "spring/tiles/r03_c04.png"
            )
            self.assertEqual(legacy_tile.read_bytes(), sentinel)

            # Tampering an imported row-3 tile must fail offline verification.
            imported_tile = (
                project / importer.DELIVERY_REL / "guideng-market" / importer.REVISION
                / "midautumn/tiles/r03_c04.png"
            )
            imported_tile.write_bytes(imported_tile.read_bytes() + b"\x00")
            with self.assertRaises(ValueError):
                importer.verify_offline(project, require_preserved_legacy=True)


if __name__ == "__main__":
    unittest.main()
