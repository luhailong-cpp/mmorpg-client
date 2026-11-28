"""Import and verify original map dual-festival static previews.

Copies verified lossless PNG tiles verbatim (preserving exact source SHA-256,
top-left half-open pixel coordinates, and edge-tile remainder heights such as
1024x683) into Assets/Resources/World/OriginalMapPreview20261124/<slug>/<revision>/,
derives resized overview fallback images for first-frame streaming display from
the full art PNGs, records full provenance (shared neutral base, art acceptance,
and per-image generation records), writes Unity .meta files, preserves legacy
revision directories intact, and supports offline `--verify` with zero external
path dependency.
"""

from __future__ import annotations

import argparse
import hashlib
import io
import json
from pathlib import Path
import re
import sys
import uuid

from PIL import Image

Image.MAX_IMAGE_PIXELS = None
sys.dont_write_bytecode = True

PROJECT = Path(__file__).resolve().parents[1]
DELIVERY_REL = Path("Assets/Resources/World/OriginalMapPreview20261124")
REVISION = "festivals-20261127-v001"
LEGACY_PRESERVED_REVISION = "delivery-candidate-v001"
EVIDENCE_REL = Path("Docs/VerificationEvidence/guideng-market-festivals-20261127-v001")
RESOURCE_ROOT = "World/OriginalMapPreview20261124/"
CATALOG_KIND = "CLIENT_ORIGINAL_MAP_PREVIEW_CATALOG"
CATALOG_REVISION = "original-preview-20261128"
ART_STAGE_FORMAL = "formal_art_delivery"
SOURCE_STATUS = "MAP_ART_AND_PIXEL_COORDINATES_ACCEPTED"
IMAGE_REPO = "https://luyuacpp.visualstudio.com/image/_git/image"
DEFAULT_IMAGE_COMMIT = "7a3c25f9898d1bc743af5964330d5eacd304a83f"
DEFAULT_ART_PACKAGE = "designs/guideng-market-festivals-20261127-v001"
TILE_SIZE = 1024
OVERVIEW_MAX_EDGE = 2048
SHA256_RE = re.compile(r"^[0-9a-f]{64}$")

DEFAULT_GUIDENG_SOURCE = Path("D:/work/image/designs/guideng-market-festivals-20261127-v001")

APPEARANCE_DISPLAY = {
    "spring": "春节",
    "midautumn": "中秋",
}


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
    return uuid.uuid5(uuid.NAMESPACE_URL, "mmorpg-client/original-map-preview/" + rel_posix).hex


def build_meta_bytes(path: Path, project_root: Path, has_alpha: bool = False, is_overview: bool = False) -> bytes:
    rel_posix = path.relative_to(project_root).as_posix()
    header = f"fileFormatVersion: 2\nguid: {deterministic_guid(rel_posix)}\n"
    if path.is_dir():
        return (header + "folderAsset: yes\nDefaultImporter:\n  externalObjects: {}\n").encode("utf-8")
    if path.suffix.lower() not in (".png", ".jpg", ".jpeg"):
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
    preserve_existing_meta: bool = False,
) -> None:
    ensure_asset_dir(path.parent, project_root, root_guard)
    path.write_bytes(data)
    meta_path = Path(str(path) + ".meta")
    if preserve_existing_meta and meta_path.is_file():
        return
    meta_path.write_bytes(build_meta_bytes(path, project_root, has_alpha=has_alpha, is_overview=is_overview))


def compute_overview_size(native_w: int, native_h: int, max_edge: int = OVERVIEW_MAX_EDGE) -> tuple[int, int]:
    longest = max(native_w, native_h)
    if longest <= max_edge:
        return native_w, native_h
    scale = max_edge / float(longest)
    return max(1, int(round(native_w * scale))), max(1, int(round(native_h * scale)))


def image_has_non_opaque_alpha(image: Image.Image) -> bool:
    if image.mode == "RGB":
        return False
    rgba = image if image.mode == "RGBA" else image.convert("RGBA")
    alpha_min, _ = rgba.getchannel("A").getextrema()
    return alpha_min < 255


