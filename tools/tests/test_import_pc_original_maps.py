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
import import_qdao_static_map as qdao_importer


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

    def test_qdao_dual_festival_adoption_recomposition_and_tamper_rejection(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            handoff = root / "handoff"
            project = root / "client"
            delivery = root / "delivery_28002"
            handoff.mkdir()
            delivery.mkdir()
            (project / "Assets/Resources/World").mkdir(parents=True)

            # Seed 36 synthetic PC original maps including 28002 (3000x1800 RGB -> upgraded to RGBA dual-festival)
            ready_ids = ["28002"] + [f"{10000 + i:05d}" for i in range(35)]
            unavail_ids = ["29005", "11005", "41049", "50024"]
            requested = ready_ids + unavail_ids

            maps_sel = []
            for idx, map_id in enumerate(ready_ids):
                mdir = handoff / map_id
                mdir.mkdir()
                w, h = (3000, 1800) if map_id == "28002" else (600, 400)
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
                    "name": f"地图 {map_id}",
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
                            "native": {"path": "x", "sha256": "a" * 64, "bytes": 100, "pixels": [8000, 8000]},
                        },
                        {
                            "mapId": "50024",
                            "branch": "gbits",
                            "sourceStatus": "viewport_unconfirmed",
                            "native": {"path": "y", "sha256": "b" * 64, "bytes": 200, "pixels": [4000, 3000]},
                        },
                    ],
                }),
                encoding="utf-8",
            )
            importer.import_all(handoff, project)

            # Build synthetic Q-Dao spring + midautumn RGBA PNGs with partial alpha [0, 254]
            appearances_doc = {}
            for app_id, color in [("spring", (200, 40, 40, 254)), ("midautumn", (230, 190, 60, 254))]:
                adir = delivery / f"{app_id}-v002"
                adir.mkdir()
                im = Image.new("RGBA", (3000, 1800), color)
                # Transparent corner (alpha=0) and partial edge (alpha=128)
                for y in range(20):
                    for x in range(20):
                        im.putpixel((x, y), (0, 0, 0, 0))
                im.putpixel((25, 25), (100, 120, 140, 128))
                whole = adir / "28002-continuous.png"
                im.save(whole, format="PNG")
                prov = adir / "provenance.json"
                prov.write_text(json.dumps({"appearance": app_id}), encoding="utf-8")
                sidx = adir / "28002-slice-index.json"
                sidx.write_text(json.dumps({"slices": 48}), encoding="utf-8")
                pkg = adir / "28002-continuous-and-slices.zip"
                pkg.write_bytes(f"zip-{app_id}".encode("utf-8"))
                appearances_doc[app_id] = {
                    "whole": {"path": whole.as_posix(), "sha256": sha256_bytes(whole.read_bytes())},
                    "provenance": {"path": prov.as_posix(), "sha256": sha256_bytes(prov.read_bytes())},
                    "sliceIndex": {"path": sidx.as_posix(), "sha256": sha256_bytes(sidx.read_bytes())},
                    "package": {"path": pkg.as_posix(), "sha256": sha256_bytes(pkg.read_bytes())},
                }

            (delivery / "delivery.json").write_text(
                json.dumps({
                    "schemaVersion": 1,
                    "mapId": "28002",
                    "staticBackgroundOnly": True,
                    "appearances": appearances_doc,
                }),
                encoding="utf-8",
            )

            qdao_receipt = qdao_importer.import_qdao_dual_festival_map(
                map_id="28002",
                delivery_dir=delivery,
                project_root=project,
                appearance_revisions=[("spring", "qdao-spring-v002"), ("midautumn", "qdao-midautumn-v002")],
            )
            self.assertEqual(len(qdao_receipt["appearances"]), 2)
            for app in qdao_receipt["appearances"]:
                self.assertTrue(app["recompositionVerification"]["rgbaByteEqualToWholeSource"])
                self.assertEqual(app["alphaStats"]["minMax"], [0, 254])
                self.assertEqual(app["alphaStats"]["fullyOpaque"], 0)
                self.assertEqual(app["tileCount"], 6)

            summary = importer.verify_committed(project)
            self.assertEqual(summary["result"], "PASS")
            self.assertEqual(summary["readyMaps"], 36)
            self.assertEqual(summary["checkedOverviews"], 37)

            # Tamper midautumn tile -> verify_committed must reject it
            tampered_mid = project / importer.DELIVERY_REL / "28002/qdao-midautumn-v002/tiles/r02_c03.png"
            tampered_mid.write_bytes(tampered_mid.read_bytes() + b"\x00")
            with self.assertRaises(ValueError):
                importer.verify_committed(project)

    def test_hd_worldgrid_candidate_preserves_original_default_and_world_extent(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            handoff = root / "handoff"
            project = root / "client"
            hd_dir = root / "hd_08001"
            handoff.mkdir()
            hd_dir.mkdir()
            (project / "Assets/Resources/World").mkdir(parents=True)

            ready_ids = ["08001"] + [f"{10000 + i:05d}" for i in range(35)]
            unavail_ids = ["29005", "11005", "41049", "50024"]
            requested = ready_ids + unavail_ids
            maps_sel = []
            for idx, map_id in enumerate(ready_ids):
                mdir = handoff / map_id
                mdir.mkdir()
                w, h = (800, 600) if map_id == "08001" else (400, 300)
                im = Image.new("RGB", (w, h), (30 + idx, 70, 110))
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
                    "name": f"地图 {map_id}",
                    "native": {"path": png_path.as_posix(), "sha256": sha, "bytes": len(raw), "pixels": [w, h]},
                    "sourceContainer": "client.dbfs",
                    "sourceManifest": ev_path.as_posix(),
                })
            (handoff / "selection.json").write_text(json.dumps({
                "schemaVersion": 1, "kind": "LOCAL_PC_ORIGINAL_STATIC_BACK_SELECTION",
                "sourceContainerEvidence": {"path": "client.dbfs", "bytes": 4626188288, "sha256": importer.EXPECTED_CONTAINER_SHA256},
                "requestedMapIds": requested, "maps": maps_sel,
            }), encoding="utf-8")
            (handoff / "unavailable.json").write_text(json.dumps({
                "schemaVersion": 1, "kind": "LOCAL_PC_ORIGINAL_STATIC_BACK_UNAVAILABLE", "requestedMapIds": requested,
                "maps": [{"mapId": gid, "reason": {"code": "NO_STATIC_BACKGROUND_BRANCH", "infoHeaderCount": 1, "variants": []}} for gid in unavail_ids],
            }), encoding="utf-8")
            (handoff / "special-candidates.json").write_text(json.dumps({
                "schemaVersion": 1, "kind": "LOCAL_PC_ORIGINAL_STATIC_BACK_NONDEFAULT_CANDIDATES", "requestedMapIds": requested,
                "maps": [
                    {"mapId": "15000", "branch": "gbits_jdhj", "sourceStatus": "source_valid_visual_hold", "native": {"path": "x", "sha256": "a" * 64, "bytes": 100, "pixels": [8000, 8000]}},
                    {"mapId": "50024", "branch": "gbits", "sourceStatus": "viewport_unconfirmed", "native": {"path": "y", "sha256": "b" * 64, "bytes": 200, "pixels": [4000, 3000]}},
                ],
            }), encoding="utf-8")
            importer.import_all(handoff, project)

            # Build 2x HD (1600x1200) RGBA spring with 2x2 precut 800x600 tiles mapped to 800x600 worldGrid
            hd_img = Image.new("RGBA", (1600, 1200), (180, 60, 50, 254))
            hd_img.putpixel((0, 0), (0, 0, 0, 0))
            whole = hd_dir / "08001-spring-1600x1200.png"
            hd_img.save(whole, format="PNG")
            ev = hd_dir / "delivery.json"
            ev.write_text(json.dumps({"season": "spring"}), encoding="utf-8")
            precut = []
            for r in range(2):
                for c in range(2):
                    x0, y0 = c * 800, r * 600
                    tp = hd_dir / f"{x0}_{y0}.png"
                    hd_img.crop((x0, y0, x0 + 800, y0 + 600)).save(tp, format="PNG")
                    precut.append({"path": tp, "pixelOrigin": [x0, y0], "sha256": sha256_bytes(tp.read_bytes())})

            rec = qdao_importer.adopt_map_appearances(
                map_id="08001",
                project_root=project,
                qdao_specs=[{
                    "id": "spring",
                    "displayName": "春节高清",
                    "revision": "qdao-spring-4x-v001",
                    "wholePng": whole,
                    "expectedSha": sha256_bytes(whole.read_bytes()),
                    "evidencePath": ev,
                    "worldGrid": {"widthPixels": 800, "heightPixels": 600, "tileWidthPixels": 400, "tileHeightPixels": 300},
                    "precutTiles": precut,
                }],
                default_appearance="normal",
                include_preserved_original=True,
                original_first=True,
            )
            self.assertEqual(rec["defaultAppearance"], "normal")
            summary = importer.verify_committed(project)
            self.assertEqual(summary["result"], "PASS")
            cat = importer.read_json(project / importer.DELIVERY_REL / "catalog.json")
            m08001 = next(m for m in cat["maps"] if m["mapId"] == "08001")
            self.assertEqual(m08001["artStage"], "pc_original_static_back")
            self.assertEqual(m08001["defaultAppearance"], "normal")
            spring_app = next(a for a in m08001["appearances"] if a["id"] == "spring")
            self.assertEqual((spring_app["nativeWidth"], spring_app["nativeHeight"]), (1600, 1200))
            self.assertEqual(spring_app["tileCount"], 4)

    def test_opaque_lossless_png_worldgrid_slicing_16001_shape(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            handoff = root / "handoff"
            project = root / "client"
            hd_dir = root / "hd_16001"
            handoff.mkdir()
            hd_dir.mkdir()
            (project / "Assets/Resources/World").mkdir(parents=True)

            ready_ids = ["16001"] + [f"{10000 + i:05d}" for i in range(35)]
            unavail_ids = ["29005", "11005", "41049", "50024"]
            requested = ready_ids + unavail_ids
            maps_sel = []
            for idx, map_id in enumerate(ready_ids):
                mdir = handoff / map_id
                mdir.mkdir()
                w, h = (1920, 1286) if map_id == "16001" else (400, 300)
                im = Image.new("RGB", (w, h), (40 + idx, 70, 110))
                png_path = mdir / f"{map_id}-native.png"
                im.save(png_path, format="PNG")
                raw = png_path.read_bytes()
                sha = sha256_bytes(raw)
                ev_path = mdir / "source-evidence.json"
                ev_path.write_text(
                    json.dumps({
                        "schemaVersion": 1, "mapId": map_id, "branch": "gbits",
                        "native": {"path": png_path.as_posix(), "sha256": sha, "bytes": len(raw), "pixels": [w, h]},
                        "sourceContainer": {"sha256": importer.EXPECTED_CONTAINER_SHA256},
                    }),
                    encoding="utf-8",
                )
                maps_sel.append({
                    "mapId": map_id, "name": f"地图 {map_id}",
                    "native": {"path": png_path.as_posix(), "sha256": sha, "bytes": len(raw), "pixels": [w, h]},
                    "sourceContainer": "client.dbfs", "sourceManifest": ev_path.as_posix(),
                })
            (handoff / "selection.json").write_text(json.dumps({
                "schemaVersion": 1, "kind": "LOCAL_PC_ORIGINAL_STATIC_BACK_SELECTION",
                "sourceContainerEvidence": {"path": "client.dbfs", "bytes": 4626188288, "sha256": importer.EXPECTED_CONTAINER_SHA256},
                "requestedMapIds": requested, "maps": maps_sel,
            }), encoding="utf-8")
            (handoff / "unavailable.json").write_text(json.dumps({
                "schemaVersion": 1, "kind": "LOCAL_PC_ORIGINAL_STATIC_BACK_UNAVAILABLE", "requestedMapIds": requested,
                "maps": [{"mapId": gid, "reason": {"code": "NO_STATIC_BACKGROUND_BRANCH", "infoHeaderCount": 1, "variants": []}} for gid in unavail_ids],
            }), encoding="utf-8")
            (handoff / "special-candidates.json").write_text(json.dumps({
                "schemaVersion": 1, "kind": "LOCAL_PC_ORIGINAL_STATIC_BACK_NONDEFAULT_CANDIDATES", "requestedMapIds": requested,
                "maps": [
                    {"mapId": "15000", "branch": "gbits_jdhj", "sourceStatus": "source_valid_visual_hold", "native": {"path": "x", "sha256": "a" * 64, "bytes": 100, "pixels": [8000, 8000]}},
                    {"mapId": "50024", "branch": "gbits", "sourceStatus": "viewport_unconfirmed", "native": {"path": "y", "sha256": "b" * 64, "bytes": 200, "pixels": [4000, 3000]}},
                ],
            }), encoding="utf-8")
            importer.import_all(handoff, project)

            # Synthetic 3840x2572 opaque RGB PNG sliced directly onto 16001 400x300 worldGrid (5x5=25 lossless RGB PNG tiles)
            hd_img = Image.new("RGB", (3840, 2572), (120, 140, 160))
            hd_img.putpixel((3839, 2571), (250, 10, 20))
            whole = hd_dir / "16001-spring-3840x2572.png"
            hd_img.save(whole, format="PNG")
            ev = hd_dir / "delivery.json"
            ev.write_text(json.dumps({"mapId": "16001", "season": "spring"}), encoding="utf-8")

            rec = qdao_importer.adopt_map_appearances(
                map_id="16001",
                project_root=project,
                qdao_specs=[{
                    "id": "spring",
                    "displayName": "春节高清",
                    "revision": "qdao-spring-4x-v001",
                    "wholePng": whole,
                    "expectedSha": sha256_bytes(whole.read_bytes()),
                    "evidencePath": ev,
                    "worldGrid": {"widthPixels": 1920, "heightPixels": 1286, "tileWidthPixels": 400, "tileHeightPixels": 300},
                    "losslessPng": True,
                }],
                default_appearance="normal",
                include_preserved_original=True,
                original_first=True,
            )
            spring_rec = next(a for a in rec["appearances"] if a["id"] == "spring")
            self.assertFalse(spring_rec["hasAlpha"])
            self.assertEqual(spring_rec["tileFormat"], "png_rgb_hd_worldgrid_800x600")
            self.assertEqual(spring_rec["tileCount"], 25)
            self.assertTrue(spring_rec["recompositionVerification"]["rgbByteEqualToWholeSource"])

            tm = importer.read_json(project / importer.DELIVERY_REL / "16001/qdao-spring-4x-v001/tiles.json")
            self.assertEqual(tm["schemaVersion"], 2)
            self.assertEqual((tm["tiles"][0]["width"], tm["tiles"][0]["height"]), (800, 600))
            self.assertEqual((tm["tiles"][4]["width"], tm["tiles"][4]["height"]), (640, 600))
            self.assertEqual((tm["tiles"][20]["width"], tm["tiles"][20]["height"]), (800, 172))
            self.assertEqual((tm["tiles"][24]["width"], tm["tiles"][24]["height"]), (640, 172))
            meta_txt = (project / importer.DELIVERY_REL / "16001/qdao-spring-4x-v001/tiles/r05_c05.png.meta").read_text(encoding="utf-8")
            self.assertIn("textureFormat: 3", meta_txt)
            self.assertIn("alphaSource: 0", meta_txt)
            self.assertEqual(importer.verify_committed(project)["result"], "PASS")


if __name__ == "__main__":
    unittest.main()
