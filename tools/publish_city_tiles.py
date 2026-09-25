"""Validate, stage and publish accepted complete 64K cities (Python 3 + Pillow).

Run ``contract --appearance penglai_day`` for the delivery contract. Its draft
values deliberately fail validation. ``audit`` reads the existing art ledgers;
it never promotes candidates. Staging is safe while Unity is open. Publishing,
rollback and recovery require the project editor to be closed and acquire its
UnityLockfile for the duration. No source .meta files are copied.

Large immutable releases and rollback snapshots live in ignored .utmp, outside
Assets. Keep that directory until a durable backup exists. Small publication
receipts, source maps and acceptance records also live in Docs/VerificationEvidence.
These checks establish file/provenance integrity, not visual or gameplay approval.
"""
from __future__ import annotations

import argparse
from contextlib import contextmanager
from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import re
import shutil
import subprocess
import sys
import uuid


PROJECT = Path(__file__).resolve().parents[1]
ART_ROOT = PROJECT.parent / "image" / "qdao_city_tiles_4k_20260916"
APPEARANCES = {
    "tianyong_festival": ("tianyong", "festival"),
    "penglai_day": ("penglai", "day"),
    "penglai_mid_autumn": ("penglai", "festival"),
    "donghai_day": ("donghai", "day"),
    "donghai_lantern": ("donghai", "festival"),
    "lanxian_day": ("lanxian", "day"),
    "lanxian_spring": ("lanxian", "festival"),
}
TILE_PIXELS, ROWS, COLUMNS = 4096, 16, 16
WORLD_RECT = {"x": 50, "y": 0, "width": 300, "height": 300}
ART_CHECKS = ("wholeCityLayout", "horizontalSeams", "verticalSeams",
              "fourTileJunctions", "closestCameraClarity", "navigationAlignment")
FOREGROUND_CHECKS = ("silhouettes", "occlusion", "footPoints", "lighting", "closestCameraClarity")


class ValidationError(ValueError):
    pass


def require(condition, message):
    if not condition:
        raise ValidationError(message)


def sha256(path):
    digest = hashlib.sha256()
    with Path(path).open("rb") as stream:
        for data in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(data)
    return digest.hexdigest()


def read_json(path):
    with Path(path).open(encoding="utf-8-sig") as stream:
        return json.load(stream)


def write_json(path, value):
    """Replace a JSON file only after the complete new bytes are on disk."""
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_name(path.name + "." + uuid.uuid4().hex + ".writing")
    with temporary.open("w", encoding="utf-8", newline="\n") as stream:
        json.dump(value, stream, indent=2, ensure_ascii=False)
        stream.write("\n")
        stream.flush()
        os.fsync(stream.fileno())
    os.replace(temporary, path)


def contained(root, path):
    root, path = Path(root).resolve(), Path(path).resolve()
    require(path != root and path.is_relative_to(root), f"Path escapes expected directory: {path}")
    return path


def relative_file(root, name):
    require(isinstance(name, str) and "\\" not in name and ":" not in name,
            f"Expected portable relative path: {name!r}")
    parts = PurePosixPath(name)
    require(not parts.is_absolute() and ".." not in parts.parts, f"Unsafe source path: {name}")
    path = contained(root, Path(root) / name)
    require(path.is_file(), f"Missing file: {path}")
    return path


def check_reference(root, reference):
    require(isinstance(reference, dict), "Evidence/source reference must be an object")
    expected = reference.get("sha256", "")
    require(isinstance(expected, str) and re.fullmatch(r"[0-9a-f]{64}", expected),
            "Every source and evidence file needs a lower-case SHA-256")
    path = relative_file(root, reference.get("file"))
    require(sha256(path) == expected, f"SHA-256 mismatch: {path}")
    return path