def summarize_generation_record(source_dir: Path, rel_path: str) -> dict:
    path = source_dir / rel_path
    require(path.is_file(), f"Missing generation record: {path}")
    raw = path.read_bytes()
    data = json.loads(raw.decode("utf-8-sig"))
    cfg = data.get("configSnapshot") or {}
    submitted = data.get("submittedParameters") or {}
    return {
        "file": rel_path,
        "sha256": sha256_bytes(raw),
        "bytes": len(raw),
        "mapId": data.get("mapId"),
        "targetFile": data.get("file"),
        "targetSha256": data.get("sha256"),
        "width": data.get("width"),
        "height": data.get("height"),
        "kind": data.get("kind"),
        "route": data.get("route"),
        "tool": data.get("tool"),
        "toolCallId": data.get("toolCallId"),
        "physicalCalls": data.get("physicalCalls"),
        "configTargetModel": cfg.get("model"),
        "configTargetQuality": cfg.get("quality"),
        "submittedModel": submitted.get("model"),
        "submittedQuality": submitted.get("quality"),
        "submittedSize": submitted.get("size"),
        "actualModel": data.get("actualModel"),
        "actualQuality": data.get("actualQuality"),
        "unverifiedReason": data.get("unverifiedReason"),
        "generatedAt": data.get("generatedAt"),
    }


def verify_recomposition_against_whole(whole_image: Image.Image, tile_paths: list[tuple[dict, Path]]) -> dict:
    whole_rgb = whole_image if whole_image.mode == "RGB" else whole_image.convert("RGB")
    width, height = whole_rgb.size
    canvas = Image.new("RGB", (width, height), (0, 0, 0))
    covered = 0
    for meta, path in tile_paths:
        with Image.open(path) as part:
            part.load()
            require(part.format == "PNG", f"Tile must be PNG: {path}")
            require(part.size == (meta["width"], meta["height"]), f"Tile size mismatch: {path} {part.size}")
            require(not image_has_non_opaque_alpha(part), f"Tile has unexpected non-opaque alpha: {path}")
            part_rgb = part if part.mode == "RGB" else part.convert("RGB")
            canvas.paste(part_rgb, (meta["x"], meta["y"]))
            covered += meta["width"] * meta["height"]
    require(covered == width * height, f"Tile coverage {covered} != {width * height}")
    exact_equal = canvas.tobytes() == whole_rgb.tobytes()
    require(exact_equal, "Recomposed imported tiles differ from whole source PNG")
    return {
        "recomposedPixels": [width, height],
        "coveredPixels": covered,
        "tileCount": len(tile_paths),
        "rgbByteEqualToWholeSource": exact_equal,
        "totalPixelMismatch": 0 if exact_equal else -1,
    }


def verify_delivery_file(source_dir: Path, rel_path: str, expected_sha: str, expected_bytes: int | None = None) -> tuple[Path, str, int]:
    path = source_dir / rel_path
    require(path.is_file(), f"Missing delivery file: {path}")
    actual_bytes = path.stat().st_size
    if expected_bytes is not None:
        require(actual_bytes == expected_bytes, f"{rel_path}: bytes mismatch ({actual_bytes} != {expected_bytes})")
    actual_sha = sha256_file(path)
    require(actual_sha == expected_sha, f"{rel_path}: SHA-256 mismatch ({actual_sha} != {expected_sha})")
    return path, actual_sha, actual_bytes


