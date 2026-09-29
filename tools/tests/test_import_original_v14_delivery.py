"""Safety checks for the one-character V14 import boundary."""

from contextlib import redirect_stderr, redirect_stdout
from io import BytesIO, StringIO
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

from PIL import Image, ImageDraw


TOOL = Path(__file__).resolve().parents[1] / "import_original_v14_delivery.py"
SPEC = importlib.util.spec_from_file_location("import_original_v14_delivery", TOOL)
importer = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(importer)


class OriginalV14DeliveryTests(unittest.TestCase):
    CHARACTER = "09_bamboo_archer_girl"

    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.project = self.root / "project"
        (self.project / "Assets").mkdir(parents=True)
        (self.project / "ProjectSettings").mkdir()
        (self.project / "ProjectSettings/ProjectVersion.txt").write_text("m_EditorVersion: 6000.6.0f1\n")
        self.legacy = self.project / "Assets/Resources/World/Characters/QdaoOriginalRosterV13/03_lotus_healer_girl/keep.txt"
        self.legacy.parent.mkdir(parents=True)
        self.legacy.write_bytes(b"protected legacy resource")
        self.source_dir = self.root / "frames"
        self.source_dir.mkdir()
        self.frame_bytes = {}
        slots = []
        for number, relative in enumerate(importer.SLOT_PATHS):
            image = Image.new("RGBA", (1024, 1024), (number, 166, 102, 0))
            ImageDraw.Draw(image).rectangle((400, 400, 624, 900), fill=(number, 166, 102, 255))
            frame = BytesIO()
            image.save(frame, format="PNG")
            self.frame_bytes[relative] = frame.getvalue()
            path = self.source_dir / relative
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(self.frame_bytes[relative])
            slots.append({"path": relative, "sha256": importer.sha256(self.frame_bytes[relative]),
                          "width": 1024, "height": 1024})
        self.source_document = self.root / "source.json"
        reviewed_snapshot = "1" * 64
        self.source_document.write_bytes(importer.json_bytes({
            "character": self.CHARACTER, "visualReview": "passed",
            "offlineAcceptance": {"status": "passed"},
            "reviewedSnapshotManifestSha256": reviewed_snapshot,
            "files": [{"key": row["path"], "sha256": row["sha256"]} for row in slots],
        }))
        self.approval_document = self.root / "approval.json"
        self.approval_document.write_bytes(importer.json_bytes({
            "character": self.CHARACTER, "status": "passed",
            "reviewedManifestSha256": reviewed_snapshot,
        }))
        self.portrait = self.root / "original.png"
        portrait = Image.new("RGBA", (4096, 4096), (56, 103, 155, 0))
        ImageDraw.Draw(portrait).rectangle((1500, 800, 2600, 3500), fill=(56, 103, 155, 255))
        portrait.save(self.portrait)
        portrait_hash = importer.file_sha256(self.portrait)
        self.inventory = self.root / "inventory.json"
        self.inventory.write_bytes(importer.json_bytes({
            "source_commit": importer.SOURCE_COMMIT,
            "characters": [{"character_id": self.CHARACTER, "source_commit": importer.SOURCE_COMMIT,
                            "sha256": portrait_hash, "git_manifest_sha256": portrait_hash}],
        }))
        self.manifest = self.root / "delivery.json"
        self.delivery = {
            "schema": importer.SCHEMA, "character_id": self.CHARACTER, "status": "passed_offline",
            "source_dir": str(self.source_dir), "source_document": str(self.source_document),
            "source_document_sha256": importer.file_sha256(self.source_document),
            "approval_document": str(self.approval_document),
            "approval_document_sha256": importer.file_sha256(self.approval_document),
            "portrait_source": str(self.portrait), "portrait_source_sha256": portrait_hash,
            "portrait_inventory": str(self.inventory),
            "portrait_inventory_sha256": importer.file_sha256(self.inventory),
            "slots": slots,
        }
        self.save_delivery()
        approved = patch.dict(importer.APPROVED_DELIVERIES, {self.CHARACTER: (
            self.delivery["source_document_sha256"], self.delivery["approval_document_sha256"], portrait_hash)})
        approved.start()
        self.addCleanup(approved.stop)
        inventory_pin = patch.object(importer, "PORTRAIT_INVENTORY_SHA256",
                                     self.delivery["portrait_inventory_sha256"])
        inventory_pin.start()
        self.addCleanup(inventory_pin.stop)

    def save_delivery(self):
        self.manifest.write_bytes(importer.json_bytes(self.delivery))

    def run_import(self, execute=False, character_id=None):
        output, errors = StringIO(), StringIO()
        args = ["--manifest", str(self.manifest), "--character-id", character_id or self.CHARACTER,
                "--project", str(self.project)]
        if execute:
            args.append("--execute")
        with redirect_stdout(output), redirect_stderr(errors):
            code = importer.main(args)
        return code, json.loads(output.getvalue() if code == 0 else errors.getvalue())

    def test_dry_run_rejects_changed_frame_then_imports_one_role_without_touching_legacy(self):
        target = self.project / importer.RESOURCE_FAMILY / self.CHARACTER
        code, plan = self.run_import()
        self.assertEqual(code, 0)
        self.assertEqual(plan["status"], "ready_dry_run")
        self.assertEqual(plan["runtime_file_count"], 140)
        self.assertFalse(target.exists())

        altered = self.source_dir / importer.SLOT_PATHS[0]
        altered.write_bytes(b"not the pinned PNG")
        code, blocked = self.run_import(execute=True)
        self.assertEqual(code, 1)
        self.assertEqual(blocked["status"], "blocked")
        self.assertFalse(target.exists())
        altered.write_bytes(self.frame_bytes[importer.SLOT_PATHS[0]])

        code, result = self.run_import(execute=True)
        self.assertEqual(code, 0)
        self.assertEqual(result["status"], "imported_pending_unity_validation")
        self.assertEqual(len([path for path in target.rglob("*") if path.is_file()]), 140)
        self.assertEqual(self.legacy.read_bytes(), b"protected legacy resource")
        manifest = json.loads((target / "manifest.json").read_text())
        validation = json.loads((target / "validation.json").read_text())
        activation = json.loads((target / "appearance.json").read_text())
        self.assertEqual(len(manifest["files"]), 137)
        self.assertEqual(validation["manifest_sha256"], importer.file_sha256(target / "manifest.json"))
        self.assertEqual(activation["validation_sha256"], importer.file_sha256(target / "validation.json"))
        self.assertEqual(activation["qc_sha256"], importer.file_sha256(self.approval_document))
        with Image.open(target / "portrait.png") as portrait:
            self.assertEqual((portrait.size, portrait.mode), ((1024, 1024), "RGBA"))

        code, blocked = self.run_import(execute=True)
        self.assertEqual(code, 1)
        self.assertIn("refusing to overwrite", blocked["error"])
        self.assertEqual(self.legacy.read_bytes(), b"protected legacy resource")

    def test_wrong_identity_portrait_inventory_fails_closed(self):
        inventory = json.loads(self.inventory.read_text())
        inventory["characters"][0]["character_id"] = "08_alchemy_prodigy_boy"
        self.inventory.write_bytes(importer.json_bytes(inventory))
        self.delivery["portrait_inventory_sha256"] = importer.file_sha256(self.inventory)
        self.save_delivery()
        with patch.object(importer, "PORTRAIT_INVENTORY_SHA256", self.delivery["portrait_inventory_sha256"]):
            code, blocked = self.run_import()
        self.assertEqual(code, 1)
        self.assertIn("original identity inventory", blocked["error"])
        self.assertFalse((self.project / importer.RESOURCE_FAMILY / self.CHARACTER).exists())

    def test_duplicate_frame_bytes_do_not_count_as_distinct_poses(self):
        first, second = importer.SLOT_PATHS[:2]
        (self.source_dir / second).write_bytes(self.frame_bytes[first])
        self.delivery["slots"][1]["sha256"] = importer.sha256(self.frame_bytes[first])
        self.save_delivery()
        code, blocked = self.run_import(execute=True)
        self.assertEqual(code, 1)
        self.assertIn("identical PNG bytes", blocked["error"])
        self.assertFalse((self.project / importer.RESOURCE_FAMILY / self.CHARACTER).exists())

    def test_only_seven_independently_approved_full_hd_ids_are_allowed(self):
        self.assertEqual(set(importer.APPROVED_DELIVERIES), {
            "06_thunder_caster_boy", "08_alchemy_prodigy_boy", "09_bamboo_archer_girl",
            "10_crimson_spear_girl", "14_short_hair_snow_summoner_girl",
            "17_ghost_script_calligrapher_boy", "20_star_formation_master_girl",
        })
        for character_id in ("07_moon_shadow_assassin_girl", "15_water_dragon_scholar_boy"):
            with self.subTest(character_id=character_id):
                self.delivery["character_id"] = character_id
                self.save_delivery()
                code, blocked = self.run_import(character_id=character_id)
                self.assertEqual(code, 1)
                self.assertIn("seven approved full-HD identities", blocked["error"])

    def test_profile_cannot_self_approve_changed_source_or_approval_documents(self):
        for field, path in (("source_document_sha256", self.source_document),
                            ("approval_document_sha256", self.approval_document)):
            with self.subTest(field=field):
                original = path.read_bytes()
                path.write_bytes(original + b" ")
                self.delivery[field] = importer.file_sha256(path)
                self.save_delivery()
                code, blocked = self.run_import()
                self.assertEqual(code, 1)
                self.assertIn("not independently approved", blocked["error"])
                path.write_bytes(original)
                self.delivery[field] = importer.file_sha256(path)

    def test_opaque_frame_is_rejected_even_when_profile_hash_matches(self):
        relative = importer.SLOT_PATHS[0]
        opaque = self.source_dir / relative
        Image.new("RGBA", (1024, 1024), (22, 44, 66, 255)).save(opaque)
        self.delivery["slots"][0]["sha256"] = importer.file_sha256(opaque)
        self.save_delivery()
        code, blocked = self.run_import()
        self.assertEqual(code, 1)
        self.assertIn("transparent pixels and a visible subject", blocked["error"])

    def test_profile_cannot_replace_a_reviewed_frame_with_new_valid_art(self):
        relative = importer.SLOT_PATHS[0]
        replacement = Image.new("RGBA", (1024, 1024), (0, 0, 0, 0))
        ImageDraw.Draw(replacement).rectangle((300, 300, 700, 800), fill=(200, 40, 80, 255))
        replacement.save(self.source_dir / relative)
        self.delivery["slots"][0]["sha256"] = importer.file_sha256(self.source_dir / relative)
        self.save_delivery()
        code, blocked = self.run_import()
        self.assertEqual(code, 1)
        self.assertIn("differ from the approved source document", blocked["error"])

    def test_changed_approval_during_staging_cannot_be_committed(self):
        target = self.project / importer.RESOURCE_FAMILY / self.CHARACTER
        original_copy = importer.shutil.copy2
        changed = False

        def copy_and_change(source, destination):
            nonlocal changed
            result = original_copy(source, destination)
            if not changed:
                self.approval_document.write_bytes(self.approval_document.read_bytes() + b" ")
                changed = True
            return result

        with patch.object(importer.shutil, "copy2", side_effect=copy_and_change):
            code, blocked = self.run_import(execute=True)
        self.assertEqual(code, 1)
        self.assertIn("Input evidence changed during staging", blocked["error"])
        self.assertFalse(target.exists())


if __name__ == "__main__":
    unittest.main()
