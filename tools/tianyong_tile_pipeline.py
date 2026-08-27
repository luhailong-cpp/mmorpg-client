#!/usr/bin/env python3
"""Prepare, finalize, and verify a seamless 6x6 Tianyong scene tile set."""

from __future__ import annotations

import argparse
import json
import re
import uuid
from pathlib import Path

import numpy as np
from PIL import Image, ImageChops, ImageDraw, ImageFilter, ImageFont, ImageStat


GRID_SIZE = 6
TILE_SIZE = 1024
MASTER_SIZE = GRID_SIZE * TILE_SIZE
DEFAULT_OVERLAP = 256
QUICK_PREVIEW_SIZE = 2048
OVERLAP_METADATA_NAME = "overlap_metadata.json"
TILE_ASSET_ROOT = "Assets/Resources/World/Tianyong/SceneTiles6x6"
TILE_RESOURCES_ROOT = "World/Tianyong/SceneTiles6x6/Tiles"


def _open_rgb(path: Path) -> Image.Image:
    with Image.open(path) as image:
        return image.convert("RGB")


def _cover_square(image: Image.Image, size: int) -> Image.Image:
    width, height = image.size
    side = min(width, height)
    left = (width - side) // 2
    top = (height - side) // 2
    square = image.crop((left, top, left + side, top + side))
    return square.resize((size, size), Image.Resampling.LANCZOS)


def _tile_name(row: int, column: int) -> str:
    return f"tianyong_r{row:02d}_c{column:02d}.png"


def _expected_tile_names() -> list[str]:
    return [
        _tile_name(row, column)
        for row in range(1, GRID_SIZE + 1)
        for column in range(1, GRID_SIZE + 1)
    ]


def _validate_overlap(overlap: int) -> None:
    if overlap <= 0:
        raise ValueError(f"Overlap must be greater than zero, got {overlap}.")


def _overlap_metadata(overlap: int) -> dict[str, object]:
    patch_size = TILE_SIZE + overlap * 2
    return {
        "schemaVersion": 1,
        "grid": {"columns": GRID_SIZE, "rows": GRID_SIZE},
        "tileSize": {"width": TILE_SIZE, "height": TILE_SIZE},
        "masterSize": {"width": MASTER_SIZE, "height": MASTER_SIZE},
        "contextPerSidePixels": overlap,
        "adjacentPatchOverlapPixels": overlap * 2,
        "patchSize": {"width": patch_size, "height": patch_size},
        "guideDirectory": "overlap_guides",
        "rawDirectory": "refined_overlap_raw",
        "tileNames": _expected_tile_names(),
    }


