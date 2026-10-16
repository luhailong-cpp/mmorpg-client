"""Integration tests for the batch importer, using small generated fixture PNGs."""

import hashlib
import json
import tempfile
import unittest
from pathlib import Path

from PIL import Image

import sync_city_npcs as sync


def write_json(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value), encoding="utf-8")


def snapshot(root):
    return {p.relative_to(root).as_posix(): p.read_bytes() for p in root.rglob("*") if p.is_file()}


class CityNpcImportTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.source = self.root / "delivery"
        self.project = self.root / "client"
        self.positions = self.root / "placements.json"
        self.make_delivery()

    def make_delivery(self, legacy=False):
        self.source.mkdir(exist_ok=True)
        size = (1024, 1024) if legacy else (12, 20)
        bbox = [200, 120, 800, 900] if legacy else [2, 3, 10, 18]
        entries, positions = [], []
        for number in range(1, 24):
            npc_id = f"{number:02}"
            image = Image.new("RGBA", size, (0, 0, 0, 0))
            image.paste((120, 70, 30, 255), bbox)
            path = self.source / f"{npc_id}-fixture.png"
            image.save(path)
            image_entry = dict(file=path.name, sha256=hashlib.sha256(path.read_bytes()).hexdigest(), visibleBBox=bbox)
            entry = dict(id=npc_id, name=f"NPC {npc_id}", actualQuality=None)
            if legacy:
                entry.update(actualModel="gpt-image", export=[dict(image_entry, size=1024)])
            else:
                entry.update(image_entry, nativeSize=list(size), actualModelFamily="gpt-image", actualModelVersion=None)
            entries.append(entry)
            positions.append(dict(id=npc_id, displayName=f"Existing {npc_id}", x=60 + number * 5, z=40 + number * 3,
                                  visibleHeight=6 + number / 10, pivotX=0.3, pivotY=0.12, location=f"Place {npc_id}"))
        self.manifest = dict(npcCount=23, entries=entries)
        self.position_data = dict(entries=positions)
        self.save_inputs()

    def save_inputs(self):
        write_json(self.source / "manifest.json", self.manifest)
        write_json(self.positions, self.position_data)

    def run_import(self, **kwargs):
        plan = sync.build_plan(self.source, self.positions, self.project, **kwargs)
        plan.apply()
        return plan

    def test_native_pngs_keep_bytes_dimensions_positions_and_new_anchors(self):
        anchor_path = self.root / "anchors.json"
        write_json(anchor_path, {"01": {"pixelX": 7, "pixelY": 17}})
        plan = self.run_import(anchors=anchor_path)
        for before, after in zip(self.position_data["entries"], plan.placements["entries"]):
            for key in ("x", "z", "displayName", "visibleHeight", "location"):
                self.assertEqual(before[key], after[key], key)
        first, second = plan.placements["entries"][:2]
        self.assertAlmostEqual(first["pivotX"], 7 / 12)
        self.assertAlmostEqual(first["pivotY"], 1 - 17 / 20)
        self.assertAlmostEqual(first["visibleHeightFraction"], 15 / 20)
        self.assertEqual((first["nativeWidth"], first["nativeHeight"]), (12, 20))
        self.assertAlmostEqual(second["pivotX"], 0.5)
        self.assertAlmostEqual(second["pivotY"], 0.1)
        self.assertNotEqual(second["pivotY"], self.position_data["entries"][1]["pivotY"])
        for record in plan.sources["entries"]:
            original, copied = Path(record["source"]), self.project / record["destination"]
            self.assertEqual(original.read_bytes(), copied.read_bytes())
            with Image.open(copied) as image:
                self.assertEqual(image.size, (12, 20))

    def test_bad_late_inputs_leave_existing_project_byte_identical(self):
        self.run_import()
        before = snapshot(self.project)
        for failure in ("duplicate_id", "missing_id", "missing_file", "bad_hash", "bad_dimensions", "bad_alpha", "non_rgba"):
            with self.subTest(failure=failure):
                self.make_delivery()
                last = self.manifest["entries"][-1]
                if failure == "duplicate_id":
                    last["id"] = "01"
                elif failure == "missing_id":
                    self.manifest["entries"].pop()
                elif failure == "missing_file":
                    (self.source / last["file"]).unlink()
                elif failure == "bad_hash":
                    last["sha256"] = "0" * 64
                elif failure == "bad_dimensions":
                    last["nativeSize"] = [13, 20]
                else:
                    path = self.source / last["file"]
                    Image.new("RGB" if failure == "non_rgba" else "RGBA", (12, 20), "red").save(path)
                    last["sha256"] = hashlib.sha256(path.read_bytes()).hexdigest()
                self.save_inputs()
                with self.assertRaises(sync.ValidationError):
                    self.run_import()
                self.assertEqual(before, snapshot(self.project))

    def test_old_export_schema_preserves_legacy_pivots(self):
        self.make_delivery(legacy=True)
        plan = self.run_import()
        self.assertEqual(len(plan.placements["entries"]), 23)
        first = plan.placements["entries"][0]
        self.assertAlmostEqual(first["pivotX"], 0.3)
        self.assertAlmostEqual(first["pivotY"], 0.12)
        self.assertAlmostEqual(first["visibleHeightFraction"], 780 / 1024)
        record = plan.sources["entries"][0]
        self.assertEqual(record["sourceSchema"], "legacy-export-1024")
        self.assertEqual(record["actualModelFamily"], "gpt-image")
        self.assertIsNone(record["actualModelVersion"])

    def test_existing_meta_guids_survive_and_2048_import_settings_replace_old_cap(self):
        self.run_import()
        meta = self.project / sync.RESOURCE / "Sprites/01-fixture.png.meta"
        guid = "1234567890abcdef1234567890abcdef"
        meta.write_text(f"fileFormatVersion: 2\nguid: {guid}\nTextureImporter:\n  maxTextureSize: 1024\n", encoding="utf-8")
        untouched = self.project / sync.RESOURCE / "Sprites/previous-unused.png"
        untouched.write_bytes(b"old asset is not deleted")
        self.run_import()
        text = meta.read_text(encoding="utf-8")
        self.assertIn(f"guid: {guid}", text)
        self.assertIn("maxTextureSize: 2048", text)
        self.assertNotIn("maxTextureSize: 1024", text)
        self.assertEqual(untouched.read_bytes(), b"old asset is not deleted")

    def test_dry_run_and_missing_placements_never_create_project(self):
        result = sync.main(["--source", str(self.source), "--positions", str(self.positions),
                            "--project-root", str(self.project), "--dry-run"])
        self.assertEqual(result, 0)
        self.assertFalse(self.project.exists())
        self.positions.unlink()
        with self.assertRaisesRegex(sync.ValidationError, "provide --positions"):
            self.run_import()
        self.assertFalse(self.project.exists())

    def test_evidence_separates_targets_submissions_and_actuals_and_keeps_historical_references(self):
        record_path = self.source / "01.generation.json"
        historical = [{"path": "../old-deleted-reference.png", "role": "identity"}]
        write_json(record_path, dict(configSnapshot={"model": "configured-target", "quality": "max"},
                                     submittedParameters={"model": None, "quality": None},
                                     actualModel="gpt-image", actualModelVersion=None, actualQuality=None,
                                     references=historical))
        self.manifest["entries"][0]["generationRecord"] = record_path.name
        self.save_inputs()
        record = self.run_import().sources["entries"][0]
        self.assertEqual(record["configTarget"]["model"], "configured-target")
        self.assertIsNone(record["submittedParameters"]["model"])
        self.assertIsNone(record["actualModelVersion"])
        self.assertIsNone(record["actualQuality"])
        self.assertEqual(record["historicalGenerationReferences"], historical)
        self.assertEqual(record["sourceGenerationRecordStatus"], "present")
        self.assertFalse((self.source.parent / "old-deleted-reference.png").exists())

    def test_duplicate_placements_and_out_of_bounds_anchor_are_rejected_before_writes(self):
        self.position_data["entries"][-1]["id"] = "01"
        self.save_inputs()
        with self.assertRaisesRegex(sync.ValidationError, "Duplicate NPC id"):
            self.run_import()
        self.assertFalse(self.project.exists())
        self.make_delivery()
        anchors = self.root / "anchors.json"
        write_json(anchors, {"23": {"pixelX": 13, "pixelY": 19}})
        with self.assertRaisesRegex(sync.ValidationError, "outside its image"):
            self.run_import(anchors=anchors)
        self.assertFalse(self.project.exists())


if __name__ == "__main__":
    unittest.main()
