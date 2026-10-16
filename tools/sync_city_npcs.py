"""Validate the complete 23-NPC delivery, then import its PNG bytes unchanged.

Native delivery manifests and the older export[1024] manifest are supported.
Invalid inputs never change the output project. This does not promise rollback
after an operating-system write failure. No images are resized or deleted.
"""

import argparse
import hashlib
import io
import json
import math
import re
import uuid
from dataclasses import dataclass
from datetime import datetime, timezone
from pathlib import Path

from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
RESOURCE = Path("Assets/Resources/World/Tianyong/Npcs")
SPRITE_RESOURCE_ROOT = "World/Tianyong/Npcs/Sprites/"
EXPECTED_IDS = {f"{number:02}" for number in range(1, 24)}
MAX_TEXTURE_SIZE = 2048


class ValidationError(ValueError):
    """An actionable input error detected before any project writes."""


def require(condition, message):
    if not condition:
        raise ValidationError(message)


def read_json(path, label):
    require(path.is_file(), f"Missing {label}: {path}")
    try:
        return json.loads(path.read_text(encoding="utf-8-sig"))
    except (ValueError, UnicodeError) as error:
        raise ValidationError(f"Invalid {label}: {path}: {error}") from error


def normalized_id(value):
    require(not isinstance(value, bool) and re.fullmatch(r"\d{1,2}", str(value)) is not None,
            f"Invalid NPC id: {value!r}")
    return str(value).zfill(2)


def roster(entries, label):
    require(isinstance(entries, list), f"{label} must contain an entries list")
    result = {}
    for entry in entries:
        require(isinstance(entry, dict), f"Invalid {label} entry: expected an object")
        npc_id = normalized_id(entry.get("id"))
        require(npc_id not in result, f"Duplicate NPC id {npc_id} in {label}")
        result[npc_id] = entry
    require(set(result) == EXPECTED_IDS,
            f"{label} must contain exactly IDs 01..23; "
            f"missing={sorted(EXPECTED_IDS - set(result))}, unexpected={sorted(set(result) - EXPECTED_IDS)}")
    return result


def finite_number(value, label):
    require(isinstance(value, (int, float)) and not isinstance(value, bool) and math.isfinite(value),
            f"{label} must be a finite number")
    return value


def source_path(source, relative, label):
    require(isinstance(relative, str) and relative, f"Missing {label} path")
    path = (source / relative).resolve()
    require(path.is_relative_to(source), f"{label} escapes the source directory: {relative}")
    return path


def load_anchors(path):
    if path is None:
        return {}
    data = read_json(path, "anchors JSON")
    require(isinstance(data, dict), "Anchors must be an object mapping NPC IDs to pixelX/pixelY")
    anchors = {}
    for key, anchor in data.items():
        npc_id = normalized_id(key)
        require(npc_id in EXPECTED_IDS and npc_id not in anchors, f"Unexpected or duplicate anchor id {npc_id}")
        require(isinstance(anchor, dict), f"Anchor {npc_id} must contain pixelX and pixelY")
        anchors[npc_id] = (finite_number(anchor.get("pixelX"), f"{npc_id} anchor pixelX"),
                           finite_number(anchor.get("pixelY"), f"{npc_id} anchor pixelY"))
    return anchors


def select_image(npc, npc_id):
    if "file" in npc:
        return npc, npc.get("nativeSize"), True
    exports = [entry for entry in npc.get("export", []) if entry.get("size") == 1024]
    require(len(exports) == 1, f"NPC {npc_id} needs one legacy 1024 export or a native file")
    return exports[0], [1024, 1024], False


