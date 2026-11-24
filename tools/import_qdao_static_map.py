"""Import and verify a Q-Dao redrawn dual-festival static background map into PcOriginalPreview20261123.

Scoped to adopting a single map (e.g. 28002 spring/midautumn) from its frozen art
delivery directory without overwriting the other 35 PC original maps. Losslessly
crops each appearance's native RGBA PNG into <=1024 square-grid runtime tiles,
proves exact RGBA byte-equality on 6-tile recomposition, writes derived overview
fallbacks, tiles.json, source.json, Unity .meta files, updates the single mapId
entry in catalog.json with appearances[], preserves PC original provenance, and
keeps import-receipt.json synchronized.
"""

from __future__ import annotations

import argparse
import io
import json
import math
from pathlib import Path
import sys

from PIL import Image

Image.MAX_IMAGE_PIXELS = None
sys.dont_write_bytecode = True

TOOLS_DIR = Path(__file__).resolve().parent
if str(TOOLS_DIR) not in sys.path:
    sys.path.insert(0, str(TOOLS_DIR))

import import_pc_original_maps as pc_imp  # noqa: E402

PROJECT = pc_imp.PROJECT
DELIVERY_REL = pc_imp.DELIVERY_REL
EVIDENCE_REL = pc_imp.EVIDENCE_REL
RESOURCE_ROOT = pc_imp.RESOURCE_ROOT
QDAO_ART_STAGE = "qdao_redrawn_static_back"
DEFAULT_28002_DELIVERY = Path("D:/work/image/designs/client-map-qdao-20261123/28002")

APPEARANCE_DISPLAY_NAMES = {
    "spring": "春节",
    "midautumn": "中秋",
    "mid-autumn": "中秋",
    "normal": "日常",
    "lantern": "元宵",
}


def compute_alpha_stats(image: Image.Image) -> dict:
    rgba = image if image.mode == "RGBA" else image.convert("RGBA")
    alpha_band = rgba.getchannel("A")
    alpha_min, alpha_max = alpha_band.getextrema()
    hist = alpha_band.histogram()
    zero = hist[0]
    full = hist[255]
    total = rgba.size[0] * rgba.size[1]
    partial = total - zero - full
    return {
        "minMax": [int(alpha_min), int(alpha_max)],
        "fullyTransparent": int(zero),
        "fullyOpaque": int(full),
        "partialAlpha": int(partial),
        "totalPixels": int(total),
    }


def verify_recomposition_exact_rgba(
    source_rgba: Image.Image,
    tile_entries: list[dict],
    tiles_dir: Path,
) -> dict:
    """Reload every written runtime tile PNG from disk and prove RGBA byte equality."""
    width, height = source_rgba.size
    canvas = Image.new("RGBA", (width, height), (0, 0, 0, 0))
    covered = 0
    for tile in tile_entries:
        tile_path = tiles_dir / f"r{tile['row']:02d}_c{tile['column']:02d}{tile['fileExtension']}"
        with Image.open(tile_path) as part:
            part.load()
            pc_imp.require(part.format == "PNG", f"Runtime tile must be PNG: {tile_path}")
            pc_imp.require(part.mode == "RGBA", f"Runtime tile must preserve RGBA mode: {tile_path}")
            pc_imp.require(
                part.size == (tile["width"], tile["height"]),
                f"Runtime tile size mismatch {part.size} != {(tile['width'], tile['height'])}",
            )
            canvas.paste(part, (tile["x"], tile["y"]))
            covered += tile["width"] * tile["height"]
    pc_imp.require(covered == width * height, f"Tile coverage {covered} != {width * height}")
    exact_equal = canvas.tobytes() == source_rgba.tobytes()
    pc_imp.require(exact_equal, "Recomposed runtime tiles RGBA bytes differ from source whole PNG")
    return {
        "recomposedPixels": [width, height],
        "coveredPixels": covered,
        "tileCount": len(tile_entries),
        "rgbaByteEqualToWholeSource": exact_equal,
        "alphaStats": compute_alpha_stats(canvas),
    }


def verify_recomposition_exact_rgb(
    source_image: Image.Image,
    tile_entries: list[dict],
    tiles_dir: Path,
) -> dict:
    """Reload every written opaque PNG runtime tile from disk and prove exact RGB byte equality."""
    source_rgb = source_image if source_image.mode == "RGB" else source_image.convert("RGB")
    width, height = source_rgb.size
    canvas = Image.new("RGB", (width, height), (0, 0, 0))
    covered = 0
    for tile in tile_entries:
        tile_path = tiles_dir / f"r{tile['row']:02d}_c{tile['column']:02d}{tile['fileExtension']}"
        with Image.open(tile_path) as part:
            part.load()
            pc_imp.require(part.format == "PNG", f"Runtime tile must be PNG: {tile_path}")
            pc_imp.require(part.mode == "RGB", f"Opaque lossless tile must use RGB mode: {tile_path}")
            pc_imp.require(
                part.size == (tile["width"], tile["height"]),
                f"Runtime tile size mismatch {part.size} != {(tile['width'], tile['height'])}",
            )
            canvas.paste(part, (tile["x"], tile["y"]))
            covered += tile["width"] * tile["height"]
    pc_imp.require(covered == width * height, f"Tile coverage {covered} != {width * height}")
    exact_equal = canvas.tobytes() == source_rgb.tobytes()
    pc_imp.require(exact_equal, "Recomposed runtime tiles RGB bytes differ from source whole PNG")
    return {
        "recomposedPixels": [width, height],
        "coveredPixels": covered,
        "tileCount": len(tile_entries),
        "rgbByteEqualToWholeSource": exact_equal,
    }


