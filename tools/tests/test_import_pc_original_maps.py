"""Unit tests for PC original static background importer and offline verifier."""

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

import import_pc_original_maps as importer


def sha256_bytes(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


class ImportPcOriginalMapsTests(unittest.TestCase):
    def test_overview_size_preserves_aspect_and_respects_cap(self) -> None:
        self.assertEqual(importer.compute_overview_size(1920, 1080), (1920, 1080))
        self.assertEqual(importer.compute_overview_size(10000, 6774), (2048, 1387))
        self.assertEqual(importer.compute_overview_size(8000, 8945), (1832, 2048))
        w, h = importer.compute_overview_size(3634, 4000)
        self.assertEqual(h, 2048)
        self.assertAlmostEqual(w / h, 3634 / 4000, places=3)

    def test_rgba_alpha_detection(self) -> None:
        opaque_rgb = Image.new("RGB", (16, 16), (120, 80, 40))
        self.assertFalse(importer.image_has_non_opaque_alpha(opaque_rgb))

        opaque_rgba = Image.new("RGBA", (16, 16), (120, 80, 40, 255))
        self.assertFalse(importer.image_has_non_opaque_alpha(opaque_rgba))

        transparent_rgba = Image.new("RGBA", (16, 16), (120, 80, 40, 255))
        transparent_rgba.putpixel((0, 0), (0, 0, 0, 0))
        self.assertTrue(importer.image_has_non_opaque_alpha(transparent_rgba))

    def test_synthetic_import_and_tamper_rejection(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            handoff = root / "handoff"
            project = root / "client"
            handoff.mkdir()
            (project / "Assets/Resources/World").mkdir(parents=True)

            # Build 40 synthetic IDs: 36 ready (2 distinct sizes including 1 RGBA) + 4 unavailable + 2 HOLD
            ready_ids = [f"{10000 + i:05d}" for i in range(36)]
            unavail_ids = ["29005", "11005", "41049", "50024"]
            requested = ready_ids + unavail_ids

            maps_sel = []
            for idx, map_id in enumerate(ready_ids):
                mdir = handoff / map_id
                mdir.mkdir()
                is_rgba = idx == 0
                w, h = (1500, 1200) if idx % 2 == 0 else (900, 700)
                if is_rgba:
                    im = Image.new("RGBA", (w, h), (40, 90, 130, 255))
                    im.putpixel((10, 10), (0, 0, 0, 0))
                else:
                    im = Image.new("RGB", (w, h), (30 + idx, 80, 120))
                png_path = mdir / f"{map_id}-native.png"
                im.save(png_path, format="PNG")
                raw = png_path.read_bytes()
                sha = sha256_bytes(raw)
                ev_path = mdir / "source-evidence.json"
                ev_path.write_text(
                    json.dumps({
                        "schemaVersion": 1,
                        "mapId": map_id,
                        "branch": "gbits",
                        "native": {"path": png_path.as_posix(), "sha256": sha, "bytes": len(raw), "pixels": [w, h]},
                        "sourceContainer": {"sha256": importer.EXPECTED_CONTAINER_SHA256},
                    }),
                    encoding="utf-8",
                )
                maps_sel.append({
                    "mapId": map_id,
                    "name": f"测试图 {map_id}",
                    "native": {"path": png_path.as_posix(), "sha256": sha, "bytes": len(raw), "pixels": [w, h]},
                    "sourceContainer": "client.dbfs",
                    "sourceManifest": ev_path.as_posix(),
                })

            (handoff / "selection.json").write_text(
                json.dumps({
                    "schemaVersion": 1,
                    "kind": "LOCAL_PC_ORIGINAL_STATIC_BACK_SELECTION",
                    "sourceContainerEvidence": {
                        "path": "client.dbfs",
                        "bytes": 4626188288,
                        "sha256": importer.EXPECTED_CONTAINER_SHA256,
                    },
                    "requestedMapIds": requested,
                    "maps": maps_sel,
                }),
                encoding="utf-8",
            )
            (handoff / "unavailable.json").write_text(
                json.dumps({
                    "schemaVersion": 1,
                    "kind": "LOCAL_PC_ORIGINAL_STATIC_BACK_UNAVAILABLE",
                    "requestedMapIds": requested,
                    "maps": [
                        {"mapId": gid, "reason": {"code": "NO_STATIC_BACKGROUND_BRANCH", "infoHeaderCount": 1, "variants": []}}
                        for gid in unavail_ids
                    ],
                }),
                encoding="utf-8",
            )
            (handoff / "special-candidates.json").write_text(
                json.dumps({
                    "schemaVersion": 1,
                    "kind": "LOCAL_PC_ORIGINAL_STATIC_BACK_NONDEFAULT_CANDIDATES",
                    "requestedMapIds": requested,
                    "maps": [
                        {
                            "mapId": "15000",
                            "branch": "gbits_jdhj",
                            "sourceStatus": "source_valid_visual_hold",
                            "defaultBranchUnconfirmed": True,
                            "native": {"path": "x", "sha256": "a" * 64, "bytes": 100, "pixels": [8000, 8000]},
                        },
                        {
                            "mapId": "50024",
                            "branch": "gbits",
                            "sourceStatus": "viewport_unconfirmed",
                            "gameMAPViewportConfirmed": False,
                            "native": {"path": "y", "sha256": "b" * 64, "bytes": 200, "pixels": [4000, 3000]},
                        },
                    ],
                }),
                encoding="utf-8",
            )

            receipt = importer.import_all(handoff, project)
            self.assertEqual(receipt["readyMapCount"], 36)
            self.assertEqual(receipt["unavailableMapCount"], 4)
            self.assertEqual(receipt["specialHoldCandidateCount"], 2)

            summary = importer.verify_committed(project)
            self.assertEqual(summary["result"], "PASS")
            self.assertEqual(summary["readyMaps"], 36)

            # RGBA map (index 0, 1500x1200) must produce 2x2 PNG tiles with exact remainder sizes
            rgba_tiles = importer.read_json(
                project / importer.DELIVERY_REL / ready_ids[0] / importer.REVISION / "tiles.json"
            )
            self.assertTrue(rgba_tiles["hasAlpha"])
            self.assertEqual(len(rgba_tiles["tiles"]), 4)
            self.assertEqual(rgba_tiles["tiles"][0]["fileExtension"], ".png")
            self.assertEqual((rgba_tiles["tiles"][3]["width"], rgba_tiles["tiles"][3]["height"]), (476, 176))

            # RGB map (index 1, 900x700) must produce 1x1 JPG tile
            rgb_tiles = importer.read_json(
                project / importer.DELIVERY_REL / ready_ids[1] / importer.REVISION / "tiles.json"
            )
            self.assertFalse(rgb_tiles["hasAlpha"])
            self.assertEqual(len(rgb_tiles["tiles"]), 1)
            self.assertEqual(rgb_tiles["tiles"][0]["fileExtension"], ".jpg")

            # Tamper a tile byte -> verify must fail
            tampered = project / importer.DELIVERY_REL / ready_ids[1] / importer.REVISION / "tiles/r01_c01.jpg"
            tampered.write_bytes(tampered.read_bytes() + b"\x00")
            with self.assertRaises(ValueError):
                importer.verify_committed(project)


if __name__ == "__main__":
    unittest.main()