def import_guideng(
    source_dir: Path,
    project_root: Path,
    *,
    revision: str = REVISION,
    image_commit: str = DEFAULT_IMAGE_COMMIT,
    art_package: str = DEFAULT_ART_PACKAGE,
) -> dict:
    require(source_dir.is_dir(), f"Missing source directory: {source_dir}")
    require(revision != LEGACY_PRESERVED_REVISION, f"Refusing to overwrite legacy revision '{LEGACY_PRESERVED_REVISION}'")

    map_id = "original:guideng-market"
    map_slug = "guideng-market"
    map_name = "桂灯集"

    manifest_path = source_dir / "manifest.json"
    readme_path = source_dir / "README.md"
    require(manifest_path.is_file(), f"Missing manifest.json: {manifest_path}")
    require(readme_path.is_file(), f"Missing README.md: {readme_path}")

    manifest_raw = manifest_path.read_bytes()
    manifest_sha = sha256_bytes(manifest_raw)
    manifest = json.loads(manifest_raw.decode("utf-8-sig"))
    readme_sha = sha256_file(readme_path)

    require(manifest.get("schemaVersion") == 1, f"Unexpected manifest schemaVersion: {manifest.get('schemaVersion')}")
    require(manifest.get("mapId") == map_id, f"Unexpected mapId: {manifest.get('mapId')}")
    require(manifest.get("numericMapId") is None, f"Unexpected numericMapId: {manifest.get('numericMapId')}")
    require(
        manifest.get("acceptanceScope") == "map_art_and_pixel_coordinates",
        f"Unexpected acceptanceScope: {manifest.get('acceptanceScope')}",
    )

    canvas = manifest["canvas"]
    native_w = int(canvas["width"])
    native_h = int(canvas["height"])
    require((native_w, native_h) == (4096, 2731), f"Unexpected canvas dimensions: {native_w}x{native_h}")

    tile_grid = manifest["tileGrid"]
    tile_size = int(tile_grid["maxTileWidth"])
    require(
        tile_size == TILE_SIZE
        and int(tile_grid["maxTileHeight"]) == TILE_SIZE
        and int(tile_grid["columns"]) == 4
        and int(tile_grid["rows"]) == 3
        and tile_grid["resampled"] is False
        and tile_grid["padded"] is False
        and tile_grid["mode"] == "RGB",
        f"Unexpected tileGrid: {tile_grid}",
    )

    # Verify shared neutral base + generation record.
    neutral_info = manifest["sharedNeutral"]
    neutral_path, neutral_sha, neutral_bytes = verify_delivery_file(
        source_dir, neutral_info["file"], neutral_info["sha256"], int(neutral_info["bytes"])
    )
    verify_delivery_file(source_dir, neutral_info["generationRecord"], neutral_info["generationRecordSha256"])
    with Image.open(neutral_path) as neutral_img:
        neutral_img.load()
        require(neutral_img.size == (native_w, native_h), f"Unexpected neutral size: {neutral_img.size}")
        neutral_has_alpha = image_has_non_opaque_alpha(neutral_img)
        require(not neutral_has_alpha, "Expected fully opaque shared neutral PNG")

    # Verify all additional accepted files (art-acceptance, native outputs, generation records, prompts).
    verified_additional: list[dict] = []
    art_acceptance_sha = None
    native_gen_record_paths: list[str] = []
    for item in manifest.get("additionalAcceptedFiles") or []:
        rel_file = item["file"]
        _, f_sha, f_bytes = verify_delivery_file(source_dir, rel_file, item["sha256"])
        verified_additional.append({"file": rel_file, "sha256": f_sha, "bytes": f_bytes})
        if rel_file == "records/art-acceptance.json":
            art_acceptance_sha = f_sha
        if rel_file.endswith(".generation.json"):
            native_gen_record_paths.append(rel_file)

    gen_record_paths = [
        neutral_info["generationRecord"],
        manifest["appearances"]["spring"]["source"]["generationRecord"],
        manifest["appearances"]["midautumn"]["source"]["generationRecord"],
        *sorted(native_gen_record_paths),
    ]
    gen_records = [summarize_generation_record(source_dir, rel) for rel in gen_record_paths]

    delivery_root = project_root / DELIVERY_REL
    evidence_root = project_root / EVIDENCE_REL
    evidence_root.mkdir(parents=True, exist_ok=True)

    appearances_catalog: list[dict] = []
    appearances_receipt: list[dict] = []

    for app_id in ("spring", "midautumn"):
        app_spec = manifest["appearances"][app_id]
        src_info = app_spec["source"]
        require(
            int(src_info["width"]) == native_w
            and int(src_info["height"]) == native_h
            and src_info.get("opaque") is True,
            f"{app_id}: unexpected source info {src_info}",
        )
        whole_path, whole_sha, whole_bytes = verify_delivery_file(
            source_dir, src_info["file"], src_info["sha256"], int(src_info["bytes"])
        )
        verify_delivery_file(source_dir, src_info["generationRecord"], src_info["generationRecordSha256"])

        app_rev = f"{revision}-{app_id}"
        app_res_root = f"{RESOURCE_ROOT}{map_slug}/{revision}/{app_id}/"
        app_dir = delivery_root / map_slug / revision / app_id
        tiles_dir = app_dir / "tiles"

        with Image.open(whole_path) as whole_img:
            whole_img.load()
            require(whole_img.size == (native_w, native_h), f"{app_id}: whole PNG size mismatch")
            require(not image_has_non_opaque_alpha(whole_img), f"{app_id}: expected fully opaque RGB canvas")

            tile_entries: list[dict] = []
            written_tile_pairs: list[tuple[dict, Path]] = []
            total_tile_bytes = 0

            require(len(app_spec["tiles"]) == 12 and int(app_spec["tileCount"]) == 12, f"{app_id}: expected 12 tiles")
            for src_tile in app_spec["tiles"]:
                row = int(src_tile["row"])
                col = int(src_tile["column"])
                x = int(src_tile["x"])
                y = int(src_tile["y"])
                tw = int(src_tile["width"])
                th = int(src_tile["height"])
                require(src_tile["sourceSha256"] == whole_sha, f"{app_id} ({row},{col}): tile sourceSha256 mismatch")

                src_tile_file, actual_tile_sha, actual_tile_bytes = verify_delivery_file(
                    source_dir, src_tile["file"], src_tile["sha256"], int(src_tile["bytes"])
                )
                raw_tile = src_tile_file.read_bytes()
                with Image.open(io.BytesIO(raw_tile)) as tile_im:
                    tile_im.load()
                    require(tile_im.format == "PNG", f"{app_id}/{src_tile['file']}: must be PNG")
                    require(tile_im.size == (tw, th), f"{app_id}/{src_tile['file']}: size {tile_im.size} != {(tw, th)}")
                    require(not image_has_non_opaque_alpha(tile_im), f"{app_id}/{src_tile['file']}: unexpected alpha")

                stem = f"r{row:02d}_c{col:02d}"
                dst_tile_file = tiles_dir / f"{stem}.png"
                write_asset_file(dst_tile_file, raw_tile, project_root, delivery_root, has_alpha=False, is_overview=False)
                total_tile_bytes += actual_tile_bytes
                entry = {
                    "row": row,
                    "column": col,
                    "x": x,
                    "y": y,
                    "width": tw,
                    "height": th,
                    "resourcePath": f"{app_res_root}tiles/{stem}",
                    "fileExtension": ".png",
                    "bytes": actual_tile_bytes,
                    "sha256": actual_tile_sha,
                }
                tile_entries.append(entry)
                written_tile_pairs.append((entry, dst_tile_file))

            recomp = verify_recomposition_against_whole(whole_img, written_tile_pairs)

            ov_w, ov_h = compute_overview_size(native_w, native_h, OVERVIEW_MAX_EDGE)
            ov_img = whole_img.convert("RGB").resize((ov_w, ov_h), Image.Resampling.LANCZOS)
            ov_buf = io.BytesIO()
            ov_img.save(ov_buf, format="PNG", optimize=True)
            ov_raw = ov_buf.getvalue()
            ov_sha = sha256_bytes(ov_raw)
            ov_bytes = len(ov_raw)
            write_asset_file(app_dir / "overview.png", ov_raw, project_root, delivery_root, has_alpha=False, is_overview=True)

        tile_format = "png_rgb_lossless_native_1024_grid"
        tiles_manifest = {
            "schemaVersion": 1,
            "mapId": map_id,
            "numericMapId": 0,
            "revision": app_rev,
            "artStage": ART_STAGE_FORMAL,
            "appearance": app_id,
            "nativeWidth": native_w,
            "nativeHeight": native_h,
            "tileSize": tile_size,
            "columns": 4,
            "rows": 3,
            "hasAlpha": False,
            "tileFormat": tile_format,
            "resampled": False,
            "resourceRoot": app_res_root,
            "sourceNativeSha256": whole_sha,
            "tiles": tile_entries,
        }
        tiles_manifest_raw = json_bytes(tiles_manifest)
        tiles_manifest_sha = sha256_bytes(tiles_manifest_raw)
        write_asset_file(app_dir / "tiles.json", tiles_manifest_raw, project_root, delivery_root)

        source_doc = {
            "schemaVersion": 1,
            "kind": "CLIENT_ORIGINAL_MAP_FORMAL_ART_PREVIEW_SOURCE",
            "status": SOURCE_STATUS,
            "acceptanceScope": manifest["acceptanceScope"],
            "mapId": map_id,
            "mapKey": map_slug,
            "numericMapId": 0,
            "numericMapIdStatus": manifest.get("numericMapIdStatus", "not_assigned"),
            "name": map_name,
            "appearance": app_id,
            "appearanceDisplayName": APPEARANCE_DISPLAY[app_id],
            "revision": app_rev,
            "artStage": ART_STAGE_FORMAL,
            "isTemporaryOriginal": False,
            "staticBackOnly": True,
            "completeGameplaySceneClaimed": False,
            "productionTravelEnabled": False,
            "worldCoordinatesAndNavigationConfirmed": False,
            "serverRegistrationStatus": "unregistered_local_preview_only",
            "imageRepo": IMAGE_REPO,
            "imageCommit": image_commit,
            "artPackage": art_package,
            "sourceOriginLabel": f"guideng-market/{revision}/{app_id}",
            "sourceWholeFile": src_info["file"],
            "sourceNativeSha256": whole_sha,
            "sourceNativeBytes": whole_bytes,
            "sourceGenerationRecord": src_info["generationRecord"],
            "sourceGenerationRecordSha256": src_info["generationRecordSha256"],
            "sourceManifestFile": "manifest.json",
            "sourceManifestSha256": manifest_sha,
            "sourceReadmeSha256": readme_sha,
            "artAcceptanceSha256": art_acceptance_sha,
            "sharedNeutralBase": {
                "file": neutral_info["file"],
                "width": native_w,
                "height": native_h,
                "bytes": neutral_bytes,
                "sha256": neutral_sha,
                "generationRecord": neutral_info["generationRecord"],
                "generationRecordSha256": neutral_info["generationRecordSha256"],
                "hasNonOpaqueAlpha": neutral_has_alpha,
            },
            "coordinateConvention": canvas["coordinateConvention"],
            "nativeWidth": native_w,
            "nativeHeight": native_h,
            "tileSize": tile_size,
            "columns": 4,
            "rows": 3,
            "lastRowHeight": 683,
            "tileCount": len(tile_entries),
            "hasAlpha": False,
            "tileFormat": tile_format,
            "resampled": False,
            "padded": False,
            "tilesCopiedVerbatim": True,
            "overviewDerived": True,
            "overviewIsResizedPreviewOnly": True,
            "overviewWidth": ov_w,
            "overviewHeight": ov_h,
            "overviewExtension": ".png",
            "overviewBytes": ov_bytes,
            "overviewSha256": ov_sha,
            "tilesManifestSha256": tiles_manifest_sha,
            "editedRectsHalfOpenPixels": (app_spec.get("verification") or {}).get("editedRects"),
            "recompositionVerification": recomp,
            "generationRecords": gen_records,
            "additionalAcceptedFiles": verified_additional,
        }
        write_asset_file(app_dir / "source.json", json_bytes(source_doc), project_root, delivery_root)

        appearances_catalog.append({
            "id": app_id,
            "displayName": APPEARANCE_DISPLAY[app_id],
            "artStage": ART_STAGE_FORMAL,
            "isTemporaryOriginal": False,
            "revision": app_rev,
            "hasAlpha": False,
            "tileFormat": tile_format,
            "resampled": False,
            "nativeWidth": native_w,
            "nativeHeight": native_h,
            "tileSize": tile_size,
            "columns": 4,
            "rows": 3,
            "tileCount": len(tile_entries),
            "resourceRoot": app_res_root,
            "overviewResourcePath": f"{app_res_root}overview",
            "overviewExtension": ".png",
            "overviewWidth": ov_w,
            "overviewHeight": ov_h,
            "overviewSha256": ov_sha,
            "overviewBytes": ov_bytes,
            "tilesManifestResourcePath": f"{app_res_root}tiles",
            "tilesManifestSha256": tiles_manifest_sha,
            "sourceManifestResourcePath": f"{app_res_root}source",
            "sourceNativeSha256": whole_sha,
            "sourceNativeBytes": whole_bytes,
            "sourceEvidenceSha256": manifest_sha,
        })

        appearances_receipt.append({
            "id": app_id,
            "displayName": APPEARANCE_DISPLAY[app_id],
            "revision": app_rev,
            "resourceRoot": app_res_root,
            "assetDirectory": (DELIVERY_REL / map_slug / revision / app_id).as_posix(),
            "sourceWholeFile": src_info["file"],
            "sourceNativeSha256": whole_sha,
            "sourceNativeBytes": whole_bytes,
            "sourceGenerationRecordSha256": src_info["generationRecordSha256"],
            "sourceManifestSha256": manifest_sha,
            "nativePixels": [native_w, native_h],
            "columns": 4,
            "rows": 3,
            "lastRowHeight": 683,
            "tileCount": len(tile_entries),
            "totalTileBytes": total_tile_bytes,
            "tilesManifestSha256": tiles_manifest_sha,
            "overviewPixels": [ov_w, ov_h],
            "overviewBytes": ov_bytes,
            "overviewSha256": ov_sha,
            "recompositionVerification": recomp,
            "tiles": tile_entries,
        })
        print(
            f"[GUIDENG/{revision}/{app_id}] {native_w}x{native_h} 4x3={len(tile_entries)} lossless PNG tiles "
            f"(lastRow=683, {total_tile_bytes / 1048576:.2f} MB) ov={ov_w}x{ov_h} ({ov_bytes / 1048576:.2f} MB) "
            f"recomposedEqual={recomp['rgbByteEqualToWholeSource']}"
        )

    default_app = appearances_catalog[0]
    catalog = {
        "schemaVersion": 1,
        "kind": CATALOG_KIND,
        "catalogRevision": CATALOG_REVISION,
        "artStageDefault": ART_STAGE_FORMAL,
        "staticBackOnly": True,
        "completeGameplaySceneClaimed": False,
        "productionTravelEnabled": False,
        "requestedMapIds": [map_id],
        "readyCount": 1,
        "unavailableCount": 0,
        "specialHoldCount": 0,
        "maps": [
            {
                "mapId": map_id,
                "numericMapId": 0,
                "name": map_name,
                "artStage": default_app["artStage"],
                "isTemporaryOriginal": False,
                "replaceableByQdaoArt": False,
                "revision": default_app["revision"],
                "sourceBranch": "original_continuous_edit",
                "sourceStatus": SOURCE_STATUS,
                "nativeWidth": native_w,
                "nativeHeight": native_h,
                "aspectRatio": native_w / float(native_h),
                "tileSize": tile_size,
                "columns": 4,
                "rows": 3,
                "tileCount": 12,
                "hasAlpha": False,
                "tileFormat": default_app["tileFormat"],
                "resampled": False,
                "resourceRoot": default_app["resourceRoot"],
                "overviewResourcePath": default_app["overviewResourcePath"],
                "overviewExtension": default_app["overviewExtension"],
                "overviewWidth": default_app["overviewWidth"],
                "overviewHeight": default_app["overviewHeight"],
                "overviewSha256": default_app["overviewSha256"],
                "overviewBytes": default_app["overviewBytes"],
                "tilesManifestResourcePath": default_app["tilesManifestResourcePath"],
                "tilesManifestSha256": default_app["tilesManifestSha256"],
                "sourceManifestResourcePath": default_app["sourceManifestResourcePath"],
                "sourceNativeSha256": default_app["sourceNativeSha256"],
                "sourceNativeBytes": default_app["sourceNativeBytes"],
                "sourceEvidenceSha256": default_app["sourceEvidenceSha256"],
                "defaultAppearance": "spring",
                "appearances": appearances_catalog,
                "serverRegistrationStatus": "unregistered_local_preview_only",
            }
        ],
        "unavailableMaps": [],
        "specialHoldCandidates": [],
    }
    catalog_raw = json_bytes(catalog)
    catalog_sha = sha256_bytes(catalog_raw)
    write_asset_file(
        delivery_root / "catalog.json",
        catalog_raw,
        project_root,
        delivery_root,
        preserve_existing_meta=True,
    )

    receipt = {
        "schemaVersion": 1,
        "kind": "CLIENT_ORIGINAL_MAP_PREVIEW_IMPORT_RECEIPT",
        "status": SOURCE_STATUS,
        "acceptanceScope": manifest["acceptanceScope"],
        "artStage": ART_STAGE_FORMAL,
        "mapId": map_id,
        "mapKey": map_slug,
        "name": map_name,
        "revision": revision,
        "preservedLegacyRevision": LEGACY_PRESERVED_REVISION,
        "imageRepo": IMAGE_REPO,
        "imageCommit": image_commit,
        "artPackage": art_package,
        "staticBackOnly": True,
        "completeGameplaySceneClaimed": False,
        "productionTravelEnabled": False,
        "worldCoordinatesAndNavigationConfirmed": False,
        "sourceDirectoryNote": (
            f"Imported from {art_package} (commit {image_commit}); "
            "runtime has zero external path dependency; legacy delivery-candidate-v001 preserved in place."
        ),
        "sourceManifestSha256": manifest_sha,
        "sourceReadmeSha256": readme_sha,
        "artAcceptanceSha256": art_acceptance_sha,
        "sharedNeutralBase": {
            "file": neutral_info["file"],
            "width": native_w,
            "height": native_h,
            "bytes": neutral_bytes,
            "sha256": neutral_sha,
            "generationRecord": neutral_info["generationRecord"],
            "generationRecordSha256": neutral_info["generationRecordSha256"],
        },
        "catalogPath": (DELIVERY_REL / "catalog.json").as_posix(),
        "catalogSha256": catalog_sha,
        "generationRecords": gen_records,
        "additionalAcceptedFiles": verified_additional,
        "appearances": appearances_receipt,
    }
    receipt_path = evidence_root / "import-receipt.json"
    receipt_path.write_bytes(json_bytes(receipt))
    print(f"WROTE {receipt_path} (catalogSha256={catalog_sha})")
    return receipt


