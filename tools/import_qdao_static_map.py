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


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Adopt a Q-Dao dual-festival static map into PcOriginalPreview20261123.")
    parser.add_argument("--map-id", default="28002")
    parser.add_argument("--delivery-dir", type=Path, default=DEFAULT_28002_DELIVERY)
    parser.add_argument("--project-root", type=Path, default=PROJECT)
    parser.add_argument("--spring-revision", default="qdao-spring-v002")
    parser.add_argument("--midautumn-revision", default="qdao-midautumn-v002")
    parser.add_argument("--default-appearance", default="spring")
    parser.add_argument("--verify", action="store_true", help="Run full offline catalog + multi-appearance verification.")
    args = parser.parse_args(argv)

    project_root = args.project_root.resolve()
    if args.verify:
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
