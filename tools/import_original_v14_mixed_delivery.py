"""Validate and stage the 05 mixed-resolution V14 delivery in an isolated Unity project.

This consumes the final offline delivery and its preserved V13 snapshot. It does
not reconstruct deleted generation raws, run Unity, or grant formal release.
Dry run is the default. --execute only adds a previously absent character folder.
"""

from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import re
import shutil
import sys
import tempfile

from PIL import Image


SCHEMA = "qdao-original-v14-mixed/import-delivery-v1"
MODE = "mixed-preserved-v1"
CHARACTER_ID = "05_celestial_musician_girl"
SOURCE_COMMIT = "9adcf9291e4a867601868889a5965f3cd48630ba"
SOURCE_DOCUMENT_SHA256 = "885cadf1e891c780d2ce897631cee467f8feea4dc24961c7c0d4eedf453cdc06"
APPROVAL_DOCUMENT_SHA256 = "e85949017ad3f2c496c35ad0171463db6fadd3a877297f2dae8a18d6ed65ba9a"
PACKAGE_CHECK_SHA256 = "7f588783fba965529359f44abb58662c52b8ed4f083cff0ab615f2da57cf654b"
PRESERVED_SNAPSHOT_SHA256 = "d7a406983abafd1f0256b84fcd592b211f848ec835dbca81ce8d74bdc6183cd7"
RESOURCE_FAMILY = Path("Assets/Resources/World/Characters/QdaoOriginalRosterV14")
DIRECTIONS = ("N", "NE", "E", "SE", "S", "SW", "W", "NW")
ACTION_PATHS = tuple(
    [f"idle/{direction}.png" for direction in DIRECTIONS]
    + [f"walk/{direction}/{frame:02d}.png" for direction in DIRECTIONS for frame in range(1, 17)]
)
REQUIRED_PATHS = ("portrait.png", *ACTION_PATHS)
HASH = re.compile(r"[0-9a-f]{64}\Z")


class DeliveryError(ValueError):
    """The selected delivery cannot be staged without breaking its contract."""


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
    require(isinstance(value, str) and HASH.fullmatch(value) is not None,
            f"{label} must be a lower-case SHA256")
    return value


def read_json(path: Path, label: str) -> tuple[dict, bytes]:
    require(path.is_file() and not path.is_symlink(), f"Missing or linked {label}: {path}")
    data = path.read_bytes()
    try:
        document = json.loads(data.decode("utf-8-sig"))
    except (UnicodeDecodeError, json.JSONDecodeError) as error:
        raise DeliveryError(f"Invalid {label}: {error}") from error
    require(isinstance(document, dict), f"{label} must be an object")
    return document, data


def json_bytes(value: dict) -> bytes:
    return (json.dumps(value, ensure_ascii=False, indent=2, sort_keys=True) + "\n").encode("utf-8")


def input_path(profile: dict, field: str, checkout: Path) -> Path:
    value = profile.get(field)
    require(isinstance(value, str) and value.strip(), f"Profile needs {field}")
    path = Path(value)
    return (path if path.is_absolute() else checkout / path).resolve()


def check_document(profile: dict, field: str, pinned_field: str, checkout: Path,
                   expected: str | None = None) -> tuple[Path, dict, str]:
    path = input_path(profile, field, checkout)
    digest = pinned_hash(profile.get(pinned_field), pinned_field)
    if expected is not None:
        require(digest == expected, f"{field} is not the pinned 05 evidence")
    document, data = read_json(path, field)
    require(sha256(data) == digest, f"{field} SHA256 differs: {path}")
    return path, document, digest


def check_png(path: Path, size: tuple[int, int], label: str) -> None:
    require(path.is_file() and not path.is_symlink(), f"Missing or linked PNG: {label}")
    try:
        with Image.open(path) as image:
            require(image.format == "PNG" and image.mode == "RGBA" and image.size == size,
                    f"Wrong PNG format or size: {label}")
            image.load()
            minimum_alpha, maximum_alpha = image.getchannel("A").getextrema()
            require(minimum_alpha == 0 and maximum_alpha > 0,
                    f"PNG needs transparent canvas and visible subject: {label}")
    except (OSError, ValueError) as error:
        if isinstance(error, DeliveryError):
            raise
        raise DeliveryError(f"Cannot decode PNG {label}: {error}") from error