def derive_single_appearance(
    *,
    map_id: str,
    numeric_id: int,
    map_name: str,
    appearance_id: str,
    revision: str,
    whole_png_path: Path,
    expected_whole_sha: str,
    provenance_path: Path,
    expected_provenance_sha: str,
    slice_index_path: Path,
    expected_slice_index_sha: str,
    package_zip_path: Path,
    expected_package_sha: str,
    delivery_json_sha: str,
    pc_original_ref: dict,
    project_root: Path,
    tile_size: int = pc_imp.TILE_SIZE,
) -> tuple[dict, dict]:
    """Derive 1024-grid lossless RGBA tiles + overview + manifests + .meta for one appearance."""
    pc_imp.require(whole_png_path.is_file(), f"Missing whole PNG: {whole_png_path}")
    actual_whole_sha = pc_imp.sha256_file(whole_png_path)
    pc_imp.require(
        actual_whole_sha == expected_whole_sha,
        f"{map_id}/{appearance_id}: whole PNG SHA mismatch ({actual_whole_sha} != {expected_whole_sha})",
    )
    whole_bytes = whole_png_path.stat().st_size

    pc_imp.require(provenance_path.is_file(), f"Missing provenance.json: {provenance_path}")
    actual_prov_sha = pc_imp.sha256_file(provenance_path)
    pc_imp.require(
        actual_prov_sha == expected_provenance_sha,
        f"{map_id}/{appearance_id}: provenance SHA mismatch ({actual_prov_sha} != {expected_provenance_sha})",
    )
    pc_imp.require(slice_index_path.is_file(), f"Missing slice-index: {slice_index_path}")
    pc_imp.require(
        pc_imp.sha256_file(slice_index_path) == expected_slice_index_sha,
        f"{map_id}/{appearance_id}: slice-index SHA mismatch",
    )
    pc_imp.require(package_zip_path.is_file(), f"Missing package zip: {package_zip_path}")
    pc_imp.require(
        pc_imp.sha256_file(package_zip_path) == expected_package_sha,
        f"{map_id}/{appearance_id}: package zip SHA mismatch",
    )

    delivery_root = project_root / DELIVERY_REL
    map_resource_root = f"{RESOURCE_ROOT}{map_id}/{revision}/"
    app_dir = delivery_root / map_id / revision
    tiles_dir = app_dir / "tiles"

    with Image.open(whole_png_path) as image:
        pc_imp.require(image.format == "PNG", f"{map_id}/{appearance_id}: expected PNG source")
        pc_imp.require(image.mode == "RGBA", f"{map_id}/{appearance_id}: expected RGBA PNG, got {image.mode}")
        image.load()
        native_w, native_h = image.size
        pc_imp.require(
            (native_w, native_h) == (pc_original_ref["nativeWidth"], pc_original_ref["nativeHeight"]),
            f"{map_id}/{appearance_id}: pixel size {(native_w, native_h)} != PC original",
        )
        has_alpha = pc_imp.image_has_non_opaque_alpha(image)
        pc_imp.require(has_alpha, f"{map_id}/{appearance_id}: expected non-opaque RGBA alpha channel")
        alpha_stats = compute_alpha_stats(image)

        columns = math.ceil(native_w / tile_size)
        rows = math.ceil(native_h / tile_size)
        tile_format = "png_rgba_native_1024_grid"
        tile_entries: list[dict] = []
        total_tile_bytes = 0

        for row in range(1, rows + 1):
            y0 = (row - 1) * tile_size
            y1 = min(native_h, row * tile_size)
            tile_h = y1 - y0
            for col in range(1, columns + 1):
                x0 = (col - 1) * tile_size
                x1 = min(native_w, col * tile_size)
                tile_w = x1 - x0
                crop = image.crop((x0, y0, x1, y1))
                pc_imp.require(crop.size == (tile_w, tile_h) and crop.mode == "RGBA", "Tile crop mismatch")
                tile_data, ext = pc_imp.encode_image(crop, has_alpha=True, jpeg_quality=pc_imp.JPEG_TILE_QUALITY)
                pc_imp.require(ext == ".png", "RGBA tiles must be PNG")
                tile_stem = f"r{row:02d}_c{col:02d}"
                tile_file = tiles_dir / f"{tile_stem}{ext}"
                pc_imp.write_asset_file(
                    tile_file, tile_data, project_root, delivery_root, has_alpha=True, is_overview=False
                )
                tile_sha = pc_imp.sha256_bytes(tile_data)
                tile_len = len(tile_data)
                total_tile_bytes += tile_len
                tile_entries.append({
                    "row": row,
                    "column": col,
                    "x": x0,
                    "y": y0,
                    "width": tile_w,
                    "height": tile_h,
                    "resourcePath": f"{map_resource_root}tiles/{tile_stem}",
                    "fileExtension": ext,
                    "bytes": tile_len,
                    "sha256": tile_sha,
                })

        recomp_proof = verify_recomposition_exact_rgba(image, tile_entries, tiles_dir)

        ov_w, ov_h = pc_imp.compute_overview_size(native_w, native_h, pc_imp.OVERVIEW_MAX_EDGE)
        overview_img = image if (ov_w, ov_h) == (native_w, native_h) else image.resize(
            (ov_w, ov_h), Image.Resampling.LANCZOS
        )
        ov_data, ov_ext = pc_imp.encode_image(overview_img, has_alpha=True, jpeg_quality=pc_imp.JPEG_OVERVIEW_QUALITY)
        pc_imp.require(ov_ext == ".png", "RGBA overview must be PNG")
        ov_file = app_dir / f"overview{ov_ext}"
        pc_imp.write_asset_file(ov_file, ov_data, project_root, delivery_root, has_alpha=True, is_overview=True)
        ov_sha = pc_imp.sha256_bytes(ov_data)
        ov_bytes = len(ov_data)

    tiles_manifest = {
        "schemaVersion": 1,
        "mapId": map_id,
        "numericMapId": numeric_id,
        "revision": revision,
        "artStage": QDAO_ART_STAGE,
        "appearance": appearance_id,
        "nativeWidth": native_w,
        "nativeHeight": native_h,
        "tileSize": tile_size,
        "columns": columns,
        "rows": rows,
        "hasAlpha": True,
        "tileFormat": tile_format,
        "resampled": False,
        "resourceRoot": map_resource_root,
        "sourceNativeSha256": actual_whole_sha,
        "tiles": tile_entries,
    }
    tiles_manifest_raw = pc_imp.json_bytes(tiles_manifest)
    tiles_manifest_sha = pc_imp.sha256_bytes(tiles_manifest_raw)
    pc_imp.write_asset_file(app_dir / "tiles.json", tiles_manifest_raw, project_root, delivery_root)

    per_appearance_source = {
        "schemaVersion": 1,
        "kind": "CLIENT_QDAO_REDRAWN_STATIC_BACK_SOURCE",
        "mapId": map_id,
        "numericMapId": numeric_id,
        "name": map_name,
        "appearance": appearance_id,
        "appearanceDisplayName": APPEARANCE_DISPLAY_NAMES.get(appearance_id, appearance_id),
        "revision": revision,
        "artStage": QDAO_ART_STAGE,
        "isTemporaryOriginal": False,
        "replaceableByQdaoArt": True,
        "staticBackOnly": True,
        "completeGameplaySceneClaimed": False,
        "productionTravelEnabled": False,
        "sourceBranch": pc_original_ref["sourceBranch"],
        "sourceStatus": "ready_qdao_static_back",
        "sourceDeliveryJsonSha256": delivery_json_sha,
        "sourceNativeSha256": actual_whole_sha,
        "sourceNativeBytes": whole_bytes,
        "sourceEvidenceSha256": actual_prov_sha,
        "sourceSliceIndexSha256": expected_slice_index_sha,
        "sourcePackageZipSha256": expected_package_sha,
        "pcOriginalResourceRoot": pc_original_ref["resourceRoot"],
        "pcOriginalSourceNativeSha256": pc_original_ref["sourceNativeSha256"],
        "pcOriginalSourceNativeBytes": pc_original_ref["sourceNativeBytes"],
        "pcOriginalSourceEvidenceSha256": pc_original_ref["sourceEvidenceSha256"],
        "pcOriginalSourceContainerSha256": pc_imp.EXPECTED_CONTAINER_SHA256,
        "nativeWidth": native_w,
        "nativeHeight": native_h,
        "alphaStats": alpha_stats,
        "tileSize": tile_size,
        "columns": columns,
        "rows": rows,
        "tileCount": len(tile_entries),
        "hasAlpha": True,
        "tileFormat": tile_format,
        "resampled": False,
        "overviewDerived": True,
        "overviewIsResizedPreviewOnly": True,
        "overviewWidth": ov_w,
        "overviewHeight": ov_h,
        "overviewExtension": ov_ext,
        "overviewBytes": ov_bytes,
        "overviewSha256": ov_sha,
        "tilesManifestSha256": tiles_manifest_sha,
        "recompositionVerification": recomp_proof,
        "serverRegistrationStatus": "unregistered_local_preview_only",
    }
    pc_imp.write_asset_file(app_dir / "source.json", pc_imp.json_bytes(per_appearance_source), project_root, delivery_root)

    appearance_catalog_entry = {
        "id": appearance_id,
        "displayName": APPEARANCE_DISPLAY_NAMES.get(appearance_id, appearance_id),
        "artStage": QDAO_ART_STAGE,
        "isTemporaryOriginal": False,
        "revision": revision,
        "hasAlpha": True,
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
        "sourceNativeSha256": actual_whole_sha,
        "sourceNativeBytes": whole_bytes,
        "sourceEvidenceSha256": actual_prov_sha,
    }
    appearance_receipt = {
        "id": appearance_id,
        "displayName": APPEARANCE_DISPLAY_NAMES.get(appearance_id, appearance_id),
        "revision": revision,
        "resourceRoot": map_resource_root,
        "assetDirectory": (DELIVERY_REL / map_id / revision).as_posix(),
        "sourceWholePngPath": whole_png_path.as_posix(),
        "sourceNativeSha256": actual_whole_sha,
        "sourceNativeBytes": whole_bytes,
        "sourceProvenanceSha256": actual_prov_sha,
        "sourceSliceIndexSha256": expected_slice_index_sha,
        "sourcePackageZipSha256": expected_package_sha,
        "nativePixels": [native_w, native_h],
        "alphaStats": alpha_stats,
        "columns": columns,
        "rows": rows,
        "tileCount": len(tile_entries),
        "totalTileBytes": total_tile_bytes,
        "tilesManifestSha256": tiles_manifest_sha,
        "overviewPixels": [ov_w, ov_h],
        "overviewBytes": ov_bytes,
        "overviewSha256": ov_sha,
        "recompositionVerification": recomp_proof,
        "tiles": tile_entries,
    }
    return appearance_catalog_entry, appearance_receipt


