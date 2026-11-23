"""Import and verify audited PC original static background maps for local client viewing.

Reads the frozen handoff package (selection.json, unavailable.json,
special-candidates.json, and per-map source manifests), verifies every source
PNG's SHA-256, byte count, and pixel dimensions, and slices the 36 ready static
`back` backgrounds into native-resolution <=1024x1024 runtime tiles without
resampling. Preserves exact mapId, native pixel dimensions, aspect ratio, and
alpha channels (RGBA PNG for maps with non-opaque alpha; high-quality JPEG q=90
for opaque RGB maps). Keeps all 4 unavailable gaps and 2 special HOLD candidates
explicitly separate and non-integrated.

The `--verify` mode validates all committed client assets and manifests offline
without requiring a checkout of the external image repository.
"""

from __future__ import annotations

import argparse
import hashlib
import io
import json
import math
from pathlib import Path
import re
import sys
import uuid

from PIL import Image

Image.MAX_IMAGE_PIXELS = None
sys.dont_write_bytecode = True

PROJECT = Path(__file__).resolve().parents[1]
DELIVERY_REL = Path("Assets/Resources/World/PcOriginalPreview20261123")
EVIDENCE_REL = Path("Docs/VerificationEvidence/pc-original-static-backgrounds-20261123")
RESOURCE_ROOT = "World/PcOriginalPreview20261123/"
REVISION = "pc-original-20261123"
ART_STAGE = "pc_original_static_back"
TILE_SIZE = 1024
OVERVIEW_MAX_EDGE = 2048
JPEG_TILE_QUALITY = 90
JPEG_OVERVIEW_QUALITY = 88
EXPECTED_CONTAINER_SHA256 = "724b3f9b1018951ac21d502a798d54b1b86d0a04250f01e1524add33525dc07f"
DEFAULT_HANDOFF_DIR = Path("D:/work/image/.codex-artifacts/pc-original-static-backgrounds-20261123")
SHA256_RE = re.compile(r"^[0-9a-f]{64}$")
MAP_ID_RE = re.compile(r"^[0-9]{5}$")


def require(condition: bool, message: str) -> None:
    if not condition:
        raise ValueError(message)