def check_project(project_argument: Path, checkout: Path) -> Path:
    project = project_argument.resolve()
    isolated_root = (checkout.parent / "tmp").resolve()
    require(project != isolated_root and project.is_relative_to(isolated_root),
            f"--project must be an independent Unity candidate under {isolated_root}")
    require((project / "Assets").is_dir() and
            (project / "ProjectSettings/ProjectVersion.txt").is_file(),
            f"Target is not a Unity project: {project}")
    for part in (project / "Assets", project / "Temp", project / RESOURCE_FAMILY):
        current = part
        while current != project:
            require(not current.is_symlink() and not (hasattr(current, "is_junction") and current.is_junction()),
                    f"Linked candidate path is not supported: {current}")
            current = current.parent
    return project


def check_portrait_inventory(profile: dict, checkout: Path, portrait_hash: str) -> str:
    _, inventory, digest = check_document(profile, "portrait_inventory", "portrait_inventory_sha256", checkout)
    matches = [row for row in inventory.get("characters", []) if isinstance(row, dict) and
               row.get("character_id") == CHARACTER_ID]
    require(inventory.get("source_commit") == SOURCE_COMMIT and len(matches) == 1 and
            matches[0].get("source_commit") == SOURCE_COMMIT and
            matches[0].get("sha256") == portrait_hash and
            matches[0].get("git_manifest_sha256") == portrait_hash,
            "Original 4096 portrait does not match the identity inventory")
    return digest


def check_evidence(profile: dict, checkout: Path) -> dict:
    source_path, source, source_digest = check_document(
        profile, "source_document", "source_document_sha256", checkout, SOURCE_DOCUMENT_SHA256)
    approval_path, approval, approval_digest = check_document(
        profile, "approval_document", "approval_document_sha256", checkout, APPROVAL_DOCUMENT_SHA256)
    package_path, package, package_digest = check_document(
        profile, "package_check_document", "package_check_document_sha256", checkout, PACKAGE_CHECK_SHA256)
    snapshot_path, snapshot, snapshot_digest = check_document(
        profile, "preserved_snapshot", "preserved_snapshot_sha256", checkout, PRESERVED_SNAPSHOT_SHA256)
    require(source.get("schema") == "qdao-05-final-delivery-v1" and
            source.get("character_id") == CHARACTER_ID and source.get("actual_walk") == 128 and
            source.get("actual_idle") == 8 and source.get("offline_accepted") is True and
            source.get("formal_approval") is False and source.get("client_integration") is False and
            source.get("unity_validation") is False,
            "Final 05 delivery has an unexpected status or inventory")
    require(approval.get("character_id") == CHARACTER_ID and
            approval.get("status") == "accepted_with_nonblocking_observations" and
            approval.get("all_walk_and_idle_accepted") is True and
            approval.get("actual_walk") == 128 and approval.get("actual_idle") == 8 and
            approval.get("missing_slots") == [] and approval.get("blocking_issues") == [] and
            approval.get("formal_release") is False and approval.get("client_integration") is False and
            approval.get("unity_validation") is False and
            approval.get("reviewed_manifest_sha256") == source.get("reviewed_prior_manifest_sha256"),
            "Offline review does not bind this 05 delivery")
    require(package.get("status") == "passed" and package.get("issues") == [] and
            package.get("final_manifest_sha256") == source_digest and
            package.get("portrait_sha256") == source.get("portrait", {}).get("sha256") and
            package.get("preserved_v13_action_count") == 60 and
            isinstance(package.get("counts"), dict) and package["counts"].get("runtime_png") == 137,
            "Final package check does not bind all 137 runtime images")
    require(snapshot.get("schema") == "qdao-original-v14/preserved-output-snapshot-v1" and
            snapshot.get("resolution_mode") == MODE and len(snapshot.get("files", [])) == 199,
            "Preserved snapshot has the wrong schema or inventory")
    frozen = {}
    for row in snapshot["files"]:
        require(isinstance(row, dict), "Invalid preserved snapshot row")
        key = (row.get("character_id"), row.get("path"))
        require(key not in frozen, f"Duplicate preserved snapshot row: {key}")
        frozen[key] = row
    preserved = {path: row for (identity, path), row in frozen.items() if identity == CHARACTER_ID}
    require(len(preserved) == 61 and "portrait.png" in preserved,
            "05 must have 60 preserved actions and its original portrait")
    return {"source_path": source_path, "source": source, "source_digest": source_digest,
            "approval_digest": approval_digest, "package_digest": package_digest,
            "snapshot_digest": snapshot_digest, "preserved": preserved,
            "documents": {source_path: source_digest, approval_path: approval_digest,
                          package_path: package_digest, snapshot_path: snapshot_digest}}