def import_qdao_dual_festival_map(
    *,
    map_id: str,
    delivery_dir: Path,
    project_root: Path,
    appearance_revisions: list[tuple[str, str]],
    default_appearance: str = "spring",
) -> dict:
    """Adopt a dual-festival Q-Dao static map into the existing PcOriginalPreview20261123 catalog."""
    delivery_json_path = delivery_dir / "delivery.json"
    pc_imp.require(delivery_json_path.is_file(), f"Missing delivery.json: {delivery_json_path}")
    delivery_raw = delivery_json_path.read_bytes()
    delivery_sha = pc_imp.sha256_bytes(delivery_raw)
    delivery = json.loads(delivery_raw.decode("utf-8-sig"))
    pc_imp.require(delivery.get("mapId") == map_id, f"delivery.json mapId mismatch: {delivery.get('mapId')}")
    pc_imp.require(delivery.get("staticBackgroundOnly") is True, "delivery.json must remain staticBackgroundOnly=true")

    catalog_path = project_root / DELIVERY_REL / "catalog.json"
    receipt_path = project_root / EVIDENCE_REL / "import-receipt.json"
    pc_imp.require(catalog_path.is_file(), f"Missing catalog.json: {catalog_path}")
    pc_imp.require(receipt_path.is_file(), f"Missing import-receipt.json: {receipt_path}")

    catalog = pc_imp.read_json(catalog_path)
    receipt = pc_imp.read_json(receipt_path)

    target_index = next((i for i, m in enumerate(catalog["maps"]) if m["mapId"] == map_id), None)
    pc_imp.require(target_index is not None, f"Map {map_id} not found in catalog.json")
    existing = catalog["maps"][target_index]

    # Always resolve original PC provenance from the preserved pc-original-20261123/source.json
    # so repeated runs remain deterministic and never lose the original client.dbfs SHA.
    pc_orig_source_path = project_root / DELIVERY_REL / map_id / pc_imp.REVISION / "source.json"
    pc_imp.require(pc_orig_source_path.is_file(), f"Missing preserved PC original source.json: {pc_orig_source_path}")
    pc_orig_src = pc_imp.read_json(pc_orig_source_path)
    pc_original_ref = {
        "resourceRoot": f"{RESOURCE_ROOT}{map_id}/{pc_imp.REVISION}/",
        "sourceBranch": pc_orig_src["sourceBranch"],
        "sourceNativeSha256": pc_orig_src["sourceNativeSha256"],
        "sourceNativeBytes": int(pc_orig_src["sourceNativeBytes"]),
        "sourceEvidenceSha256": pc_orig_src["sourceEvidenceSha256"],
        "nativeWidth": int(pc_orig_src["nativeWidth"]),
        "nativeHeight": int(pc_orig_src["nativeHeight"]),
    }

    appearances_catalog: list[dict] = []
    appearances_receipt: list[dict] = []
    for appearance_id, revision in appearance_revisions:
        app_delivery = (delivery.get("appearances") or {}).get(appearance_id)
        pc_imp.require(app_delivery is not None, f"Appearance '{appearance_id}' missing from delivery.json")
        app_cat, app_rec = derive_single_appearance(
            map_id=map_id,
            numeric_id=int(map_id),
            map_name=existing["name"],
            appearance_id=appearance_id,
            revision=revision,
            whole_png_path=Path(app_delivery["whole"]["path"]),
            expected_whole_sha=app_delivery["whole"]["sha256"],
            provenance_path=Path(app_delivery["provenance"]["path"]),
            expected_provenance_sha=app_delivery["provenance"]["sha256"],
            slice_index_path=Path(app_delivery["sliceIndex"]["path"]),
            expected_slice_index_sha=app_delivery["sliceIndex"]["sha256"],
            package_zip_path=Path(app_delivery["package"]["path"]),
            expected_package_sha=app_delivery["package"]["sha256"],
            delivery_json_sha=delivery_sha,
            pc_original_ref=pc_original_ref,
            project_root=project_root,
        )
        appearances_catalog.append(app_cat)
        appearances_receipt.append(app_rec)
        print(
            f"[QDAO {map_id}/{appearance_id}] {app_rec['nativePixels'][0]}x{app_rec['nativePixels'][1]} "
            f"{app_rec['columns']}x{app_rec['rows']}={app_rec['tileCount']} RGBA-PNG tiles "
            f"({app_rec['totalTileBytes']/1048576:.2f}MB) ov={app_rec['overviewBytes']/1048576:.2f}MB "
            f"recomposedRGBAEqual={app_rec['recompositionVerification']['rgbaByteEqualToWholeSource']}"
        )

    default_entry = next((a for a in appearances_catalog if a["id"] == default_appearance), None)
    pc_imp.require(default_entry is not None, f"Default appearance '{default_appearance}' not in derived list")
    default_receipt = next(a for a in appearances_receipt if a["id"] == default_appearance)

    # Update ONLY this map's single entry in catalog.json (unique mapId preserved).
    updated_entry = dict(existing)
    updated_entry.update({
        "artStage": default_entry["artStage"],
        "isTemporaryOriginal": False,
        "replaceableByQdaoArt": True,
        "revision": default_entry["revision"],
        "sourceStatus": "ready_qdao_static_back",
        "hasAlpha": True,
        "tileFormat": default_entry["tileFormat"],
        "resampled": False,
        "resourceRoot": default_entry["resourceRoot"],
        "overviewResourcePath": default_entry["overviewResourcePath"],
        "overviewExtension": default_entry["overviewExtension"],
        "overviewWidth": default_entry["overviewWidth"],
        "overviewHeight": default_entry["overviewHeight"],
        "overviewSha256": default_entry["overviewSha256"],
        "overviewBytes": default_entry["overviewBytes"],
        "tilesManifestResourcePath": default_entry["tilesManifestResourcePath"],
        "tilesManifestSha256": default_entry["tilesManifestSha256"],
        "sourceManifestResourcePath": default_entry["sourceManifestResourcePath"],
        "sourceNativeSha256": default_entry["sourceNativeSha256"],
        "sourceNativeBytes": default_entry["sourceNativeBytes"],
        "sourceEvidenceSha256": default_entry["sourceEvidenceSha256"],
        "pcOriginalSourceNativeSha256": pc_original_ref["sourceNativeSha256"],
        "pcOriginalSourceNativeBytes": pc_original_ref["sourceNativeBytes"],
        "defaultAppearance": default_appearance,
        "appearances": appearances_catalog,
        "serverRegistrationStatus": "unregistered_local_preview_only",
    })
    catalog["maps"][target_index] = updated_entry

    catalog_raw = pc_imp.json_bytes(catalog)
    catalog_sha = pc_imp.sha256_bytes(catalog_raw)
    pc_imp.write_asset_file(catalog_path, catalog_raw, project_root, project_root / DELIVERY_REL)

    # Update import-receipt.json catalogSha256 and the 28002 map entry so offline --verify matches.
    rec_index = next((i for i, m in enumerate(receipt["maps"]) if m["mapId"] == map_id), None)
    pc_imp.require(rec_index is not None, f"Map {map_id} not found in import-receipt.json")
    old_rec = receipt["maps"][rec_index]
    receipt["maps"][rec_index] = {
        "mapId": map_id,
        "numericMapId": int(map_id),
        "name": existing["name"],
        "nativePixels": default_receipt["nativePixels"],
        "sourceNativeSha256": default_entry["sourceNativeSha256"],
        "sourceNativeBytes": default_entry["sourceNativeBytes"],
        "pcOriginalSourceNativeSha256": pc_original_ref["sourceNativeSha256"],
        "sourceBranch": pc_original_ref["sourceBranch"],
        "resourceRoot": default_entry["resourceRoot"],
        "revision": default_entry["revision"],
        "artStage": QDAO_ART_STAGE,
        "defaultAppearance": default_appearance,
        "appearances": [
            {
                "id": a["id"],
                "revision": a["revision"],
                "resourceRoot": a["resourceRoot"],
                "sourceNativeSha256": a["sourceNativeSha256"],
                "tilesManifestSha256": a["tilesManifestSha256"],
                "totalTileBytes": a["totalTileBytes"],
                "overviewBytes": a["overviewBytes"],
            }
            for a in appearances_receipt
        ],
        "hasAlpha": True,
        "tileFormat": default_entry["tileFormat"],
        "columns": default_receipt["columns"],
        "rows": default_receipt["rows"],
        "tileCount": default_receipt["tileCount"],
        "totalTileBytes": default_receipt["totalTileBytes"],
        "overviewPixels": default_receipt["overviewPixels"],
        "overviewBytes": default_receipt["overviewBytes"],
        "tilesManifestSha256": default_entry["tilesManifestSha256"],
    }
    receipt["catalogSha256"] = catalog_sha
    receipt["totalTileBytes"] = int(receipt["totalTileBytes"]) - int(old_rec["totalTileBytes"]) + int(default_receipt["totalTileBytes"])
    receipt["totalOverviewBytes"] = int(receipt["totalOverviewBytes"]) - int(old_rec["overviewBytes"]) + int(default_receipt["overviewBytes"])
    receipt["totalAssetBytes"] = int(receipt["totalTileBytes"]) + int(receipt["totalOverviewBytes"])
    receipt_path.write_bytes(pc_imp.json_bytes(receipt))

    qdao_receipt = {
        "schemaVersion": 1,
        "kind": "CLIENT_QDAO_DUAL_FESTIVAL_MAP_ADOPTION_RECEIPT",
        "mapId": map_id,
        "numericMapId": int(map_id),
        "name": existing["name"],
        "artStage": QDAO_ART_STAGE,
        "isTemporaryOriginal": False,
        "staticBackOnly": True,
        "completeGameplaySceneClaimed": False,
        "productionTravelEnabled": False,
        "serverRegistrationStatus": "unregistered_local_preview_only",
        "authoritativeDeliveryPath": delivery_json_path.as_posix(),
        "authoritativeDeliverySha256": delivery_sha,
        "defaultAppearance": default_appearance,
        "pcOriginalProvenance": pc_original_ref,
        "catalogPath": (DELIVERY_REL / "catalog.json").as_posix(),
        "catalogSha256": catalog_sha,
        "appearances": appearances_receipt,
    }
    qdao_receipt_path = project_root / EVIDENCE_REL / f"{map_id}-qdao-dual-festival-receipt.json"
    qdao_receipt_path.write_bytes(pc_imp.json_bytes(qdao_receipt))
    print(f"WROTE {qdao_receipt_path} (catalogSha256={catalog_sha})")
    return qdao_receipt