def validate_png(path, expected_sha):
    try:
        from PIL import Image, ImageFile
    except ImportError as error:
        raise ValidationError("Pillow is required for complete PNG decoding") from error
    # Never inherit an application's permissive truncated-image setting.
    ImageFile.LOAD_TRUNCATED_IMAGES = False
    require(sha256(path) == expected_sha, f"PNG SHA-256 mismatch: {path}")
    try:
        with Image.open(path) as picture:
            require(picture.format == "PNG", f"Not a PNG: {path}")
            require(picture.size == (TILE_PIXELS, TILE_PIXELS), f"Not 4096 x 4096: {path}")
            require(getattr(picture, "n_frames", 1) == 1, f"Animated PNG is not a city tile: {path}")
            picture.verify()  # CRC and chunk integrity, not just IHDR.
        with Image.open(path) as picture:
            picture.load()  # Full zlib/filter/pixel decode, one tile at a time.
    except (OSError, SyntaxError) as error:
        raise ValidationError(f"PNG cannot be completely decoded: {path}: {error}") from error
    require(sha256(path) == expected_sha, f"PNG changed during decoding: {path}")


def grid():
    for row in range(1, ROWS + 1):
        for column in range(1, COLUMNS + 1):
            yield row, column, f"r{row:02d}_c{column:02d}"


def tile_coordinates(row, column):
    return ([(column - 1) * TILE_PIXELS, (row - 1) * TILE_PIXELS, TILE_PIXELS, TILE_PIXELS],
            {"x": 50 + (column - 1) * 18.75, "z": 300 - row * 18.75,
             "width": 18.75, "height": 18.75})


def tiles_digest(tiles):
    """Acceptance binds all 256 identities, coordinates and byte hashes, not a path name."""
    keys = ("tile", "sha256", "finalPixelRectXYWH", "worldRect")
    canonical = [{key: item[key] for key in keys} for item in tiles]
    return hashlib.sha256(json.dumps(canonical, sort_keys=True, separators=(",", ":")).encode()).hexdigest()


def verify_review(root, reference, delivery, digest, checks):
    path = check_reference(root, reference)
    record = read_json(path)
    for key, expected in (("appearance", delivery["appearance"]), ("version", delivery["version"]),
                          ("tilesDigest", digest), ("tileCount", ROWS * COLUMNS)):
        require(record.get(key) == expected, f"Acceptance {key} is not bound to this complete delivery: {path}")
    require(record.get("reviewer") and record.get("reviewedAtUtc"), f"Missing review identity/date: {path}")
    evidence = []
    for check in checks:
        result = record.get("checks", {}).get(check, {})
        require(result.get("passed") is True, f"Acceptance check not passed: {check}")
        require(result.get("notes"), f"Acceptance check needs review notes: {check}")
        evidence_reference = result.get("evidence")
        evidence.append({"path": str(check_reference(root, evidence_reference)), "sha256": evidence_reference["sha256"]})
    return path, record, evidence


def verify_references(references):
    for reference in references:
        require(sha256(reference["path"]) == reference["sha256"], f"Source/evidence changed during validation: {reference['path']}")


def foreground_client_paths():
    scripts = "Assets/Scripts/World/Tianyong/"
    paths = [scripts + name + ".cs" for name in ("TianyongPaintedForeground", "TianyongPaintedCity", "TianyongLighting")]
    for row in range(1, 7):
        for column in range(1, 7):
            tile = f"Assets/Resources/World/Tianyong/SceneTiles6x6/Tiles/tianyong_r{row:02d}_c{column:02d}.png"
            paths.extend((tile, tile + ".meta"))
    return paths


def verify_foreground_client_files(project, review):
    files = review.get("clientFiles", [])
    require(isinstance(files, list) and {item.get("file") for item in files} == set(foreground_client_paths())
            and len(files) == len(foreground_client_paths()),
            "Foreground review must bind the 36 legacy PNGs and meta files plus PaintedForeground, PaintedCity and Lighting source")
    return [{"path": str(check_reference(project, item)), "sha256": item["sha256"]} for item in files]


def build_manifest(appearance, foreground_compatible):
    city, variant = APPEARANCES[appearance]
    prefix = f"World/CityTiles4K/{city}/{variant}/tiles"
    return {"schemaVersion": 1, "tilePixels": TILE_PIXELS, "columns": COLUMNS,
            "rows": ROWS, "worldRect": WORLD_RECT.copy(),
            "tiles": [f"{prefix}/{tile}" for _, _, tile in grid()],
            "legacyForegroundCompatible": foreground_compatible}