def check_runtime(profile: dict, evidence: dict, checkout: Path) -> list[tuple[str, Path, dict]]:
    source_dir = input_path(profile, "source_dir", checkout)
    require(source_dir.is_dir() and not source_dir.is_symlink(), f"Missing runtime directory: {source_dir}")
    require(source_dir == evidence["source_path"].parent / "runtime",
            "Runtime directory must belong to the pinned final 05 package")
    required = set(REQUIRED_PATHS)
    actual = {path.relative_to(source_dir).as_posix() for path in source_dir.rglob("*.png")}
    require(actual == required, "05 runtime must contain exactly 137 required PNG paths")
    rows = profile.get("slots")
    source_rows = evidence["source"].get("files")
    require(isinstance(rows, list) and len(rows) == 136 and
            isinstance(source_rows, list) and len(source_rows) == 136,
            "Profile and final manifest each need 136 action rows")
    profile_by_path = {}
    final_by_path = {}
    for row in rows:
        require(isinstance(row, dict) and row.get("path") in ACTION_PATHS and
                row["path"] not in profile_by_path, "Invalid or duplicate profile action path")
        profile_by_path[row["path"]] = row
    for row in source_rows:
        require(isinstance(row, dict) and row.get("path") in ACTION_PATHS and
                row["path"] not in final_by_path, "Invalid or duplicate final action path")
        final_by_path[row["path"]] = row
    require(set(profile_by_path) == set(final_by_path) == set(ACTION_PATHS),
            "Profile and final manifest must cover all 136 actions")
    snapshot_digest = evidence["snapshot_digest"]
    selected = []
    old_count = new_count = 0
    for relative in ACTION_PATHS:
        row = profile_by_path[relative]
        final = final_by_path[relative]
        old = final.get("preserved_v13") is True
        size = 512 if old else 1024
        kind = "preserved-v13" if old else "native-hd"
        digest = pinned_hash(row.get("sha256"), f"{relative} output")
        source_digest = pinned_hash(row.get("source_sha256"), f"{relative} original source")
        require(digest == final.get("sha256") and final.get("size") == [size, size] and
                row.get("width") == row.get("height") == size and
                row.get("pixels_per_unit") == (52 if old else 104) and
                row.get("pivot") == [0.5, 0.08] and
                row.get("root_px") == ([256, 471] if old else [512, 942]) and
                row.get("source_kind") == kind and
                final.get("pixels_per_unit") == row.get("pixels_per_unit"),
                f"Mixed geometry or output hash differs: {relative}")
        record = final.get("source_record")
        source = record.get("source") if isinstance(record, dict) else None
        box = source.get("cell_xyxy") if isinstance(source, dict) else None
        require(isinstance(box, list) and len(box) == 4 and
                all(type(value) is int for value in box) and
                source.get("sha256") == source_digest and
                row.get("native_cell_size") == [box[2] - box[0], box[3] - box[1]],
                f"Native source cell binding differs: {relative}")
        record_relative = row.get("source_record_file")
        require(isinstance(record_relative, str) and
                record_relative == f"lineage/{relative[:-4]}/frame-sources.json",
                f"Wrong lineage path: {relative}")
        record_path = evidence["source_path"].parent / record_relative
        require(record_path.resolve().is_relative_to(evidence["source_path"].parent) and
                record_path.is_file() and not record_path.is_symlink() and
                file_sha256(record_path) == pinned_hash(row.get("source_record_sha256"), f"{relative} lineage") and
                final.get("source_record_file_sha256") == row["source_record_sha256"],
                f"Lineage sidecar differs: {relative}")
        lineage, _ = read_json(record_path, f"{relative} lineage")
        require(lineage.get(relative) == record, f"Lineage content differs: {relative}")
        path = source_dir / relative
        require(path.resolve().is_relative_to(source_dir), f"Runtime PNG escapes source: {relative}")
        check_png(path, (size, size), relative)
        require(file_sha256(path) == digest, f"Runtime PNG SHA256 differs: {relative}")
        if old:
            old_count += 1
            frozen = evidence["preserved"].get(relative)
            require(isinstance(frozen, dict) and
                    row.get("preserved_snapshot_sha256") == snapshot_digest and
                    row.get("preserved_sha256") == digest and
                    frozen.get("source_kind") == kind and frozen.get("sha256") == digest and
                    frozen.get("preserved_sha256") == digest and
                    frozen.get("source_sha256") == source_digest and
                    frozen.get("native_cell_size") == row["native_cell_size"] and
                    frozen.get("width") == frozen.get("height") == size and
                    frozen.get("pixels_per_unit") == row["pixels_per_unit"] and
                    frozen.get("pivot") == row["pivot"] and frozen.get("root_px") == row["root_px"],
                    f"Preserved snapshot row differs: {relative}")
        else:
            new_count += 1
            native = row.get("native_cell_size")
            require(isinstance(native, list) and len(native) == 2 and
                    all(type(value) is int and value >= 1024 for value in native) and
                    row.get("native_source_size") == source.get("native_size") and
                    row.get("source_provenance_status") == "verified_before_raw_cleanup" and
                    final.get("source_provenance_status") == row["source_provenance_status"],
                    f"Native-HD source size or historical provenance differs: {relative}")
        runtime_row = {field: row[field] for field in
                       ("path", "sha256", "width", "height", "pixels_per_unit", "pivot",
                        "root_px", "source_kind", "source_sha256", "native_cell_size")}
        if old:
            runtime_row["preserved_sha256"] = digest
        selected.append((relative, path, runtime_row))
    require(old_count == profile.get("preserved_actions") == 60 and
            new_count == profile.get("native_hd_actions") == 76,
            "05 mixed delivery must have 60 preserved and 76 native-HD actions")
    portrait = profile.get("portrait")
    frozen_portrait = evidence["preserved"]["portrait.png"]
    final_portrait = evidence["source"].get("portrait")
    require(isinstance(portrait, dict) and isinstance(final_portrait, dict) and
            portrait.get("path") == "portrait.png" and portrait.get("width") == portrait.get("height") == 1024 and
            portrait.get("source_kind") == "original-portrait" and
            portrait.get("sha256") == final_portrait.get("sha256") == frozen_portrait.get("sha256") and
            portrait.get("source_sha256") == frozen_portrait.get("source_sha256"),
            "Original portrait does not bind the final delivery and preserved snapshot")
    portrait_path = source_dir / "portrait.png"
    check_png(portrait_path, (1024, 1024), "portrait.png")
    require(file_sha256(portrait_path) == pinned_hash(portrait["sha256"], "portrait output"),
            "Original portrait bytes differ from snapshot")
    return [("portrait.png", portrait_path, portrait), *selected]