def verify_preserved_legacy_candidate(project_root: Path) -> None:
    legacy_root = project_root / DELIVERY_REL / "guideng-market" / LEGACY_PRESERVED_REVISION
    require(legacy_root.is_dir(), f"Preserved legacy candidate directory missing: {legacy_root}")
    for app_id in ("spring", "midautumn"):
        app_dir = legacy_root / app_id
        for rel in ("tiles.json", "source.json", "overview.png"):
            require((app_dir / rel).is_file(), f"Preserved legacy file missing: {app_dir / rel}")
        tm = read_json(app_dir / "tiles.json")
        require(len(tm.get("tiles") or []) == 12, f"Preserved legacy {app_id} must still contain 12 tiles")
        for tile in tm["tiles"]:
            tile_file = project_root / "Assets/Resources" / (tile["resourcePath"] + tile["fileExtension"])
            require(tile_file.is_file(), f"Preserved legacy tile missing: {tile_file}")


def verify_offline(project_root: Path, *, require_preserved_legacy: bool = True) -> None:
    delivery_root = project_root / DELIVERY_REL
    evidence_root = project_root / EVIDENCE_REL
    catalog_path = delivery_root / "catalog.json"
    receipt_path = evidence_root / "import-receipt.json"
    require(catalog_path.is_file(), f"Missing catalog.json: {catalog_path}")
    require(receipt_path.is_file(), f"Missing import-receipt.json: {receipt_path}")

    catalog_raw = catalog_path.read_bytes()
    catalog_sha = sha256_bytes(catalog_raw)
    catalog = json.loads(catalog_raw.decode("utf-8-sig"))
    receipt = read_json(receipt_path)

    require(catalog.get("kind") == CATALOG_KIND, f"Unexpected catalog kind: {catalog.get('kind')}")
    require(catalog.get("staticBackOnly") is True, "staticBackOnly must be true")
    require(catalog.get("completeGameplaySceneClaimed") is False, "completeGameplaySceneClaimed must be false")
    require(catalog.get("productionTravelEnabled") is False, "productionTravelEnabled must be false")
    require(receipt.get("catalogSha256") == catalog_sha, "import-receipt catalogSha256 mismatch")

    seen_guids: set[str] = set()
    guideng = next((m for m in catalog["maps"] if m["mapId"] == "original:guideng-market"), None)
    require(guideng is not None, "Missing original:guideng-market in catalog.json")
    require(guideng["artStage"] == ART_STAGE_FORMAL, f"Expected artStage={ART_STAGE_FORMAL}, got {guideng['artStage']}")
    require(guideng["nativeWidth"] == 4096 and guideng["nativeHeight"] == 2731, "Guideng dimensions mismatch")
    require(guideng["defaultAppearance"] == "spring", "Guideng defaultAppearance must be spring")
    require(len(guideng["appearances"]) == 2, "Guideng must have 2 appearances (spring, midautumn)")

    tile_hashes_by_app: dict[str, dict[tuple[int, int], str]] = {}

    for app in guideng["appearances"]:
        app_id = app["id"]
        res_root = app["resourceRoot"]
        require(res_root.startswith(RESOURCE_ROOT), f"{app_id}: unexpected resourceRoot {res_root}")
        require(f"/{REVISION}/{app_id}/" in res_root, f"{app_id}: expected revision {REVISION} in {res_root}")
        require(app["artStage"] == ART_STAGE_FORMAL, f"{app_id}: unexpected artStage {app['artStage']}")
        app_dir = project_root / "Assets/Resources" / res_root.rstrip("/")
        tiles_manifest_path = app_dir / "tiles.json"
        source_path = app_dir / "source.json"
        overview_path = app_dir / ("overview" + app["overviewExtension"])

        for p in (tiles_manifest_path, source_path, overview_path):
            require(p.is_file(), f"Missing file: {p}")
            meta = Path(str(p) + ".meta")
            require(meta.is_file(), f"Missing meta: {meta}")
            m_text = meta.read_text(encoding="utf-8")
            m_guid = re.search(r"^guid:\s*([0-9a-f]{32})$", m_text, re.M)
            require(m_guid is not None, f"Invalid guid in {meta}")
            require(m_guid.group(1) not in seen_guids, f"Duplicate guid in {meta}")
            seen_guids.add(m_guid.group(1))

        tm_raw = tiles_manifest_path.read_bytes()
        require(sha256_bytes(tm_raw) == app["tilesManifestSha256"], f"{app_id}: tiles.json SHA mismatch")
        tm = json.loads(tm_raw.decode("utf-8-sig"))
        require(tm["nativeWidth"] == 4096 and tm["nativeHeight"] == 2731, f"{app_id}: manifest size mismatch")
        require(tm["columns"] == 4 and tm["rows"] == 3 and len(tm["tiles"]) == 12, f"{app_id}: grid mismatch")
        require(tm["hasAlpha"] is False and tm["resampled"] is False, f"{app_id}: alpha/resampled mismatch")

        ov_raw = overview_path.read_bytes()
        require(sha256_bytes(ov_raw) == app["overviewSha256"], f"{app_id}: overview SHA mismatch")
        with Image.open(io.BytesIO(ov_raw)) as ov_im:
            ov_im.load()
            require(ov_im.size == (app["overviewWidth"], app["overviewHeight"]), f"{app_id}: overview size mismatch")

        covered_pixels = 0
        app_hashes: dict[tuple[int, int], str] = {}
        for tile in tm["tiles"]:
            row, col = int(tile["row"]), int(tile["column"])
            exp_x = (col - 1) * TILE_SIZE
            exp_y = (row - 1) * TILE_SIZE
            exp_w = min(TILE_SIZE, 4096 - exp_x)
            exp_h = min(TILE_SIZE, 2731 - exp_y)
            require(
                (tile["x"], tile["y"], tile["width"], tile["height"]) == (exp_x, exp_y, exp_w, exp_h),
                f"{app_id} ({row},{col}): coordinate rect mismatch",
            )
            if row == 3:
                require(tile["height"] == 683, f"{app_id} row 3 height must be 683")
            tile_file = project_root / "Assets/Resources" / (tile["resourcePath"] + tile["fileExtension"])
            require(tile_file.is_file(), f"Missing tile: {tile_file}")
            raw = tile_file.read_bytes()
            actual_sha = sha256_bytes(raw)
            require(actual_sha == tile["sha256"] and len(raw) == tile["bytes"], f"{app_id} ({row},{col}): SHA/bytes mismatch")
            with Image.open(io.BytesIO(raw)) as im:
                im.load()
                require(im.format == "PNG" and im.size == (exp_w, exp_h), f"{app_id} ({row},{col}): PNG size mismatch")
            meta_file = Path(str(tile_file) + ".meta")
            meta_text = meta_file.read_text(encoding="utf-8")
            m_guid = re.search(r"^guid:\s*([0-9a-f]{32})$", meta_text, re.M)
            require(m_guid is not None and m_guid.group(1) not in seen_guids, f"Invalid/duplicate guid in {meta_file}")
            seen_guids.add(m_guid.group(1))
            require("maxTextureSize: 1024" in meta_text and "nPOTScale: 0" in meta_text, f"Bad meta: {meta_file}")
            require("enableMipMap: 0" in meta_text and "textureCompression: 0" in meta_text, f"Bad meta: {meta_file}")
            require("textureFormat: 3" in meta_text, f"Expected RGB24 (3) in {meta_file}")
            covered_pixels += exp_w * exp_h
            app_hashes[(row, col)] = actual_sha

        require(covered_pixels == 4096 * 2731, f"{app_id}: coverage mismatch {covered_pixels}")
        tile_hashes_by_app[app_id] = app_hashes

    # Confirm festival edits stay strictly on the eastern storefront tiles (cols 3-4) while all 6 west tiles match.
    spring_h = tile_hashes_by_app["spring"]
    mid_h = tile_hashes_by_app["midautumn"]
    diff_coords = sorted(k for k in spring_h if spring_h[k] != mid_h[k])
    same_coords = sorted(k for k in spring_h if spring_h[k] == mid_h[k])
    expected_west = {(r, c) for r in (1, 2, 3) for c in (1, 2)}
    expected_east_diff = {(1, 3), (1, 4), (2, 3), (2, 4), (3, 4)}
    require(expected_west.issubset(set(same_coords)), f"West tiles must stay shared across festivals; diff={diff_coords}")
    require(all(c >= 3 for _, c in diff_coords), f"Festival edits escaped east columns (3-4): {diff_coords}")
    require(
        expected_east_diff.issubset(set(diff_coords)),
        f"Expected eastern storefront tiles {sorted(expected_east_diff)} to differ, got {diff_coords}",
    )

    if require_preserved_legacy:
        verify_preserved_legacy_candidate(project_root)

    print(
        f"OFFLINE VERIFY PASS: original:guideng-market/{REVISION} (4096x2731, 4x3, lastRow=683) "
        f"2 appearances, 24 lossless PNG tiles, {len(diff_coords)} festival-differing east tiles, "
        f"legacyPreserved={require_preserved_legacy}, catalogSha256={catalog_sha}"
    )


def main() -> int:
    parser = argparse.ArgumentParser(description="Import/verify original map dual-festival preview assets.")
    parser.add_argument("--source", type=Path, default=DEFAULT_GUIDENG_SOURCE, help="Source art delivery directory")
    parser.add_argument("--project-root", type=Path, default=PROJECT, help="Unity client project root")
    parser.add_argument("--revision", default=REVISION, help="Target revision directory name")
    parser.add_argument("--image-commit", default=DEFAULT_IMAGE_COMMIT, help="Source image repository commit SHA")
    parser.add_argument("--verify", action="store_true", help="Run offline verification only (no external source needed)")
    args = parser.parse_args()

    if args.verify:
        verify_offline(args.project_root)
        return 0

    import_guideng(args.source, args.project_root, revision=args.revision, image_commit=args.image_commit)
    verify_offline(args.project_root)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