def validate_delivery(delivery_path, art_root, project=PROJECT):
    delivery_path, art_root = Path(delivery_path).resolve(), Path(art_root).resolve()
    original_sha = sha256(delivery_path)
    delivery = read_json(delivery_path)
    require(delivery.get("schemaVersion") == 1, "Unsupported delivery schema")
    require(delivery.get("purpose") == "production_city_tiles", "Candidates, layout references and previews are not production deliveries")
    require(delivery.get("status") == "accepted_complete", "Delivery is not accepted and complete")
    appearance = delivery.get("appearance")
    require(appearance in APPEARANCES, f"Unknown appearance: {appearance}")
    require(isinstance(delivery.get("version"), str) and delivery["version"].strip(), "Missing art version")
    for key, expected in (("tilePixels", TILE_PIXELS), ("rows", ROWS), ("columns", COLUMNS), ("worldRect", WORLD_RECT)):
        require(delivery.get(key) == expected, f"Delivery {key} must be {expected}")
    entries = delivery.get("tiles", [])
    require(isinstance(entries, list) and len(entries) == ROWS * COLUMNS, "Complete production delivery requires 256 tiles")
    sources, seen_paths, seen_hashes, references = [], set(), set(), []
    for entry, (row, column, name) in zip(entries, grid()):
        require(entry.get("tile") == name, f"Tiles must be complete top-left row-major order; expected {name}")
        require(entry.get("role") == "production_tile", f"Tile is a candidate/reference/preview: {name}")
        rect, world = tile_coordinates(row, column)
        require(entry.get("finalPixelRectXYWH") == rect and entry.get("worldRect") == world,
                f"Incorrect pixel/world coordinates: {name}")
        source = check_reference(art_root, entry)
        require(source.suffix.lower() == ".png", f"Production tile must be PNG: {source}")
        require(source not in seen_paths and entry["sha256"] not in seen_hashes, f"Duplicate source tile/content: {name}")
        seen_paths.add(source)
        seen_hashes.add(entry["sha256"])
        require(isinstance(entry.get("sources"), list) and entry["sources"], f"Missing native source records: {name}")
        for reference in entry["sources"]:
            path = check_reference(art_root, reference)
            references.append({"path": str(path), "sha256": reference["sha256"]})
        path = check_reference(art_root, entry.get("assembly"))
        references.append({"path": str(path), "sha256": entry["assembly"]["sha256"]})
        references.append({"path": str(source), "sha256": entry["sha256"]})
        sources.append(source)
    digest = tiles_digest(entries)
    review_path, review, evidence = verify_review(art_root, delivery.get("acceptance"), delivery, digest, ART_CHECKS)
    reviews = [{"path": str(review_path), "sha256": delivery["acceptance"]["sha256"], "record": review}]
    foreground = appearance == "tianyong_festival"
    if foreground:
        path, record, other = verify_review(art_root, delivery.get("foregroundAcceptance"), delivery, digest, FOREGROUND_CHECKS)
        reviews.append({"path": str(path), "sha256": delivery["foregroundAcceptance"]["sha256"], "record": record})
        evidence.extend(other)
        references.extend(verify_foreground_client_files(project, record))
    # Reject missing provenance/acceptance cheaply, before decoding 256 large PNGs.
    for source, entry in zip(sources, entries):
        validate_png(source, entry["sha256"])
    references = list({(item["path"], item["sha256"]): item for item in references}.values())
    evidence = list({(item["path"], item["sha256"]): item for item in evidence}.values())
    verify_references(references + reviews + evidence)
    require(sha256(delivery_path) == original_sha, "Delivery ledger changed during validation")
    return {"appearance": appearance, "version": delivery["version"], "tilesDigest": digest,
            "deliveryPath": str(delivery_path), "deliverySha256": original_sha,
            "artRoot": str(art_root), "delivery": delivery, "reviews": reviews,
            "reviewEvidence": evidence, "provenanceReferences": references,
            "manifest": build_manifest(appearance, foreground)}