def build_plan(args: argparse.Namespace) -> dict:
    checkout = Path(__file__).resolve().parents[1]
    project = check_project(args.project, checkout)
    profile_path = args.manifest.resolve()
    profile, profile_data = read_json(profile_path, "mixed profile")
    require(args.character_id == CHARACTER_ID and profile.get("character_id") == CHARACTER_ID and
            profile.get("schema") == SCHEMA and profile.get("status") == "passed_offline" and
            profile.get("resolution_mode") == MODE and
            profile.get("formal_approval") is False and
            profile.get("client_integration") is False and
            profile.get("unity_validation") is False,
            "Only the unchanged offline 05 mixed profile is supported")
    evidence = check_evidence(profile, checkout)
    runtime = check_runtime(profile, evidence, checkout)
    portrait_source = input_path(profile, "portrait_source", checkout)
    portrait_source_hash = pinned_hash(profile.get("portrait_source_sha256"), "original 4096 portrait")
    require(portrait_source.is_file() and not portrait_source.is_symlink() and
            file_sha256(portrait_source) == portrait_source_hash == profile["portrait"]["source_sha256"],
            "Original 4096 portrait SHA256 differs")
    check_png(portrait_source, (4096, 4096), "original 4096 portrait")
    inventory_digest = check_portrait_inventory(profile, checkout, portrait_source_hash)
    files = [item[2] for item in runtime]
    manifest = {
        "version": 14, "character_id": CHARACTER_ID, "status": "passed", "visual_review": "passed",
        "resolution_mode": MODE, "preserved_snapshot_sha256": evidence["snapshot_digest"],
        "frame_size": [1024, 1024], "portrait_size": [1024, 1024],
        "frame_count": 16, "frame_duration_ms": 30, "cycle_duration_ms": 480,
        "dedicated_idle": True, "contact_frame": 0,
        "alignment": {"alignment_version": 2, "root_px": [512, 942], "per_subject_bbox_scaling": False},
        "runtime_geometry": {"reference_frame_size": 512, "pixels_per_unit": 104,
                             "pivot": [0.5, 0.08]},
        "source_commit": SOURCE_COMMIT, "source_family": "original-00-22",
        "source_manifest_sha256": sha256(profile_data),
        "source_document_sha256": evidence["source_digest"],
        "approval_document_sha256": evidence["approval_digest"],
        "package_check_sha256": evidence["package_digest"],
        "portrait_source_sha256": portrait_source_hash,
        "portrait_inventory_sha256": inventory_digest,
        "model_identity_verification": "not_claimed",
        "files": files,
    }
    manifest_data = json_bytes(manifest)
    manifest_digest = sha256(manifest_data)
    validation = {
        "schema": "qdao-original-v14-mixed/import-validation-v1", "version": 14,
        "character_id": CHARACTER_ID, "status": "passed", "visual_review": "passed",
        "scope": "isolated_candidate_offline_accepted", "formal_release": False,
        "unity_validation": False, "source_reconstruction": "historical_pre_cleanup_audit",
        "model_identity_verification": "not_claimed",
        "source_manifest_sha256": sha256(profile_data),
        "source_document_sha256": evidence["source_digest"],
        "approval_document_sha256": evidence["approval_digest"],
        "package_check_sha256": evidence["package_digest"],
        "preserved_snapshot_sha256": evidence["snapshot_digest"],
        "portrait_source_sha256": portrait_source_hash,
        "manifest_sha256": manifest_digest, "qc_sha256": evidence["package_digest"],
        "runtime_png_sha256": {row["path"]: row["sha256"] for row in files},
    }
    validation_data = json_bytes(validation)
    validation_digest = sha256(validation_data)
    appearance = {
        "version": 14, "characterId": CHARACTER_ID, "status": "passed", "visualReview": "passed",
        "resolutionMode": MODE, "frameCount": 16, "frameDurationMs": 30,
        "cycleDurationMs": 480, "alignmentVersion": 2, "dedicatedIdle": True,
        "contactFrame": 0, "frameWidth": 1024, "frameHeight": 1024,
        "portraitWidth": 1024, "portraitHeight": 1024,
        "pixelsPerUnit": 104, "pivotX": 0.5, "pivotY": 0.08,
        "manifest_sha256": manifest_digest, "qc_sha256": evidence["package_digest"],
        "validation_sha256": validation_digest,
        "sourceCommit": SOURCE_COMMIT, "sourceFamily": "original-00-22",
    }
    appearance_data = json_bytes(appearance)
    target = project / RESOURCE_FAMILY / CHARACTER_ID
    require(not target.exists() and not target.with_suffix(".meta").exists(),
            f"V14 character or Unity GUID already exists; refusing to overwrite: {target}")
    outputs = {row["path"]: row["sha256"] for row in files}
    outputs.update({"manifest.json": manifest_digest, "validation.json": validation_digest,
                    "appearance.json": sha256(appearance_data)})
    require(len(outputs) == 140 and set(outputs) == set(REQUIRED_PATHS) |
            {"manifest.json", "validation.json", "appearance.json"},
            "Only 137 PNGs and three activation JSON files may be staged")
    documents = {**evidence["documents"], profile_path: sha256(profile_data),
                 portrait_source: portrait_source_hash,
                 input_path(profile, "portrait_inventory", checkout): inventory_digest}
    return {"project": project, "target": target, "profile_sha256": sha256(profile_data),
            "runtime": runtime, "metadata": {"manifest.json": manifest_data,
                                             "validation.json": validation_data,
                                             "appearance.json": appearance_data},
            "documents": documents,
            "outputs": outputs}


