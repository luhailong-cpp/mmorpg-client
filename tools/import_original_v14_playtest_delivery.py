"""Import the two explicitly requested local playtest candidates without art approval.

The seven approved deliveries keep their separate importer and approval pins.
Here the source audit remains pending visual review. This tool only proves the
complete, immutable 8 x 16 walk + 8 idle inventory and original portrait identity.
The runtime needs the narrowly pinned local-playtest contract to select these
resources; ordinary pending artwork remains unavailable.
"""

from __future__ import annotations

import argparse
import json
from pathlib import Path
import sys

import import_original_v14_delivery as hd


SCHEMA = "qdao-original-v14-hd/local-playtest-delivery-v1"
MODE = "local-playtest-20260929-v1"
ART_STATUS = "pending_dynamic_review"
AUTHORIZED_DELIVERIES = {
    "07_moon_shadow_assassin_girl": {
        "source_document_sha256": "753379b98791927da895e6bb96ad17541b6454fed1c6b5925eca4de0e569a631",
        "verification_document_sha256": "9c308e79a37d03b7e77b4c8fc5d9b75f74a12b41e2271f96c29f26f006d44bbe",
        "provenance_document_sha256": "cecac592e6d413d47693e88921efef2b84f5bb9b1408a28f4acc8ad607ccbbea",
        "portrait_source_sha256": "8ccc7e9118ed1ca608a407687e5a76cf392b1f694ef7737ccc28536caae84bfc",
    },
    "15_water_dragon_scholar_boy": {
        "source_document_sha256": "b16409d18fcabe4acce43d35e70d28d8813935be5895b62e96ade5f3e2af0997",
        "portrait_source_sha256": "c3b96a88c2fdebb36951c8b47d3b1860b0e5895ae775bb360beb036bb393de70",
    },
}


def source_slots(character_id: str, source: dict) -> dict[str, str]:
    hd.require(source.get("character") == character_id, "Source audit has a different identity")
    if character_id == "07_moon_shadow_assassin_girl":
        hd.require(source.get("walk") == 128 and source.get("independentIdle") == 8 and
                   source.get("missingSlots") == [] and source.get("all1024RGBA") is True and
                   source.get("selectedRawAwaitingExport") == [] and
                   source.get("all136FinalShaMatchManifestAndAudits") is True,
                   "07 current candidate inventory is not complete")
        hd.require(source.get("offlineAcceptanceComplete") is False and
                   source.get("formalApproval") is False and source.get("allPassed") is False,
                   "07 source review state changed; re-audit instead of asserting approval")
        return hd.document_slot_hashes(source.get("bindings"), "slot", "", "07 source audit")
    hd.require(source.get("walkCount") == 128 and source.get("idleCount") == 8 and
               source.get("sourceUniqueCount") == 136 and source.get("outputUniqueCount") == 136 and
               source.get("allMachineChecksPass") is True and source.get("allGifTimingPass") is True,
               "15 current candidate inventory is not complete")
    hd.require(source.get("dynamicPlaybackObserved") is False and
               source.get("machineChecksAreNotArtAcceptance") is True,
               "15 source review state changed; re-audit instead of asserting approval")
    return hd.document_slot_hashes(source.get("frames"), "slot", "", "15 source audit")