def audit(art_root):
    catalog_path, status_path = Path(art_root) / "production_catalog.json", Path(art_root) / "status.json"
    catalog, status = read_json(catalog_path), read_json(status_path)
    variants = {(item["city"], item.get("runtimeVariant", item["variant"])): item for item in catalog["variants"]}
    rows = []
    for appearance, key in APPEARANCES.items():
        item = variants.get(key, {})
        candidates = {tile["tile"] for tile in item.get("currentCandidates", [])}
        rows.append({"appearance": appearance, "city": key[0], "variant": key[1],
                     "candidateTiles": len(candidates), "acceptedTilesReported": item.get("productionTilesAccepted", 0),
                     "missingCandidateCoordinates": [name for _, _, name in grid() if name not in candidates],
                     "deliveryManifest": item.get("deliveryManifest"), "state": item.get("state", "missing"),
                     "productionReady": False,
                     "reason": "Requires separate accepted-complete delivery validation; candidates are never promoted"})
    return {"mode": "audit", "sourceCatalog": str(catalog_path), "sourceCatalogSha256": sha256(catalog_path),
            "sourceStatusSha256": sha256(status_path), "sourceUpdatedAtUtc": status.get("updatedAtUtc"),
            "completedWholeCityCountReported": status.get("completedWholeCityCount"),
            "appearances": rows, "productionManifestWritten": False}


@contextmanager
def publication_lock(workspace):
    workspace.mkdir(parents=True, exist_ok=True)
    with (workspace / "publisher.lock").open("a+b") as stream:
        if os.fstat(stream.fileno()).st_size == 0:
            stream.write(b"0")
            stream.flush()
        stream.seek(0)
        try:
            if os.name == "nt":
                import msvcrt
                msvcrt.locking(stream.fileno(), msvcrt.LK_NBLCK, 1)
            else:
                import fcntl
                fcntl.flock(stream.fileno(), fcntl.LOCK_EX | fcntl.LOCK_NB)
        except OSError as error:
            raise ValidationError("Another city publisher is running") from error
        try:
            yield
        finally:
            stream.seek(0)
            if os.name == "nt":
                msvcrt.locking(stream.fileno(), msvcrt.LK_UNLCK, 1)
            else:
                fcntl.flock(stream.fileno(), fcntl.LOCK_UN)


@contextmanager
def closed_unity_project(project):
    """Do not expose a two-directory rename to a live AssetDatabase or Play Mode."""
    lockfile = project / "Temp" / "UnityLockfile"
    require(not lockfile.exists(), f"Unity project is in use (or has a stale lock): {lockfile}; close its editor first")
    lockfile.parent.mkdir(parents=True, exist_ok=True)
    if os.name != "nt":
        raise ValidationError("Production publication currently supports Windows Unity project locking only")
    # An editor can exist before/after it owns its lock. Fail closed on inaccessible
    # command lines and on editors started with no explicit projectPath.
    script = "$ErrorActionPreference = 'Stop'; Get-CimInstance Win32_Process -Filter \"Name = 'Unity.exe'\" | Select-Object CommandLine | ConvertTo-Json -Compress"
    result = subprocess.run(["powershell.exe", "-NoProfile", "-Command", script], capture_output=True, text=True)
    require(result.returncode == 0 and not result.stderr.strip(), "Cannot inspect running Unity editors; no publication performed")
    processes = json.loads(result.stdout or "[]")
    processes = processes if isinstance(processes, list) else [processes]
    normalized = str(project.resolve()).replace("\\", "/").lower()
    for process in processes:
        command = (process.get("CommandLine") or "").replace("\\", "/").lower()
        require("-projectpath" in command and normalized not in command,
                "A Unity editor may be using this project; close it before publication")
    import ctypes
    from ctypes import wintypes
    kernel = ctypes.WinDLL("kernel32", use_last_error=True)
    kernel.CreateFileW.argtypes = [wintypes.LPCWSTR, wintypes.DWORD, wintypes.DWORD, wintypes.LPVOID,
                                  wintypes.DWORD, wintypes.DWORD, wintypes.HANDLE]
    kernel.CreateFileW.restype = wintypes.HANDLE
    kernel.CloseHandle.argtypes = [wintypes.HANDLE]
    # CREATE_NEW, no sharing, DELETE_ON_CLOSE: a crash cannot leave our own stale
    # Unity lock, and a newly launched editor cannot open it during the swap.
    handle = kernel.CreateFileW(str(lockfile), 0xC0000000, 0, None, 1, 0x04000080, None)
    require(handle != wintypes.HANDLE(-1).value, "Cannot exclusively lock the Unity project")
    try:
        yield
    finally:
        kernel.CloseHandle(handle)


