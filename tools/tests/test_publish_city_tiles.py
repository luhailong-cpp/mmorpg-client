"""Publisher contract/transaction tests; tiny fixtures are NEVER production assets.

Run: python -m unittest discover -s tools/tests -p test_publish_city_tiles.py -v
PNG tests decode real 4096 images. Transaction tests replace only the PNG decoder
with a SHA check so that testing swaps/failures does not create gigabytes of art.
Windows Unity locking is tested separately from those simulated transactions.
"""
from contextlib import contextmanager
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
from unittest import mock

from PIL import Image

SPEC = importlib.util.spec_from_file_location("publish_city_tiles", Path(__file__).resolve().parents[1] / "publish_city_tiles.py")
pub = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(pub)


@contextmanager
def no_editor(_):
    yield


class PublisherTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.art, self.project = self.root / "art", self.root / "client"
        self.art.mkdir()
        self.project.mkdir()
        self.delivery_path = self.art / "delivery.json"
        self.delivery = pub.contract("penglai_day")["delivery"]
        self.delivery.update(status="accepted_complete", version="test-version-1")
        (self.art / "source-record.json").write_text('{"testOnly":true}', encoding="utf-8")
        (self.art / "assembly.json").write_text('{"testOnly":"assembly"}', encoding="utf-8")
        (self.art / "review.txt").write_text("TEST FIXTURE ONLY - NOT A REAL VISUAL ACCEPTANCE", encoding="utf-8")
        for index, tile in enumerate(self.delivery["tiles"]):
            path = self.art / "tiles" / (tile["tile"] + ".png")
            path.parent.mkdir(exist_ok=True)
            Image.new("RGB", (4, 4), (index, 40, 90)).save(path)
            tile.update(file=path.relative_to(self.art).as_posix(), sha256=pub.sha256(path),
                        sources=[self.reference("source-record.json")], assembly=self.reference("assembly.json"))
        self.refresh_review()

    def reference(self, name):
        return {"file": name, "sha256": pub.sha256(self.art / name)}

    def refresh_review(self):
        record = {"appearance": self.delivery["appearance"], "version": self.delivery["version"],
                  "tileCount": 256, "tilesDigest": pub.tiles_digest(self.delivery["tiles"]),
                  "reviewer": "unittest fixture", "reviewedAtUtc": "2026-09-21T00:00:00Z",
                  "checks": {name: {"passed": True, "notes": "test fixture", "evidence": self.reference("review.txt")}
                             for name in pub.ART_CHECKS}}
        pub.write_json(self.art / "acceptance.json", record)
        self.delivery["acceptance"] = self.reference("acceptance.json")
        self.save_delivery()

    def save_delivery(self):
        pub.write_json(self.delivery_path, self.delivery)

    def fast_decode(self, path, expected):
        pub.require(pub.sha256(path) == expected, "Fixture SHA mismatch")

    def stage(self):
        with mock.patch.object(pub, "validate_png", side_effect=self.fast_decode):
            return pub.stage(self.delivery_path, self.art, self.project)

    def publish(self, release):
        with mock.patch.object(pub, "validate_png", side_effect=self.fast_decode), mock.patch.object(pub, "closed_unity_project", no_editor):
            return pub.publish(self.project, release, self.project / "evidence")

    def test_mapping_has_exact_seven_appearances(self):
        self.assertEqual(len(pub.APPEARANCES), 7)
        self.assertEqual(pub.APPEARANCES["penglai_mid_autumn"], ("penglai", "festival"))
        self.assertEqual(pub.APPEARANCES["donghai_lantern"], ("donghai", "festival"))
        self.assertEqual(pub.APPEARANCES["lanxian_spring"], ("lanxian", "festival"))

    def test_candidates_and_drafts_cannot_be_promoted(self):
        for field, value in (("purpose", "local_candidate_editor_review_only"), ("status", "DRAFT_NOT_ACCEPTED")):
            with self.subTest(field=field):
                original = self.delivery[field]
                self.delivery[field] = value
                self.save_delivery()
                with self.assertRaises(pub.ValidationError):
                    pub.validate_delivery(self.delivery_path, self.art)
                self.delivery[field] = original

    def test_missing_tile_fails_before_any_decode(self):
        self.delivery["tiles"].pop()
        self.save_delivery()
        with mock.patch.object(pub, "validate_png") as decode, self.assertRaisesRegex(pub.ValidationError, "256"):
            pub.validate_delivery(self.delivery_path, self.art)
        decode.assert_not_called()

    def test_wrong_order_and_coordinates_fail(self):
        original = self.delivery["tiles"][0].copy()
        for field, value in (("tile", "r16_c16"), ("finalPixelRectXYWH", [1, 0, 4096, 4096]),
                             ("worldRect", {"x": 50, "z": 0, "width": 18.75, "height": 18.75})):
            with self.subTest(field=field):
                self.delivery["tiles"][0] = {**original, field: value}
                self.save_delivery()
                with self.assertRaises(pub.ValidationError):
                    pub.validate_delivery(self.delivery_path, self.art)

    def test_duplicate_content_rejected(self):
        first, second = self.delivery["tiles"][:2]
        second.update(file=first["file"], sha256=first["sha256"])
        self.save_delivery()
        with self.assertRaisesRegex(pub.ValidationError, "Duplicate"):
            pub.validate_delivery(self.delivery_path, self.art)

    def test_source_traversal_rejected(self):
        self.delivery["tiles"][0]["file"] = "../outside.png"
        self.save_delivery()
        with self.assertRaisesRegex(pub.ValidationError, "Unsafe source path"):
            pub.validate_delivery(self.delivery_path, self.art)

    def test_sha_mismatch_rejected(self):
        self.delivery["tiles"][0]["sha256"] = "0" * 64
        self.save_delivery()
        with self.assertRaisesRegex(pub.ValidationError, "SHA-256 mismatch"):
            pub.validate_delivery(self.delivery_path, self.art)

    def test_acceptance_bound_to_full_tile_digest(self):
        review = pub.read_json(self.art / "acceptance.json")
        review["tilesDigest"] = "0" * 64
        pub.write_json(self.art / "acceptance.json", review)
        self.delivery["acceptance"] = self.reference("acceptance.json")
        self.save_delivery()
        with self.assertRaisesRegex(pub.ValidationError, "tilesDigest"):
            pub.validate_delivery(self.delivery_path, self.art)

    def test_changed_evidence_during_decoding_is_rejected(self):
        def change_review(path, expected):
            self.fast_decode(path, expected)
            (self.art / "review.txt").write_text("CHANGED DURING FULL DECODE", encoding="utf-8")
        with mock.patch.object(pub, "validate_png", side_effect=change_review), self.assertRaisesRegex(pub.ValidationError, "changed during validation"):
            pub.validate_delivery(self.delivery_path, self.art)

    def test_changed_native_record_during_decoding_is_rejected(self):
        def change_source(path, expected):
            self.fast_decode(path, expected)
            (self.art / "source-record.json").write_text("CHANGED", encoding="utf-8")
        with mock.patch.object(pub, "validate_png", side_effect=change_source), self.assertRaisesRegex(pub.ValidationError, "changed during validation"):
            pub.validate_delivery(self.delivery_path, self.art)

    def test_tianyong_requires_foreground_review(self):
        self.delivery["appearance"] = "tianyong_festival"
        self.refresh_review()
        with self.assertRaisesRegex(pub.ValidationError, "reference must be an object"):
            pub.validate_delivery(self.delivery_path, self.art)

    def test_foreground_review_requires_all_client_assets(self):
        with self.assertRaisesRegex(pub.ValidationError, "36 legacy PNGs"):
            pub.verify_foreground_client_files(self.project, {"clientFiles": []})
        review = {"clientFiles": []}
        for name in pub.foreground_client_paths():
            path = self.project / name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(b"test version")
            review["clientFiles"].append({"file": name, "sha256": pub.sha256(path)})
        self.assertEqual(len(pub.verify_foreground_client_files(self.project, review)), 75)
        (self.project / review["clientFiles"][0]["file"]).write_bytes(b"different version")
        with self.assertRaisesRegex(pub.ValidationError, "SHA-256 mismatch"):
            pub.verify_foreground_client_files(self.project, review)

    def test_real_4096_png_complete_decode_and_corruption(self):
        path = self.art / "real4k.png"
        Image.new("RGB", (4096, 4096), (70, 150, 220)).save(path)
        pub.validate_png(path, pub.sha256(path))
        original = path.read_bytes()
        path.write_bytes(original[:len(original) // 2])
        with self.assertRaises(pub.ValidationError):
            pub.validate_png(path, pub.sha256(path))

    def test_2048_png_rejected(self):
        path = self.art / "2k.png"
        Image.new("RGB", (2048, 2048)).save(path)
        with self.assertRaisesRegex(pub.ValidationError, "4096"):
            pub.validate_png(path, pub.sha256(path))

    def test_stage_is_outside_assets_and_copies_no_meta(self):
        tile = self.art / self.delivery["tiles"][0]["file"]
        tile.with_suffix(".png.meta").write_text("bad art importer", encoding="utf-8")
        result = self.stage()
        release = Path(result["path"])
        self.assertFalse((self.project / "Assets").exists())
        self.assertEqual(len(list((release / "tiles").iterdir())), 256)
        self.assertFalse(list(release.rglob("*.meta")))
        self.assertTrue((release / "evidence").is_dir())

    def test_staged_corruption_cannot_publish(self):
        result = self.stage()
        (Path(result["path"]) / "tiles" / "r01_c01.png").write_bytes(b"broken")
        with self.assertRaises(pub.ValidationError):
            self.publish(result["release"])
        self.assertFalse(pub.destination_for(self.project, "penglai_day").exists())

    def test_manifest_is_installed_after_complete_directory(self):
        result = self.stage()
        actual_replace = pub.os.replace
        observed = []
        def observe(source, destination):
            if Path(destination).name == "manifest.json" and "Resources" in Path(destination).parts:
                target = Path(destination).parent
                self.assertFalse((target / "manifest.json").exists())
                self.assertEqual(len(list((target / "tiles").glob("*.png"))), 256)
                observed.append(True)
            return actual_replace(source, destination)
        with mock.patch.object(pub.os, "replace", side_effect=observe):
            record = self.publish(result["release"])
        self.assertEqual(observed, [True])
        manifest = pub.read_json(Path(record["destination"]) / "manifest.json")
        self.assertEqual(manifest["worldRect"], pub.WORLD_RECT)
        self.assertEqual(manifest["tiles"][0], "World/CityTiles4K/penglai/day/tiles/r01_c01")
        self.assertEqual(manifest["tiles"][-1], "World/CityTiles4K/penglai/day/tiles/r16_c16")

    def test_failed_manifest_swap_restores_previous_bytes(self):
        release = self.stage()["release"]
        first = self.publish(release)
        destination = Path(first["destination"])
        (destination / "tiles" / "r01_c01.png.meta").write_text("client generated guid", encoding="utf-8")
        before = pub.inventory(destination)
        actual_replace = pub.os.replace
        def fail_manifest(source, target):
            if Path(target).name == "manifest.json" and "Resources" in Path(target).parts:
                raise OSError("injected manifest replacement failure")
            return actual_replace(source, target)
        with mock.patch.object(pub.os, "replace", side_effect=fail_manifest), self.assertRaisesRegex(OSError, "injected"):
            self.publish(release)
        self.assertEqual(pub.inventory(destination), before)
        self.assertFalse(pub.journal_path(pub.workspace_for(self.project)).exists())

    def test_first_publish_failure_removes_incomplete_resources(self):
        release = self.stage()["release"]
        actual_replace = pub.os.replace
        def fail_manifest(source, target):
            if Path(target).name == "manifest.json" and "Resources" in Path(target).parts:
                raise OSError("injected")
            return actual_replace(source, target)
        with mock.patch.object(pub.os, "replace", side_effect=fail_manifest), self.assertRaises(OSError):
            self.publish(release)
        self.assertFalse(pub.destination_for(self.project, "penglai_day").exists())

    def test_rollback_restores_previous_including_meta(self):
        release = self.stage()["release"]
        first = self.publish(release)
        destination = Path(first["destination"])
        (destination / "manifest.json.meta").write_text("original client guid", encoding="utf-8")
        before = pub.inventory(destination)
        second = self.publish(release)
        with mock.patch.object(pub, "validate_png", side_effect=self.fast_decode), mock.patch.object(pub, "closed_unity_project", no_editor):
            pub.rollback(self.project, second["transaction"], self.project / "evidence")
        self.assertEqual(pub.inventory(destination), before)

    def test_rollback_refuses_edited_tile(self):
        release = self.stage()["release"]
        record = self.publish(release)
        target = Path(record["destination"]) / "tiles" / "r01_c01.png"
        target.write_bytes(b"another windows work")
        with mock.patch.object(pub, "validate_png", side_effect=self.fast_decode), mock.patch.object(pub, "closed_unity_project", no_editor), self.assertRaisesRegex(pub.ValidationError, "were edited"):
            pub.rollback(self.project, record["transaction"], self.project / "evidence")
        self.assertEqual(target.read_bytes(), b"another windows work")

    def test_rollback_refuses_unregistered_resource(self):
        record = self.publish(self.stage()["release"])
        extra = Path(record["destination"]) / "extra.png"
        extra.write_bytes(b"another windows work")
        with mock.patch.object(pub, "validate_png", side_effect=self.fast_decode), mock.patch.object(pub, "closed_unity_project", no_editor), self.assertRaisesRegex(pub.ValidationError, "Unregistered"):
            pub.rollback(self.project, record["transaction"], self.project / "evidence")
        self.assertTrue(extra.exists())

    def test_old_resources_changed_during_preparation_are_preserved(self):
        release = self.stage()["release"]
        first = self.publish(release)
        destination = Path(first["destination"])
        extra = destination / "another-window.txt"
        actual_copytree = pub.shutil.copytree
        def change_after_copy(source, target, *args, **kwargs):
            result = actual_copytree(source, target, *args, **kwargs)
            if Path(target).parent.name == "incoming":
                extra.write_bytes(b"preserve concurrent work")
            return result
        with mock.patch.object(pub.shutil, "copytree", side_effect=change_after_copy), self.assertRaisesRegex(pub.ValidationError, "changed while preparing"):
            self.publish(release)
        self.assertEqual(extra.read_bytes(), b"preserve concurrent work")
        self.assertTrue((destination / "manifest.json").exists())

    def test_interrupted_swap_recovers_from_durable_journal(self):
        release = self.stage()["release"]
        record = self.publish(release)
        destination = Path(record["destination"])
        before = pub.inventory(destination)
        actual_replace, actual_rename = pub.os.replace, pub.Path.rename
        def fail_manifest(source, target):
            if Path(target).name == "manifest.json" and "Resources" in Path(target).parts:
                raise OSError("injected manifest failure")
            return actual_replace(source, target)
        def fail_restore(path, target):
            if path.name == "previous":
                raise OSError("injected transient restore failure")
            return actual_rename(path, target)
        with mock.patch.object(pub.os, "replace", side_effect=fail_manifest), mock.patch.object(pub.Path, "rename", fail_restore), self.assertRaisesRegex(OSError, "transient restore"):
            self.publish(release)
        workspace = pub.workspace_for(self.project)
        journal = pub.read_json(pub.journal_path(workspace))
        pub.restore_transaction(self.project, workspace, journal)
        self.assertEqual(pub.inventory(destination), before)
        self.assertFalse(pub.journal_path(workspace).exists())

    def test_recover_refuses_missing_previous_snapshot(self):
        workspace = pub.workspace_for(self.project)
        destination = pub.destination_for(self.project, "penglai_day")
        destination.mkdir(parents=True)
        (destination / "changed.txt").write_bytes(b"not the original directory")
        journal = {"id": "missing-snapshot-test", "appearance": "penglai_day", "hadPrevious": True,
                   "previousInventory": {"original.txt": "0" * 64}}
        with self.assertRaisesRegex(pub.ValidationError, "snapshot is missing"):
            pub.restore_transaction(self.project, workspace, journal)
        self.assertTrue((destination / "changed.txt").exists())

    def test_editor_lock_blocks_publication_without_touching_resources(self):
        lock = self.project / "Temp" / "UnityLockfile"
        lock.parent.mkdir()
        lock.write_text("test editor", encoding="utf-8")
        with self.assertRaisesRegex(pub.ValidationError, "close its editor"):
            with pub.closed_unity_project(self.project):
                self.fail("Project guard was bypassed")
        self.assertEqual(lock.read_text(encoding="utf-8"), "test editor")

    def test_concurrent_publisher_lock_rejected(self):
        workspace = pub.workspace_for(self.project)
        with pub.publication_lock(workspace):
            with self.assertRaisesRegex(pub.ValidationError, "Another city publisher"):
                with pub.publication_lock(workspace):
                    self.fail("Two publisher locks were acquired")


if __name__ == "__main__":
    unittest.main()