def sha256_bytes(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for chunk in iter(lambda: handle.read(8 * 1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def json_bytes(payload: object) -> bytes:
    return (json.dumps(payload, ensure_ascii=False, indent=2) + "\n").encode("utf-8")


def read_json(path: Path) -> dict:
    return json.loads(path.read_bytes().decode("utf-8-sig"))


def deterministic_guid(rel_posix: str) -> str:
    return uuid.uuid5(uuid.NAMESPACE_URL, "mmorpg-client/pc-original-maps/" + rel_posix).hex


def build_meta_bytes(path: Path, project_root: Path, has_alpha: bool = False, is_overview: bool = False) -> bytes:
    rel_posix = path.relative_to(project_root).as_posix()
    header = f"fileFormatVersion: 2\nguid: {deterministic_guid(rel_posix)}\n"
    if path.is_dir():
        return (header + "folderAsset: yes\nDefaultImporter:\n  externalObjects: {}\n").encode("utf-8")
    suffix = path.suffix.lower()
    if suffix not in (".png", ".jpg", ".jpeg"):
        return (header + "TextScriptImporter:\n  externalObjects: {}\n").encode("utf-8")
    limit = OVERVIEW_MAX_EDGE if is_overview else TILE_SIZE
    alpha_source = 1 if has_alpha else 0
    alpha_transparency = 1 if has_alpha else 0
    tex_format = 4 if has_alpha else 3  # 4 = RGBA32, 3 = RGB24
    body = (
        "TextureImporter:\n"
        "  externalObjects: {}\n"
        "  serializedVersion: 13\n"
        "  mipmaps:\n"
        "    enableMipMap: 0\n"
        "    sRGBTexture: 1\n"
        "  isReadable: 0\n"
        "  textureType: 0\n"
        "  textureShape: 1\n"
        f"  maxTextureSize: {limit}\n"
        "  nPOTScale: 0\n"
        "  textureSettings:\n"
        "    serializedVersion: 2\n"
        "    filterMode: 1\n"
        "    aniso: 1\n"
        "    wrapU: 1\n"
        "    wrapV: 1\n"
        "    wrapW: 1\n"
        "  textureCompression: 0\n"
        f"  alphaIsTransparency: {alpha_transparency}\n"
        f"  alphaSource: {alpha_source}\n"
        "  platformSettings:\n"
        "  - serializedVersion: 3\n"
        "    buildTarget: DefaultTexturePlatform\n"
        f"    maxTextureSize: {limit}\n"
        "    resizeAlgorithm: 0\n"
        f"    textureFormat: {tex_format}\n"
        "    textureCompression: 0\n"
        "    compressionQuality: 100\n"
        "    overridden: 1\n"
    )
    return (header + body).encode("utf-8")


def ensure_asset_dir(directory: Path, project_root: Path, root_guard: Path) -> None:
    require(directory == root_guard or root_guard in directory.parents, f"Output escaped root: {directory}")
    pending = []
    cur = directory
    while cur != root_guard.parent:
        pending.append(cur)
        cur = cur.parent
    for folder in reversed(pending):
        folder.mkdir(exist_ok=True)
        meta_path = Path(str(folder) + ".meta")
        if not meta_path.exists():
            meta_path.write_bytes(build_meta_bytes(folder, project_root))


def write_asset_file(
    path: Path,
    data: bytes,
    project_root: Path,
    root_guard: Path,
    *,
    has_alpha: bool = False,
    is_overview: bool = False,
) -> None:
    ensure_asset_dir(path.parent, project_root, root_guard)
    path.write_bytes(data)
    meta_path = Path(str(path) + ".meta")
    meta_path.write_bytes(build_meta_bytes(path, project_root, has_alpha=has_alpha, is_overview=is_overview))


def compute_overview_size(native_w: int, native_h: int, max_edge: int = OVERVIEW_MAX_EDGE) -> tuple[int, int]:
    require(native_w > 0 and native_h > 0, f"Invalid native dimensions: {native_w}x{native_h}")
    longest = max(native_w, native_h)
    if longest <= max_edge:
        return native_w, native_h
    scale = max_edge / float(longest)
    return max(1, int(round(native_w * scale))), max(1, int(round(native_h * scale)))


def image_has_non_opaque_alpha(image: Image.Image) -> bool:
    if image.mode == "RGB":
        return False
    if image.mode == "RGBA":
        alpha_min, _ = image.getextrema()[3]
        return alpha_min < 255
    converted = image.convert("RGBA")
    alpha_min, _ = converted.getextrema()[3]
    return alpha_min < 255


def encode_image(image: Image.Image, *, has_alpha: bool, jpeg_quality: int) -> tuple[bytes, str]:
    buf = io.BytesIO()
    if has_alpha:
        rgba = image if image.mode == "RGBA" else image.convert("RGBA")
        rgba.save(buf, format="PNG", optimize=False)
        return buf.getvalue(), ".png"
    rgb = image if image.mode == "RGB" else image.convert("RGB")
    rgb.save(buf, format="JPEG", quality=jpeg_quality, optimize=True)
    return buf.getvalue(), ".jpg"


def format_gap_detail(entry: dict) -> str:
    reason = entry.get("reason", {})
    code = reason.get("code", "UNKNOWN")
    if code == "NO_STATIC_BACKGROUND_BRANCH":
        return "No static back branch exists in the audited client.dbfs index."
    if code == "NO_AUDITED_COMPLETE_STATIC_BACKGROUND":
        variants = reason.get("variants") or []
        if variants:
            v = variants[0]
            return (
                f"Static back tiles incomplete in branch {v.get('branch', '?')}: "
                f"{v.get('qualifiedCoordinates', 0)}/{v.get('expectedCoordinates', 0)} coordinates qualified, "
                f"{v.get('missingCoordinates', 0)} missing."
            )
        return "Audited static back tile grid is incomplete."
    if code == "NO_INFO_MAP_VIEWPORT_PROOF":
        return "Observed back layer lacks info.map viewport proof; held as unconfirmed candidate."
    return f"Unavailable ({code})."


def format_special_reason(entry: dict) -> str:
    map_id = entry["mapId"]
    status = entry.get("sourceStatus", "")
    if map_id == "15000" and status == "source_valid_visual_hold":
        return (
            "8000x8000 gbits_jdhj candidate has a visual cliff/sheer drop and is held out of default integration; "
            "ordinary 1920x2298 gbits RGBA static back is integrated in maps[]."
        )
    if map_id == "50024" and status == "viewport_unconfirmed":
        return (
            "100-tile 4000x3000 observed static back lacks info.map viewport proof and cannot be marked "
            "as a confirmed runtime background."
        )
    return f"Non-default candidate held ({status})."


def validate_handoff_inputs(handoff_dir: Path) -> tuple[dict, dict, dict]:
    selection = read_json(handoff_dir / "selection.json")
    unavailable = read_json(handoff_dir / "unavailable.json")
    special = read_json(handoff_dir / "special-candidates.json")

    require(selection.get("kind") == "LOCAL_PC_ORIGINAL_STATIC_BACK_SELECTION", "Unexpected selection.json kind")
    require(unavailable.get("kind") == "LOCAL_PC_ORIGINAL_STATIC_BACK_UNAVAILABLE", "Unexpected unavailable.json kind")
    require(
        special.get("kind") == "LOCAL_PC_ORIGINAL_STATIC_BACK_NONDEFAULT_CANDIDATES",
        "Unexpected special-candidates.json kind",
    )

    container = selection.get("sourceContainerEvidence") or {}
    require(
        container.get("sha256") == EXPECTED_CONTAINER_SHA256,
        f"Unexpected sourceContainerEvidence.sha256: {container.get('sha256')}",
    )

    requested = selection.get("requestedMapIds") or []
    require(len(requested) == 40 and len(set(requested)) == 40, f"Expected 40 unique requestedMapIds, got {len(requested)}")
    require(unavailable.get("requestedMapIds") == requested, "unavailable.json requestedMapIds mismatch")
    require(special.get("requestedMapIds") == requested, "special-candidates.json requestedMapIds mismatch")

    ready_ids = [m["mapId"] for m in selection.get("maps") or []]
    unavail_ids = [m["mapId"] for m in unavailable.get("maps") or []]
    require(len(ready_ids) == 36 and len(set(ready_ids)) == 36, f"Expected 36 ready maps, got {len(ready_ids)}")
    require(len(unavail_ids) == 4 and len(set(unavail_ids)) == 4, f"Expected 4 unavailable maps, got {len(unavail_ids)}")
    require(set(ready_ids).isdisjoint(set(unavail_ids)), "Ready and unavailable mapId sets overlap")
    require(set(ready_ids) | set(unavail_ids) == set(requested), "Ready + unavailable does not equal requestedMapIds")
    require(len(special.get("maps") or []) == 2, "Expected 2 special HOLD candidates")

    return selection, unavailable, special


def import_all(
    handoff_dir: Path,
    project_root: Path,
    *,
    only_ids: list[str] | None = None,
    tile_size: int = TILE_SIZE,
    jpeg_tile_quality: int = JPEG_TILE_QUALITY,
    jpeg_overview_quality: int = JPEG_OVERVIEW_QUALITY,
) -> dict:
    selection, unavailable, special = validate_handoff_inputs(handoff_dir)
    delivery_root = project_root / DELIVERY_REL
    evidence_root = project_root / EVIDENCE_REL
    evidence_root.mkdir(parents=True, exist_ok=True)
    (evidence_root / "source-manifests").mkdir(parents=True, exist_ok=True)

    selected_maps = selection["maps"]
    if only_ids is not None:
        id_set = set(only_ids)
        selected_maps = [m for m in selected_maps if m["mapId"] in id_set]
        require(len(selected_maps) == len(id_set), "Some --ids were not found in selection.json")

    container = selection["sourceContainerEvidence"]
    catalog_maps = []
    receipt_maps = []
    total_tile_count = 0
    total_tile_bytes = 0
    total_overview_bytes = 0

    for index, entry in enumerate(selected_maps, start=1):
        map_id = entry["mapId"]
        require(bool(MAP_ID_RE.match(map_id)), f"Invalid mapId: {map_id}")
        numeric_id = int(map_id)
        name = entry.get("name") or f"地图 {map_id}"
        native_info = entry["native"]
        native_path = Path(native_info["path"])
        expected_sha = native_info["sha256"]
        expected_bytes = int(native_info["bytes"])
        expected_w, expected_h = int(native_info["pixels"][0]), int(native_info["pixels"][1])

        require(native_path.is_file(), f"{map_id}: missing source PNG: {native_path}")
        actual_bytes = native_path.stat().st_size
        require(actual_bytes == expected_bytes, f"{map_id}: source byte count mismatch ({actual_bytes} != {expected_bytes})")
        actual_sha = sha256_file(native_path)
        require(actual_sha == expected_sha, f"{map_id}: source SHA-256 mismatch ({actual_sha} != {expected_sha})")

        manifest_path = Path(entry["sourceManifest"])
        require(manifest_path.is_file(), f"{map_id}: missing sourceManifest: {manifest_path}")
        manifest_raw = manifest_path.read_bytes()
        manifest_sha = sha256_bytes(manifest_raw)
        source_ev = json.loads(manifest_raw.decode("utf-8-sig"))
        require(source_ev.get("mapId") == map_id, f"{map_id}: sourceManifest mapId mismatch")
        require(source_ev["native"]["sha256"] == expected_sha, f"{map_id}: sourceManifest native sha256 mismatch")
        require(source_ev["native"]["pixels"] == [expected_w, expected_h], f"{map_id}: sourceManifest pixels mismatch")
        require(source_ev["native"]["bytes"] == expected_bytes, f"{map_id}: sourceManifest bytes mismatch")
        require(source_ev.get("sourceContainer", {}).get("sha256") == EXPECTED_CONTAINER_SHA256, f"{map_id}: container SHA mismatch")

        # Freeze source evidence snapshot in Docs/VerificationEvidence
        evidence_manifest_rel = f"source-manifests/{map_id}-source-evidence.json"
        (evidence_root / evidence_manifest_rel).write_bytes(manifest_raw)

        source_branch = source_ev.get("branch", "gbits")
        source_status = entry.get("sourceStatus") or source_ev.get("sourceStatus") or "ready_static_back"

        with Image.open(native_path) as image:
            require(image.format == "PNG", f"{map_id}: expected PNG source")
            require(image.size == (expected_w, expected_h), f"{map_id}: decoded dimensions {image.size} != {(expected_w, expected_h)}")
            image.load()
            has_alpha = image_has_non_opaque_alpha(image)
            tile_format = "png_rgba_native_1024_grid" if has_alpha else f"jpg_q{jpeg_tile_quality}_native_1024_grid"
            columns = math.ceil(expected_w / tile_size)
            rows = math.ceil(expected_h / tile_size)
            map_resource_root = f"{RESOURCE_ROOT}{map_id}/{REVISION}/"
            map_dir = delivery_root / map_id / REVISION
            tiles_dir = map_dir / "tiles"

            tile_entries = []
            map_tile_bytes = 0
            for row in range(1, rows + 1):
                y0 = (row - 1) * tile_size
                y1 = min(expected_h, row * tile_size)
                tile_h = y1 - y0
                for col in range(1, columns + 1):
                    x0 = (col - 1) * tile_size
                    x1 = min(expected_w, col * tile_size)
                    tile_w = x1 - x0
                    crop = image.crop((x0, y0, x1, y1))
                    require(crop.size == (tile_w, tile_h), f"{map_id}: tile crop size mismatch")
                    tile_data, ext = encode_image(crop, has_alpha=has_alpha, jpeg_quality=jpeg_tile_quality)
                    tile_stem = f"r{row:02d}_c{col:02d}"
                    tile_file = tiles_dir / f"{tile_stem}{ext}"
                    write_asset_file(tile_file, tile_data, project_root, delivery_root, has_alpha=has_alpha, is_overview=False)
                    tile_sha = sha256_bytes(tile_data)
                    tile_bytes = len(tile_data)
                    map_tile_bytes += tile_bytes
                    tile_entries.append({
                        "row": row,
                        "column": col,
                        "x": x0,
                        "y": y0,
                        "width": tile_w,
                        "height": tile_h,
                        "resourcePath": f"{map_resource_root}tiles/{tile_stem}",
                        "fileExtension": ext,
                        "bytes": tile_bytes,
                        "sha256": tile_sha,
                    })

            # Overview derived texture (aspect-preserved, max edge <= OVERVIEW_MAX_EDGE)
            ov_w, ov_h = compute_overview_size(expected_w, expected_h, OVERVIEW_MAX_EDGE)
            if (ov_w, ov_h) == (expected_w, expected_h):
                overview_img = image
            else:
                overview_img = image.resize((ov_w, ov_h), Image.Resampling.LANCZOS)
            ov_data, ov_ext = encode_image(overview_img, has_alpha=has_alpha, jpeg_quality=jpeg_overview_quality)
            ov_file = map_dir / f"overview{ov_ext}"
            write_asset_file(ov_file, ov_data, project_root, delivery_root, has_alpha=has_alpha, is_overview=True)
            ov_sha = sha256_bytes(ov_data)
            ov_bytes = len(ov_data)

        tiles_manifest = {
            "schemaVersion": 1,
            "mapId": map_id,
            "numericMapId": numeric_id,
            "revision": REVISION,
            "artStage": ART_STAGE,
            "nativeWidth": expected_w,
            "nativeHeight": expected_h,
            "tileSize": tile_size,
            "columns": columns,
            "rows": rows,
            "hasAlpha": has_alpha,
            "tileFormat": tile_format,
            "resampled": False,
            "resourceRoot": map_resource_root,
            "sourceNativeSha256": expected_sha,
            "tiles": tile_entries,
        }
        tiles_manifest_raw = json_bytes(tiles_manifest)
        tiles_manifest_sha = sha256_bytes(tiles_manifest_raw)
        write_asset_file(map_dir / "tiles.json", tiles_manifest_raw, project_root, delivery_root)

        per_map_source = {
            "schemaVersion": 1,
            "kind": "CLIENT_PC_ORIGINAL_STATIC_BACK_SOURCE",
            "mapId": map_id,
            "numericMapId": numeric_id,
            "name": name,
            "revision": REVISION,
            "artStage": ART_STAGE,
            "isTemporaryOriginal": True,
            "replaceableByQdaoArt": True,
            "staticBackOnly": True,
            "completeGameplaySceneClaimed": False,
            "runtimeBranchSelectionProved": False,
            "sourceBranch": source_branch,
            "sourceStatus": source_status,
            "sourceContainerSha256": EXPECTED_CONTAINER_SHA256,
            "sourceContainerBytes": int(container["bytes"]),
            "sourceNativeSha256": expected_sha,
            "sourceNativeBytes": expected_bytes,
            "nativeWidth": expected_w,
            "nativeHeight": expected_h,
            "sourceEvidenceSha256": manifest_sha,
            "sourceEvidenceSnapshot": (EVIDENCE_REL / evidence_manifest_rel).as_posix(),
            "tileSize": tile_size,
            "columns": columns,
            "rows": rows,
            "tileCount": len(tile_entries),
            "hasAlpha": has_alpha,
            "tileFormat": tile_format,
            "resampled": False,
            "overviewDerived": True,
            "overviewWidth": ov_w,
            "overviewHeight": ov_h,
            "overviewExtension": ov_ext,
            "overviewBytes": ov_bytes,
            "overviewSha256": ov_sha,
            "tilesManifestSha256": tiles_manifest_sha,
            "serverRegistrationStatus": "unregistered_local_preview_only",
        }
        write_asset_file(map_dir / "source.json", json_bytes(per_map_source), project_root, delivery_root)

        catalog_entry = {
            "mapId": map_id,
            "numericMapId": numeric_id,
            "name": name,
            "artStage": ART_STAGE,
            "isTemporaryOriginal": True,
            "replaceableByQdaoArt": True,
            "revision": REVISION,
            "sourceBranch": source_branch,
            "sourceStatus": source_status,
            "nativeWidth": expected_w,
            "nativeHeight": expected_h,
            "aspectRatio": expected_w / float(expected_h),
            "tileSize": tile_size,
            "columns": columns,
            "rows": rows,
            "tileCount": len(tile_entries),
            "hasAlpha": has_alpha,
            "tileFormat": tile_format,
            "resampled": False,
            "resourceRoot": map_resource_root,
            "overviewResourcePath": f"{map_resource_root}overview",
            "overviewExtension": ov_ext,
            "overviewWidth": ov_w,
            "overviewHeight": ov_h,
            "overviewSha256": ov_sha,
            "overviewBytes": ov_bytes,
            "tilesManifestResourcePath": f"{map_resource_root}tiles",
            "tilesManifestSha256": tiles_manifest_sha,
            "sourceManifestResourcePath": f"{map_resource_root}source",
            "sourceNativeSha256": expected_sha,
            "sourceNativeBytes": expected_bytes,
            "sourceEvidenceSha256": manifest_sha,
            "serverRegistrationStatus": "unregistered_local_preview_only",
        }
        catalog_maps.append(catalog_entry)
        receipt_maps.append({
            "mapId": map_id,
            "numericMapId": numeric_id,
            "name": name,
            "nativePixels": [expected_w, expected_h],
            "sourceNativeSha256": expected_sha,
            "sourceNativeBytes": expected_bytes,
            "sourceBranch": source_branch,
            "resourceRoot": map_resource_root,
            "revision": REVISION,
            "artStage": ART_STAGE,
            "hasAlpha": has_alpha,
            "tileFormat": tile_format,
            "columns": columns,
            "rows": rows,
            "tileCount": len(tile_entries),
            "totalTileBytes": map_tile_bytes,
            "overviewPixels": [ov_w, ov_h],
            "overviewBytes": ov_bytes,
            "tilesManifestSha256": tiles_manifest_sha,
        })
        total_tile_count += len(tile_entries)
        total_tile_bytes += map_tile_bytes
        total_overview_bytes += ov_bytes
        print(
            f"[{index:02d}/{len(selected_maps):02d}] {map_id} {expected_w}x{expected_h} "
            f"{columns}x{rows}={len(tile_entries)} tiles ({'RGBA-PNG' if has_alpha else 'RGB-JPG'}) "
            f"tiles={map_tile_bytes/1048576:.2f}MB ov={ov_bytes/1024:.0f}KB"
        )

    unavailable_entries = []
    for gap in unavailable["maps"]:
        reason = gap.get("reason") or {}
        unavailable_entries.append({
            "mapId": gap["mapId"],
            "numericMapId": int(gap["mapId"]),
            "status": "unavailable_gap",
            "reasonCode": reason.get("code", "UNKNOWN"),
            "infoHeaderCount": int(reason.get("infoHeaderCount", 0)),
            "detail": format_gap_detail(gap),
            "integrated": False,
        })

    special_entries = []
    for cand in special["maps"]:
        native = cand["native"]
        special_entries.append({
            "mapId": cand["mapId"],
            "numericMapId": int(cand["mapId"]),
            "branch": cand["branch"],
            "sourceStatus": cand["sourceStatus"],
            "nativeWidth": int(native["pixels"][0]),
            "nativeHeight": int(native["pixels"][1]),
            "sourceNativeSha256": native["sha256"],
            "sourceNativeBytes": int(native["bytes"]),
            "defaultBranchUnconfirmed": bool(cand.get("defaultBranchUnconfirmed", True)),
            "gameMAPViewportConfirmed": bool(cand.get("gameMAPViewportConfirmed", cand["sourceStatus"] != "viewport_unconfirmed")),
            "reason": format_special_reason(cand),
            "integrated": False,
        })

    catalog = {
        "schemaVersion": 1,
        "kind": "CLIENT_PC_ORIGINAL_STATIC_BACK_PREVIEW_CATALOG",
        "catalogRevision": REVISION,
        "artStageDefault": ART_STAGE,
        "staticBackOnly": True,
        "completeGameplaySceneClaimed": False,
        "productionTravelEnabled": False,
        "sourceContainer": {
            "path": container["path"],
            "bytes": int(container["bytes"]),
            "sha256": container["sha256"],
        },
        "requestedMapIds": selection["requestedMapIds"],
        "readyCount": len(catalog_maps),
        "unavailableCount": len(unavailable_entries),
        "specialHoldCount": len(special_entries),
        "maps": catalog_maps,
        "unavailableMaps": unavailable_entries,
        "specialHoldCandidates": special_entries,
    }
    catalog_raw = json_bytes(catalog)
    catalog_sha = sha256_bytes(catalog_raw)
    write_asset_file(delivery_root / "catalog.json", catalog_raw, project_root, delivery_root)

    # Save frozen handoff summary copies in Docs/VerificationEvidence
    (evidence_root / "unavailable-snapshot.json").write_bytes((handoff_dir / "unavailable.json").read_bytes())
    (evidence_root / "special-candidates-snapshot.json").write_bytes((handoff_dir / "special-candidates.json").read_bytes())

    receipt = {
        "schemaVersion": 1,
        "kind": "CLIENT_PC_ORIGINAL_STATIC_BACK_IMPORT_RECEIPT",
        "catalogPath": (DELIVERY_REL / "catalog.json").as_posix(),
        "catalogSha256": catalog_sha,
        "catalogRevision": REVISION,
        "artStage": ART_STAGE,
        "staticBackOnly": True,
        "completeGameplaySceneClaimed": False,
        "productionTravelEnabled": False,
        "sourceContainerSha256": EXPECTED_CONTAINER_SHA256,
        "readyMapCount": len(receipt_maps),
        "unavailableMapCount": len(unavailable_entries),
        "specialHoldCandidateCount": len(special_entries),
        "totalTileCount": total_tile_count,
        "totalTileBytes": total_tile_bytes,
        "totalOverviewBytes": total_overview_bytes,
        "totalAssetBytes": total_tile_bytes + total_overview_bytes,
        "maps": receipt_maps,
        "unavailableMaps": unavailable_entries,
        "specialHoldCandidates": special_entries,
    }
    (evidence_root / "import-receipt.json").write_bytes(json_bytes(receipt))
    print(
        f"IMPORT COMPLETE: {len(receipt_maps)} ready maps, {total_tile_count} tiles, "
        f"tiles={total_tile_bytes/1048576:.2f}MB, overviews={total_overview_bytes/1048576:.2f}MB, "
        f"total={(total_tile_bytes+total_overview_bytes)/1048576:.2f}MB, "
        f"gaps={len(unavailable_entries)}, hold={len(special_entries)}"
    )
    return receipt


def verify_committed(project_root: Path, *, check_image_pixels: bool = True) -> dict:
    """Offline verification of committed catalog, tiles, overviews, metas, and receipt."""
    delivery_root = project_root / DELIVERY_REL
    evidence_root = project_root / EVIDENCE_REL
    catalog_path = delivery_root / "catalog.json"
    receipt_path = evidence_root / "import-receipt.json"

    require(catalog_path.is_file(), f"Missing catalog.json: {catalog_path}")
    require(Path(str(catalog_path) + ".meta").is_file(), "Missing catalog.json.meta")
    require(receipt_path.is_file(), f"Missing import-receipt.json: {receipt_path}")

    catalog_raw = catalog_path.read_bytes()
    catalog_sha = sha256_bytes(catalog_raw)
    catalog = json.loads(catalog_raw.decode("utf-8"))
    receipt = read_json(receipt_path)

    require(catalog.get("schemaVersion") == 1, "Invalid catalog schemaVersion")
    require(catalog.get("kind") == "CLIENT_PC_ORIGINAL_STATIC_BACK_PREVIEW_CATALOG", "Invalid catalog kind")
    require(catalog.get("staticBackOnly") is True, "staticBackOnly must be true")
    require(catalog.get("completeGameplaySceneClaimed") is False, "completeGameplaySceneClaimed must be false")
    require(catalog.get("productionTravelEnabled") is False, "productionTravelEnabled must be false")
    require(catalog["sourceContainer"]["sha256"] == EXPECTED_CONTAINER_SHA256, "Unexpected container SHA")
    require(receipt.get("catalogSha256") == catalog_sha, "import-receipt catalogSha256 mismatch")

    requested = catalog.get("requestedMapIds") or []
    maps = catalog.get("maps") or []
    unavail = catalog.get("unavailableMaps") or []
    special = catalog.get("specialHoldCandidates") or []

    require(len(requested) == 40 and len(set(requested)) == 40, "Expected 40 unique requestedMapIds")
    require(len(maps) == 36, f"Expected 36 ready maps, got {len(maps)}")
    require(len(unavail) == 4, f"Expected 4 unavailable maps, got {len(unavail)}")
    require(len(special) == 2, f"Expected 2 special HOLD candidates, got {len(special)}")
    require(catalog.get("readyCount") == 36, "readyCount mismatch")
    require(catalog.get("unavailableCount") == 4, "unavailableCount mismatch")
    require(catalog.get("specialHoldCount") == 2, "specialHoldCount mismatch")

    ready_ids = [m["mapId"] for m in maps]
    unavail_ids = [g["mapId"] for g in unavail]
    require(len(set(ready_ids)) == 36, "Duplicate ready mapId")
    require(len(set(unavail_ids)) == 4, "Duplicate unavailable mapId")
    require(set(ready_ids).isdisjoint(set(unavail_ids)), "Ready and unavailable mapIds overlap")
    require(set(ready_ids) | set(unavail_ids) == set(requested), "Ready + unavailable != requestedMapIds")

    for gap in unavail:
        require(gap.get("integrated") is False, f"Gap {gap.get('mapId')} must not be marked integrated")
    for cand in special:
        require(cand.get("integrated") is False, f"HOLD candidate {cand.get('mapId')} must not be marked integrated")

    checked_tiles = 0
    checked_overviews = 0
    for entry in maps:
        map_id = entry["mapId"]
        require(bool(MAP_ID_RE.match(map_id)), f"Invalid mapId: {map_id}")
        require(entry["numericMapId"] == int(map_id), f"{map_id}: numericMapId mismatch")
        require(entry.get("artStage") in ("pc_original_static_back", "qdao_redrawn_static_back", "formal_art_delivery"), f"{map_id}: invalid artStage")
        require(entry.get("resampled") is False, f"{map_id}: tiles must not be resampled")
        require(bool(SHA256_RE.match(entry.get("sourceNativeSha256", ""))), f"{map_id}: invalid sourceNativeSha256")

        res_root = entry["resourceRoot"]
        require(res_root.startswith(RESOURCE_ROOT) and res_root.endswith("/"), f"{map_id}: invalid resourceRoot")
        map_dir = project_root / "Assets/Resources" / res_root.rstrip("/")
        require(map_dir.is_dir(), f"{map_id}: missing directory {map_dir}")
        require(Path(str(map_dir) + ".meta").is_file(), f"{map_id}: missing dir meta")

        # Verify tiles.json
        tiles_json_path = map_dir / "tiles.json"
        require(tiles_json_path.is_file() and Path(str(tiles_json_path) + ".meta").is_file(), f"{map_id}: missing tiles.json or meta")
        tiles_raw = tiles_json_path.read_bytes()
        require(sha256_bytes(tiles_raw) == entry["tilesManifestSha256"], f"{map_id}: tilesManifestSha256 mismatch")
        tm = json.loads(tiles_raw.decode("utf-8"))

        native_w, native_h = int(entry["nativeWidth"]), int(entry["nativeHeight"])
        tile_size = int(entry["tileSize"])
        cols, rows = int(entry["columns"]), int(entry["rows"])
        require(tm["mapId"] == map_id and tm["nativeWidth"] == native_w and tm["nativeHeight"] == native_h, f"{map_id}: tiles.json header mismatch")
        require(cols == math.ceil(native_w / tile_size) and rows == math.ceil(native_h / tile_size), f"{map_id}: grid dimension mismatch")
        require(len(tm["tiles"]) == cols * rows == int(entry["tileCount"]), f"{map_id}: tile count mismatch")
        require(abs(entry["aspectRatio"] - native_w / float(native_h)) < 1e-9, f"{map_id}: aspect ratio mismatch")

        # Verify source.json
        source_json_path = map_dir / "source.json"
        require(source_json_path.is_file() and Path(str(source_json_path) + ".meta").is_file(), f"{map_id}: missing source.json or meta")
        src = read_json(source_json_path)
        require(src["mapId"] == map_id and src["sourceNativeSha256"] == entry["sourceNativeSha256"], f"{map_id}: source.json mismatch")

        # Verify overview
        ov_ext = entry["overviewExtension"]
        ov_path = map_dir / f"overview{ov_ext}"
        require(ov_path.is_file() and Path(str(ov_path) + ".meta").is_file(), f"{map_id}: missing overview or meta")
        ov_bytes = ov_path.read_bytes()
        require(len(ov_bytes) == int(entry["overviewBytes"]), f"{map_id}: overview bytes mismatch")
        require(sha256_bytes(ov_bytes) == entry["overviewSha256"], f"{map_id}: overview sha256 mismatch")
        if check_image_pixels:
            with Image.open(io.BytesIO(ov_bytes)) as ov_im:
                require(ov_im.size == (int(entry["overviewWidth"]), int(entry["overviewHeight"])), f"{map_id}: overview pixel size mismatch")
                require(max(ov_im.size) <= OVERVIEW_MAX_EDGE, f"{map_id}: overview exceeds {OVERVIEW_MAX_EDGE}")
        checked_overviews += 1

        # Verify exact tile coverage & per-tile hashes/dimensions
        seen_coords = set()
        covered_area = 0
        for tile in tm["tiles"]:
            r, c = int(tile["row"]), int(tile["column"])
            require(1 <= r <= rows and 1 <= c <= cols and (r, c) not in seen_coords, f"{map_id}: invalid/duplicate tile ({r},{c})")
            seen_coords.add((r, c))
            exp_x = (c - 1) * tile_size
            exp_y = (r - 1) * tile_size
            exp_w = min(tile_size, native_w - exp_x)
            exp_h = min(tile_size, native_h - exp_y)
            require(
                (int(tile["x"]), int(tile["y"]), int(tile["width"]), int(tile["height"])) == (exp_x, exp_y, exp_w, exp_h),
                f"{map_id}: tile ({r},{c}) rect mismatch",
            )
            require(exp_w <= tile_size and exp_h <= tile_size, f"{map_id}: tile exceeds {tile_size}")
            covered_area += exp_w * exp_h

            ext = tile["fileExtension"]
            if entry["hasAlpha"]:
                require(ext == ".png", f"{map_id}: RGBA map must use .png tiles")
            tile_file = map_dir / "tiles" / f"r{r:02d}_c{c:02d}{ext}"
            require(tile_file.is_file() and Path(str(tile_file) + ".meta").is_file(), f"{map_id}: missing tile or meta: {tile_file}")
            t_bytes = tile_file.read_bytes()
            require(len(t_bytes) == int(tile["bytes"]), f"{map_id}: tile ({r},{c}) byte count mismatch")
            require(sha256_bytes(t_bytes) == tile["sha256"], f"{map_id}: tile ({r},{c}) SHA mismatch")
            if check_image_pixels:
                with Image.open(io.BytesIO(t_bytes)) as t_im:
                    require(t_im.size == (exp_w, exp_h), f"{map_id}: tile ({r},{c}) image dimensions {t_im.size} != {(exp_w, exp_h)}")
                    if entry["hasAlpha"]:
                        require(t_im.mode == "RGBA", f"{map_id}: RGBA tile must keep RGBA mode")
            checked_tiles += 1

        require(covered_area == native_w * native_h, f"{map_id}: tile coverage area mismatch")

    summary = {
        "result": "PASS",
        "catalogSha256": catalog_sha,
        "readyMaps": len(maps),
        "unavailableMaps": len(unavail),
        "specialHoldCandidates": len(special),
        "checkedTiles": checked_tiles,
        "checkedOverviews": checked_overviews,
    }
    print(
        f"VERIFY PASS: {len(maps)} ready maps, {checked_tiles} tiles, {checked_overviews} overviews, "
        f"{len(unavail)} unavailable gaps, {len(special)} special HOLD candidates"
    )
    return summary


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Import or verify PC original static background maps.")
    parser.add_argument("--verify", action="store_true", help="Verify committed assets offline without external image checkout.")
    parser.add_argument("--handoff-dir", type=Path, default=DEFAULT_HANDOFF_DIR, help="Path to pc-original-static-backgrounds-20261123.")
    parser.add_argument("--project-root", type=Path, default=PROJECT, help="Path to mmorpg-client repository root.")
    parser.add_argument("--ids", nargs="*", help="Optional subset of 5-digit map IDs (testing only).")
    args = parser.parse_args(argv)

    if args.verify:
        verify_committed(args.project_root.resolve())
        return 0

    import_all(args.handoff_dir.resolve(), args.project_root.resolve(), only_ids=args.ids)
    if not args.ids:
        verify_committed(args.project_root.resolve())
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
