"""Import one approved, complete original-roster V14 HD delivery into Unity.

The normalized delivery manifest is an audited input snapshot, not an art
approval tool. Its document hashes must be pinned before this command runs.
This verifies document bytes and PNG/identity/geometry contracts; it cannot
repeat a human visual review or a real Unity run. A successful import still
needs Unity index generation and runtime checks in the target project.
Dry run is the default; --execute is the only path that writes client assets.
"""

from __future__ import annotations

import argparse
import hashlib
from io import BytesIO
import json
from pathlib import Path
import re
import shutil
import sys
import tempfile

from PIL import Image


SCHEMA = "qdao-original-v14-hd/import-delivery-v1"
SOURCE_COMMIT = "9adcf9291e4a867601868889a5965f3cd48630ba"
RESOURCE_FAMILY = Path("Assets/Resources/World/Characters/QdaoOriginalRosterV14")
DIRECTIONS = ("N", "NE", "E", "SE", "S", "SW", "W", "NW")
# These pins come from the seven independently reviewed full-HD art deliveries.
# A delivery profile is an input inventory, not an authority to approve itself.
APPROVED_DELIVERIES = {
    "06_thunder_caster_boy": (
        "da2b07c19c4118ff2b0756afa5974ed097a75e1563d6ec5207512b6cb194f635",
        "541bdf51ee82fbab61288ec2be126fbf143853b8fbc7231d7b3bad7104e35b78",
        "abdfbe1fdc243ea6360a5d588f9ff1294fa2f0d43876d8b63aa452ec4889cd31"),
    "08_alchemy_prodigy_boy": (
        "d605cb139692c6d0e9b4fe0354829d41ed64b7e3b64bb73f7cf32e3b6d5c5107",
        "9853b7475f6b8dc7850d3c020692fbe7b3dc759a38687d6dd14ed099593eec65",
        "fcbf04089dbc229887d9c18b2b21e9dfb7997c5ec28c086e45db2d78e9bef82e"),
    "09_bamboo_archer_girl": (
        "589d77a4d100c409c16055f33bce238d6964d0d0a3a53841864533f6bc583ae3",
        "13e971458e04952d3d2292e9de655f3a58b727841ad160b4eea4f8ee9614e3c1",
        "c7ba2937630b589f16c0581f76421f20eccb19599aba6b47cca748de450447dd"),
    "10_crimson_spear_girl": (
        "74a0144906323bf4767a8b72e3f8883bb86a01d2c5b7df6b84c84e7458b7ffab",
        "3c5d40bd2a6f31bd97fed03552de3b08f3b9db000d929583056dfed12bda0382",
        "ea068eb40dbd372b108d2e7d041c8e270e898f8ae979ce039873a08e49a0260f"),
    "14_short_hair_snow_summoner_girl": (
        "0e0686ac4e0cdf94189d0a2e1e3290807a33f4a5bf12bf3d7798636b8b29facf",
        "d5e7b0d60bb554072b1d95fc5252fad08c34d03fb2a5ad6601670c5e13f6c5d6",
        "834fbe56bc3de4aa8a241d7e12a9725095cfcf486b98066876a8e70fa343c2a8"),
    "17_ghost_script_calligrapher_boy": (
        "fecd8028b6facb779ee6f8f86d28243937912bca790cf61997368ecc1ceeea8d",
        "fc8e3e1337264c5498a2e89d4733304cd4615753b8511d35a167920add08f575",
        "34d13fe2558dee7ab8403180a7cd4c26e95b0b9795181053057b5759c69a5614"),
    "20_star_formation_master_girl": (
        "780e2efe3a620dc60ba231c1d69458db021ab1b56cfcaf31e9a7d74076317638",
        "f523981c0581f51783c8ac093d85c40cfe5fabcc40ce8250206ae8f4256090e9",
        "4a5de67ffb880a852554d460c709f7179bffd3706fa7bf825767e5369e9bd3e5"),
}
PORTRAIT_INVENTORY_SHA256 = "0a548c644fb64452291622318d5df922abec8af55ff82e5355e2675f49688826"
SLOT_PATHS = tuple(
    [f"idle/{direction}.png" for direction in DIRECTIONS]
    + [f"walk/{direction}/{frame:02d}.png" for direction in DIRECTIONS for frame in range(1, 17)]
)
SLOT_SET = frozenset(SLOT_PATHS)
OUTPUT_SET = SLOT_SET | {"portrait.png", "manifest.json", "validation.json", "appearance.json"}
HASH_PATTERN = re.compile(r"[0-9a-fA-F]{64}\Z")