def inspect_image(source, npc, npc_id):
    image_entry, expected_size, native = select_image(npc, npc_id)
    path = source_path(source, image_entry.get("file"), f"NPC {npc_id} image")
    require(path.is_file(), f"Missing NPC {npc_id} image: {path}")
    require(path.suffix.lower() == ".png" and re.fullmatch(r"[A-Za-z0-9_-]+", path.stem),
            f"NPC {npc_id} requires a PNG with an extensionless Resources-safe filename: {path.name}")
    expected_sha = image_entry.get("sha256")
    require(isinstance(expected_sha, str) and re.fullmatch(r"[0-9a-fA-F]{64}", expected_sha),
            f"NPC {npc_id} has an invalid SHA-256")
    data = path.read_bytes()
    sha = hashlib.sha256(data).hexdigest()
    require(sha == expected_sha.lower(), f"NPC {npc_id} SHA-256 mismatch: {path}")
    require(isinstance(expected_size, list) and len(expected_size) == 2 and
            all(type(value) is int and 0 < value <= MAX_TEXTURE_SIZE for value in expected_size),
            f"NPC {npc_id} needs positive dimensions no larger than {MAX_TEXTURE_SIZE}: {expected_size}")
    try:
        with Image.open(io.BytesIO(data)) as image:
            image.load()
            require(image.format == "PNG" and image.mode == "RGBA", f"NPC {npc_id} must be an RGBA PNG")
            require(list(image.size) == expected_size,
                    f"NPC {npc_id} dimensions {image.size} do not match {expected_size}")
            require(image.getchannel("A").getextrema() == (0, 255),
                    f"NPC {npc_id} must contain both transparent and fully opaque pixels")
    except (OSError, SyntaxError) as error:
        raise ValidationError(f"Cannot decode NPC {npc_id}: {path}: {error}") from error
    bbox = image_entry.get("visibleBBox")
    width, height = expected_size
    require(isinstance(bbox, list) and len(bbox) == 4 and all(type(value) is int for value in bbox),
            f"NPC {npc_id} needs an integer visibleBBox [left, top, right, bottom]")
    require(0 <= bbox[0] < bbox[2] <= width and 0 <= bbox[1] < bbox[3] <= height,
            f"NPC {npc_id} visibleBBox is outside its image")
    return path, data, sha, width, height, bbox, native, image_entry


def resolve_pivot(npc_id, position, image_entry, native, width, height, bbox, anchors):
    if npc_id in anchors:
        pixel_x, pixel_y = anchors[npc_id]
        method = "explicit-pixel-anchor"
    elif native:
        # Native canvases have different margins from the former 1024 exports.
        pixel_x, pixel_y = (bbox[0] + bbox[2]) / 2, bbox[3]
        method = "native-visible-bbox-bottom-centre"
    else:
        legacy = image_entry.get("pivotBottomLeftNormalized", [0.5, 0.12])
        require(isinstance(legacy, list) and len(legacy) == 2, f"Invalid legacy pivot for NPC {npc_id}")
        pivot_x = finite_number(position.get("pivotX", legacy[0]), f"{npc_id} pivotX")
        pivot_y = finite_number(position.get("pivotY", legacy[1]), f"{npc_id} pivotY")
        pixel_x, pixel_y = pivot_x * width, (1 - pivot_y) * height
        method = "legacy-normalized-pivot"
    require(0 <= pixel_x <= width and 0 <= pixel_y <= height, f"NPC {npc_id} feet anchor is outside its image")
    return pixel_x / width, 1 - pixel_y / height, dict(method=method, pixelX=pixel_x, pixelY=pixel_y)


def generation_evidence(source, npc):
    relative = npc.get("generationRecord")
    path = source_path(source, relative, "generation record") if relative else None
    record = read_json(path, "generation record") if path and path.is_file() else {}
    require(isinstance(record, dict), f"Generation record must be an object: {path}")
    family = npc.get("actualModelFamily", record.get("actualModelFamily"))
    reported_model = npc.get("actualModel", record.get("actualModel"))
    if family is None and reported_model == "gpt-image":
        family = reported_model  # A family name is not a confirmed numerical version.
    return dict(
        sourceGenerationRecord=path.as_posix() if path else None,
        sourceGenerationRecordStatus="present" if path and path.is_file() else "missing" if path else "not-specified",
        actualModelFamily=family,
        actualModelVersion=npc.get("actualModelVersion", record.get("actualModelVersion")),
        actualQuality=npc.get("actualQuality", record.get("actualQuality")),
        reportedActualModel=reported_model,
        configTarget=record.get("configSnapshot"),
        submittedParameters=record.get("submittedParameters"),
        historicalGenerationReferences=record.get("references", []),
        historicalReferencesNote="Recorded generation inputs only; not current runtime dependencies or a claim that those files still exist.",
    )