def _write_overlap_metadata(work_dir: Path, overlap: int) -> Path:
    metadata_path = work_dir / OVERLAP_METADATA_NAME
    metadata_path.write_text(
        json.dumps(_overlap_metadata(overlap), ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
    )
    return metadata_path


def _read_overlap_metadata(work_dir: Path, overlap: int) -> dict[str, object]:
    metadata_path = work_dir / OVERLAP_METADATA_NAME
    if not metadata_path.exists():
        raise FileNotFoundError(
            f"Missing overlap metadata: {metadata_path}. Run prepare with this work directory first."
        )
    try:
        metadata = json.loads(metadata_path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as error:
        raise RuntimeError(f"Invalid overlap metadata: {metadata_path}") from error
    expected = _overlap_metadata(overlap)
    mismatches = [
        key for key, expected_value in expected.items() if metadata.get(key) != expected_value
    ]
    if mismatches:
        raise RuntimeError(
            f"Overlap metadata does not match --overlap={overlap} or the current grid constants "
            f"for fields: {', '.join(mismatches)}"
        )
    return metadata


def _png_names(folder: Path) -> set[str]:
    if not folder.is_dir():
        raise FileNotFoundError(f"Missing PNG directory: {folder}")
    return {
        path.name
        for path in folder.iterdir()
        if path.is_file() and path.suffix.lower() == ".png"
    }


def _validate_overlap_inputs(work_dir: Path, overlap: int) -> tuple[int, int]:
    _validate_overlap(overlap)
    _read_overlap_metadata(work_dir, overlap)
    guides_dir = work_dir / "overlap_guides"
    raw_dir = work_dir / "refined_overlap_raw"
    expected_names = set(_expected_tile_names())
    for label, folder in (("guide", guides_dir), ("raw", raw_dir)):
        actual_names = _png_names(folder)
        if actual_names != expected_names:
            missing = sorted(expected_names - actual_names)
            extra = sorted(actual_names - expected_names)
            raise RuntimeError(
                f"Unexpected {label} PNG set in {folder}; missing={missing}, extra={extra}"
            )

    patch_size = TILE_SIZE + overlap * 2
    raw_size: tuple[int, int] | None = None
    for name in _expected_tile_names():
        guide_path = guides_dir / name
        raw_path = raw_dir / name
        with Image.open(guide_path) as guide:
            guide.load()
            if guide.format != "PNG" or guide.mode != "RGB" or guide.size != (patch_size, patch_size):
                raise RuntimeError(
                    f"Overlap guide must be an RGB PNG of {patch_size}x{patch_size}: "
                    f"{guide_path} is format={guide.format}, mode={guide.mode}, size={guide.size}"
                )
        with Image.open(raw_path) as raw:
            raw.load()
            if raw.format != "PNG" or raw.mode != "RGB":
                raise RuntimeError(
                    f"Overlap refinement must be an RGB PNG: {raw_path} is "
                    f"format={raw.format}, mode={raw.mode}"
                )
            if raw.width != raw.height or raw.width < TILE_SIZE:
                raise RuntimeError(
                    f"Overlap refinement must be square and at least {TILE_SIZE}px: "
                    f"{raw_path} is {raw.size}"
                )
            if raw_size is None:
                raw_size = raw.size
            elif raw.size != raw_size:
                raise RuntimeError(
                    f"All overlap refinements must share one native size; "
                    f"expected {raw_size}, got {raw.size} for {raw_path}"
                )
    if raw_size is None:
        raise RuntimeError("No overlap refinement PNGs were found.")
    return raw_size


def _write_texture_meta(image_path: Path, *, sprite: bool, max_size: int) -> None:
    meta_path = image_path.with_suffix(image_path.suffix + ".meta")
    if meta_path.exists():
        return
    guid = uuid.uuid4().hex
    sprite_id = uuid.uuid4().hex if sprite else ""
    texture_type = 8 if sprite else 0
    sprite_mode = 1 if sprite else 0
    meta = f"""fileFormatVersion: 2
guid: {guid}
TextureImporter:
  internalIDToNameTable: []
  externalObjects: {{}}
  serializedVersion: 13
  mipmaps:
    mipMapMode: 0
    enableMipMap: 0
    sRGBTexture: 1
    linearTexture: 0
    fadeOut: 0
    borderMipMap: 0
    mipMapsPreserveCoverage: 0
    alphaTestReferenceValue: 0.5
    mipMapFadeDistanceStart: 1
    mipMapFadeDistanceEnd: 3
  bumpmap:
    convertToNormalMap: 0
    externalNormalMap: 0
    heightScale: 0.25
    normalMapFilter: 0
    flipGreenChannel: 0
  isReadable: 0
  streamingMipmaps: 0
  streamingMipmapsPriority: 0
  vTOnly: 0
  ignoreMipmapLimit: 0
  grayScaleToAlpha: 0
  generateCubemap: 6
  cubemapConvolution: 0
  seamlessCubemap: 0
  textureFormat: 1
  maxTextureSize: {max_size}
  textureSettings:
    serializedVersion: 2
    filterMode: 1
    aniso: 1
    mipBias: 0
    wrapU: 1
    wrapV: 1
    wrapW: 1
  nPOTScale: 0
  lightmap: 0
  compressionQuality: 100
  spriteMode: {sprite_mode}
  spriteExtrude: 1
  spriteMeshType: 0
  alignment: 0
  spritePivot: {{x: 0.5, y: 0.5}}
  spritePixelsToUnits: 100
  spriteBorder: {{x: 0, y: 0, z: 0, w: 0}}
  spriteGenerateFallbackPhysicsShape: 0
  alphaUsage: 0
  alphaIsTransparency: 0
  spriteTessellationDetail: -1
  textureType: {texture_type}
  textureShape: 1
  singleChannelComponent: 0
  flipbookRows: 1
  flipbookColumns: 1
  maxTextureSizeSet: 0
  compressionQualitySet: 0
  textureFormatSet: 0
  ignorePngGamma: 0
  applyGammaDecoding: 0
  swizzle: 50462976
  cookieLightType: 0
  platformSettings:
  - serializedVersion: 4
    buildTarget: DefaultTexturePlatform
    maxTextureSize: {max_size}
    resizeAlgorithm: 0
    textureFormat: -1
    textureCompression: 0
    compressionQuality: 100
    crunchedCompression: 0
    allowsAlphaSplitting: 0
    overridden: 0
    ignorePlatformSupport: 0
    androidETC2FallbackOverride: 0
    forceMaximumCompressionQuality_BC6H_BC7: 0
  - serializedVersion: 4
    buildTarget: Standalone
    maxTextureSize: {max_size}
    resizeAlgorithm: 0
    textureFormat: -1
    textureCompression: 0
    compressionQuality: 100
    crunchedCompression: 0
    allowsAlphaSplitting: 0
    overridden: 0
    ignorePlatformSupport: 0
    androidETC2FallbackOverride: 0
    forceMaximumCompressionQuality_BC6H_BC7: 0
  spriteSheet:
    serializedVersion: 2
    sprites: []
    outline: []
    customData:
    physicsShape: []
    bones: []
    spriteID: {sprite_id}
    internalID: 0
    vertices: []
    indices:
    edges: []
    weights: []
    secondaryTextures: []
    spriteCustomMetadata:
      entries: []
    nameFileIdTable: {{}}
  mipmapLimitGroupName:
  pSDRemoveMatte: 0
  userData:
  assetBundleName:
  assetBundleVariant:
"""
    meta_path.write_text(meta, encoding="utf-8")


def _write_folder_meta(folder_path: Path) -> None:
    meta_path = Path(str(folder_path) + ".meta")
    if meta_path.exists():
        return
    meta_path.write_text(
        f"""fileFormatVersion: 2
guid: {uuid.uuid4().hex}
folderAsset: yes
DefaultImporter:
  externalObjects: {{}}
  userData:
  assetBundleName:
  assetBundleVariant:
""",
        encoding="utf-8",
    )


def _write_text_meta(text_path: Path) -> None:
    meta_path = text_path.with_suffix(text_path.suffix + ".meta")
    if meta_path.exists():
        return
    meta_path.write_text(
        f"""fileFormatVersion: 2
guid: {uuid.uuid4().hex}
TextScriptImporter:
  externalObjects: {{}}
  userData:
  assetBundleName:
  assetBundleVariant:
""",
        encoding="utf-8",
    )


def _write_delivery_folder_metas(output_dir: Path, preview_dir: Path) -> None:
    for folder in (output_dir.parent, output_dir, preview_dir.parent, preview_dir):
        _write_folder_meta(folder)


def _write_delivery_text_metas(scene_art_dir: Path, manifest_path: Path) -> None:
    _write_text_meta(manifest_path)
    for name in ("README.md", "PROMPTS.md"):
        document_path = scene_art_dir / name
        if document_path.exists():
            _write_text_meta(document_path)


def prepare(source: Path, work_dir: Path, overlap: int) -> None:
    _validate_overlap(overlap)
    guides_dir = work_dir / "guides"
    overlap_guides_dir = work_dir / "overlap_guides"
    raw_dir = work_dir / "refined_raw"
    overlap_raw_dir = work_dir / "refined_overlap_raw"
    guides_dir.mkdir(parents=True, exist_ok=True)
    overlap_guides_dir.mkdir(parents=True, exist_ok=True)
    raw_dir.mkdir(parents=True, exist_ok=True)
    overlap_raw_dir.mkdir(parents=True, exist_ok=True)

    source_image = _open_rgb(source)
    square = _cover_square(source_image, MASTER_SIZE)
    square = square.filter(ImageFilter.UnsharpMask(radius=1.1, percent=65, threshold=3))
    master_path = work_dir / "tianyong_city_master_6144_guide.png"
    square.save(master_path, format="PNG", compress_level=4)

    master_array = np.asarray(square)
    padded = np.pad(
        master_array,
        ((overlap, overlap), (overlap, overlap), (0, 0)),
        mode="reflect",
    )
    patch_size = TILE_SIZE + overlap * 2

    for row in range(1, GRID_SIZE + 1):
        for column in range(1, GRID_SIZE + 1):
            left = (column - 1) * TILE_SIZE
            top = (row - 1) * TILE_SIZE
            tile = square.crop((left, top, left + TILE_SIZE, top + TILE_SIZE))
            tile.save(guides_dir / _tile_name(row, column), format="PNG", compress_level=4)
            patch = Image.fromarray(
                padded[top : top + patch_size, left : left + patch_size],
                mode="RGB",
            )
            patch.save(overlap_guides_dir / _tile_name(row, column), format="PNG", compress_level=4)

    metadata_path = _write_overlap_metadata(work_dir, overlap)
    print(f"Prepared {GRID_SIZE * GRID_SIZE} guide tiles in {guides_dir}")
    print(f"Prepared {GRID_SIZE * GRID_SIZE} overlap guides in {overlap_guides_dir}")
    print(f"Guide master: {master_path}")
    print(f"Overlap metadata: {metadata_path}")


def _channel_stats(image: Image.Image) -> tuple[np.ndarray, np.ndarray]:
    stats = ImageStat.Stat(image)
    return np.asarray(stats.mean[:3], dtype=np.float32), np.asarray(stats.stddev[:3], dtype=np.float32)


def _match_color(source: Image.Image, reference: Image.Image) -> Image.Image:
    source_array = np.asarray(source, dtype=np.float32)
    source_mean, source_std = _channel_stats(source)
    reference_mean, reference_std = _channel_stats(reference)
    source_std = np.maximum(source_std, 1.0)
    scale = np.clip(reference_std / source_std, 0.72, 1.38)
    matched = (source_array - source_mean) * scale + reference_mean
    return Image.fromarray(np.uint8(np.clip(matched, 0, 255)), mode="RGB")


def _seam_weight(size: int, border: int) -> Image.Image:
    y, x = np.ogrid[:size, :size]
    distance = np.minimum.reduce(
        [
            np.broadcast_to(x, (size, size)),
            np.broadcast_to(y, (size, size)),
            np.broadcast_to(size - 1 - x, (size, size)),
            np.broadcast_to(size - 1 - y, (size, size)),
        ]
    ).astype(np.float32)
    t = np.clip(distance / float(border), 0.0, 1.0)
    smooth = t * t * (3.0 - 2.0 * t)
    return Image.fromarray(np.uint8(np.round(smooth * 255.0)), mode="L")


def _overlap_weight(patch_size: int, overlap: int) -> np.ndarray:
    axis = np.ones(patch_size, dtype=np.float32)
    t = np.linspace(0.0, 1.0, overlap, endpoint=False, dtype=np.float32)
    ramp = t * t * (3.0 - 2.0 * t)
    axis[:overlap] = ramp
    axis[-overlap:] = ramp[::-1]
    return np.outer(axis, axis).astype(np.float32)


def _minimum_vertical_seam(left: np.ndarray, right: np.ndarray) -> np.ndarray:
    """Return the lowest-error top-to-bottom seam through two equal overlap strips."""
    difference = left.astype(np.float32) - right.astype(np.float32)
    cost = np.mean(difference * difference, axis=2)
    height, width = cost.shape
    center = (width - 1) / 2.0
    normalized = np.abs((np.arange(width, dtype=np.float32) - center) / max(center, 1.0))
    typical = float(np.median(cost)) + 1.0
    cost += (normalized**4)[None, :] * typical * 0.16
    margin = min(24, max(1, width // 12))
    cost[:, :margin] += typical * 8.0
    cost[:, -margin:] += typical * 8.0

    previous = cost[0].copy()
    backtrack = np.zeros((height, width), dtype=np.int8)
    infinity = np.float32(np.finfo(np.float32).max / 16.0)
    for y in range(1, height):
        from_left = np.empty_like(previous)
        from_right = np.empty_like(previous)
        from_left[0] = infinity
        from_left[1:] = previous[:-1]
        from_right[-1] = infinity
        from_right[:-1] = previous[1:]
        candidates = np.stack((from_left, previous, from_right), axis=0)
        choices = np.argmin(candidates, axis=0)
        backtrack[y] = choices.astype(np.int8) - 1
        previous = cost[y] + np.take_along_axis(candidates, choices[None, :], axis=0)[0]

    seam = np.empty(height, dtype=np.int32)
    seam[-1] = int(np.argmin(previous))
    for y in range(height - 1, 0, -1):
        seam[y - 1] = seam[y] + int(backtrack[y, seam[y]])
    return seam


def _append_with_minimum_seam(base: np.ndarray, patch: np.ndarray, overlap: int) -> np.ndarray:
    """Append patch to the right of base using a feathered minimum-error overlap seam."""
    if base.shape[0] != patch.shape[0]:
        raise ValueError("Base and patch heights must match.")
    left_overlap = base[:, -overlap:]
    right_overlap = patch[:, :overlap]
    seam = _minimum_vertical_seam(left_overlap, right_overlap)
    x_coordinates = np.arange(overlap, dtype=np.int32)[None, :]
    binary_mask = np.uint8(x_coordinates >= seam[:, None]) * 255
    feathered = Image.fromarray(binary_mask, mode="L").filter(ImageFilter.GaussianBlur(radius=2.0))
    blend_weight = np.asarray(feathered, dtype=np.float32)[..., None] / 255.0
    blended = (
        left_overlap.astype(np.float32) * (1.0 - blend_weight)
        + right_overlap.astype(np.float32) * blend_weight
    )
    return np.concatenate(
        (
            base[:, :-overlap],
            np.uint8(np.clip(blended, 0, 255)),
            patch[:, overlap:],
        ),
        axis=1,
    )


def _load_font(size: int) -> ImageFont.ImageFont:
    candidates = (
        Path("C:/Windows/Fonts/arialbd.ttf"),
        Path("C:/Windows/Fonts/msyhbd.ttc"),
    )
    for candidate in candidates:
        if candidate.exists():
            return ImageFont.truetype(str(candidate), size=size)
    return ImageFont.load_default()


def _contact_sheet(tiles: list[tuple[int, int, Image.Image]], destination: Path) -> None:
    preview_tile = 256
    sheet = Image.new("RGB", (preview_tile * GRID_SIZE, preview_tile * GRID_SIZE), "#142a2b")
    draw = ImageDraw.Draw(sheet, "RGBA")
    font = _load_font(22)
    for row, column, tile in tiles:
        x = (column - 1) * preview_tile
        y = (row - 1) * preview_tile
        preview = tile.resize((preview_tile, preview_tile), Image.Resampling.LANCZOS)
        sheet.paste(preview, (x, y))
        draw.rectangle((x + 8, y + 8, x + 92, y + 40), fill=(12, 30, 30, 180))
        draw.text((x + 16, y + 12), f"R{row} C{column}", font=font, fill=(255, 245, 212, 255))
    for index in range(1, GRID_SIZE):
        coordinate = index * preview_tile
        draw.line((coordinate, 0, coordinate, sheet.height), fill=(255, 245, 212, 180), width=2)
        draw.line((0, coordinate, sheet.width, coordinate), fill=(255, 245, 212, 180), width=2)
    sheet.save(destination, format="PNG", compress_level=6)


def finalize(work_dir: Path, output_dir: Path, preview_dir: Path, border: int) -> None:
    guides_dir = work_dir / "guides"
    raw_dir = work_dir / "refined_raw"
    output_dir.mkdir(parents=True, exist_ok=True)
    preview_dir.mkdir(parents=True, exist_ok=True)
    _write_delivery_folder_metas(output_dir, preview_dir)
    mask = _seam_weight(TILE_SIZE, border)
    assembled = Image.new("RGB", (MASTER_SIZE, MASTER_SIZE))
    finalized: list[tuple[int, int, Image.Image]] = []
    manifest_tiles: list[dict[str, object]] = []

    for row in range(1, GRID_SIZE + 1):
        for column in range(1, GRID_SIZE + 1):
            name = _tile_name(row, column)
            guide_path = guides_dir / name
            raw_path = raw_dir / name
            if not guide_path.exists():
                raise FileNotFoundError(f"Missing guide tile: {guide_path}")
            if not raw_path.exists():
                raise FileNotFoundError(f"Missing refined tile: {raw_path}")

            guide = _open_rgb(guide_path)
            raw = _cover_square(_open_rgb(raw_path), TILE_SIZE)
            matched = _match_color(raw, guide)
            tile = Image.composite(matched, guide, mask)
            tile = tile.filter(ImageFilter.UnsharpMask(radius=0.55, percent=30, threshold=4))
            # Restore an exact two-pixel perimeter from the common guide after sharpening.
            perimeter = Image.new("L", (TILE_SIZE, TILE_SIZE), 0)
            perimeter_draw = ImageDraw.Draw(perimeter)
            perimeter_draw.rectangle((0, 0, TILE_SIZE - 1, TILE_SIZE - 1), outline=255, width=2)
            tile = Image.composite(guide, tile, perimeter)

            destination = output_dir / name
            _write_texture_meta(destination, sprite=True, max_size=2048)
            tile.save(destination, format="PNG", compress_level=6)
            x = (column - 1) * TILE_SIZE
            y = (row - 1) * TILE_SIZE
            assembled.paste(tile, (x, y))
            finalized.append((row, column, tile))
            manifest_tiles.append(
                {
                    "id": f"r{row:02d}_c{column:02d}",
                    "row": row,
                    "column": column,
                    "file": f"Tiles/{name}",
                    "resourcesPath": f"{TILE_RESOURCES_ROOT}/{Path(name).stem}",
                    "pixelBounds": {"x": x, "y": y, "width": TILE_SIZE, "height": TILE_SIZE},
                    "neighbors": {
                        "north": f"r{row - 1:02d}_c{column:02d}" if row > 1 else None,
                        "south": f"r{row + 1:02d}_c{column:02d}" if row < GRID_SIZE else None,
                        "west": f"r{row:02d}_c{column - 1:02d}" if column > 1 else None,
                        "east": f"r{row:02d}_c{column + 1:02d}" if column < GRID_SIZE else None,
                    },
                }
            )

    master_path = preview_dir / "tianyong_city_master_6144.png"
    quick_preview_path = preview_dir / "tianyong_city_master_preview_2048.png"
    contact_path = preview_dir / "tianyong_city_tiles_contact_sheet.png"
    _write_texture_meta(master_path, sprite=False, max_size=8192)
    _write_texture_meta(quick_preview_path, sprite=False, max_size=2048)
    _write_texture_meta(contact_path, sprite=False, max_size=2048)
    assembled.save(master_path, format="PNG", compress_level=6)
    assembled.resize(
        (QUICK_PREVIEW_SIZE, QUICK_PREVIEW_SIZE),
        Image.Resampling.LANCZOS,
    ).save(quick_preview_path, format="PNG", compress_level=6)
    _contact_sheet(finalized, contact_path)

    manifest = {
        "name": "Tianyong Main City 6x6 Scene Tiles",
        "assetRoot": TILE_ASSET_ROOT,
        "grid": {"columns": GRID_SIZE, "rows": GRID_SIZE, "origin": "top-left", "order": "row-major"},
        "tileSize": {"width": TILE_SIZE, "height": TILE_SIZE},
        "assembledSize": {"width": MASTER_SIZE, "height": MASTER_SIZE},
        "seamTreatment": {
            "guideBorderPixels": border,
            "exactGuidePerimeterPixels": 2,
            "description": "Refined interiors feather into a shared master guide at every tile boundary.",
        },
        "tiles": manifest_tiles,
    }
    manifest_path = preview_dir.parent / "tile_manifest.json"
    manifest_path.write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    _write_delivery_text_metas(preview_dir.parent, manifest_path)
    print(f"Finalized {len(finalized)} tiles in {output_dir}")
    print(f"Assembled master: {master_path}")
    print(f"Quick preview: {quick_preview_path}")
    print(f"Contact sheet: {contact_path}")
    print(f"Manifest: {manifest_path}")


def finalize_overlap(
    work_dir: Path,
    output_dir: Path,
    preview_dir: Path,
    overlap: int,
) -> None:
    raw_native_size = _validate_overlap_inputs(work_dir, overlap)
    guides_dir = work_dir / "overlap_guides"
    raw_dir = work_dir / "refined_overlap_raw"
    output_dir.mkdir(parents=True, exist_ok=True)
    preview_dir.mkdir(parents=True, exist_ok=True)
    _write_delivery_folder_metas(output_dir, preview_dir)

    patch_size = TILE_SIZE + overlap * 2
    stitched_master: np.ndarray | None = None
    for row in range(1, GRID_SIZE + 1):
        stitched_row: np.ndarray | None = None
        for column in range(1, GRID_SIZE + 1):
            name = _tile_name(row, column)
            guide_path = guides_dir / name
            raw_path = raw_dir / name
            if not guide_path.exists():
                raise FileNotFoundError(f"Missing overlap guide: {guide_path}")
            if not raw_path.exists():
                raise FileNotFoundError(f"Missing overlap refinement: {raw_path}")

            guide = _open_rgb(guide_path)
            raw = _cover_square(_open_rgb(raw_path), patch_size)
            matched = _match_color(raw, guide)
            patch_array = np.asarray(matched, dtype=np.uint8)
            stitched_row = (
                patch_array
                if stitched_row is None
                else _append_with_minimum_seam(stitched_row, patch_array, overlap * 2)
            )

        if stitched_row is None:
            raise RuntimeError(f"No overlap patches were stitched for row {row}.")
        if stitched_master is None:
            stitched_master = stitched_row
        else:
            transposed_master = np.transpose(stitched_master, (1, 0, 2))
            transposed_row = np.transpose(stitched_row, (1, 0, 2))
            transposed_result = _append_with_minimum_seam(
                transposed_master,
                transposed_row,
                overlap * 2,
            )
            stitched_master = np.transpose(transposed_result, (1, 0, 2))

    if stitched_master is None:
        raise RuntimeError("No overlap patches were stitched.")
    expected_padded_size = MASTER_SIZE + overlap * 2
    if stitched_master.shape[:2] != (expected_padded_size, expected_padded_size):
        raise RuntimeError(f"Unexpected stitched dimensions: {stitched_master.shape}")
    cropped = stitched_master[
        overlap : overlap + MASTER_SIZE,
        overlap : overlap + MASTER_SIZE,
    ]
    assembled = Image.fromarray(cropped, mode="RGB")
    assembled = assembled.filter(ImageFilter.UnsharpMask(radius=0.45, percent=25, threshold=4))
    del stitched_master, cropped

    finalized: list[tuple[int, int, Image.Image]] = []
    manifest_tiles: list[dict[str, object]] = []
    for row in range(1, GRID_SIZE + 1):
        for column in range(1, GRID_SIZE + 1):
            name = _tile_name(row, column)
            x = (column - 1) * TILE_SIZE
            y = (row - 1) * TILE_SIZE
            tile = assembled.crop((x, y, x + TILE_SIZE, y + TILE_SIZE))
            destination = output_dir / name
            _write_texture_meta(destination, sprite=True, max_size=2048)
            tile.save(destination, format="PNG", compress_level=6)
            finalized.append((row, column, tile))
            manifest_tiles.append(
                {
                    "id": f"r{row:02d}_c{column:02d}",
                    "row": row,
                    "column": column,
                    "file": f"Tiles/{name}",
                    "resourcesPath": f"{TILE_RESOURCES_ROOT}/{Path(name).stem}",
                    "pixelBounds": {"x": x, "y": y, "width": TILE_SIZE, "height": TILE_SIZE},
                    "neighbors": {
                        "north": f"r{row - 1:02d}_c{column:02d}" if row > 1 else None,
                        "south": f"r{row + 1:02d}_c{column:02d}" if row < GRID_SIZE else None,
                        "west": f"r{row:02d}_c{column - 1:02d}" if column > 1 else None,
                        "east": f"r{row:02d}_c{column + 1:02d}" if column < GRID_SIZE else None,
                    },
                }
            )

    master_path = preview_dir / "tianyong_city_master_6144.png"
    quick_preview_path = preview_dir / "tianyong_city_master_preview_2048.png"
    contact_path = preview_dir / "tianyong_city_tiles_contact_sheet.png"
    _write_texture_meta(master_path, sprite=False, max_size=8192)
    _write_texture_meta(quick_preview_path, sprite=False, max_size=2048)
    _write_texture_meta(contact_path, sprite=False, max_size=2048)
    assembled.save(master_path, format="PNG", compress_level=6)
    assembled.resize(
        (QUICK_PREVIEW_SIZE, QUICK_PREVIEW_SIZE),
        Image.Resampling.LANCZOS,
    ).save(quick_preview_path, format="PNG", compress_level=6)
    _contact_sheet(finalized, contact_path)

    manifest = {
        "name": "Tianyong Main City 6x6 Wide-Road Scene Tiles",
        "assetRoot": TILE_ASSET_ROOT,
        "grid": {"columns": GRID_SIZE, "rows": GRID_SIZE, "origin": "top-left", "order": "row-major"},
        "tileSize": {"width": TILE_SIZE, "height": TILE_SIZE},
        "assembledSize": {"width": MASTER_SIZE, "height": MASTER_SIZE},
        "gameplayLayout": {
            "priority": "wide multiplayer circulation and crowd-capacity standing areas",
            "protectedAreas": ["main avenues", "central plaza", "district courtyards", "gate forecourts", "bridges"],
        },
        "seamTreatment": {
            "contextPerSidePixels": overlap,
            "adjacentPatchOverlapPixels": overlap * 2,
            "patchSize": {"width": patch_size, "height": patch_size},
            "refinementSourceSize": {
                "width": raw_native_size[0],
                "height": raw_native_size[1],
            },
            "blendMethod": "minimum-error seam with narrow feathering",
            "description": "Every tile was refined with neighboring context; 36 overlapping patches were quilted along low-error paths into one master and then re-sliced, so adjacent final pixels share one assembled raster.",
        },
        "tiles": manifest_tiles,
    }
    manifest_path = preview_dir.parent / "tile_manifest.json"
    manifest_path.write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    _write_delivery_text_metas(preview_dir.parent, manifest_path)
    print(f"Overlap-finalized {len(finalized)} tiles in {output_dir}")
    print(f"Assembled master: {master_path}")
    print(f"Quick preview: {quick_preview_path}")
    print(f"Contact sheet: {contact_path}")
    print(f"Manifest: {manifest_path}")


def _expected_neighbors(row: int, column: int) -> dict[str, str | None]:
    return {
        "north": f"r{row - 1:02d}_c{column:02d}" if row > 1 else None,
        "south": f"r{row + 1:02d}_c{column:02d}" if row < GRID_SIZE else None,
        "west": f"r{row:02d}_c{column - 1:02d}" if column > 1 else None,
        "east": f"r{row:02d}_c{column + 1:02d}" if column < GRID_SIZE else None,
    }


def _expected_manifest_tiles() -> list[dict[str, object]]:
    tiles: list[dict[str, object]] = []
    for row in range(1, GRID_SIZE + 1):
        for column in range(1, GRID_SIZE + 1):
            name = _tile_name(row, column)
            tiles.append(
                {
                    "id": f"r{row:02d}_c{column:02d}",
                    "row": row,
                    "column": column,
                    "file": f"Tiles/{name}",
                    "resourcesPath": f"{TILE_RESOURCES_ROOT}/{Path(name).stem}",
                    "pixelBounds": {
                        "x": (column - 1) * TILE_SIZE,
                        "y": (row - 1) * TILE_SIZE,
                        "width": TILE_SIZE,
                        "height": TILE_SIZE,
                    },
                    "neighbors": _expected_neighbors(row, column),
                }
            )
    return tiles


def _read_meta(meta_path: Path, guids: dict[str, Path]) -> str:
    if not meta_path.exists():
        raise RuntimeError(f"Missing Unity meta: {meta_path}")
    text = meta_path.read_text(encoding="utf-8")
    match = re.search(r"^guid: ([0-9a-f]{32})$", text, flags=re.MULTILINE)
    if match is None:
        raise RuntimeError(f"Missing or malformed GUID in Unity meta: {meta_path}")
    guid = match.group(1)
    previous = guids.get(guid)
    if previous is not None:
        raise RuntimeError(f"Duplicate Unity GUID {guid}: {previous} and {meta_path}")
    guids[guid] = meta_path
    return text


def _verify_texture_meta(
    image_path: Path,
    *,
    sprite: bool,
    max_size: int,
    guids: dict[str, Path],
) -> None:
    meta_path = image_path.with_suffix(image_path.suffix + ".meta")
    text = _read_meta(meta_path, guids)
    lines = set(text.splitlines())
    required_lines = {
        "TextureImporter:",
        "    enableMipMap: 0",
        "    sRGBTexture: 1",
        "  isReadable: 0",
        f"  maxTextureSize: {max_size}",
        "    filterMode: 1",
        "    wrapU: 1",
        "    wrapV: 1",
        "    wrapW: 1",
        "  nPOTScale: 0",
        "    textureCompression: 0",
        "    crunchedCompression: 0",
        "    overridden: 0",
    }
    if sprite:
        required_lines.update(
            {
                "  spriteMode: 1",
                "  spriteMeshType: 0",
                "  spritePixelsToUnits: 100",
                "  textureType: 8",
            }
        )
    else:
        required_lines.update({"  spriteMode: 0", "  textureType: 0"})
    missing = sorted(required_lines - lines)
    if missing:
        raise RuntimeError(f"Unexpected TextureImporter settings in {meta_path}; missing={missing}")
    if text.count("    textureCompression: 0") < 2:
        raise RuntimeError(f"Expected uncompressed Default and Standalone settings in {meta_path}")


def _verify_folder_meta(folder: Path, guids: dict[str, Path]) -> None:
    meta_path = Path(str(folder) + ".meta")
    text = _read_meta(meta_path, guids)
    if "folderAsset: yes" not in text or "DefaultImporter:" not in text:
        raise RuntimeError(f"Unexpected folder meta importer: {meta_path}")


def _verify_text_meta(text_path: Path, guids: dict[str, Path]) -> None:
    if not text_path.exists():
        raise RuntimeError(f"Missing text asset: {text_path}")
    meta_path = text_path.with_suffix(text_path.suffix + ".meta")
    text = _read_meta(meta_path, guids)
    if "TextScriptImporter:" not in text:
        raise RuntimeError(f"Unexpected text meta importer: {meta_path}")


def verify(output_dir: Path, preview_dir: Path) -> None:
    expected_names = _expected_tile_names()
    expected_name_set = set(expected_names)
    actual_name_set = _png_names(output_dir)
    if actual_name_set != expected_name_set:
        missing = sorted(expected_name_set - actual_name_set)
        extra = sorted(actual_name_set - expected_name_set)
        raise RuntimeError(f"Unexpected final tile PNG set; missing={missing}, extra={extra}")
    expected_meta_names = {f"{name}.meta" for name in expected_names}
    actual_meta_names = {
        path.name
        for path in output_dir.iterdir()
        if path.is_file() and path.name.lower().endswith(".png.meta")
    }
    if actual_meta_names != expected_meta_names:
        missing = sorted(expected_meta_names - actual_meta_names)
        extra = sorted(actual_meta_names - expected_meta_names)
        raise RuntimeError(f"Unexpected final tile PNG meta set; missing={missing}, extra={extra}")

    reassembled = Image.new("RGB", (MASTER_SIZE, MASTER_SIZE))
    guids: dict[str, Path] = {}
    for row in range(1, GRID_SIZE + 1):
        for column in range(1, GRID_SIZE + 1):
            path = output_dir / _tile_name(row, column)
            with Image.open(path) as image:
                image.load()
                if image.format != "PNG" or image.size != (TILE_SIZE, TILE_SIZE) or image.mode != "RGB":
                    raise RuntimeError(
                        f"Final tile must be a {TILE_SIZE}x{TILE_SIZE} RGB PNG: "
                        f"{path} is format={image.format}, mode={image.mode}, size={image.size}"
                    )
                reassembled.paste(
                    image,
                    ((column - 1) * TILE_SIZE, (row - 1) * TILE_SIZE),
                )
            _verify_texture_meta(path, sprite=True, max_size=2048, guids=guids)

    master_path = preview_dir / "tianyong_city_master_6144.png"
    with Image.open(master_path) as master:
        master.load()
        if master.format != "PNG" or master.size != (MASTER_SIZE, MASTER_SIZE) or master.mode != "RGB":
            raise RuntimeError(
                f"Master must be a {MASTER_SIZE}x{MASTER_SIZE} RGB PNG: "
                f"format={master.format}, mode={master.mode}, size={master.size}"
            )
        master_rgb = master.copy()
    if ImageChops.difference(reassembled, master_rgb).getbbox() is not None:
        raise RuntimeError("Reassembled final tiles differ from the delivered master.")
    _verify_texture_meta(master_path, sprite=False, max_size=8192, guids=guids)

    quick_preview_path = preview_dir / "tianyong_city_master_preview_2048.png"
    with Image.open(quick_preview_path) as quick_preview:
        quick_preview.load()
        if (
            quick_preview.format != "PNG"
            or quick_preview.size != (QUICK_PREVIEW_SIZE, QUICK_PREVIEW_SIZE)
            or quick_preview.mode != "RGB"
        ):
            raise RuntimeError(
                f"Quick preview must be a {QUICK_PREVIEW_SIZE}x{QUICK_PREVIEW_SIZE} RGB PNG: "
                f"format={quick_preview.format}, mode={quick_preview.mode}, size={quick_preview.size}"
            )
        expected_quick_preview = master_rgb.resize(
            (QUICK_PREVIEW_SIZE, QUICK_PREVIEW_SIZE),
            Image.Resampling.LANCZOS,
        )
        if ImageChops.difference(quick_preview, expected_quick_preview).getbbox() is not None:
            raise RuntimeError("Quick preview does not exactly match the delivered master resize.")
    _verify_texture_meta(quick_preview_path, sprite=False, max_size=2048, guids=guids)

    contact_path = preview_dir / "tianyong_city_tiles_contact_sheet.png"
    with Image.open(contact_path) as contact:
        contact.load()
        if (
            contact.format != "PNG"
            or contact.size != (GRID_SIZE * 256, GRID_SIZE * 256)
            or contact.mode != "RGB"
        ):
            raise RuntimeError(
                f"Unexpected contact sheet: format={contact.format}, "
                f"mode={contact.mode}, size={contact.size}"
            )
    _verify_texture_meta(contact_path, sprite=False, max_size=2048, guids=guids)

    manifest_path = preview_dir.parent / "tile_manifest.json"
    try:
        manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as error:
        raise RuntimeError(f"Invalid manifest: {manifest_path}") from error
    if manifest.get("assetRoot") != TILE_ASSET_ROOT:
        raise RuntimeError(f"Unexpected manifest assetRoot: {manifest.get('assetRoot')}")
    if manifest.get("grid") != {
        "columns": GRID_SIZE,
        "rows": GRID_SIZE,
        "origin": "top-left",
        "order": "row-major",
    }:
        raise RuntimeError("Manifest grid/origin/order is invalid.")
    if manifest.get("tileSize") != {"width": TILE_SIZE, "height": TILE_SIZE}:
        raise RuntimeError("Manifest tileSize is invalid.")
    if manifest.get("assembledSize") != {"width": MASTER_SIZE, "height": MASTER_SIZE}:
        raise RuntimeError("Manifest assembledSize is invalid.")
    expected_tiles = _expected_manifest_tiles()
    if manifest.get("tiles") != expected_tiles:
        raise RuntimeError("Manifest tile order, IDs, paths, bounds, or neighbors are invalid.")

    seam = manifest.get("seamTreatment")
    if not isinstance(seam, dict):
        raise RuntimeError("Manifest seamTreatment is missing or invalid.")
    if "contextPerSidePixels" in seam:
        context = seam.get("contextPerSidePixels")
        if not isinstance(context, int) or context <= 0:
            raise RuntimeError("Manifest contextPerSidePixels must be a positive integer.")
        if seam.get("adjacentPatchOverlapPixels") != context * 2:
            raise RuntimeError("Manifest adjacentPatchOverlapPixels must equal twice the context.")
        patch_size = TILE_SIZE + context * 2
        if seam.get("patchSize") != {"width": patch_size, "height": patch_size}:
            raise RuntimeError("Manifest overlap patchSize is invalid.")
        source_size = seam.get("refinementSourceSize")
        if (
            not isinstance(source_size, dict)
            or not isinstance(source_size.get("width"), int)
            or source_size.get("width") != source_size.get("height")
            or source_size.get("width", 0) < TILE_SIZE
        ):
            raise RuntimeError("Manifest refinementSourceSize is invalid.")
        if seam.get("blendMethod") != "minimum-error seam with narrow feathering":
            raise RuntimeError("Manifest overlap blendMethod is invalid.")
    elif not {
        "guideBorderPixels",
        "exactGuidePerimeterPixels",
        "description",
    }.issubset(seam):
        raise RuntimeError("Manifest seamTreatment does not describe a supported pipeline.")

    for folder in (output_dir.parent, output_dir, preview_dir.parent, preview_dir):
        _verify_folder_meta(folder, guids)
    _verify_text_meta(manifest_path, guids)
    _verify_text_meta(preview_dir.parent / "README.md", guids)
    _verify_text_meta(preview_dir.parent / "PROMPTS.md", guids)

    print(
        "Verification passed: exact 36-tile PNG/meta set; strict importer settings and unique GUIDs; "
        "exact 6144x6144 reassembly; 2048 preview, contact sheet, manifest, and delivery metas valid."
    )


def main() -> None:
    parser = argparse.ArgumentParser()
    subparsers = parser.add_subparsers(dest="command", required=True)

    prepare_parser = subparsers.add_parser("prepare")
    prepare_parser.add_argument("--source", type=Path, required=True)
    prepare_parser.add_argument("--work-dir", type=Path, required=True)
    prepare_parser.add_argument("--overlap", type=int, default=DEFAULT_OVERLAP)

    finalize_parser = subparsers.add_parser("finalize")
    finalize_parser.add_argument("--work-dir", type=Path, required=True)
    finalize_parser.add_argument("--output-dir", type=Path, required=True)
    finalize_parser.add_argument("--preview-dir", type=Path, required=True)
    finalize_parser.add_argument("--border", type=int, default=96)

    overlap_parser = subparsers.add_parser("finalize-overlap")
    overlap_parser.add_argument("--work-dir", type=Path, required=True)
    overlap_parser.add_argument("--output-dir", type=Path, required=True)
    overlap_parser.add_argument("--preview-dir", type=Path, required=True)
    overlap_parser.add_argument("--overlap", type=int, default=DEFAULT_OVERLAP)

    verify_parser = subparsers.add_parser("verify")
    verify_parser.add_argument("--output-dir", type=Path, required=True)
    verify_parser.add_argument("--preview-dir", type=Path, required=True)

    args = parser.parse_args()
    if args.command == "prepare":
        prepare(args.source, args.work_dir, args.overlap)
    elif args.command == "finalize":
        finalize(args.work_dir, args.output_dir, args.preview_dir, args.border)
    elif args.command == "finalize-overlap":
        finalize_overlap(args.work_dir, args.output_dir, args.preview_dir, args.overlap)
    else:
        verify(args.output_dir, args.preview_dir)


if __name__ == "__main__":
    main()
