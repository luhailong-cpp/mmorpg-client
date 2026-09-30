"""04 snapshot identity, mixed geometry, and no-overwrite import checks."""

from contextlib import redirect_stderr, redirect_stdout
from io import StringIO
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest


TOOL = Path(__file__).resolve().parents[1] / "import_original_v14_mixed_delivery.py"
SPEC = importlib.util.spec_from_file_location("mixed04_importer", TOOL)
importer = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(importer)
CHECKOUT = TOOL.parents[1]
PROFILE = CHECKOUT / "tools/character_deliveries/04_mountain_guardian_boy.json"


class GuardianMixedDeliveryTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix="mixed04-test-", dir=CHECKOUT.parent / "tmp")
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.project = self.root / "candidate"
        (self.project / "Assets").mkdir(parents=True)
        (self.project / "ProjectSettings").mkdir()
        (self.project / "ProjectSettings/ProjectVersion.txt").write_text("m_EditorVersion: 6000.6.0f1\n")
        self.profile = json.loads(PROFILE.read_text(encoding="utf-8"))
        self.profile_path = self.root / "profile.json"
        self.target = self.project / importer.RESOURCE_FAMILY / importer.GUARDIAN_ID

    def run_import(self, execute=False):
        self.profile_path.write_bytes(importer.json_bytes(self.profile))
        output, errors = StringIO(), StringIO()
        argv = ["--manifest", str(self.profile_path), "--character-id", importer.GUARDIAN_ID,
                "--project", str(self.project)]
        if execute:
            argv.append("--execute")
        with redirect_stdout(output), redirect_stderr(errors):
            code = importer.main(argv)
        return code, json.loads(output.getvalue() if code == 0 else errors.getvalue())

    def test_real_snapshot_dry_run_preserves_review_scope_and_geometry(self):
        code, result = self.run_import()
        self.assertEqual(code, 0, result)
        self.assertEqual(result["character_id"], importer.GUARDIAN_ID)
        self.assertEqual((result["runtime_png_count"], result["output_count"]), (137, 140))
        self.assertFalse(self.target.exists())
        args = type("Args", (), {"project": self.project, "manifest": self.profile_path,
                                 "character_id": importer.GUARDIAN_ID})()
        plan = importer.build_plan(args)
        manifest = json.loads(plan["metadata"]["manifest.json"])
        validation = json.loads(plan["metadata"]["validation.json"])
        self.assertEqual(manifest["source_snapshot_status"], "inventory_complete_visual_pending")
        self.assertFalse(manifest["formal_approval"])
        self.assertFalse(validation["unity_validation"])
        self.assertFalse(validation["formal_release"])
        self.assertEqual(sum(row.get("source_kind") == "preserved-v13" for row in manifest["files"]), 112)
        self.assertEqual(sum(row.get("source_kind") == "native-hd" for row in manifest["files"]), 24)
        for row in manifest["files"]:
            if row["path"] != "portrait.png":
                self.assertAlmostEqual(row["height"] / row["pixels_per_unit"], 512 / 52)

    def test_changed_png_sha_is_rejected_before_any_write(self):
        self.profile["slots"][0]["sha256"] = "0" * 64
        code, result = self.run_import(execute=True)
        self.assertEqual(code, 1)
        self.assertIn("slot geometry, SHA, or source binding differs", result["error"])
        self.assertFalse(self.target.exists())

    def test_native_size_cannot_be_relabelled_as_preserved(self):
        row = next(row for row in self.profile["slots"] if row["source_kind"] == "native-hd")
        row["width"] = row["height"] = 512
        row["pixels_per_unit"] = 52
        code, result = self.run_import(execute=True)
        self.assertEqual(code, 1)
        self.assertIn("slot geometry, SHA, or source binding differs", result["error"])
        self.assertFalse(self.target.exists())

    def test_review_pin_and_portrait_identity_cannot_be_replaced(self):
        self.profile["offline_review_sha256"] = "0" * 64
        code, result = self.run_import()
        self.assertEqual(code, 1)
        self.assertIn("pinned evidence differs: offline_review", result["error"])
        self.profile["offline_review_sha256"] = importer.GUARDIAN_PINS["offline_review"]
        self.profile["portrait"]["source_sha256"] = "0" * 64
        code, result = self.run_import()
        self.assertEqual(code, 1)
        self.assertIn("portrait identity differs", result["error"])

    def test_atomic_import_copies_bytes_and_refuses_existing_role(self):
        retained = self.project / "Assets/existing.txt"
        retained.write_bytes(b"other work stays unchanged")
        code, result = self.run_import(execute=True)
        self.assertEqual(code, 0, result)
        self.assertEqual(len([p for p in self.target.rglob("*") if p.is_file()]), 140)
        for row in [self.profile["portrait"], *self.profile["slots"]]:
            self.assertEqual(importer.file_sha256(self.target / row["path"]), row["sha256"])
        self.assertEqual(retained.read_bytes(), b"other work stays unchanged")
        code, result = self.run_import(execute=True)
        self.assertEqual(code, 1)
        self.assertIn("refusing to overwrite", result["error"])
        self.assertEqual(retained.read_bytes(), b"other work stays unchanged")


if __name__ == "__main__":
    unittest.main()