def build_preserved_original_appearance(
    *,
    map_id: str,
    project_root: Path,
    display_name: str = "原版",
) -> tuple[dict, dict]:
    """Build a catalog appearance entry pointing directly to the untouched pc-original-20261123/ directory."""
    orig_dir = project_root / DELIVERY_REL / map_id / pc_imp.REVISION
    src = pc_imp.read_json(orig_dir / "source.json")
    tiles_raw = (orig_dir / "tiles.json").read_bytes()
    tiles_sha = pc_imp.sha256_bytes(tiles_raw)
    tm = json.loads(tiles_raw.decode("utf-8"))
    ov_ext = src["overviewExtension"]
    ov_bytes_raw = (orig_dir / f"overview{ov_ext}").read_bytes()
    ov_sha = pc_imp.sha256_bytes(ov_bytes_raw)
    pc_imp.require(ov_sha == src["overviewSha256"], f"{map_id}: preserved pc-original overview SHA mismatch")
    pc_imp.require(tiles_sha == src["tilesManifestSha256"], f"{map_id}: preserved pc-original tiles.json SHA mismatch")
    res_root = f"{RESOURCE_ROOT}{map_id}/{pc_imp.REVISION}/"
    total_tile_bytes = sum(int(t["bytes"]) for t in tm["tiles"])
    native_w, native_h = int(src["nativeWidth"]), int(src["nativeHeight"])
    entry = {
        "id": "normal",
        "displayName": display_name,
        "artStage": pc_imp.ART_STAGE,
        "isTemporaryOriginal": True,
        "revision": pc_imp.REVISION,
        "hasAlpha": bool(src["hasAlpha"]),
        "tileFormat": src["tileFormat"],
        "resampled": False,
        "nativeWidth": native_w,
        "nativeHeight": native_h,
        "tileSize": int(src["tileSize"]),
        "columns": int(src["columns"]),
        "rows": int(src["rows"]),
        "tileCount": int(src["tileCount"]),
        "resourceRoot": res_root,
        "overviewResourcePath": f"{res_root}overview",
        "overviewExtension": ov_ext,
        "overviewWidth": int(src["overviewWidth"]),
        "overviewHeight": int(src["overviewHeight"]),
        "overviewSha256": ov_sha,
        "overviewBytes": len(ov_bytes_raw),
        "tilesManifestResourcePath": f"{res_root}tiles",
        "tilesManifestSha256": tiles_sha,
        "sourceManifestResourcePath": f"{res_root}source",
        "sourceNativeSha256": src["sourceNativeSha256"],
        "sourceNativeBytes": int(src["sourceNativeBytes"]),
        "sourceEvidenceSha256": src["sourceEvidenceSha256"],
    }
    receipt = {
        "id": "normal",
        "displayName": display_name,
        "artStage": pc_imp.ART_STAGE,
        "revision": pc_imp.REVISION,
        "resourceRoot": res_root,
        "assetDirectory": (DELIVERY_REL / map_id / pc_imp.REVISION).as_posix(),
        "sourceNativeSha256": src["sourceNativeSha256"],
        "sourceNativeBytes": int(src["sourceNativeBytes"]),
        "nativePixels": [native_w, native_h],
        "worldPixels": [native_w, native_h],
        "columns": int(src["columns"]),
        "rows": int(src["rows"]),
        "tileCount": int(src["tileCount"]),
        "totalTileBytes": total_tile_bytes,
        "tilesManifestSha256": tiles_sha,
        "overviewPixels": [int(src["overviewWidth"]), int(src["overviewHeight"])],
        "overviewBytes": len(ov_bytes_raw),
        "overviewSha256": ov_sha,
        "preservedUntouched": True,
    }
    return entry, receipt