class DeliveryError(ValueError):
    """Input cannot be imported without violating the V14 resource contract."""


def require(condition: bool, message: str) -> None:
    if not condition:
        raise DeliveryError(message)


def sha256(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def file_sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def pinned_hash(value: object, label: str) -> str:
    require(isinstance(value, str) and HASH_PATTERN.fullmatch(value) is not None,
            f"{label} must be a 64-character SHA256")
    return value.lower()


def read_json(path: Path, label: str) -> tuple[dict, bytes]:
    require(path.is_file(), f"Missing {label}: {path}")
    contents = path.read_bytes()
    try:
        document = json.loads(contents.decode("utf-8-sig"))
    except (UnicodeDecodeError, json.JSONDecodeError) as error:
        raise DeliveryError(f"Invalid {label}: {error}") from error
    require(isinstance(document, dict), f"{label} must be a JSON object")
    return document, contents


def json_bytes(value: dict) -> bytes:
    return (json.dumps(value, ensure_ascii=False, indent=2, sort_keys=True) + "\n").encode("utf-8")


def input_path(explicit: Path | None, delivery: dict, field: str, reference_root: Path) -> Path:
    raw = explicit if explicit is not None else delivery.get(field)
    require(isinstance(raw, (str, Path)) and str(raw).strip(),
            f"Provide --{field.replace('_', '-')} or {field} in the delivery manifest")
    path = Path(raw)
    # Delivery paths are relative to the client checkout holding this tool,
    # even when --project points at a separate Unity test copy.
    return (path if path.is_absolute() else reference_root / path).resolve()


def check_document(path: Path, expected: object, label: str) -> str:
    require(path.is_file() and not path.is_symlink(), f"Missing or linked {label}: {path}")
    pinned = pinned_hash(expected, f"{label} SHA256")
    require(file_sha256(path) == pinned, f"{label} SHA256 differs from delivery manifest: {path}")
    return pinned


def check_png(path: Path, expected_size: tuple[int, int], label: str) -> None:
    require(path.is_file() and not path.is_symlink(), f"Missing or linked {label}: {path}")
    try:
        with Image.open(path) as image:
            require(image.format == "PNG" and image.mode == "RGBA" and image.size == expected_size,
                    f"{label} must be a {expected_size[0]}x{expected_size[1]} RGBA PNG: {path}")
            image.load()
            minimum, maximum = image.getchannel("A").getextrema()
            require(minimum == 0 and maximum > 0,
                    f"{label} needs transparent pixels and a visible subject: {path}")
    except (OSError, ValueError) as error:
        if isinstance(error, DeliveryError):
            raise
        raise DeliveryError(f"Cannot decode {label}: {path}: {error}") from error


def portrait_png(path: Path) -> bytes:
    # Matches qdao_original_roster_v14_hd/tools/pipeline.py: imread() converts
    # to RGBA, then resizes the entire original image with Pillow LANCZOS.
    with Image.open(path) as source:
        image = source.convert("RGBA").resize((1024, 1024), Image.Resampling.LANCZOS)
    output = BytesIO()
    image.save(output, format="PNG")
    encoded = output.getvalue()
    with Image.open(BytesIO(encoded)) as result:
        require(result.format == "PNG" and result.mode == "RGBA" and result.size == (1024, 1024),
                "Derived portrait differs from the 1024 RGBA contract")
        result.load()
    return encoded


def check_slots(delivery: dict, source_dir: Path) -> list[tuple[str, Path, str]]:
    require(source_dir.is_dir() and not source_dir.is_symlink(), f"Missing or linked source directory: {source_dir}")
    slots = delivery.get("slots")
    require(isinstance(slots, list) and len(slots) == 136, "Delivery needs exactly 136 walk/idle slot rows")
    selected: dict[str, tuple[Path, str]] = {}
    for row in slots:
        require(isinstance(row, dict), "Every slot must be an object")
        relative = row.get("path")
        require(isinstance(relative, str) and relative in SLOT_SET and relative not in selected,
                f"Unexpected, duplicate, or missing slot path: {relative}")
        require(type(row.get("width")) is int and type(row.get("height")) is int and
                row["width"] == row["height"] == 1024,
                f"Only native 1024x1024 full-HD frames are supported: {relative}")
        digest = pinned_hash(row.get("sha256"), f"{relative} SHA256")
        raw_source = source_dir / relative
        require(not raw_source.is_symlink(), f"Linked slot source is not allowed: {relative}")
        source = raw_source.resolve()
        require(source.is_relative_to(source_dir), f"Slot escapes source directory: {relative}")
        check_png(source, (1024, 1024), relative)
        require(file_sha256(source) == digest, f"Source PNG SHA256 differs: {relative}")
        selected[relative] = (source, digest)
    require(set(selected) == SLOT_SET, "Delivery omits required walk/idle slots")
    require(len({digest for _, digest in selected.values()}) == len(SLOT_PATHS),
            "Delivery repeats identical PNG bytes across walk/idle slots")
    return [(relative, *selected[relative]) for relative in SLOT_PATHS]


def document_slot_hashes(rows: object, path_field: str, prefix: str, label: str) -> dict[str, str]:
    require(isinstance(rows, list) and len(rows) == 136, f"{label} needs 136 slot rows")
    selected = {}
    for row in rows:
        require(isinstance(row, dict), f"{label} has a non-object slot row")
        relative = row.get(path_field)
        require(isinstance(relative, str), f"{label} has an invalid slot path")
        if prefix:
            require(relative.startswith(prefix), f"{label} has an unexpected slot prefix: {relative}")
            relative = relative[len(prefix):]
        if not relative.endswith(".png"):
            relative += ".png"
        require(relative in SLOT_SET and relative not in selected,
                f"{label} has an unexpected or duplicate slot: {relative}")
        selected[relative] = pinned_hash(row.get("sha256"), f"{label} {relative} SHA256")
    require(set(selected) == SLOT_SET, f"{label} omits a walk/idle slot")
    return selected


def check_art_evidence(character_id: str, source: dict, approval: dict,
                       source_hash: str, approval_hash: str) -> dict[str, str]:
    """Bind profile slots to the reviewed art document, across its seven formats."""
    identity_key = "character_id" if character_id[:2] in ("06", "08", "17") else "character"
    require(source.get(identity_key) == character_id and approval.get(identity_key) == character_id,
            "Source and approval documents must identify the selected character")
    if character_id.startswith(("06_", "17_")):
        slots = document_slot_hashes(source.get("files"), "path", "", "source document")
        require(approval.get("manifest_sha256") == source_hash,
                "Approval does not bind the source document")
        if character_id.startswith("06_"):
            require(source.get("status") == "complete_passed_offline" and
                    approval.get("status") == "passed_offline", "06 offline approval is absent")
        else:
            require(approval.get("offline_visual_approval") is True and
                    approval.get("materials_complete") is True, "17 offline approval is absent")
    elif character_id.startswith("08_"):
        slots = document_slot_hashes(source.get("files"), "path", "runtime/", "source document")
        require(source.get("status") == "offline_accepted" and source.get("visual_approval") is True and
                source.get("visual_acceptance_sha256") == approval_hash and
                approval.get("visual_approval") is True and
                approval.get("unresolved_visual_blockers") == [], "08 offline approval is absent")
        require(document_slot_hashes(approval.get("files"), "slot", "", "approval document") == slots,
                "08 approval slots differ from source document")
    elif character_id.startswith("09_"):
        slots = document_slot_hashes(source.get("files"), "key", "", "source document")
        offline = source.get("offlineAcceptance")
        require(source.get("visualReview") == "passed" and isinstance(offline, dict) and
                offline.get("status") == "passed" and approval.get("status") == "passed" and
                source.get("reviewedSnapshotManifestSha256") == approval.get("reviewedManifestSha256"),
                "09 offline approval is absent or refers to a different reviewed snapshot")
    elif character_id.startswith("10_"):
        frames = source.get("frames")
        require(isinstance(frames, dict), "10 source document needs a frame map")
        slots = document_slot_hashes(list(frames.values()), "file", "", "source document")
        require(source.get("visualReview") == "accepted_offline" and approval.get("status") == "accepted" and
                source.get("acceptedAt") == approval.get("acceptedAt"), "10 offline approval is absent")
        require(document_slot_hashes(approval.get("files"), "file", "", "approval document") == slots,
                "10 approval slots differ from source document")
    elif character_id.startswith("14_"):
        walk, idle = source.get("walk"), source.get("idle")
        require(isinstance(walk, dict) and isinstance(idle, dict) and
                set(walk) == set(idle) == set(DIRECTIONS), "14 source directions are incomplete")
        rows = [row for direction in DIRECTIONS for row in walk[direction]] + [idle[direction] for direction in DIRECTIONS]
        slots = document_slot_hashes(rows, "file", "assets/", "source document")
        require(approval.get("offlineApproved") is True and approval.get("walkCount") == 128 and
                approval.get("idleCount") == 8, "14 offline approval is absent")
        require(document_slot_hashes(approval.get("assets"), "file", "", "approval document") == slots and
                all(row.get("offlineApproved") is True for row in approval["assets"]),
                "14 approval slots differ from source document")
    else:  # 20
        slots = document_slot_hashes(source.get("files"), "slot", "", "source document")
        status = source.get("status")
        require(isinstance(status, dict) and status.get("offlineAcceptance") == "passed" and
                approval.get("offlineVisualAcceptance") == "passed" and
                approval.get("visualApproval") is True, "20 offline approval is absent")
        require(document_slot_hashes(approval.get("files"), "slot", "", "approval document") == slots,
                "20 approval slots differ from source document")
    return slots


def check_portrait_inventory(delivery: dict, reference_root: Path, character_id: str,
                             portrait_hash: str) -> tuple[Path, str]:
    inventory_name = delivery.get("portrait_inventory")
    inventory_hash = delivery.get("portrait_inventory_sha256")
    require(inventory_name is not None and inventory_hash is not None,
            "Portrait inventory path and SHA256 must be provided together")
    inventory_path = input_path(None, delivery, "portrait_inventory", reference_root)
    expected = check_document(inventory_path, inventory_hash, "portrait inventory")
    require(expected == PORTRAIT_INVENTORY_SHA256,
            "Portrait inventory is not the independently pinned original identity inventory")
    inventory, _ = read_json(inventory_path, "portrait inventory")
    require(inventory.get("source_commit") == SOURCE_COMMIT,
            "Portrait inventory has a different original source commit")
    rows = inventory.get("characters")
    require(isinstance(rows, list), "Portrait inventory needs a character list")
    matches = [row for row in rows if isinstance(row, dict) and row.get("character_id") == character_id]
    require(len(matches) == 1 and matches[0].get("source_commit") == SOURCE_COMMIT and
            matches[0].get("sha256") == portrait_hash and
            matches[0].get("git_manifest_sha256") == portrait_hash,
            "Portrait source does not match the original identity inventory")
    return inventory_path, expected


def build_plan(args: argparse.Namespace) -> dict:
    project = args.project.resolve()
    require((project / "Assets").is_dir() and (project / "ProjectSettings/ProjectVersion.txt").is_file(),
            f"Target is not a Unity project: {project}")
    delivery_path = args.manifest.resolve()
    delivery, delivery_data = read_json(delivery_path, "delivery manifest")
    character_id = args.character_id
    require(character_id in APPROVED_DELIVERIES and delivery.get("character_id") == character_id,
            "Character ID must match one of the seven approved full-HD identities")
    require(delivery.get("schema") == SCHEMA and delivery.get("status") == "passed_offline",
            "Delivery needs the audited import-delivery-v1 schema and passed_offline status")
    require(delivery.get("resolution_mode") in (None, "full-hd-v14"),
            "Mixed/512-pixel deliveries are not supported by this importer")
    reference_root = Path(__file__).resolve().parents[1]
    source_dir = input_path(args.source_dir, delivery, "source_dir", reference_root)
    source_document = input_path(args.source_document, delivery, "source_document", reference_root)
    approval_document = input_path(args.approval_document, delivery, "approval_document", reference_root)
    portrait_source = input_path(args.portrait_source, delivery, "portrait_source", reference_root)
    source_document_hash = check_document(source_document, delivery.get("source_document_sha256"), "source document")
    approval_hash = check_document(approval_document, delivery.get("approval_document_sha256"), "approval document")
    portrait_hash = check_document(portrait_source, delivery.get("portrait_source_sha256"), "original portrait")
    require((source_document_hash, approval_hash, portrait_hash) == APPROVED_DELIVERIES[character_id],
            "Source, approval, or portrait evidence is not independently approved for this character")
    portrait_inventory, portrait_inventory_hash = check_portrait_inventory(
        delivery, reference_root, character_id, portrait_hash)
    source_record, _ = read_json(source_document, "source document")
    approval_record, _ = read_json(approval_document, "approval document")
    authoritative_slots = check_art_evidence(character_id, source_record, approval_record,
                                             source_document_hash, approval_hash)
    check_png(portrait_source, (4096, 4096), "original portrait")
    slots = check_slots(delivery, source_dir)
    require({relative: digest for relative, _, digest in slots} == authoritative_slots,
            "Delivery slot hashes differ from the approved source document")
    portrait_bytes = portrait_png(portrait_source)
    portrait_hash_out = sha256(portrait_bytes)
    delivery_hash = sha256(delivery_data)
    files = [{"path": "portrait.png", "sha256": portrait_hash_out}]
    files.extend({"path": relative, "sha256": digest} for relative, _, digest in slots)
    manifest = {
        "version": 14, "character_id": character_id, "status": "passed", "visual_review": "passed",
        "frame_size": [1024, 1024], "portrait_size": [1024, 1024],
        "frame_count": 16, "frame_duration_ms": 30, "cycle_duration_ms": 480,
        "dedicated_idle": True, "contact_frame": 0,
        "alignment": {"alignment_version": 2, "root_px": [512, 942], "per_subject_bbox_scaling": False},
        "runtime_geometry": {"reference_frame_size": 512, "pixels_per_unit": 104, "pivot": [0.5, 0.08]},
        "source_commit": SOURCE_COMMIT, "source_family": "original-00-22",
        "source_manifest_sha256": delivery_hash,
        "source_document_sha256": source_document_hash,
        "approval_document_sha256": approval_hash,
        "portrait_source_sha256": portrait_hash,
        "files": files,
    }
    manifest["portrait_inventory_sha256"] = portrait_inventory_hash
    manifest_bytes = json_bytes(manifest)
    manifest_hash = sha256(manifest_bytes)
    validation = {
        "schema": "qdao-original-v14-hd/import-validation-v1", "version": 14,
        "character_id": character_id, "status": "passed", "visual_review": "passed", "scope": "all8",
        "source_manifest_sha256": delivery_hash,
        "source_document_sha256": source_document_hash,
        "approval_document_sha256": approval_hash,
        "qc_sha256_source": "approval_document_sha256",
        "portrait_source_sha256": portrait_hash,
        "portrait_processing": "whole_image_LANCZOS_downsample_no_crop",
        "manifest_sha256": manifest_hash, "qc_sha256": approval_hash,
        "runtime_png_sha256": {row["path"]: row["sha256"] for row in files},
    }
    validation["portrait_inventory_sha256"] = portrait_inventory_hash
    validation_bytes = json_bytes(validation)
    validation_hash = sha256(validation_bytes)
    activation = {
        "version": 14, "characterId": character_id, "status": "passed", "visualReview": "passed",
        "frameCount": 16, "frameDurationMs": 30, "cycleDurationMs": 480,
        "alignmentVersion": 2, "dedicatedIdle": True, "contactFrame": 0,
        "frameWidth": 1024, "frameHeight": 1024,
        "portraitWidth": 1024, "portraitHeight": 1024,
        "pixelsPerUnit": 104, "pivotX": 0.5, "pivotY": 0.08,
        "manifest_sha256": manifest_hash, "qc_sha256": approval_hash,
        "validation_sha256": validation_hash,
        "sourceCommit": SOURCE_COMMIT, "sourceFamily": "original-00-22",
    }
    activation_bytes = json_bytes(activation)
    outputs = {"portrait.png": portrait_hash_out, "manifest.json": manifest_hash,
               "validation.json": validation_hash, "appearance.json": sha256(activation_bytes)}
    outputs.update({relative: digest for relative, _, digest in slots})
    require(len(outputs) == 140 and set(outputs) == OUTPUT_SET,
            "Only 137 runtime PNGs and three activation JSON files may be imported")
    target = project / RESOURCE_FAMILY / character_id
    require(not target.exists() and not target.with_suffix(".meta").exists(),
            f"V14 character or Unity GUID already exists; refusing to overwrite: {target}")
    return {"project": project, "target": target, "character_id": character_id,
            "source_manifest_sha256": delivery_hash, "source_dir": source_dir,
            "source_document_sha256": source_document_hash,
            "approval_document_sha256": approval_hash,
            "portrait_source_sha256": portrait_hash,
            "evidence": [(delivery_path, delivery_hash), (source_document, source_document_hash),
                         (approval_document, approval_hash), (portrait_source, portrait_hash),
                         (portrait_inventory, portrait_inventory_hash)] +
                        [(source, digest) for _, source, digest in slots],
            "portrait_bytes": portrait_bytes, "slots": slots,
            "metadata": {"manifest.json": manifest_bytes, "validation.json": validation_bytes,
                         "appearance.json": activation_bytes},
            "outputs": outputs}


def execute(plan: dict) -> None:
    project: Path = plan["project"]
    target: Path = plan["target"]
    require(not (project / "Temp/UnityLockfile").exists(),
            "Close the target Unity project before importing a complete character")
    require(not target.exists() and not target.with_suffix(".meta").exists(),
            f"V14 character or Unity GUID appeared during validation: {target}")
    temp_root = project / "Temp"
    temp_root.mkdir(exist_ok=True)
    with tempfile.TemporaryDirectory(prefix="qdao-v14-import-", dir=temp_root) as temporary:
        staged = Path(temporary) / plan["character_id"]
        staged.mkdir()
        for relative, source, digest in plan["slots"]:
            destination = staged / relative
            destination.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(source, destination)
            require(file_sha256(destination) == digest, f"Source changed during staging: {relative}")
        (staged / "portrait.png").write_bytes(plan["portrait_bytes"])
        for name, contents in plan["metadata"].items():
            (staged / name).write_bytes(contents)
        for relative, digest in plan["outputs"].items():
            require(file_sha256(staged / relative) == digest, f"Staged file SHA256 differs: {relative}")
        for path, digest in plan["evidence"]:
            require(path.is_file() and not path.is_symlink() and file_sha256(path) == digest,
                    f"Input evidence changed during staging: {path}")
        require(not target.exists() and not target.with_suffix(".meta").exists(),
                f"V14 character or Unity GUID appeared during staging: {target}")
        target.parent.mkdir(parents=True, exist_ok=True)
        staged.rename(target)  # Same-volume directory rename; no partial role appears under Resources.


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--manifest", type=Path, required=True, help="Audited immutable delivery JSON")
    parser.add_argument("--character-id", required=True, help="Exact retained original identity")
    parser.add_argument("--source-dir", type=Path, help="Normalized 136-frame root")
    parser.add_argument("--source-document", type=Path, help="Pinned source inventory document")
    parser.add_argument("--approval-document", type=Path, help="Pinned offline approval document")
    parser.add_argument("--portrait-source", type=Path, help="Pinned original 4096 RGBA portrait")
    parser.add_argument("--project", type=Path, default=Path(__file__).resolve().parents[1],
                        help="Unity project root; defaults to this repository")
    parser.add_argument("--execute", action="store_true", help="Atomically add one role after validation")
    args = parser.parse_args(argv)
    try:
        plan = build_plan(args)
        if args.execute:
            execute(plan)
        print(json.dumps({"status": "imported_pending_unity_validation" if args.execute else "ready_dry_run",
                          "writes_performed": args.execute, "character_id": plan["character_id"],
                          "target": str(plan["target"]), "source_manifest_sha256": plan["source_manifest_sha256"],
                          "source_document_sha256": plan["source_document_sha256"],
                          "approval_document_sha256": plan["approval_document_sha256"],
                          "portrait_source_sha256": plan["portrait_source_sha256"],
                          "runtime_file_count": len(plan["outputs"]), "outputs": plan["outputs"]},
                         ensure_ascii=False, indent=2))
        return 0
    except (DeliveryError, OSError) as error:
        print(json.dumps({"status": "blocked", "writes_performed": False, "error": str(error)},
                         ensure_ascii=False), file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