def execute(plan: dict) -> None:
    project: Path = plan["project"]
    target: Path = plan["target"]
    require(not (project / "Temp/UnityLockfile").exists(), "Close the candidate Unity editor before staging")
    require(not target.exists() and not target.with_suffix(".meta").exists(),
            f"V14 character or Unity GUID appeared; refusing to overwrite: {target}")
    temp_root = project / "Temp"
    temp_root.mkdir(exist_ok=True)
    with tempfile.TemporaryDirectory(prefix="qdao-v14-mixed-import-", dir=temp_root) as temporary:
        staged = Path(temporary) / CHARACTER_ID
        staged.mkdir()
        for relative, source, row in plan["runtime"]:
            destination = staged / relative
            destination.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(source, destination)
            require(file_sha256(destination) == row["sha256"], f"Source changed during staging: {relative}")
        for name, contents in plan["metadata"].items():
            (staged / name).write_bytes(contents)
        for relative, digest in plan["outputs"].items():
            require(file_sha256(staged / relative) == digest, f"Staged file SHA256 differs: {relative}")
        for path, digest in plan["documents"].items():
            require(path.is_file() and file_sha256(path) == digest,
                    f"Pinned evidence changed during staging: {path}")
        require(not (project / "Temp/UnityLockfile").exists() and
                not target.exists() and not target.with_suffix(".meta").exists(),
                "Candidate editor, V14 target, or Unity GUID appeared during staging")
        target.parent.mkdir(parents=True, exist_ok=True)
        staged.rename(target)


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--manifest", type=Path, required=True, help="Pinned 05 mixed profile JSON")
    parser.add_argument("--character-id", required=True, help="Must be 05_celestial_musician_girl")
    parser.add_argument("--project", type=Path, required=True,
                        help="Independent Unity candidate under this workspace's tmp directory")
    parser.add_argument("--execute", action="store_true", help="Atomically add one character to the candidate")
    args = parser.parse_args(argv)
    try:
        plan = build_plan(args)
        if args.execute:
            execute(plan)
        print(json.dumps({"status": "staged_pending_unity_validation" if args.execute else "ready_dry_run",
                          "writes_performed": args.execute, "character_id": CHARACTER_ID,
                          "target": str(plan["target"]), "profile_sha256": plan["profile_sha256"],
                          "runtime_png_count": len(plan["runtime"]), "output_count": len(plan["outputs"]),
                          "manifest_sha256": plan["outputs"]["manifest.json"],
                          "validation_sha256": plan["outputs"]["validation.json"],
                          "appearance_sha256": plan["outputs"]["appearance.json"],
                          "formal_release": False, "unity_validation": False}, ensure_ascii=False, indent=2))
        return 0
    except (DeliveryError, OSError, KeyError, TypeError) as error:
        print(json.dumps({"status": "blocked", "writes_performed": False,
                          "error": str(error)}, ensure_ascii=False), file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
