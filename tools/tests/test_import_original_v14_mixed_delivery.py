"""Evidence and isolation gates for the 05 mixed-size candidate importer."""

from contextlib import redirect_stderr, redirect_stdout
from io import StringIO
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

from PIL import Image


TOOL = Path(__file__).resolve().parents[1] / "import_original_v14_mixed_delivery.py"
SPEC = importlib.util.spec_from_file_location("import_original_v14_mixed_delivery", TOOL)
importer = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(importer)
CHECKOUT = TOOL.parents[1]
PROFILE = CHECKOUT / "tools/character_deliveries/05_celestial_musician_girl.json"
TMP_ROOT = CHECKOUT.parent / "tmp"


class MixedDeliveryTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix="mixed-import-test-", dir=TMP_ROOT)
        self.addCleanup(self.temporary.cleanup)
        self.project = Path(self.temporary.name) / "candidate"
        (self.project / "Assets").mkdir(parents=True)
        (self.project / "ProjectSettings").mkdir()
        (self.project / "ProjectSettings/ProjectVersion.txt").write_text("m_EditorVersion: 6000.6.0f1\n")
        self.profile = json.loads(PROFILE.read_text(encoding="utf-8"))
        self.profile_path = Path(self.temporary.name) / "profile.json"
        self.save_profile()

    def save_profile(self):
        self.profile_path.write_bytes(importer.json_bytes(self.profile))

    def run_import(self, execute=False):
        output, errors = StringIO(), StringIO()
        argv = ["--manifest", str(self.profile_path), "--character-id", importer.CHARACTER_ID,
                "--project", str(self.project)]
        if execute:
            argv.append("--execute")
        with redirect_stdout(output), redirect_stderr(errors):
            status = importer.main(argv)
        return status, json.loads(output.getvalue() if status == 0 else errors.getvalue())

    def test_real_delivery_builds_complete_mixed_contract_without_writes(self):
        status, result = self.run_import()
        self.assertEqual(status, 0, result)
        self.assertEqual(result["status"], "ready_dry_run")
        self.assertEqual((result["runtime_png_count"], result["output_count"]), (137, 140))
        self.assertFalse((self.project / importer.RESOURCE_FAMILY / importer.CHARACTER_ID).exists())
        args = type("Args", (), {"project": self.project, "manifest": self.profile_path,
                                  "character_id": importer.CHARACTER_ID})()
        plan = importer.build_plan(args)
        manifest = json.loads(plan["metadata"]["manifest.json"])
        appearance = json.loads(plan["metadata"]["appearance.json"])
        validation = json.loads(plan["metadata"]["validation.json"])
        self.assertEqual(manifest["resolution_mode"], importer.MODE)
        self.assertEqual(appearance["resolutionMode"], importer.MODE)
        self.assertEqual(manifest["preserved_snapshot_sha256"], importer.PRESERVED_SNAPSHOT_SHA256)
        self.assertEqual(len(manifest["files"]), 137)
        self.assertEqual(sum(row.get("source_kind") == "preserved-v13" for row in manifest["files"]), 60)
        self.assertEqual(sum(row.get("source_kind") == "native-hd" for row in manifest["files"]), 76)
        self.assertFalse(validation["formal_release"])
        self.assertFalse(validation["unity_validation"])

    def test_changed_preserved_binding_and_native_cell_fail_closed(self):
        old = next(row for row in self.profile["slots"] if row["source_kind"] == "preserved-v13")
        old["preserved_sha256"] = "0" * 64
        self.save_profile()
        status, blocked = self.run_import()
        self.assertEqual(status, 1)
        self.assertIn("Preserved snapshot row differs", blocked["error"])
        self.assertFalse((self.project / importer.RESOURCE_FAMILY / importer.CHARACTER_ID).exists())

        old["preserved_sha256"] = old["sha256"]
        native = next(row for row in self.profile["slots"] if row["source_kind"] == "native-hd")
        native["native_cell_size"] = [627, 1254]
        self.save_profile()
        status, blocked = self.run_import()
        self.assertEqual(status, 1)
        self.assertIn("Native source cell binding differs", blocked["error"])

    def test_changed_snapshot_pin_and_unity_lock_fail_closed(self):
        self.profile["preserved_snapshot_sha256"] = "0" * 64
        self.save_profile()
        status, blocked = self.run_import()
        self.assertEqual(status, 1)
        self.assertIn("pinned 05 evidence", blocked["error"])

        self.profile["preserved_snapshot_sha256"] = importer.PRESERVED_SNAPSHOT_SHA256
        self.save_profile()
        (self.project / "Temp").mkdir()
        (self.project / "Temp/UnityLockfile").write_text("held")
        status, blocked = self.run_import(execute=True)
        self.assertEqual(status, 1)
        self.assertIn("Close the candidate Unity editor", blocked["error"])
        self.assertFalse((self.project / importer.RESOURCE_FAMILY / importer.CHARACTER_ID).exists())

    def test_rgba_requires_both_transparent_canvas_and_visible_subject(self):
        png = Path(self.temporary.name) / "alpha-boundary.png"
        Image.new("RGBA", (16, 16), (10, 40, 70, 255)).save(png)
        with self.assertRaisesRegex(importer.DeliveryError, "transparent canvas and visible subject"):
            importer.check_png(png, (16, 16), "opaque")

        Image.new("RGBA", (16, 16), (0, 0, 0, 0)).save(png)
        with self.assertRaisesRegex(importer.DeliveryError, "transparent canvas and visible subject"):
            importer.check_png(png, (16, 16), "empty")

        image = Image.new("RGBA", (16, 16), (0, 0, 0, 0))
        image.putpixel((8, 8), (10, 40, 70, 255))
        image.save(png)
        importer.check_png(png, (16, 16), "valid")

    def test_execute_only_adds_role_to_disposable_candidate(self):
        retained = self.project / "Assets/Resources/World/Characters/QdaoOriginalRosterV13/03_lotus_healer_girl/keep.txt"
        retained.parent.mkdir(parents=True)
        retained.write_bytes(b"unchanged V13")
        target = self.project / importer.RESOURCE_FAMILY / importer.CHARACTER_ID
        status, staged = self.run_import(execute=True)
        self.assertEqual(status, 0, staged)
        self.assertEqual(staged["status"], "staged_pending_unity_validation")
        self.assertEqual(len([path for path in target.rglob("*") if path.is_file()]), 140)
        self.assertEqual(retained.read_bytes(), b"unchanged V13")
        self.assertEqual(importer.file_sha256(target / "manifest.json"), staged["manifest_sha256"])
        self.assertEqual(importer.file_sha256(target / "appearance.json"), staged["appearance_sha256"])
        status, blocked = self.run_import(execute=True)
        self.assertEqual(status, 1)
        self.assertIn("refusing to overwrite", blocked["error"])
        self.assertEqual(retained.read_bytes(), b"unchanged V13")


if __name__ == "__main__":
    unittest.main()