def workspace_for(project):
    path = contained(project, project / ".utmp" / "city-tiles4k")
    require(not path.is_relative_to((project / "Assets").resolve()), "Staging must be outside Assets")
    return path


def release_path(workspace, release_id):
    require(isinstance(release_id, str) and re.fullmatch(r"[A-Za-z0-9_-]+", release_id), "Invalid release identifier")
    return contained(workspace / "releases", workspace / "releases" / release_id)


def stage(delivery_path, art_root, project):
    workspace = workspace_for(project)
    with publication_lock(workspace):
        verified = validate_delivery(delivery_path, art_root, project)
        release_id = datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%SZ") + "-" + uuid.uuid4().hex[:12]
        target = release_path(workspace, release_id)
        (target / "tiles").mkdir(parents=True)
        # An unfinished release has no receipt and can never be published.
        for entry in verified["delivery"]["tiles"]:
            destination = target / "tiles" / (entry["tile"] + ".png")
            shutil.copyfile(relative_file(art_root, entry["file"]), destination)
            validate_png(destination, entry["sha256"])
        for reference in verified["reviews"] + verified["reviewEvidence"]:
            source = Path(reference["path"])
            relative = "evidence/" + reference["sha256"] + source.suffix
            destination = target / relative
            destination.parent.mkdir(exist_ok=True)
            shutil.copyfile(source, destination)
            require(sha256(destination) == reference["sha256"], f"Review evidence changed during staging: {source}")
            reference["snapshotFile"] = relative
        verify_references(verified["provenanceReferences"] + verified["reviews"] + verified["reviewEvidence"])
        write_json(target / "source-map.json", verified)
        write_json(target / "manifest.json", verified["manifest"])
        write_json(target / "receipt.json", {"release": release_id, "appearance": verified["appearance"],
                   "sourceMapSha256": sha256(target / "source-map.json"), "manifestSha256": sha256(target / "manifest.json")})
        return {"mode": "stage", "release": release_id, "path": str(target), "appearance": verified["appearance"],
                "tileCount": ROWS * COLUMNS, "productionManifestWritten": False}


def verify_release(workspace, release_id):
    target = release_path(workspace, release_id)
    receipt = read_json(target / "receipt.json")
    require(receipt["release"] == release_id, "Release identifier mismatch")
    require(sha256(target / "source-map.json") == receipt["sourceMapSha256"], "Staged source map changed")
    require(sha256(target / "manifest.json") == receipt["manifestSha256"], "Staged manifest changed")
    source_map = read_json(target / "source-map.json")
    require(source_map["manifest"] == read_json(target / "manifest.json"), "Staged manifest/source map mismatch")
    require(source_map["appearance"] == receipt["appearance"], "Staged appearance mismatch")
    expected = {name + ".png" for _, _, name in grid()}
    require({path.name for path in (target / "tiles").iterdir()} == expected, "Staged release must contain exactly 256 PNGs, no meta files")
    for entry in source_map["delivery"]["tiles"]:
        validate_png(target / "tiles" / (entry["tile"] + ".png"), entry["sha256"])
    for reference in source_map["reviews"] + source_map["reviewEvidence"]:
        require(sha256(relative_file(target, reference["snapshotFile"])) == reference["sha256"], "Staged acceptance evidence changed")
    return target, source_map


def destination_for(project, appearance):
    city, variant = APPEARANCES[appearance]
    base = project / "Assets" / "Resources" / "World" / "CityTiles4K"
    return contained(project / "Assets", base / city / variant)


def journal_path(workspace):
    return workspace / "pending-transaction.json"


def inventory(directory):
    result = {}
    for path in sorted(directory.rglob("*")):
        require(not path.is_symlink() and not (hasattr(path, "is_junction") and path.is_junction()),
                f"Linked files/directories are not publication snapshots: {path}")
        contained(directory, path)
        if path.is_file():
            result[path.relative_to(directory).as_posix()] = sha256(path)
    return result