def derive_appearance_flexible(
    *,
    map_id: str,
    numeric_id: int,
    map_name: str,
    appearance_id: str,
    display_name: str,
    revision: str,
    whole_png_path: Path,
    expected_whole_sha: str,
    source_evidence_path: Path,
    pc_original_ref: dict,
    project_root: Path,
    world_grid: dict | None = None,
    precut_tiles: list[dict] | None = None,
    lossless_png: bool = False,
    tile_size: int = pc_imp.TILE_SIZE,
) -> tuple[dict, dict]:
    """Derive runtime tiles + overview + manifests + .meta for one Q-Dao appearance (1x or HD worldGrid, RGB or RGBA)."""
    pc_imp.require(whole_png_path.is_file(), f"Missing whole PNG: {whole_png_path}")
    actual_whole_sha = pc_imp.sha256_file(whole_png_path)
    pc_imp.require(
        actual_whole_sha == expected_whole_sha,
        f"{map_id}/{appearance_id}: whole PNG SHA mismatch ({actual_whole_sha} != {expected_whole_sha})",
    )
    whole_bytes = whole_png_path.stat().st_size
    pc_imp.require(source_evidence_path.is_file(), f"Missing source evidence: {source_evidence_path}")
    evidence_sha = pc_imp.sha256_file(source_evidence_path)

    delivery_root = project_root / DELIVERY_REL
    map_resource_root = f"{RESOURCE_ROOT}{map_id}/{revision}/"
    app_dir = delivery_root / map_id / revision
    tiles_dir = app_dir / "tiles"

    orig_w, orig_h = pc_original_ref["nativeWidth"], pc_original_ref["nativeHeight"]
    with Image.open(whole_png_path) as image:
        pc_imp.require(image.format == "PNG", f"{map_id}/{appearance_id}: expected PNG source")
        image.load()
        native_w, native_h = image.size
        if world_grid is not None:
            pc_imp.require(
                (int(world_grid["widthPixels"]), int(world_grid["heightPixels"])) == (orig_w, orig_h),
                f"{map_id}/{appearance_id}: worldGrid extent must equal PC original {orig_w}x{orig_h}",
            )
            pc_imp.require(
                native_w >= orig_w and native_h >= orig_h and native_w * orig_h == native_h * orig_w,
                f"{map_id}/{appearance_id}: HD size {native_w}x{native_h} invalid for world {orig_w}x{orig_h}",
            )
        else:
            pc_imp.require(
                (native_w, native_h) == (orig_w, orig_h),
                f"{map_id}/{appearance_id}: pixel size {(native_w, native_h)} != PC original {(orig_w, orig_h)}",
            )

        has_alpha = pc_imp.image_has_non_opaque_alpha(image)
        use_png = has_alpha or lossless_png
        alpha_stats = compute_alpha_stats(image) if has_alpha else None
        tile_entries: list[dict] = []
        total_tile_bytes = 0

        if precut_tiles is not None:
            pc_imp.require(world_grid is not None, f"{map_id}/{appearance_id}: precut_tiles requires world_grid")
            gw, gh = int(world_grid["widthPixels"]), int(world_grid["heightPixels"])
            gtw, gth = int(world_grid["tileWidthPixels"]), int(world_grid["tileHeightPixels"])
            columns = math.ceil(gw / gtw)
            rows = math.ceil(gh / gth)
            pc_imp.require(len(precut_tiles) == columns * rows, f"{map_id}/{appearance_id}: precut tile count mismatch")
            nom_tw = (min(gtw, gw) * native_w + gw // 2) // gw
            nom_th = (min(gth, gh) * native_h + gh // 2) // gh
            tile_format = f"png_{'rgba' if has_alpha else 'rgb'}_hd_worldgrid_{nom_tw}x{nom_th}"
            # Map delivered (pixelOrigin) tiles into deterministic r{row:02d}_c{col:02d}.png without re-encoding.
            by_origin = {(int(t["pixelOrigin"][0]), int(t["pixelOrigin"][1])): t for t in precut_tiles}
            for row in range(1, rows + 1):
                y0 = (min((row - 1) * gth, gh) * native_h + gh // 2) // gh
                y1 = (min(row * gth, gh) * native_h + gh // 2) // gh
                tile_h = y1 - y0
                for col in range(1, columns + 1):
                    x0 = (min((col - 1) * gtw, gw) * native_w + gw // 2) // gw
                    x1 = (min(col * gtw, gw) * native_w + gw // 2) // gw
                    tile_w = x1 - x0
                    pc_imp.require(tile_w <= tile_size and tile_h <= tile_size, f"{map_id}: tile exceeds {tile_size}")
                    src_tile = by_origin.get((x0, y0))
                    pc_imp.require(src_tile is not None, f"{map_id}/{appearance_id}: missing precut tile at {(x0, y0)}")
                    src_tile_path = Path(src_tile["path"])
                    raw_tile = src_tile_path.read_bytes()
                    actual_t_sha = pc_imp.sha256_bytes(raw_tile)
                    pc_imp.require(actual_t_sha == src_tile["sha256"], f"{map_id}: precut tile SHA mismatch {src_tile_path}")
                    tile_stem = f"r{row:02d}_c{col:02d}"
                    tile_file = tiles_dir / f"{tile_stem}.png"
                    pc_imp.write_asset_file(
                        tile_file, raw_tile, project_root, delivery_root, has_alpha=has_alpha, is_overview=False
                    )
                    total_tile_bytes += len(raw_tile)
                    tile_entries.append({
                        "row": row,
                        "column": col,
                        "x": x0,
                        "y": y0,
                        "width": tile_w,
                        "height": tile_h,
                        "resourcePath": f"{map_resource_root}tiles/{tile_stem}",
                        "fileExtension": ".png",
                        "bytes": len(raw_tile),
                        "sha256": actual_t_sha,
                    })
        elif world_grid is not None:
            gw, gh = int(world_grid["widthPixels"]), int(world_grid["heightPixels"])
            gtw, gth = int(world_grid["tileWidthPixels"]), int(world_grid["tileHeightPixels"])
            columns = math.ceil(gw / gtw)
            rows = math.ceil(gh / gth)
            nom_tw = (min(gtw, gw) * native_w + gw // 2) // gw
            nom_th = (min(gth, gh) * native_h + gh // 2) // gh
            tile_format = (
                f"png_{'rgba' if has_alpha else 'rgb'}_hd_worldgrid_{nom_tw}x{nom_th}"
                if use_png
                else f"jpg_q{pc_imp.JPEG_TILE_QUALITY}_hd_worldgrid_{nom_tw}x{nom_th}"
            )
            for row in range(1, rows + 1):
                y0 = (min((row - 1) * gth, gh) * native_h + gh // 2) // gh
                y1 = (min(row * gth, gh) * native_h + gh // 2) // gh
                tile_h = y1 - y0
                for col in range(1, columns + 1):
                    x0 = (min((col - 1) * gtw, gw) * native_w + gw // 2) // gw
                    x1 = (min(col * gtw, gw) * native_w + gw // 2) // gw
                    tile_w = x1 - x0
                    pc_imp.require(tile_w <= tile_size and tile_h <= tile_size, f"{map_id}: tile exceeds {tile_size}")
                    crop = image.crop((x0, y0, x1, y1))
                    pc_imp.require(crop.size == (tile_w, tile_h), "Tile crop mismatch")
                    if use_png and not has_alpha:
                        buf = io.BytesIO()
                        (crop if crop.mode == "RGB" else crop.convert("RGB")).save(buf, format="PNG", optimize=False)
                        tile_data, ext = buf.getvalue(), ".png"
                    else:
                        tile_data, ext = pc_imp.encode_image(crop, has_alpha=has_alpha, jpeg_quality=pc_imp.JPEG_TILE_QUALITY)
                    tile_stem = f"r{row:02d}_c{col:02d}"
                    tile_file = tiles_dir / f"{tile_stem}{ext}"
                    pc_imp.write_asset_file(
                        tile_file, tile_data, project_root, delivery_root, has_alpha=has_alpha, is_overview=False
                    )
                    tile_sha = pc_imp.sha256_bytes(tile_data)
                    tile_len = len(tile_data)
                    total_tile_bytes += tile_len
                    tile_entries.append({
                        "row": row,
                        "column": col,
                        "x": x0,
                        "y": y0,
                        "width": tile_w,
                        "height": tile_h,
                        "resourcePath": f"{map_resource_root}tiles/{tile_stem}",
                        "fileExtension": ext,
                        "bytes": tile_len,
                        "sha256": tile_sha,
                    })
        else:
            columns = math.ceil(native_w / tile_size)
            rows = math.ceil(native_h / tile_size)
            tile_format = (
                f"png_{'rgba' if has_alpha else 'rgb'}_native_1024_grid"
                if use_png
                else f"jpg_q{pc_imp.JPEG_TILE_QUALITY}_native_1024_grid"
            )
            for row in range(1, rows + 1):
                y0 = (row - 1) * tile_size
                y1 = min(native_h, row * tile_size)
                tile_h = y1 - y0
                for col in range(1, columns + 1):
                    x0 = (col - 1) * tile_size
                    x1 = min(native_w, col * tile_size)
                    tile_w = x1 - x0
                    crop = image.crop((x0, y0, x1, y1))
                    pc_imp.require(crop.size == (tile_w, tile_h), "Tile crop mismatch")
                    if use_png and not has_alpha:
                        buf = io.BytesIO()
                        (crop if crop.mode == "RGB" else crop.convert("RGB")).save(buf, format="PNG", optimize=False)
                        tile_data, ext = buf.getvalue(), ".png"
                    else:
                        tile_data, ext = pc_imp.encode_image(crop, has_alpha=has_alpha, jpeg_quality=pc_imp.JPEG_TILE_QUALITY)
                    tile_stem = f"r{row:02d}_c{col:02d}"
                    tile_file = tiles_dir / f"{tile_stem}{ext}"
                    pc_imp.write_asset_file(
                        tile_file, tile_data, project_root, delivery_root, has_alpha=has_alpha, is_overview=False
                    )
                    tile_sha = pc_imp.sha256_bytes(tile_data)
                    tile_len = len(tile_data)
                    total_tile_bytes += tile_len
                    tile_entries.append({
                        "row": row,
                        "column": col,
                        "x": x0,
                        "y": y0,
                        "width": tile_w,
                        "height": tile_h,
                        "resourcePath": f"{map_resource_root}tiles/{tile_stem}",
                        "fileExtension": ext,
                        "bytes": tile_len,
                        "sha256": tile_sha,
                    })

        if has_alpha:
            recomp_proof = verify_recomposition_exact_rgba(image, tile_entries, tiles_dir)
        elif use_png:
            recomp_proof = verify_recomposition_exact_rgb(image, tile_entries, tiles_dir)
        else:
            recomp_proof = None

        ov_w, ov_h = pc_imp.compute_overview_size(native_w, native_h, pc_imp.OVERVIEW_MAX_EDGE)
        overview_img = image if (ov_w, ov_h) == (native_w, native_h) else image.resize(
            (ov_w, ov_h), Image.Resampling.LANCZOS
        )
        ov_data, ov_ext = pc_imp.encode_image(overview_img, has_alpha=has_alpha, jpeg_quality=pc_imp.JPEG_OVERVIEW_QUALITY)
        ov_file = app_dir / f"overview{ov_ext}"
        pc_imp.write_asset_file(ov_file, ov_data, project_root, delivery_root, has_alpha=has_alpha, is_overview=True)
        ov_sha = pc_imp.sha256_bytes(ov_data)
        ov_bytes = len(ov_data)

    tiles_manifest: dict = {
        "schemaVersion": 2 if world_grid is not None else 1,
        "mapId": map_id,
        "numericMapId": numeric_id,
        "revision": revision,
        "artStage": QDAO_ART_STAGE,
        "appearance": appearance_id,
        "nativeWidth": native_w,
        "nativeHeight": native_h,
    }
    if world_grid is not None:
        tiles_manifest["worldGrid"] = world_grid
    tiles_manifest.update({
        "tileSize": tile_size,
        "columns": columns,
        "rows": rows,
        "hasAlpha": has_alpha,
        "tileFormat": tile_format,
        "resampled": False,
        "resourceRoot": map_resource_root,
        "sourceNativeSha256": actual_whole_sha,
        "tiles": tile_entries,
    })
    tiles_manifest_raw = pc_imp.json_bytes(tiles_manifest)
    tiles_manifest_sha = pc_imp.sha256_bytes(tiles_manifest_raw)
    pc_imp.write_asset_file(app_dir / "tiles.json", tiles_manifest_raw, project_root, delivery_root)

    per_appearance_source: dict = {
        "schemaVersion": 1,
        "kind": "CLIENT_QDAO_REDRAWN_STATIC_BACK_SOURCE",
        "mapId": map_id,
        "numericMapId": numeric_id,
        "name": map_name,
        "appearance": appearance_id,
        "appearanceDisplayName": display_name,
        "revision": revision,
        "artStage": QDAO_ART_STAGE,
        "isTemporaryOriginal": False,
        "replaceableByQdaoArt": True,
        "staticBackOnly": True,
        "completeGameplaySceneClaimed": False,
        "productionTravelEnabled": False,
        "sourceBranch": pc_original_ref["sourceBranch"],
        "sourceStatus": "ready_qdao_static_back",
        "sourceEvidencePath": source_evidence_path.as_posix(),
        "sourceEvidenceSha256": evidence_sha,
        "sourceNativeSha256": actual_whole_sha,
        "sourceNativeBytes": whole_bytes,
        "pcOriginalResourceRoot": pc_original_ref["resourceRoot"],
        "pcOriginalSourceNativeSha256": pc_original_ref["sourceNativeSha256"],
        "pcOriginalSourceNativeBytes": pc_original_ref["sourceNativeBytes"],
        "pcOriginalSourceEvidenceSha256": pc_original_ref["sourceEvidenceSha256"],
        "pcOriginalSourceContainerSha256": pc_imp.EXPECTED_CONTAINER_SHA256,
        "nativeWidth": native_w,
        "nativeHeight": native_h,
        "worldWidthPixels": orig_w,
        "worldHeightPixels": orig_h,
        "tileSize": tile_size,
        "columns": columns,
        "rows": rows,
        "tileCount": len(tile_entries),
        "hasAlpha": has_alpha,
        "tileFormat": tile_format,
        "resampled": False,
        "overviewDerived": True,
        "overviewIsResizedPreviewOnly": True,
        "overviewWidth": ov_w,
        "overviewHeight": ov_h,
        "overviewExtension": ov_ext,
        "overviewBytes": ov_bytes,
        "overviewSha256": ov_sha,
        "tilesManifestSha256": tiles_manifest_sha,
        "serverRegistrationStatus": "unregistered_local_preview_only",
    }
    if world_grid is not None:
        per_appearance_source["worldGrid"] = world_grid
    if alpha_stats is not None:
        per_appearance_source["alphaStats"] = alpha_stats
    if recomp_proof is not None:
        per_appearance_source["recompositionVerification"] = recomp_proof
    pc_imp.write_asset_file(app_dir / "source.json", pc_imp.json_bytes(per_appearance_source), project_root, delivery_root)

    appearance_catalog_entry: dict = {
        "id": appearance_id,
        "displayName": display_name,
        "artStage": QDAO_ART_STAGE,
        "isTemporaryOriginal": False,
        "revision": revision,
        "hasAlpha": has_alpha,
        "tileFormat": tile_format,
        "resampled": False,
        "nativeWidth": native_w,
        "nativeHeight": native_h,
        "tileSize": tile_size,
        "columns": columns,
        "rows": rows,
        "tileCount": len(tile_entries),
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
        "sourceNativeSha256": actual_whole_sha,
        "sourceNativeBytes": whole_bytes,
        "sourceEvidenceSha256": evidence_sha,
    }
    if world_grid is not None:
        appearance_catalog_entry["worldGrid"] = world_grid

    appearance_receipt: dict = {
        "id": appearance_id,
        "displayName": display_name,
        "artStage": QDAO_ART_STAGE,
        "revision": revision,
        "resourceRoot": map_resource_root,
        "assetDirectory": (DELIVERY_REL / map_id / revision).as_posix(),
        "sourceWholePngPath": whole_png_path.as_posix(),
        "sourceNativeSha256": actual_whole_sha,
        "sourceNativeBytes": whole_bytes,
        "sourceEvidencePath": source_evidence_path.as_posix(),
        "sourceEvidenceSha256": evidence_sha,
        "nativePixels": [native_w, native_h],
        "worldPixels": [orig_w, orig_h],
        "hasAlpha": has_alpha,
        "tileFormat": tile_format,
        "columns": columns,
        "rows": rows,
        "tileCount": len(tile_entries),
        "totalTileBytes": total_tile_bytes,
        "tilesManifestSha256": tiles_manifest_sha,
        "overviewPixels": [ov_w, ov_h],
        "overviewBytes": ov_bytes,
        "overviewSha256": ov_sha,
    }
    if world_grid is not None:
        appearance_receipt["worldGrid"] = world_grid
    if alpha_stats is not None:
        appearance_receipt["alphaStats"] = alpha_stats
    if recomp_proof is not None:
        appearance_receipt["recompositionVerification"] = recomp_proof
    return appearance_catalog_entry, appearance_receipt


def adopt_map_appearances(
    *,
    map_id: str,
    project_root: Path,
    qdao_specs: list[dict],
    default_appearance: str,
    include_preserved_original: bool = True,
    original_first: bool = False,
    receipt_filename: str | None = None,
) -> dict:
    """Update a single mapId entry in catalog.json + import-receipt.json with one or more Q-Dao appearances."""
    catalog_path = project_root / DELIVERY_REL / "catalog.json"
    receipt_path = project_root / EVIDENCE_REL / "import-receipt.json"
    catalog = pc_imp.read_json(catalog_path)
    receipt = pc_imp.read_json(receipt_path)

    target_index = next((i for i, m in enumerate(catalog["maps"]) if m["mapId"] == map_id), None)
    pc_imp.require(target_index is not None, f"Map {map_id} not found in catalog.json")
    existing = catalog["maps"][target_index]

    pc_orig_source_path = project_root / DELIVERY_REL / map_id / pc_imp.REVISION / "source.json"
    pc_orig_src = pc_imp.read_json(pc_orig_source_path)
    pc_original_ref = {
        "resourceRoot": f"{RESOURCE_ROOT}{map_id}/{pc_imp.REVISION}/",
        "sourceBranch": pc_orig_src["sourceBranch"],
        "sourceNativeSha256": pc_orig_src["sourceNativeSha256"],
        "sourceNativeBytes": int(pc_orig_src["sourceNativeBytes"]),
        "sourceEvidenceSha256": pc_orig_src["sourceEvidenceSha256"],
        "nativeWidth": int(pc_orig_src["nativeWidth"]),
        "nativeHeight": int(pc_orig_src["nativeHeight"]),
    }

    appearances_catalog: list[dict] = []
    appearances_receipt: list[dict] = []

    if include_preserved_original and original_first:
        o_cat, o_rec = build_preserved_original_appearance(map_id=map_id, project_root=project_root)
        appearances_catalog.append(o_cat)
        appearances_receipt.append(o_rec)

    for spec in qdao_specs:
        a_cat, a_rec = derive_appearance_flexible(
            map_id=map_id,
            numeric_id=int(map_id),
            map_name=existing["name"],
            appearance_id=spec["id"],
            display_name=spec.get("displayName", APPEARANCE_DISPLAY_NAMES.get(spec["id"], spec["id"])),
            revision=spec["revision"],
            whole_png_path=Path(spec["wholePng"]),
            expected_whole_sha=spec["expectedSha"],
            source_evidence_path=Path(spec["evidencePath"]),
            pc_original_ref=pc_original_ref,
            project_root=project_root,
            world_grid=spec.get("worldGrid"),
            precut_tiles=spec.get("precutTiles"),
            lossless_png=bool(spec.get("losslessPng", False)),
        )
        appearances_catalog.append(a_cat)
        appearances_receipt.append(a_rec)
        print(
            f"[QDAO {map_id}/{spec['id']}] {a_rec['nativePixels'][0]}x{a_rec['nativePixels'][1]} "
            f"(world {a_rec['worldPixels'][0]}x{a_rec['worldPixels'][1]}) "
            f"{a_rec['columns']}x{a_rec['rows']}={a_rec['tileCount']} "
            f"{'RGBA-PNG' if a_rec['hasAlpha'] else 'RGB-JPG'} "
            f"({a_rec['totalTileBytes']/1048576:.2f}MB) ov={a_rec['overviewBytes']/1048576:.2f}MB"
        )

    if include_preserved_original and not original_first:
        o_cat, o_rec = build_preserved_original_appearance(map_id=map_id, project_root=project_root)
        appearances_catalog.append(o_cat)
        appearances_receipt.append(o_rec)

    default_entry = next((a for a in appearances_catalog if a["id"] == default_appearance), None)
    pc_imp.require(default_entry is not None, f"{map_id}: defaultAppearance '{default_appearance}' not found")
    default_receipt = next(a for a in appearances_receipt if a["id"] == default_appearance)

    updated_entry = dict(existing)
    updated_entry.pop("worldGrid", None)
    updated_entry.update({
        "artStage": default_entry["artStage"],
        "isTemporaryOriginal": default_entry["isTemporaryOriginal"],
        "replaceableByQdaoArt": True,
        "revision": default_entry["revision"],
        "sourceStatus": "ready_static_back" if default_entry["artStage"] == pc_imp.ART_STAGE else "ready_qdao_static_back",
        "nativeWidth": int(default_entry["nativeWidth"]),
        "nativeHeight": int(default_entry["nativeHeight"]),
        "aspectRatio": int(default_entry["nativeWidth"]) / float(int(default_entry["nativeHeight"])),
        "tileSize": int(default_entry["tileSize"]),
        "columns": int(default_entry["columns"]),
        "rows": int(default_entry["rows"]),
        "tileCount": int(default_entry["tileCount"]),
        "hasAlpha": bool(default_entry["hasAlpha"]),
        "tileFormat": default_entry["tileFormat"],
        "resampled": False,
        "resourceRoot": default_entry["resourceRoot"],
        "overviewResourcePath": default_entry["overviewResourcePath"],
        "overviewExtension": default_entry["overviewExtension"],
        "overviewWidth": int(default_entry["overviewWidth"]),
        "overviewHeight": int(default_entry["overviewHeight"]),
        "overviewSha256": default_entry["overviewSha256"],
        "overviewBytes": int(default_entry["overviewBytes"]),
        "tilesManifestResourcePath": default_entry["tilesManifestResourcePath"],
        "tilesManifestSha256": default_entry["tilesManifestSha256"],
        "sourceManifestResourcePath": default_entry["sourceManifestResourcePath"],
        "sourceNativeSha256": default_entry["sourceNativeSha256"],
        "sourceNativeBytes": int(default_entry["sourceNativeBytes"]),
        "sourceEvidenceSha256": default_entry["sourceEvidenceSha256"],
        "pcOriginalSourceNativeSha256": pc_original_ref["sourceNativeSha256"],
        "pcOriginalSourceNativeBytes": pc_original_ref["sourceNativeBytes"],
        "defaultAppearance": default_appearance,
        "appearances": appearances_catalog,
        "serverRegistrationStatus": "unregistered_local_preview_only",
    })
    if default_entry.get("worldGrid"):
        updated_entry["worldGrid"] = default_entry["worldGrid"]
    catalog["maps"][target_index] = updated_entry

    catalog_raw = pc_imp.json_bytes(catalog)
    catalog_sha = pc_imp.sha256_bytes(catalog_raw)
    pc_imp.write_asset_file(catalog_path, catalog_raw, project_root, project_root / DELIVERY_REL)

    rec_index = next((i for i, m in enumerate(receipt["maps"]) if m["mapId"] == map_id), None)
    pc_imp.require(rec_index is not None, f"Map {map_id} not found in import-receipt.json")
    old_rec = receipt["maps"][rec_index]
    receipt["maps"][rec_index] = {
        "mapId": map_id,
        "numericMapId": int(map_id),
        "name": existing["name"],
        "nativePixels": default_receipt["nativePixels"],
        "sourceNativeSha256": default_entry["sourceNativeSha256"],
        "sourceNativeBytes": int(default_entry["sourceNativeBytes"]),
        "pcOriginalSourceNativeSha256": pc_original_ref["sourceNativeSha256"],
        "sourceBranch": pc_original_ref["sourceBranch"],
        "resourceRoot": default_entry["resourceRoot"],
        "revision": default_entry["revision"],
        "artStage": default_entry["artStage"],
        "defaultAppearance": default_appearance,
        "appearances": [
            {
                "id": a["id"],
                "artStage": a["artStage"],
                "revision": a["revision"],
                "nativePixels": a["nativePixels"],
                "worldPixels": a["worldPixels"],
                "resourceRoot": a["resourceRoot"],
                "sourceNativeSha256": a["sourceNativeSha256"],
                "tilesManifestSha256": a["tilesManifestSha256"],
                "totalTileBytes": a["totalTileBytes"],
                "overviewBytes": a["overviewBytes"],
            }
            for a in appearances_receipt
        ],
        "hasAlpha": bool(default_entry["hasAlpha"]),
        "tileFormat": default_entry["tileFormat"],
        "columns": default_receipt["columns"],
        "rows": default_receipt["rows"],
        "tileCount": default_receipt["tileCount"],
        "totalTileBytes": default_receipt["totalTileBytes"],
        "overviewPixels": default_receipt["overviewPixels"],
        "overviewBytes": default_receipt["overviewBytes"],
        "tilesManifestSha256": default_entry["tilesManifestSha256"],
    }
    receipt["catalogSha256"] = catalog_sha
    receipt["totalTileBytes"] = int(receipt["totalTileBytes"]) - int(old_rec["totalTileBytes"]) + int(default_receipt["totalTileBytes"])
    receipt["totalOverviewBytes"] = int(receipt["totalOverviewBytes"]) - int(old_rec["overviewBytes"]) + int(default_receipt["overviewBytes"])
    receipt["totalAssetBytes"] = int(receipt["totalTileBytes"]) + int(receipt["totalOverviewBytes"])
    receipt_path.write_bytes(pc_imp.json_bytes(receipt))

    map_receipt = {
        "schemaVersion": 1,
        "kind": "CLIENT_QDAO_STATIC_MAP_ADOPTION_RECEIPT",
        "mapId": map_id,
        "numericMapId": int(map_id),
        "name": existing["name"],
        "defaultAppearance": default_appearance,
        "staticBackOnly": True,
        "completeGameplaySceneClaimed": False,
        "productionTravelEnabled": False,
        "serverRegistrationStatus": "unregistered_local_preview_only",
        "pcOriginalProvenance": pc_original_ref,
        "catalogPath": (DELIVERY_REL / "catalog.json").as_posix(),
        "catalogSha256": catalog_sha,
        "appearances": appearances_receipt,
    }
    out_name = receipt_filename or f"{map_id}-qdao-adoption-receipt.json"
    out_path = project_root / EVIDENCE_REL / out_name
    out_path.write_bytes(pc_imp.json_bytes(map_receipt))
    print(f"WROTE {out_path} (default={default_appearance}, catalogSha256={catalog_sha[:16]}...)")
    return map_receipt


def adopt_completed_batch(designs_root: Path, project_root: Path) -> list[dict]:
    """Adopt 08001 4x HD candidate (original kept default) + all completed Q-Dao maps in designs_root."""
    receipts: list[dict] = []

    # 1. 08001 4x HD candidate (6400x4800 RGBA on unchanged 3200x2400 worldGrid; original stays default!)
    d08001 = designs_root / "pc-map-08001-4x-20261124-v001"
    specs_08001 = []
    for season, label, rev in [
        ("spring", "春节高清", "qdao-spring-4x-v001"),
        ("midautumn", "中秋高清", "qdao-midautumn-4x-v001"),
    ]:
        dm_path = d08001 / f"08001-{season}-delivery.json"
        dm = pc_imp.read_json(dm_path)
        ts800 = next(t for t in dm["tileSets"] if t["tilePixels"] == [800, 600])
        specs_08001.append({
            "id": season,
            "displayName": label,
            "revision": rev,
            "wholePng": d08001 / dm["highResolution"]["file"],
            "expectedSha": dm["highResolution"]["sha256"],
            "evidencePath": dm_path,
            "worldGrid": {"widthPixels": 3200, "heightPixels": 2400, "tileWidthPixels": 400, "tileHeightPixels": 300},
            "precutTiles": [
                {"path": d08001 / t["file"], "pixelOrigin": t["pixelOrigin"], "sha256": t["sha256"]}
                for t in ts800["tiles"]
            ],
        })
    receipts.append(adopt_map_appearances(
        map_id="08001",
        project_root=project_root,
        qdao_specs=specs_08001,
        default_appearance="normal",
        include_preserved_original=True,
        original_first=True,
        receipt_filename="08001-hd-4x-candidate-receipt.json",
    ))

    # 2. 03000 (4400x4400 dual-festival RGB)
    d03000 = designs_root / "pc-original-03000-festivals-20261123-v001"
    m03000 = pc_imp.read_json(d03000 / "delivery-manifest.json")
    deliv_03000 = {item["appearance"]: item for item in m03000["deliveries"]}
    receipts.append(adopt_map_appearances(
        map_id="03000",
        project_root=project_root,
        qdao_specs=[
            {
                "id": app_id,
                "revision": f"qdao-{app_id}-v001",
                "wholePng": d03000 / deliv_03000[app_id]["whole"],
                "expectedSha": deliv_03000[app_id]["sha256"],
                "evidencePath": d03000 / "delivery-manifest.json",
            }
            for app_id in ("spring", "midautumn")
        ],
        default_appearance="spring",
        include_preserved_original=True,
    ))

    # 3. 07001 (6000x4970 dual-festival RGB)
    d07001 = designs_root / "client07001-continuous-20261123-v001"
    m07001 = pc_imp.read_json(d07001 / "delivery-manifest.json")
    apps_07001 = {item["appearance"]: item for item in m07001["appearances"]}
    receipts.append(adopt_map_appearances(
        map_id="07001",
        project_root=project_root,
        qdao_specs=[
            {
                "id": "spring",
                "revision": "qdao-spring-v002",
                "wholePng": Path(apps_07001["spring"]["master"]["file"]),
                "expectedSha": apps_07001["spring"]["master"]["sha256"],
                "evidencePath": d07001 / "delivery-manifest.json",
            },
            {
                "id": "midautumn",
                "revision": "qdao-midautumn-v001",
                "wholePng": Path(apps_07001["midautumn"]["master"]["file"]),
                "expectedSha": apps_07001["midautumn"]["master"]["sha256"],
                "evidencePath": d07001 / "delivery-manifest.json",
            },
        ],
        default_appearance="spring",
        include_preserved_original=True,
    ))

    # 4. 15000 (1920x2298 spring RGBA complete; midautumn still UNKNOWN/blocked)
    d15000 = designs_root / "client-15000-festivals-20261123-v001"
    m15000 = pc_imp.read_json(d15000 / "spring/tiles/manifest.json")
    receipts.append(adopt_map_appearances(
        map_id="15000",
        project_root=project_root,
        qdao_specs=[{
            "id": "spring",
            "revision": "qdao-spring-v001",
            "wholePng": Path(m15000["source"]),
            "expectedSha": m15000["sourceSHA256"],
            "evidencePath": d15000 / "validation.json",
        }],
        default_appearance="spring",
        include_preserved_original=True,
    ))

    # 5. 19003 (1920x1080 dual-festival opaque RGBA -> RGB JPG)
    d19003 = designs_root / "client-19003-festivals-20261123-v001"
    specs_19003 = []
    for app_id in ("spring", "midautumn"):
        der_path = d19003 / f"{app_id}.png.derived.json"
        der = pc_imp.read_json(der_path)
        specs_19003.append({
            "id": app_id,
            "revision": f"qdao-{app_id}-v001",
            "wholePng": d19003 / der["file"]["path"],
            "expectedSha": der["file"]["sha256"],
            "evidencePath": der_path,
        })
    receipts.append(adopt_map_appearances(
        map_id="19003",
        project_root=project_root,
        qdao_specs=specs_19003,
        default_appearance="spring",
        include_preserved_original=True,
    ))

    # 6. 23000 (4000x5000 dual-festival opaque RGBA -> RGB JPG)
    d23000 = designs_root / "pc-23000-festivals-20261123-v001"
    m23000 = pc_imp.read_json(d23000 / "delivery.json")
    receipts.append(adopt_map_appearances(
        map_id="23000",
        project_root=project_root,
        qdao_specs=[
            {
                "id": app_id,
                "revision": f"qdao-{app_id}-v001",
                "wholePng": Path(m23000["appearances"][app_id]["full"]["file"]),
                "expectedSha": m23000["appearances"][app_id]["full"]["sha256"],
                "evidencePath": d23000 / "delivery.json",
            }
            for app_id in ("spring", "midautumn")
        ],
        default_appearance="spring",
        include_preserved_original=True,
    ))

    # 7. 33009 (4800x3600 dual-festival RGBA with real alpha)
    d33009 = designs_root / "pc-33009-dual-festival-20261123-v001"
    m33009 = pc_imp.read_json(d33009 / "DELIVERY.json")
    receipts.append(adopt_map_appearances(
        map_id="33009",
        project_root=project_root,
        qdao_specs=[
            {
                "id": app_id,
                "revision": f"qdao-{app_id}-v001",
                "wholePng": Path(m33009["variants"][app_id]["file"]),
                "expectedSha": m33009["variants"][app_id]["sha256"],
                "evidencePath": d33009 / "DELIVERY.json",
            }
            for app_id in ("spring", "midautumn")
        ],
        default_appearance="spring",
        include_preserved_original=True,
    ))

    # 8. 41081 (4000x2600 dual-festival RGB)
    d41081 = designs_root / "client-map-41081-festivals-20261123-v001"
    m41081 = pc_imp.read_json(d41081 / "manifest.json")
    receipts.append(adopt_map_appearances(
        map_id="41081",
        project_root=project_root,
        qdao_specs=[
            {
                "id": app_id,
                "revision": f"qdao-{app_id}-v001",
                "wholePng": d41081 / m41081["appearances"][app_id]["wholeImage"],
                "expectedSha": m41081["appearances"][app_id]["sha256"],
                "evidencePath": d41081 / "manifest.json",
            }
            for app_id in ("spring", "midautumn")
        ],
        default_appearance="spring",
        include_preserved_original=True,
    ))

    return receipts


DEFAULT_16001_BASELINE_DELIVERY = Path(
    "D:/work/image/.codex-artifacts/client-resume-20261123-bajing/"
    "retained16001-resume-20261123/zero-ai-festival-delivery-v001"
)


def adopt_16001_baseline(delivery_dir: Path, project_root: Path) -> dict:
    """Adopt formally accepted 1920x1286 Q-Dao dual-festival baseline for 16001 while preserving pc-original-20261123."""
    index_path = delivery_dir / "DELIVERY_INDEX.json"
    idx = pc_imp.read_json(index_path)
    pc_imp.require(idx.get("mapId") == "16001", f"Unexpected mapId in {index_path}")
    by_app = {v["appearance"]: v for v in idx.get("variants") or []}
    specs = []
    for app_id in ("spring", "midautumn"):
        pc_imp.require(app_id in by_app, f"16001 baseline missing appearance '{app_id}'")
        whole = by_app[app_id]["whole"]
        specs.append({
            "id": app_id,
            "revision": f"qdao-{app_id}-v001",
            "wholePng": Path(whole["path"]),
            "expectedSha": whole["sha256"],
            "evidencePath": index_path,
        })
    return adopt_map_appearances(
        map_id="16001",
        project_root=project_root,
        qdao_specs=specs,
        default_appearance="spring",
        include_preserved_original=True,
    )


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Adopt Q-Dao static map(s) into PcOriginalPreview20261123.")
    parser.add_argument("--map-id", default="28002")
    parser.add_argument("--delivery-dir", type=Path, default=DEFAULT_28002_DELIVERY)
    parser.add_argument("--designs-root", type=Path, default=Path("D:/work/image/designs"))
    parser.add_argument("--delivery-16001", type=Path, default=DEFAULT_16001_BASELINE_DELIVERY)
    parser.add_argument("--project-root", type=Path, default=PROJECT)
    parser.add_argument("--spring-revision", default="qdao-spring-v002")
    parser.add_argument("--midautumn-revision", default="qdao-midautumn-v002")
    parser.add_argument("--default-appearance", default="spring")
    parser.add_argument("--adopt-completed-batch", action="store_true", help="Adopt 08001 4x HD candidate + all completed Q-Dao maps.")
    parser.add_argument("--adopt-16001-baseline", action="store_true", help="Adopt accepted 1920x1286 16001 Q-Dao dual-festival baseline.")
    parser.add_argument("--verify", action="store_true", help="Run full offline catalog + multi-appearance verification.")
    args = parser.parse_args(argv)

    project_root = args.project_root.resolve()
    if args.verify:
        pc_imp.verify_committed(project_root)
        return 0

    if args.adopt_16001_baseline:
        adopt_16001_baseline(args.delivery_16001.resolve(), project_root)
        pc_imp.verify_committed(project_root)
        return 0

    if args.adopt_completed_batch:
        adopt_completed_batch(args.designs_root.resolve(), project_root)
        pc_imp.verify_committed(project_root)
        return 0

    import_qdao_dual_festival_map(
        map_id=args.map_id,
        delivery_dir=args.delivery_dir.resolve(),
        project_root=project_root,
        appearance_revisions=[
            ("spring", args.spring_revision),
            ("midautumn", args.midautumn_revision),
        ],
        default_appearance=args.default_appearance,
    )
    pc_imp.verify_committed(project_root)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