def meta_guid(path, project_root):
    meta = Path(str(path) + ".meta")
    if meta.exists():
        matches = re.findall(r"^guid: ([0-9a-fA-F]{32})\s*$", meta.read_text(encoding="utf-8-sig"), re.MULTILINE)
        require(len(matches) == 1, f"Existing meta has no unique valid GUID: {meta}")
        return matches[0]
    return uuid.uuid5(uuid.NAMESPACE_URL, path.relative_to(project_root).as_posix()).hex


def texture_meta(guid):
    return f"""fileFormatVersion: 2
guid: {guid}
TextureImporter:
  externalObjects: {{}}
  serializedVersion: 13
  mipmaps:
    enableMipMap: 0
    sRGBTexture: 1
  isReadable: 0
  textureType: 0
  textureShape: 1
  alphaSource: 1
  alphaIsTransparency: 1
  nPOTScale: 0
  maxTextureSize: {MAX_TEXTURE_SIZE}
  textureSettings:
    serializedVersion: 2
    filterMode: 1
    aniso: 1
    mipBias: 0
    wrapU: 1
    wrapV: 1
    wrapW: 1
  platformSettings:
  - serializedVersion: 3
    buildTarget: DefaultTexturePlatform
    maxTextureSize: {MAX_TEXTURE_SIZE}
    resizeAlgorithm: 0
    textureFormat: -1
    textureCompression: 0
    compressionQuality: 100
    crunchedCompression: 0
    allowsAlphaSplitting: 0
    overridden: 0
  userData:
  assetBundleName:
  assetBundleVariant:
""".encode("utf-8")


def json_bytes(value):
    return (json.dumps(value, ensure_ascii=False, indent=2, allow_nan=False) + "\n").encode("utf-8")


@dataclass
class ImportPlan:
    writes: dict
    placements: dict
    sources: dict
    project_root: Path

    def apply(self):
        # Every input, PNG and existing meta GUID has already been checked.
        for path, data in self.writes.items():
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(data)