def build_plan(args: argparse.Namespace) -> dict:
    project = args.project.resolve()
    hd.require((project / "Assets").is_dir() and (project / "ProjectSettings/ProjectVersion.txt").is_file(),
               f"Target is not a Unity project: {project}")
    profile_path = args.manifest.resolve()
    delivery, profile_bytes = hd.read_json(profile_path, "playtest delivery")
    character_id = args.character_id
    hd.require(character_id in AUTHORIZED_DELIVERIES and delivery.get("character_id") == character_id,
               "Only the explicitly requested 07 and 15 playtest identities are allowed")
    hd.require(delivery.get("schema") == SCHEMA and delivery.get("integration_mode") == MODE and
               delivery.get("status") == "authorized_local_playtest" and
               delivery.get("art_review_status") == ART_STATUS,
               "Delivery must retain its local playtest and pending art review state")
    authorization = delivery.get("user_authorization")
    hd.require(isinstance(authorization, dict) and authorization.get("date") == "2026-09-29" and
               authorization.get("scope") == "integrate_existing_v14_characters_for_local_playtest" and
               authorization.get("art_approval_claimed") is False,
               "Missing bounded user authorization; import is not art approval")
    reference_root = Path(__file__).resolve().parents[1]
    source_dir = hd.input_path(None, delivery, "source_dir", reference_root)
    evidence = [(profile_path, hd.sha256(profile_bytes))]
    documents = {}
    pins = AUTHORIZED_DELIVERIES[character_id]
    for hash_field, pin in pins.items():
        field = hash_field.removesuffix("_sha256")
        path = hd.input_path(None, delivery, field, reference_root)
        digest = hd.check_document(path, delivery.get(hash_field), field)
        hd.require(digest == pin, f"{field} is not the pinned current playtest evidence")
        evidence.append((path, digest))
        documents[field] = path
    source, _ = hd.read_json(documents["source_document"], "source audit")
    authoritative_slots = source_slots(character_id, source)
    if character_id.startswith("07_"):
        verification, _ = hd.read_json(documents["verification_document"], "source reconstruction")
        rows = verification.get("rows")
        hd.require(verification.get("verified") == 136 and isinstance(rows, list) and len(rows) == 136,
                   "07 source reconstruction inventory is incomplete")
        reconstructed = {row.get("slot"): row.get("outputSha256") for row in rows}
        hd.require(reconstructed == authoritative_slots and all(row.get("finalShaVerified") is True for row in rows),
                   "07 source reconstruction differs from selected audit slots")
        provenance, _ = hd.read_json(documents["provenance_document"], "source provenance")
        hd.require(provenance.get("character") == character_id, "07 provenance identity differs")
    portrait_source = documents["portrait_source"]
    portrait_inventory, inventory_hash = hd.check_portrait_inventory(
        delivery, reference_root, character_id, pins["portrait_source_sha256"])
    evidence.append((portrait_inventory, inventory_hash))
    hd.check_png(portrait_source, (4096, 4096), "original portrait")
    slots = hd.check_slots(delivery, source_dir)
    hd.require({relative: digest for relative, _, digest in slots} == authoritative_slots,
               "Delivery slot hashes differ from the current source audit")
    portrait_bytes = hd.portrait_png(portrait_source)
    portrait_hash = hd.sha256(portrait_bytes)
    profile_hash = hd.sha256(profile_bytes)
    files = [{"path": "portrait.png", "sha256": portrait_hash}]
    files.extend({"path": relative, "sha256": digest} for relative, _, digest in slots)
    review = {"integration_mode": MODE, "art_review_status": ART_STATUS,
              "source_dynamic_review_complete": False, "source_art_approval_claimed": False,
              "source_document_sha256": pins["source_document_sha256"],
              "source_manifest_sha256": profile_hash}
    manifest = {
        "version": 14, "character_id": character_id, "status": "passed", "visual_review": ART_STATUS,
        "frame_size": [1024, 1024], "portrait_size": [1024, 1024],
        "frame_count": 16, "frame_duration_ms": 30, "cycle_duration_ms": 480,
        "dedicated_idle": True, "contact_frame": 0,
        "alignment": {"alignment_version": 2, "root_px": [512, 942]},
        "runtime_geometry": {"reference_frame_size": 512, "pixels_per_unit": 104, "pivot": [0.5, 0.08]},
        "source_commit": hd.SOURCE_COMMIT, "source_family": "original-00-22",
        "portrait_source_sha256": pins["portrait_source_sha256"],
        "portrait_inventory_sha256": inventory_hash, "files": files, **review,
    }
    manifest_bytes = hd.json_bytes(manifest)
    manifest_hash = hd.sha256(manifest_bytes)
    validation = {
        "schema": "qdao-original-v14-hd/local-playtest-validation-v1", "version": 14,
        "character_id": character_id, "status": "passed", "visual_review": ART_STATUS,
        "scope": "all8_inventory_identity_png_hash_geometry_only",
        "manifest_sha256": manifest_hash, "qc_sha256": pins["source_document_sha256"],
        "qc_sha256_source": "pending_source_audit_not_art_approval",
        "portrait_source_sha256": pins["portrait_source_sha256"],
        "portrait_processing": "whole_image_LANCZOS_downsample_no_crop",
        "runtime_png_sha256": {row["path"]: row["sha256"] for row in files}, **review,
    }
    validation_bytes = hd.json_bytes(validation)
    activation = {
        "version": 14, "characterId": character_id, "status": "passed", "visualReview": ART_STATUS,
        "frameCount": 16, "frameDurationMs": 30, "cycleDurationMs": 480,
        "alignmentVersion": 2, "dedicatedIdle": True, "contactFrame": 0,
        "frameWidth": 1024, "frameHeight": 1024, "portraitWidth": 1024, "portraitHeight": 1024,
        "pixelsPerUnit": 104, "pivotX": 0.5, "pivotY": 0.08,
        "manifest_sha256": manifest_hash, "qc_sha256": pins["source_document_sha256"],
        "validation_sha256": hd.sha256(validation_bytes),
        "sourceCommit": hd.SOURCE_COMMIT, "sourceFamily": "original-00-22", **review,
    }
    metadata = {"manifest.json": manifest_bytes, "validation.json": validation_bytes,
                "appearance.json": hd.json_bytes(activation)}
    outputs = {"portrait.png": portrait_hash, **{name: hd.sha256(data) for name, data in metadata.items()}}
    outputs.update({relative: digest for relative, _, digest in slots})
    hd.require(len(outputs) == 140 and set(outputs) == hd.OUTPUT_SET, "Invalid runtime inventory")
    target = project / hd.RESOURCE_FAMILY / character_id
    hd.require(not target.exists() and not target.with_suffix(".meta").exists(),
               f"Character or GUID already exists; refusing to overwrite: {target}")
    evidence.extend((source_path, digest) for _, source_path, digest in slots)
    return {"project": project, "target": target, "character_id": character_id,
            "source_manifest_sha256": profile_hash, "source_document_sha256": pins["source_document_sha256"],
            "evidence": evidence, "slots": slots, "portrait_bytes": portrait_bytes,
            "metadata": metadata, "outputs": outputs}


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--manifest", type=Path, required=True)
    parser.add_argument("--character-id", required=True)
    parser.add_argument("--project", type=Path, required=True)
    parser.add_argument("--execute", action="store_true")
    args = parser.parse_args(argv)
    try:
        plan = build_plan(args)
        if args.execute:
            hd.execute(plan)
        print(json.dumps({"status": "imported_playtest_pending_unity" if args.execute else "ready_dry_run",
                          "writes_performed": args.execute, "target": str(plan["target"]),
                          "character_id": plan["character_id"], "integration_mode": MODE,
                          "source_art_review": ART_STATUS, "runtime_file_count": len(plan["outputs"]),
                          "source_manifest_sha256": plan["source_manifest_sha256"],
                          "source_document_sha256": plan["source_document_sha256"],
                          "manifest_sha256": plan["outputs"]["manifest.json"],
                          "activation_sha256": plan["outputs"]["appearance.json"]}, indent=2))
        return 0
    except (hd.DeliveryError, OSError) as error:
        print(json.dumps({"status": "blocked", "writes_performed": False, "error": str(error)}), file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