def verify_existing(directory, appearance):
    if not directory.exists():
        return None
    manifest = read_json(directory / "manifest.json")
    expected = build_manifest(appearance, appearance == "tianyong_festival")
    require(all(manifest.get(key) == value for key, value in expected.items()),
            "Existing production manifest does not match this 64K contract; preserve and inspect it before replacement")
    pngs = {path.name for path in (directory / "tiles").glob("*.png")}
    require(pngs == {name + ".png" for _, _, name in grid()}, "Existing rollback version is incomplete")
    files = inventory(directory)
    for _, _, name in grid():
        path = directory / "tiles" / (name + ".png")
        validate_png(path, files[f"tiles/{name}.png"])
    return files


def restore_transaction(project, workspace, journal):
    """Preserve displaced/failed bytes; never recursively delete a user directory."""
    destination = destination_for(project, journal["appearance"])
    transaction = contained(workspace / "transactions", workspace / "transactions" / journal["id"])
    previous = transaction / "previous"
    if previous.exists():
        require(inventory(previous) == journal["previousInventory"], "Previous snapshot changed; refusing unsafe recovery")
        if destination.exists():
            current = inventory(destination)
            expected = journal["incomingInventory"]
            require(current == expected or current == {**expected, "manifest.json": journal["incomingManifestSha256"]},
                    "Current resources changed after interruption; preserve them and inspect before recovery")
            destination.rename(transaction / ("failed-" + uuid.uuid4().hex))
        previous.rename(destination)
    elif not journal["hadPrevious"] and destination.exists():
        current = inventory(destination)
        expected = journal["incomingInventory"]
        require(current == expected or current == {**expected, "manifest.json": journal["incomingManifestSha256"]},
                "Current resources changed after interruption; refusing unsafe recovery")
        destination.rename(transaction / ("failed-" + uuid.uuid4().hex))
    elif journal["hadPrevious"]:
        # The rename either never happened, or recovery already put the exact old
        # directory back. A manually lost snapshot must not be reported restored.
        require(destination.exists() and inventory(destination) == journal["previousInventory"],
                "Previous snapshot is missing and original resources are not restored")
    write_json(transaction / "recovery.json", {"restoredAtUtc": datetime.now(timezone.utc).isoformat()})
    journal_path(workspace).unlink()


def install_release(project, workspace, release_id, evidence_dir, action="publish"):
    require(not journal_path(workspace).exists(), "An interrupted transaction exists; run recover first")
    target, source_map = verify_release(workspace, release_id)
    appearance = source_map["appearance"]
    if appearance == "tianyong_festival":
        verify_foreground_client_files(project, source_map["reviews"][1]["record"])
    destination = destination_for(project, appearance)
    previous_inventory = verify_existing(destination, appearance)
    transaction_id = datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%SZ") + "-" + uuid.uuid4().hex[:12]
    transaction = workspace / "transactions" / transaction_id
    incoming, previous = transaction / "incoming", transaction / "previous"
    incoming.mkdir(parents=True)
    shutil.copytree(target / "tiles", incoming / "tiles")
    for entry in source_map["delivery"]["tiles"]:
        validate_png(incoming / "tiles" / (entry["tile"] + ".png"), entry["sha256"])
    # Prepare the final file outside Resources. An interrupted write must never
    # leave an unregistered .writing file in the directory recovery verifies.
    write_json(transaction / "manifest.ready.json", source_map["manifest"])
    destination.parent.mkdir(parents=True, exist_ok=True)
    require((inventory(destination) if destination.exists() else None) == previous_inventory,
            "Production resources changed while preparing publication; nothing was replaced")
    if appearance == "tianyong_festival":
        verify_foreground_client_files(project, source_map["reviews"][1]["record"])
    journal = {"id": transaction_id, "appearance": appearance, "hadPrevious": destination.exists(), "release": release_id,
               "previousInventory": previous_inventory, "incomingInventory": inventory(incoming),
               "incomingManifestSha256": sha256(target / "manifest.json")}
    # This durable journal precedes BOTH renames; recover is idempotent if the
    # process is killed between them or before manifest installation.
    write_json(journal_path(workspace), journal)
    try:
        if destination.exists():
            destination.rename(previous)
        incoming.rename(destination)
        os.replace(transaction / "manifest.ready.json", destination / "manifest.json")
        record = {"action": action, "transaction": transaction_id, "release": release_id, "appearance": appearance,
                  "publishedAtUtc": datetime.now(timezone.utc).isoformat(), "tileCount": ROWS * COLUMNS,
                  "manifestSha256": sha256(destination / "manifest.json"), "destination": str(destination),
                  "previousSnapshot": str(previous) if journal["hadPrevious"] else None,
                  "sourceMapSha256": sha256(target / "source-map.json"), "runtimeAccepted": False}
        write_json(transaction / "publication.json", record)
        write_json(transaction / "journal.json", journal)
        evidence = evidence_dir / transaction_id
        write_json(evidence / "source-map.json", source_map)
        shutil.copytree(target / "evidence", evidence / "acceptance-evidence")
        write_json(evidence / "publication.json", record)
        journal_path(workspace).unlink()
        return record
    except BaseException:
        restore_transaction(project, workspace, journal)
        raise