def build_plan(source, positions, project_root, anchors=None):
    source, positions, project_root = Path(source).resolve(), Path(positions).resolve(), Path(project_root).resolve()
    manifest = read_json(source / "manifest.json", "source manifest")
    require(isinstance(manifest, dict) and manifest.get("npcCount") == 23, "Source manifest npcCount must equal 23")
    npcs = roster(manifest.get("entries"), "source manifest")
    position_data = read_json(positions, "placements JSON (provide --positions; no positions are inferred)")
    position_entries = position_data if isinstance(position_data, list) else position_data.get("entries") if isinstance(position_data, dict) else None
    positions_by_id = roster(position_entries, "placements")
    anchor_values = load_anchors(Path(anchors).resolve() if anchors else None)
    resource = project_root / RESOURCE
    writes, entries, sources, destination_names = {}, [], [], set()
    for npc_id in sorted(EXPECTED_IDS):
        npc, position = npcs[npc_id], positions_by_id[npc_id]
        require(isinstance(position.get("displayName"), str) and position["displayName"].strip(),
                f"NPC {npc_id} needs an existing displayName")
        for key in ("x", "z", "visibleHeight"):
            finite_number(position.get(key), f"NPC {npc_id} placement {key}")
        require(0 < position["visibleHeight"] <= 40, f"NPC {npc_id} visibleHeight must be in (0, 40]")
        path, data, sha, width, height, bbox, native, image_entry = inspect_image(source, npc, npc_id)
        require(path.name.casefold() not in destination_names, f"Duplicate destination filename: {path.name}")
        destination_names.add(path.name.casefold())
        dest = resource / "Sprites" / path.name
        pivot_x, pivot_y, anchor = resolve_pivot(npc_id, position, image_entry, native, width, height, bbox, anchor_values)
        entry = dict(position, id=npc_id, spriteResource=SPRITE_RESOURCE_ROOT + path.stem,
                     pivotX=pivot_x, pivotY=pivot_y, visibleHeightFraction=(bbox[3] - bbox[1]) / height,
                     nativeWidth=width, nativeHeight=height)
        entries.append(entry)
        sources.append(dict(id=npc_id, name=npc.get("name"), source=path.as_posix(),
                            destination=dest.relative_to(project_root).as_posix(), sha256=sha,
                            sourceSchema="native" if native else "legacy-export-1024", size=[width, height],
                            visibleBBox=bbox, feetAnchorTopLeftPixels=anchor,
                            referenceCompleteness=npc.get("referenceCompleteness"), **generation_evidence(source, npc)))
        writes[dest] = data
        writes[Path(str(dest) + ".meta")] = texture_meta(meta_guid(dest, project_root))

    output_placements = dict(schemaVersion=1, coordinateSystem="Unity X/Z feet points, current painted city",
                             scope="23 static visual NPCs; independent of ground, navigation and server actors", entries=entries)
    output_sources = dict(npcCount=23, importedAtUtc=datetime.now(timezone.utc).isoformat(),
                          imageOperation="byte-identical copy", sourceManifest=(source / "manifest.json").as_posix(),
                          sourceManifestSha256=hashlib.sha256((source / "manifest.json").read_bytes()).hexdigest(),
                          placementsInput=positions.as_posix(), anchorsInput=str(Path(anchors).resolve()) if anchors else None,
                          entries=sources)
    placements_path = resource / "placements.json"
    writes[placements_path] = json_bytes(output_placements)
    placements_meta = Path(str(placements_path) + ".meta")
    if not placements_meta.exists():
        writes[placements_meta] = (f"fileFormatVersion: 2\nguid: {meta_guid(placements_path, project_root)}\n"
                                   "TextScriptImporter:\n  externalObjects: {}\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n").encode("utf-8")
    else:
        meta_guid(placements_path, project_root)
    writes[project_root / "Docs/CityNpcs.sources.json"] = json_bytes(output_sources)
    for folder in (project_root / "Assets/Resources", project_root / "Assets/Resources/World",
                   resource.parent, resource, resource / "Sprites"):
        meta = Path(str(folder) + ".meta")
        guid = meta_guid(folder, project_root)
        if not meta.exists():
            writes[meta] = (f"fileFormatVersion: 2\nguid: {guid}\nfolderAsset: yes\n"
                            "DefaultImporter:\n  externalObjects: {}\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n").encode("utf-8")
    return ImportPlan(writes, output_placements, output_sources, project_root)


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--project-root", type=Path, default=ROOT, help="Client project to receive the resources")
    parser.add_argument("--source", type=Path, help="Delivery folder (default: sibling image/designs/city-npcs-hd-20260924)")
    parser.add_argument("--positions", type=Path, help="Existing 23-NPC placements JSON; required if the project has none")
    parser.add_argument("--anchors", type=Path, help="JSON {id: {pixelX, pixelY}}; pixels measured from the image top-left")
    parser.add_argument("--dry-run", action="store_true", help="Validate the complete batch and report; write nothing")
    args = parser.parse_args(argv)
    source = args.source or args.project_root.resolve().parent / "image/designs/city-npcs-hd-20260924"
    positions = args.positions or args.project_root / RESOURCE / "placements.json"
    try:
        plan = build_plan(source, positions, args.project_root, args.anchors)
        if not args.dry_run:
            plan.apply()
    except (ValidationError, OSError) as error:
        parser.exit(1, f"NPC import failed: {error}\n")
    action = "Validated (dry run; no files written)" if args.dry_run else "Imported"
    print(f"{action}: 23 byte-identical transparent NPCs; project={plan.project_root}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
