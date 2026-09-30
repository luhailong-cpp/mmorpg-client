"""Regression checks at the separate pending-art local import boundary."""

import argparse
from io import BytesIO
import importlib.util
import json
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch

from PIL import Image, ImageDraw

TOOLS = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(TOOLS))
import import_original_v14_delivery as hd
import import_original_v14_playtest_delivery as playtest


class LocalPlaytestDeliveryTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.project = self.root / "project"
        (self.project / "Assets").mkdir(parents=True)
        (self.project / "ProjectSettings").mkdir()
        (self.project / "ProjectSettings/ProjectVersion.txt").write_text("m_EditorVersion: 6000.6.0f1\n")
        self.cid = "15_water_dragon_scholar_boy"
        self.source = self.root / "source"
        self.slots = []
        for number, slot in enumerate(hd.SLOT_PATHS):
            image = Image.new("RGBA", (1024, 1024), (0, 0, 0, 0))
            ImageDraw.Draw(image).rectangle((400, 200, 650, 942), fill=(number, 150, 200, 255))
            path = self.source / slot
            path.parent.mkdir(parents=True, exist_ok=True)
            image.save(path)
            self.slots.append({"path": slot, "sha256": hd.file_sha256(path), "width": 1024, "height": 1024})
        self.audit = self.root / "source-audit.json"
        self.audit.write_bytes(hd.json_bytes({
            "character": self.cid, "walkCount": 128, "idleCount": 8,
            "sourceUniqueCount": 136, "outputUniqueCount": 136, "allMachineChecksPass": True,
            "allGifTimingPass": True, "dynamicPlaybackObserved": False,
            "machineChecksAreNotArtAcceptance": True,
            "frames": [{"slot": row["path"], "sha256": row["sha256"]} for row in self.slots],
        }))
        self.portrait = self.root / "portrait.png"
        portrait = Image.new("RGBA", (4096, 4096), (0, 0, 0, 0))
        ImageDraw.Draw(portrait).rectangle((1000, 500, 3000, 3800), fill=(10, 100, 180, 255))
        portrait.save(self.portrait)
        portrait_hash = hd.file_sha256(self.portrait)
        self.inventory = self.root / "inventory.json"
        self.inventory.write_bytes(hd.json_bytes({"source_commit": hd.SOURCE_COMMIT, "characters": [{
            "character_id": self.cid, "source_commit": hd.SOURCE_COMMIT,
            "sha256": portrait_hash, "git_manifest_sha256": portrait_hash,
        }]}))
        self.pins = {"source_document_sha256": hd.file_sha256(self.audit), "portrait_source_sha256": portrait_hash}
        self.delivery = {"schema": playtest.SCHEMA, "character_id": self.cid,
                         "status": "authorized_local_playtest", "integration_mode": playtest.MODE,
                         "art_review_status": playtest.ART_STATUS,
                         "user_authorization": {"date": "2026-09-29",
                                                "scope": "integrate_existing_v14_characters_for_local_playtest",
                                                "art_approval_claimed": False},
                         "source_dir": str(self.source), "source_document": str(self.audit),
                         "portrait_source": str(self.portrait), "portrait_inventory": str(self.inventory),
                         "portrait_inventory_sha256": hd.file_sha256(self.inventory),
                         "slots": self.slots, **self.pins}
        self.profile = self.root / "delivery.json"
        self.args = argparse.Namespace(project=self.project, manifest=self.profile, character_id=self.cid)
        self.save()
        pins = patch.dict(playtest.AUTHORIZED_DELIVERIES, {self.cid: self.pins})
        pins.start()
        self.addCleanup(pins.stop)
        inventory = patch.object(hd, "PORTRAIT_INVENTORY_SHA256", hd.file_sha256(self.inventory))
        inventory.start()
        self.addCleanup(inventory.stop)

    def save(self):
        self.profile.write_bytes(hd.json_bytes(self.delivery))

    def test_dry_run_and_atomic_import_keep_pending_art_and_original_frames(self):
        legacy = self.project / "Assets/legacy.txt"
        legacy.write_bytes(b"unrelated existing work")
        plan = playtest.build_plan(self.args)
        self.assertFalse(plan["target"].exists())
        self.assertEqual(len(plan["outputs"]), 140)
        hd.execute(plan)
        target = plan["target"]
        self.assertEqual(len(list(target.rglob("*.png"))), 137)
        for slot in self.slots:
            self.assertEqual(hd.file_sha256(target / slot["path"]), slot["sha256"])
        manifest = json.loads((target / "manifest.json").read_text())
        activation = json.loads((target / "appearance.json").read_text())
        validation = json.loads((target / "validation.json").read_text())
        self.assertEqual(manifest["visual_review"], playtest.ART_STATUS)
        self.assertEqual(activation["visualReview"], playtest.ART_STATUS)
        self.assertFalse(validation["source_art_approval_claimed"])
        self.assertFalse(validation["source_dynamic_review_complete"])
        self.assertEqual(validation["manifest_sha256"], hd.file_sha256(target / "manifest.json"))
        self.assertEqual(activation["validation_sha256"], hd.file_sha256(target / "validation.json"))
        self.assertEqual(legacy.read_bytes(), b"unrelated existing work")
        with self.assertRaisesRegex(hd.DeliveryError, "refusing to overwrite"):
            playtest.build_plan(self.args)

    def test_changed_png_is_rejected_before_writing(self):
        (self.source / self.slots[0]["path"]).write_bytes(b"changed during another artwork task")
        with self.assertRaises(hd.DeliveryError):
            playtest.build_plan(self.args)
        self.assertFalse((self.project / hd.RESOURCE_FAMILY).exists())

    def test_profile_cannot_reapprove_pending_art(self):
        self.delivery["art_review_status"] = "passed"
        self.save()
        with self.assertRaisesRegex(hd.DeliveryError, "pending art review"):
            playtest.build_plan(self.args)

    def test_source_audit_cannot_be_replaced_by_self_approved_profile(self):
        self.audit.write_bytes(self.audit.read_bytes() + b"\n")
        self.delivery["source_document_sha256"] = hd.file_sha256(self.audit)
        self.save()
        with self.assertRaisesRegex(hd.DeliveryError, "pinned current playtest evidence"):
            playtest.build_plan(self.args)

    def test_unrequested_identity_and_missing_slot_are_rejected(self):
        self.args.character_id = "11_jade_fist_flat_top_boy"
        with self.assertRaisesRegex(hd.DeliveryError, "07 and 15"):
            playtest.build_plan(self.args)
        self.args.character_id = self.cid
        self.delivery["slots"] = self.slots[:-1]
        self.save()
        with self.assertRaisesRegex(hd.DeliveryError, "136"):
            playtest.build_plan(self.args)

    def test_approved_importer_keeps_rejecting_both_pending_identities(self):
        for cid in playtest.AUTHORIZED_DELIVERIES:
            self.args.character_id = cid
            with self.assertRaisesRegex(hd.DeliveryError, "seven approved"):
                hd.build_plan(self.args)

    def test_07_audit_slots_require_complete_pending_bound_materials(self):
        cid = "07_moon_shadow_assassin_girl"
        audit = {"character": cid, "walk": 128, "independentIdle": 8,
                 "missingSlots": [], "all1024RGBA": True, "selectedRawAwaitingExport": [],
                 "all136FinalShaMatchManifestAndAudits": True, "offlineAcceptanceComplete": False,
                 "formalApproval": False, "allPassed": False,
                 "bindings": [{"slot": row["path"], "sha256": row["sha256"]} for row in self.slots]}
        self.assertEqual(len(playtest.source_slots(cid, audit)), 136)
        audit["allPassed"] = True
        with self.assertRaisesRegex(hd.DeliveryError, "re-audit"):
            playtest.source_slots(cid, audit)


if __name__ == "__main__":
    unittest.main()