def publish(project, release_id, evidence_dir, action="publish"):
    workspace = workspace_for(project)
    with publication_lock(workspace), closed_unity_project(project):
        return install_release(project, workspace, release_id, evidence_dir, action)


def rollback(project, transaction_id, evidence_dir):
    """Restore the exact prior directory (including its client-generated meta)."""
    workspace = workspace_for(project)
    require(re.fullmatch(r"[A-Za-z0-9_-]+", transaction_id), "Invalid transaction identifier")
    with publication_lock(workspace), closed_unity_project(project):
        require(not journal_path(workspace).exists(), "Run recover before rollback")
        transaction = contained(workspace / "transactions", workspace / "transactions" / transaction_id)
        record = read_json(transaction / "publication.json")
        destination = destination_for(project, record["appearance"])
        require(destination.is_dir() and sha256(destination / "manifest.json") == record["manifestSha256"],
                "Current manifest differs from that publication; refusing to overwrite newer work")
        # Validate current source tiles as well; an unchanged manifest does not
        # authorize discarding somebody else's tile edits.
        _, current = verify_release(workspace, record["release"])
        for entry in current["delivery"]["tiles"]:
            require(sha256(destination / "tiles" / (entry["tile"] + ".png")) == entry["sha256"], "Current production tiles were edited")
        expected_files = {"manifest.json"} | {f"tiles/{name}.png" for _, _, name in grid()}
        require({name for name in inventory(destination) if not name.endswith(".meta")} == expected_files,
                "Unregistered production resources appeared after publication; rollback refused")
        previous = transaction / "previous"
        require(record["previousSnapshot"] is None or previous.is_dir(), "Rollback snapshot is missing/already restored")
        journal = read_json(transaction / "journal.json")
        # Unity may have generated .meta files since publication. Keep the whole
        # current directory and its exact inventory as the reversible rollback input.
        journal["incomingInventory"] = inventory(destination)
        journal["incomingInventory"].pop("manifest.json")
        write_json(journal_path(workspace), journal)
        restore_transaction(project, workspace, journal)
        result = {"action": "rollback", "transaction": transaction_id, "appearance": record["appearance"],
                  "restoredPriorResources": record["previousSnapshot"] is not None, "runtimeAccepted": False}
        write_json(evidence_dir / transaction_id / "rollback.json", result)
        return result


