"""Integrity regressions for the portable formal festival delivery."""
from __future__ import annotations

import copy
import io
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import unittest

from PIL import Image

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import import_festival_maps as maps


def png(size, color):
    output = io.BytesIO()
    Image.new("RGB", size, color).save(output, format="PNG")
    return output.getvalue()


def descriptor(path, data, pixels=None):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(data)
    result = dict(path=str(path), sha256=maps.digest(data), bytes=len(data))
    if pixels:
        result["pixels"] = pixels
    return result


class FestivalImportTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        # Keep Windows test copies below MAX_PATH without changing host policy.
        cls.temporary = tempfile.TemporaryDirectory(prefix="fm-")
        cls.root = Path(cls.temporary.name).resolve()
        cls.art = cls.root / "art"
        cls.template = cls.root / "template"
        (cls.template / "Assets/Resources/World").mkdir(parents=True)
        selection, assemblies = dict(schemaVersion=1, sets=[]), []
        plan = dict(schemaVersion=1, maps={}, visuallyReviewedFormalAssemblies=assemblies)
        for index, ((map_id, app), (columns, rows)) in enumerate(maps.SCOPE.items()):
            image_width, image_height = columns * 1024, rows * 1024
            if map_id not in plan["maps"]:
                plan["maps"][map_id] = dict(canvasPixels=[image_width, image_height],
                    worldRect=dict(x=0, y=0, width=columns * 2, height=rows * 2),
                    imageRect=dict(x=0, y=0, width=image_width, height=image_height),
                    referencePixels=[columns, rows], cellSize=2,
                    spawnPixel=[image_width / 2, image_height / 2],
                    walkablePolygons=[[[0, 0], [image_width, 0], [image_width, image_height], [0, image_height]]],
                    obstaclePolygons=[], expectedConnectedComponents=1)
            item = dict(mapId=map_id, appearance=app, formalArtAccepted=True,
                        grid=dict(columns=columns, rows=rows, canvasPixels=[image_width, image_height]),
                        layoutSource=dict(path="historical-preview.png", sha256="e" * 64),
                        selectionAuthority=dict(path="historical-acceptance.json", sha256="f" * 64), tiles=[])
            overview = Image.new("RGB", (image_width, image_height))
            for row in range(1, rows + 1):
                for column in range(1, columns + 1):
                    tid = maps.tile_id(row, column)
                    folder = cls.art / map_id / app / tid
                    color = (index * 40, row * 50, column * 60)
                    native = descriptor(folder / "native.png", png((1254, 1254), color), [1254, 1254])
                    runtime = descriptor(folder / "runtime.png", png((1024, 1024), color), [1024, 1024])
                    qa_data = maps.json_bytes(dict(formalAccepted=True, native=native))
                    qa = descriptor(folder / "formal.json", qa_data)
                    qa["formalAccepted"] = True
                    item["tiles"].append(dict(tile=tid, row=row, column=column,
                        imageRectHalfOpen=[(column - 1) * 1024, (row - 1) * 1024, column * 1024, row * 1024],
                        native=native, runtime=runtime, formalAcceptance=qa, formalArtAccepted=True))
                    overview.paste(Image.new("RGB", (1024, 1024), color), ((column - 1) * 1024, (row - 1) * 1024))
            encoded = io.BytesIO()
            overview.save(encoded, format="PNG")
            assemblies.append(dict(mapId=map_id, appearance=app, sha256=maps.digest(encoded.getvalue())))
            selection["sets"].append(item)
        cls.selection = cls.root / "selection.json"
        cls.selection.write_bytes(maps.json_bytes(selection))
        cls.plan = cls.root / "navigation-plan.json"
        cls.plan.write_bytes(maps.json_bytes(plan))
        maps.import_maps(cls.template, cls.selection, cls.plan)

    @classmethod
    def tearDownClass(cls):
        cls.temporary.cleanup()

    def setUp(self):
        self.case = self.root / ("c" + str(len(list(self.root.glob("c*")))))
        shutil.copytree(self.template, self.case)

    def folder(self):
        catalog = maps.read_json(self.case / maps.DELIVERY / "catalog.json")
        root = catalog["maps"][0]["appearances"][0]["resourceRoot"]
        return self.case / "Assets/Resources" / root

    def import_additional_batch(self):
        original = maps.read_json(self.selection)
        selected = copy.deepcopy(next(s for s in original["sets"]
                                      if s["mapId"] == "28007" and s["appearance"] == "spring"))
        old_plan = maps.read_json(self.plan)
        pixels = maps.build_overview([(t["row"], t["column"], maps.checked_bytes(t["runtime"]))
                                      for t in selected["tiles"]], 3, 2)
        encoded = io.BytesIO()
        with Image.open(io.BytesIO(pixels)) as image:
            image.save(encoded, format="PNG", compress_level=1)
        reviewed = descriptor(self.case / "reviewed-overview.png", encoded.getvalue(), [3072, 2048])
        selection = dict(schemaVersion=1, sets=[])
        plan = dict(schemaVersion=1, maps={}, visuallyReviewedFormalAssemblies=[])
        scope = []
        for map_id, appearance in (("01001", "spring"), ("19004", "midautumn"), ("23006", "mid-autumn")):
            item = copy.deepcopy(selected)
            item.update(mapId=map_id, appearance=appearance, derivedOverview=reviewed)
            selection["sets"].append(item)
            plan["maps"][map_id] = copy.deepcopy(old_plan["maps"]["28007"])
            plan["visuallyReviewedFormalAssemblies"].append(dict(
                mapId=map_id, appearance=maps.normalized_appearance(appearance), sha256=reviewed["sha256"]))
            scope.append(dict(mapId=map_id, appearance=appearance, columns=3, rows=2))
        config = dict(schemaVersion=1, revision="festival20261107",
                      evidenceDirectory="Docs/VerificationEvidence/festival-maps-20261107", scope=scope)
        selection_path, plan_path = self.case / "new-selection.json", self.case / "new-plan.json"
        selection_path.write_bytes(maps.json_bytes(selection))
        plan_path.write_bytes(maps.json_bytes(plan))
        result = maps.import_maps(self.case, selection_path, plan_path, config)
        return config, result

    def test_additional_batch_preserves_legacy_assets_and_actual_autumn_defaults(self):
        old_catalog = maps.read_json(self.case / maps.DELIVERY / "catalog.json")
        preserved = {p.relative_to(self.case): p.read_bytes()
                     for p in (self.case / maps.DELIVERY).rglob("*")
                     if p.is_file() and p != self.case / maps.DELIVERY / "catalog.json"}
        config, result = self.import_additional_batch()
        self.assertEqual((3, 3, 18), (result["maps"], result["appearanceCount"], result["tileCount"]))
        reviewed = (self.case / "reviewed-overview.png").read_bytes()
        for entry in result["appearances"]:
            registration = next(m for m in maps.read_json(self.case / maps.DELIVERY / "catalog.json")["maps"]
                                if m["key"] == entry["key"])
            self.assertEqual(reviewed, (self.case / "Assets/Resources" / registration["appearances"][0]["resourceRoot"] / "layout.png").read_bytes())
        for relative, data in preserved.items():
            self.assertEqual(data, (self.case / relative).read_bytes(), str(relative))
        catalog = maps.read_json(self.case / maps.DELIVERY / "catalog.json")
        for item in old_catalog["maps"]:
            self.assertEqual(item, next(m for m in catalog["maps"] if m["key"] == item["key"]))
        for key in ("map19004", "map23006"):
            item = next(m for m in catalog["maps"] if m["key"] == key)
            self.assertEqual("mid-autumn", item["defaultAppearance"])
            self.assertEqual(["mid-autumn"], [a["id"] for a in item["appearances"]])
            self.assertTrue(item["clientOnly"])
        first = next(m for m in catalog["maps"] if m["key"] == "map01001")
        self.assertEqual(1001, first["sceneId"])
        self.assertEqual("spring", first["defaultAppearance"])
        self.assertEqual(36, maps.verify(self.case)["tileCount"])
        self.assertEqual(18, maps.verify(self.case, self.case / config["evidenceDirectory"] / "batch.json")["tileCount"])

    def test_additional_batch_verification_is_portable(self):
        config, _ = self.import_additional_batch()
        from unittest.mock import patch
        original = Path.read_bytes
        art = self.art
        def read(path):
            if path.resolve().is_relative_to(art):
                raise AssertionError("Portable new-batch verification opened the art checkout")
            return original(path)
        with patch.object(Path, "read_bytes", read):
            self.assertTrue(maps.verify(self.case, config)["ok"])

    def test_batch_scope_rejects_unsafe_paths_and_ambiguous_identities(self):
        base = dict(schemaVersion=1, revision="festival20261107",
                    evidenceDirectory="Docs/VerificationEvidence/festival-maps-20261107",
                    scope=[dict(mapId="19004", appearance="midautumn", columns=3, rows=2)])
        for directory in ("../outside", "Docs/VerificationEvidence/../outside", "C:/outside"):
            with self.subTest(directory=directory), self.assertRaisesRegex(ValueError, "evidence"):
                maps.resolve_batch(dict(base, evidenceDirectory=directory))
        duplicate = dict(base, scope=base["scope"] + [dict(mapId="19004", appearance="mid-autumn", columns=3, rows=2)])
        with self.assertRaisesRegex(ValueError, "Duplicate batch"):
            maps.resolve_batch(duplicate)
        with self.assertRaisesRegex(ValueError, "exactly the authorized batch"):
            maps.validate_selection(dict(schemaVersion=1, sets=[]), base)

    def test_shared_acceptance_must_bind_this_coordinate_not_only_a_neighbor(self):
        sha = "a" * 64
        document = dict(formalAccepted=True, appearance="midautumn",
                        inputCells=[dict(id="r01_c01", sha256=sha), dict(id="r01_c02", sha256="b" * 64)])
        maps.acceptance_identity(document, sha, "r01_c01", "mid-autumn")
        with self.assertRaisesRegex(ValueError, "another native"):
            maps.acceptance_identity(document, sha, "r01_c02", "mid-autumn")
        with self.assertRaisesRegex(ValueError, "appearance mismatch"):
            maps.acceptance_identity(document, sha, "r01_c01", "spring")

    def test_supplied_overview_cannot_substitute_preview_pixels(self):
        runtime = png((1024, 1024), (1, 2, 3))
        preview = png((1024, 1024), (4, 5, 6))
        item = dict(derivedOverview=descriptor(self.case / "bad-overview.png", preview, [1024, 1024]))
        with self.assertRaisesRegex(ValueError, "differs from actual runtime"):
            maps.import_overview(item, [(1, 1, runtime)], 1, 1)

    def test_catalog_conflicts_are_rejected_before_any_delivery_write(self):
        original = maps.read_json(self.selection)
        item = next(s for s in original["sets"] if s["mapId"] == "21003" and s["appearance"] == "spring")
        for map_id, message in (("21003", "all existing appearances"),
                                ("021003", "different map key")):
            with self.subTest(map_id=map_id):
                selected = copy.deepcopy(item)
                selected["mapId"] = map_id
                selection = self.case / "conflicting-selection.json"
                selection.write_bytes(maps.json_bytes(dict(schemaVersion=1, sets=[selected])))
                config = dict(schemaVersion=1, revision="future",
                              evidenceDirectory="Docs/VerificationEvidence/rejected-batch",
                              scope=[dict(mapId=map_id, appearance="spring", columns=3, rows=3)])
                before = {p.relative_to(self.case): maps.digest(p.read_bytes())
                          for p in self.case.rglob("*") if p.is_file()}
                with self.assertRaisesRegex(ValueError, message):
                    maps.import_maps(self.case, selection, self.case / "unread-navigation.json", config)
                after = {p.relative_to(self.case): maps.digest(p.read_bytes())
                         for p in self.case.rglob("*") if p.is_file()}
                self.assertEqual(before, after)
                self.assertFalse((self.case / config["evidenceDirectory"]).exists())

    def test_portable_verify_does_not_open_historical_art_paths(self):
        # A different machine has only the delivered files. Block all attempts to
        # open the original art tree while leaving the portable copy accessible.
        original = Path.read_bytes
        from unittest.mock import patch
        art = self.art
        def read(path):
            if art == path or art in path.parents:
                raise AssertionError("Portable verify tried to read the source art checkout")
            return original(path)
        with patch.object(Path, "read_bytes", read):
            result = maps.verify(self.case)
        self.assertEqual((3, 5, 36), (result["maps"], result["appearanceCount"], result["tileCount"]))

    def test_modified_runtime_png_is_rejected(self):
        tile = self.folder() / "tiles/r01_c01.png"
        tile.write_bytes(png((1024, 1024), (250, 1, 2)))
        with self.assertRaisesRegex(ValueError, "Runtime PNG SHA"):
            maps.verify(self.case)

    def test_missing_runtime_png_is_rejected(self):
        (self.folder() / "tiles/r01_c01.png").unlink()
        with self.assertRaises(FileNotFoundError):
            maps.verify(self.case)

    def test_mask_that_disagrees_with_plan_is_rejected(self):
        path = self.folder() / "walkmask.txt"
        data = bytearray(path.read_bytes())
        data[0] = ord("A") if data[0] != ord("A") else ord("B")
        path.write_bytes(data)
        with self.assertRaisesRegex(ValueError, "Navigation mask/plan"):
            maps.verify(self.case)

    def test_undelivered_normal_registration_is_rejected(self):
        path = self.case / maps.DELIVERY / "catalog.json"
        catalog = maps.read_json(path)
        normal = copy.deepcopy(catalog["maps"][0]["appearances"][0])
        normal["id"] = "normal"
        catalog["maps"][0]["appearances"].append(normal)
        path.write_bytes(maps.json_bytes(catalog))
        with self.assertRaisesRegex(ValueError, "Undelivered normal"):
            maps.verify(self.case)

    def test_formal_qa_snapshot_tampering_is_rejected(self):
        folder = self.folder()
        manifest = maps.read_json(folder / "tiles.json")
        path = self.case / manifest["tiles"][0]["evidencePath"]
        evidence = maps.read_json(path)
        evidence["originalDocuments"][0]["utf8Text"] = "{}"
        path.write_bytes(maps.json_bytes(evidence))
        with self.assertRaisesRegex(ValueError, "Tile evidence SHA"):
            maps.verify(self.case)

    def test_navigation_plan_for_another_art_assembly_is_rejected(self):
        path = self.case / maps.EVIDENCE / "navigation-plan.json"
        plan = maps.read_json(path)
        plan["visuallyReviewedFormalAssemblies"][0]["sha256"] = "a" * 64
        path.write_bytes(maps.json_bytes(plan))
        with self.assertRaisesRegex(ValueError, "different formal tile assembly"):
            maps.verify(self.case)

    def test_git_autocrlf_checkout_preserves_signed_delivery_bytes(self):
        git = shutil.which("git")
        self.assertIsNotNone(git, "Git is required for the real cross-checkout regression")
        project = maps.PROJECT
        catalog = maps.read_json(project / maps.DELIVERY / "catalog.json")
        registration = next(m for m in catalog["maps"] if m["key"] == "map21003")
        appearance = next(a for a in registration["appearances"] if a["id"] == "spring")
        folder = Path("Assets/Resources") / appearance["resourceRoot"]
        # Include root and nested signed JSON paths. Git's ** must match zero
        # directories for selection/navigation/catalog, and one or more for QA.
        # -text preserves each frozen byte sequence, including historical CRLF.
        samples = [maps.DELIVERY / "catalog.json", Path(str(maps.DELIVERY) + ".meta"),
                   folder / "source.json", folder / "source.json.meta", folder / "walkmask.txt",
                   folder / "tiles/r01_c01.png", folder / "tiles/r01_c01.png.meta",
                   maps.EVIDENCE / "selection.json", maps.EVIDENCE / "navigation-plan.json",
                   maps.EVIDENCE / "source-evidence/map21003/spring/r01_c01.json"]
        additional = next((m for m in catalog["maps"] if m["key"] == "map01001"), None)
        if additional:
            new_folder = Path("Assets/Resources") / additional["appearances"][0]["resourceRoot"]
            new_evidence = Path("Docs/VerificationEvidence/festival-maps-20261107")
            samples += [new_evidence / "batch.json", new_evidence / "selection.json",
                        new_evidence / "navigation-plan.json",
                        new_evidence / "source-evidence/map01001/spring/r01_c01.json",
                        new_folder / "source.json", new_folder / "tiles.json",
                        new_folder / "tiles/r01_c01.png", new_folder / "tiles/r01_c01.png.meta"]
        repository, checkout = self.case / "g", self.case / "x"
        self.assertTrue(repository.resolve().is_relative_to(self.root))
        self.assertTrue(checkout.resolve().is_relative_to(self.root))
        repository.mkdir()
        shutil.copyfile(project / ".gitattributes", repository / ".gitattributes")
        expected = {}
        for relative in samples:
            data = (project / relative).read_bytes()
            expected[relative] = maps.digest(data)
            target = repository / relative
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_bytes(data)
        environment = os.environ.copy()
        # A caller's Git index/worktree overrides must never reach the shared
        # checkout. This test uses only its newly created temporary repository.
        for name in ("GIT_DIR", "GIT_WORK_TREE", "GIT_INDEX_FILE", "GIT_COMMON_DIR",
                     "GIT_OBJECT_DIRECTORY", "GIT_ALTERNATE_OBJECT_DIRECTORIES"):
            environment.pop(name, None)
        def run(*arguments):
            result = subprocess.run([git, *arguments], cwd=repository, env=environment,
                                    capture_output=True, text=True, encoding="utf-8", errors="replace")
            self.assertEqual(0, result.returncode, result.stdout + result.stderr)
            return result.stdout
        run("init", "--quiet")
        run("config", "core.autocrlf", "true")
        run("config", "core.longpaths", "true")
        run("config", "user.name", "Festival checkout test")
        run("config", "user.email", "festival-checkout@example.invalid")
        run("config", "commit.gpgsign", "false")
        # Do not suppress hooks or bypass the normal add/commit filters.
        run("add", "--", ".")
        run("commit", "--quiet", "-m", "Fixture for exact-byte festival checkout")
        for relative in samples:
            if relative.suffix != ".png":
                attributes = run("check-attr", "text", "eol", "--", relative.as_posix())
                self.assertIn(": text: unset", attributes)
        checkout.mkdir()
        run("checkout-index", "--all", "--prefix=" + checkout.as_posix() + "/")
        for relative, expected_sha in expected.items():
            self.assertEqual(expected_sha, maps.digest((checkout / relative).read_bytes()), relative.as_posix())
            self.assertEqual(expected_sha, maps.digest((repository / relative).read_bytes()),
                             "Original temporary file changed: " + relative.as_posix())

    def test_body_string_key_and_truthful_derived_sources(self):
        art_dir = self.case / "body-art"
        cols, rows = 2, 1
        w, h = cols * 1024, rows * 1024
        selection, assemblies = dict(schemaVersion=1, sets=[]), []
        plan = dict(schemaVersion=1, maps={"biyuan-water-palace": dict(
            canvasPixels=[w, h], worldRect=dict(x=0.0, y=0.0, width=4.0, height=2.0),
            imageRect=dict(x=0.0, y=0.0, width=float(w), height=float(h)), referencePixels=[2, 1], cellSize=1.0,
            spawnPixel=[1024.0, 512.0], walkablePolygons=[[[0, 0], [w, 0], [w, h], [0, h]]],
            obstaclePolygons=[], expectedConnectedComponents=1)}, visuallyReviewedFormalAssemblies=assemblies)
        for app, derived_col in (("spring", None), ("mid-autumn", 2)):
            tiles, overview = [], Image.new("RGB", (w, h))
            for col in (1, 2):
                tid = maps.tile_id(1, col)
                is_derived = col == derived_col
                src_px = 1264 if is_derived else 1254
                color = (30 * col, 80 if app == "spring" else 160, 120)
                src = descriptor(art_dir / app / f"{tid}.src.png", png((src_px, src_px), color), [src_px, src_px])
                rt = descriptor(art_dir / app / f"{tid}.rt.png", png((1024, 1024), color), [1024, 1024])
                gen = descriptor(art_dir / app / f"{tid}.gen.json", maps.json_bytes(dict(tile=tid, derived=is_derived)))
                tiles.append(dict(tile=tid, row=1, column=col,
                    imageRectHalfOpen=[(col - 1) * 1024, 0, col * 1024, 1024],
                    native=src, runtime=rt, runtimeGenerationRecord=gen,
                    sourceNative=not is_derived, sourceDerived=is_derived, formalArtAccepted=True))
                overview.paste(Image.new("RGB", (1024, 1024), color), ((col - 1) * 1024, 0))
            buf = io.BytesIO()
            overview.save(buf, format="PNG")
            assemblies.append(dict(mapId="biyuan-water-palace", appearance=app, sha256=maps.digest(buf.getvalue())))
            selection["sets"].append(dict(mapId="biyuan-water-palace", appearance=app, formalArtAccepted=True,
                grid=dict(columns=cols, rows=rows, canvasPixels=[w, h]),
                layoutSource=dict(path="mother.png", sha256="a" * 64),
                selectionAuthority=dict(path="receipt.json", sha256="b" * 64), tiles=tiles))
        batch = dict(schemaVersion=1, revision="biyuan-body-test",
                     evidenceDirectory="Docs/VerificationEvidence/biyuan-body-test",
                     scope=[dict(mapId="biyuan-water-palace", key="biyuan-water-palace", sceneId=5,
                                 name="碧渊水宫", appearance=app, columns=cols, rows=rows,
                                 clientOnly=False, authoritativeServerRegistered=True, allowDerivedSource=True)
                            for app in ("spring", "mid-autumn")])
        sel_p, plan_p = self.case / "body-sel.json", self.case / "body-plan.json"
        sel_p.write_bytes(maps.json_bytes(selection))
        plan_p.write_bytes(maps.json_bytes(plan))
        result = maps.import_maps(self.case, sel_p, plan_p, batch)
        self.assertTrue(result["ok"] and result["authoritativeServerRegistered"])
        catalog = maps.read_json(self.case / maps.DELIVERY / "catalog.json")
        reg = next(m for m in catalog["maps"] if m["key"] == "biyuan-water-palace")
        self.assertEqual((5, "碧渊水宫", False, "spring"), (reg["sceneId"], reg["name"], reg["clientOnly"], reg["defaultAppearance"]))
        autumn_root = next(a["resourceRoot"] for a in reg["appearances"] if a["id"] == "mid-autumn")
        tiles_doc = maps.read_json(self.case / "Assets/Resources" / autumn_root / "tiles.json")
        by_col = {t["column"]: t for t in tiles_doc["tiles"]}
        self.assertEqual((True, False, 1254), (by_col[1]["sourceNative"], by_col[1]["sourceDerived"], by_col[1]["nativePixels"]))
        self.assertEqual((False, True, 1264), (by_col[2]["sourceNative"], by_col[2]["sourceDerived"], by_col[2]["nativePixels"]))
        # Tampering derived flag to fake native must fail verify.
        tiles_doc["tiles"][1]["sourceNative"] = True
        tiles_doc["tiles"][1]["sourceDerived"] = False
        (self.case / "Assets/Resources" / autumn_root / "tiles.json").write_bytes(maps.json_bytes(tiles_doc))
        with self.assertRaisesRegex(ValueError, "derived"):
            maps.verify(self.case, batch)

    def test_transparent_padding_checks_opaque_image_rect_and_clear_margin(self):
        art_dir = self.case / "pad-art"
        w, h = 2048, 2048
        atlas = Image.new("RGBA", (w, h), (0, 0, 0, 0))
        atlas.paste((80, 140, 90, 255), (0, 512, 2048, 1536))
        atlas_buf = io.BytesIO()
        atlas.save(atlas_buf, format="PNG")
        atlas_desc = descriptor(art_dir / "atlas.png", atlas_buf.getvalue(), [w, h])
        tiles = []
        for row in (1, 2):
            for col in (1, 2):
                tid = maps.tile_id(row, col)
                crop = atlas.crop(((col - 1) * 1024, (row - 1) * 1024, col * 1024, row * 1024))
                buf = io.BytesIO()
                crop.save(buf, format="PNG")
                rt = descriptor(art_dir / f"{tid}.png", buf.getvalue(), [1024, 1024])
                gen = descriptor(art_dir / f"{tid}.gen.json", maps.json_bytes(dict(tile=tid)))
                tiles.append(dict(tile=tid, row=row, column=col,
                    imageRectHalfOpen=[(col - 1) * 1024, (row - 1) * 1024, col * 1024, row * 1024],
                    native=atlas_desc, runtime=rt, runtimeGenerationRecord=gen,
                    sourceNative=False, sourceDerived=True, formalArtAccepted=True))
        selection = dict(schemaVersion=1, sets=[dict(
            mapId="qinglan-test", appearance="spring", formalArtAccepted=True,
            grid=dict(columns=2, rows=2, canvasPixels=[w, h]),
            layoutSource=dict(path="atlas.png", sha256=atlas_desc["sha256"]),
            selectionAuthority=dict(path="validation.json", sha256="b" * 64),
            derivedOverview=atlas_desc, tiles=tiles)])
        plan = dict(schemaVersion=1, maps={"qinglan-test": dict(
            canvasPixels=[w, h], worldRect=dict(x=-10.0, y=-10.0, width=20.0, height=20.0),
            imageRect=dict(x=0.0, y=512.0, width=2048.0, height=1024.0), referencePixels=[2048, 1024],
            cellSize=1.0, spawnPixel=[1024.0, 1024.0],
            walkablePolygons=[[[100, 600], [1900, 600], [1900, 1400], [100, 1400]]],
            obstaclePolygons=[], expectedConnectedComponents=1)},
            visuallyReviewedFormalAssemblies=[dict(mapId="qinglan-test", appearance="spring", sha256=atlas_desc["sha256"])])
        batch = dict(schemaVersion=1, revision="pad-test",
                     evidenceDirectory="Docs/VerificationEvidence/pad-test",
                     scope=[dict(mapId="qinglan-test", key="qinglan-test", sceneId=4, name="青岚测试",
                                 appearance="spring", columns=2, rows=2, clientOnly=True,
                                 authoritativeServerRegistered=False, allowDerivedSource=True,
                                 allowTransparentPadding=True)])
        sel_p, plan_p = self.case / "pad-sel.json", self.case / "pad-plan.json"
        sel_p.write_bytes(maps.json_bytes(selection))
        plan_p.write_bytes(maps.json_bytes(plan))
        self.assertTrue(maps.import_maps(self.case, sel_p, plan_p, batch)["ok"])
        # Non-opaque pixel inside effective imageRect must be rejected.
        bad_inside = atlas.crop((0, 0, 1024, 1024))
        bad_inside.putpixel((500, 700), (80, 140, 90, 128))
        bad_buf = io.BytesIO()
        bad_inside.save(bad_buf, format="PNG")
        with self.assertRaisesRegex(ValueError, "nonopaque pixel inside effective imageRect"):
            maps.inspect_png(bad_buf.getvalue(), [1024, 1024], "bad_inside", opaque_box=(0, 512, 1024, 1024))
        # Opaque pixel leaking into transparent padding must also be rejected.
        bad_outside = atlas.crop((0, 0, 1024, 1024))
        bad_outside.putpixel((500, 100), (80, 140, 90, 255))
        bad_buf2 = io.BytesIO()
        bad_outside.save(bad_buf2, format="PNG")
        with self.assertRaisesRegex(ValueError, "opaque pixel inside transparent padding"):
            maps.inspect_png(bad_buf2.getvalue(), [1024, 1024], "bad_outside", opaque_box=(0, 512, 1024, 1024))


if __name__ == "__main__":
    unittest.main()