def contract(appearance):
    tiles = []
    for row, column, name in grid():
        rect, world = tile_coordinates(row, column)
        tiles.append({"tile": name, "file": f"deliveries/{appearance}/VERSION/tiles/{name}.png",
                      "sha256": "REPLACE_WITH_ACTUAL_SHA256", "role": "production_tile",
                      "finalPixelRectXYWH": rect, "worldRect": world,
                      "sources": [{"file": "provenance/native-source-record.json", "sha256": "REPLACE_WITH_ACTUAL_SHA256"}],
                      "assembly": {"file": "provenance/assembly.json", "sha256": "REPLACE_WITH_ACTUAL_SHA256"}})
    delivery = {"schemaVersion": 1, "purpose": "production_city_tiles", "status": "DRAFT_NOT_ACCEPTED",
                "appearance": appearance, "version": "REPLACE_WITH_ART_VERSION", "tilePixels": TILE_PIXELS,
                "columns": COLUMNS, "rows": ROWS, "worldRect": WORLD_RECT, "tiles": tiles,
                "acceptance": {"file": "acceptance.json", "sha256": "REPLACE_WITH_ACTUAL_SHA256"}}
    def review(checks):
        return {"appearance": appearance, "version": delivery["version"], "tileCount": ROWS * COLUMNS,
                "tilesDigest": "SHA256_OF_CANONICAL_TILE_IDENTITIES_COORDINATES_AND_HASHES_USE_digest_COMMAND",
                "reviewer": "", "reviewedAtUtc": "", "checks": {
                    check: {"passed": False, "notes": "", "evidence": {"file": "review/REPLACE", "sha256": "REPLACE_WITH_ACTUAL_SHA256"}}
                    for check in checks}}
    result = {"instructions": "Save delivery and each review as separate JSON files under art root; all paths are relative to art root. Fill real provenance, run digest after filling all 256 tile hashes, review that exact version, hash evidence/review files, then set status accepted_complete. Never mark a draft/candidate accepted merely to pass this tool.",
              "delivery": delivery, "acceptanceExample": review(ART_CHECKS)}
    if appearance == "tianyong_festival":
        delivery["foregroundAcceptance"] = {"file": "foreground-acceptance.json", "sha256": "REPLACE_WITH_ACTUAL_SHA256"}
        result["foregroundAcceptanceExample"] = review(FOREGROUND_CHECKS)
        result["foregroundAcceptanceExample"]["clientFiles"] = [
            {"file": path, "sha256": "REPLACE_WITH_ACTUAL_SHA256_AFTER_REVIEW"} for path in foreground_client_paths()]
    return result


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--project", type=Path, default=PROJECT)
    parser.add_argument("--art-root", type=Path, default=ART_ROOT)
    parser.add_argument("--evidence-dir", type=Path)
    parser.add_argument("--output", type=Path, help="Save command result JSON, outside Assets")
    commands = parser.add_subparsers(dest="command", required=True)
    commands.add_parser("audit")
    sample = commands.add_parser("contract")
    sample.add_argument("--appearance", choices=APPEARANCES, required=True)
    for command in ("validate", "stage", "digest"):
        commands.add_parser(command).add_argument("--delivery", type=Path, required=True)
    commands.add_parser("publish").add_argument("--release", required=True)
    commands.add_parser("rollback").add_argument("--transaction", required=True)
    commands.add_parser("recover")
    args = parser.parse_args(argv)
    project = args.project.resolve()
    evidence_dir = args.evidence_dir or project / "Docs" / "VerificationEvidence" / "city-tiles4k-publications"
    for output_path in (args.output, evidence_dir):
        if output_path:
            require(not output_path.resolve().is_relative_to(project / "Assets"), "Evidence/output must remain outside Assets")
    if args.command == "audit":
        result = audit(args.art_root)
    elif args.command == "contract":
        result = contract(args.appearance)
    elif args.command == "digest":
        result = {"tilesDigest": tiles_digest(read_json(args.delivery)["tiles"]), "acceptanceVerified": False}
    elif args.command == "validate":
        result = validate_delivery(args.delivery, args.art_root, project)
    elif args.command == "stage":
        result = stage(args.delivery, args.art_root, project)
    elif args.command == "publish":
        result = publish(project, args.release, evidence_dir)
    elif args.command == "rollback":
        result = rollback(project, args.transaction, evidence_dir)
    else:
        workspace = workspace_for(project)
        with publication_lock(workspace), closed_unity_project(project):
            journal = read_json(journal_path(workspace))
            restore_transaction(project, workspace, journal)
            result = {"action": "recover", "transaction": journal["id"], "appearance": journal["appearance"]}
    if args.output:
        write_json(args.output, result)
    print(json.dumps(result, indent=2, ensure_ascii=False))
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except (ValidationError, OSError, KeyError, TypeError, json.JSONDecodeError) as error:
        print(json.dumps({"error": str(error), "productionAccepted": False}, ensure_ascii=False), file=sys.stderr)
        sys.exit(2)
