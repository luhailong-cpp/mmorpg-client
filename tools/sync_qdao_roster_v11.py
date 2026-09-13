#!/usr/bin/env python3
"""Import approved portraits/strips and recut four independent PNGs per direction.

Requires Pillow. Run from any directory; use --verify-only to audit an existing
import. The source manifest and independent validation hashes must both match.
"""

from __future__ import annotations

import argparse
import hashlib
import io
import json
import re
import shutil
import uuid
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont


APPROVED = (
    "23_lantern_courier",
    "24_lu_dongbin",
    "25_lion_drum_guard",
    "26_osmanthus_healer",
    "27_ink_kite_ranger",
    "28_moon_rabbit_artificer",
    "29_he_xiangu",
    "30_han_xiangzi",
)
DIRECTIONS = ("N", "NE", "E", "SE", "S", "SW", "W", "NW")
# These costume-compatible standing sets were accepted after the V11 import.
# Keep the original 328-image contract intact while auditing each optional
# extension as a complete eight-direction set (see the 2026-09-13 gait report).
DEDICATED_IDLE_CHARACTERS = ("24_lu_dongbin", "29_he_xiangu", "30_han_xiangzi")
RESOURCE_ROOT = Path("Assets/Resources/World/Characters/QdaoRosterV11")
REPORT_PATH = Path("Docs/ArtEvidence/qdao-roster-v11-import.json")
GUID_NAMESPACE = uuid.UUID("3197a0ab-c388-4b8f-9042-35bb4d6a74af")


def sha256(path: Path) -> str:
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def check_image(path: Path, size: tuple[int, int]) -> None:
    with Image.open(path) as image:
        image.load()
        if image.format != "PNG" or image.mode != "RGBA" or image.size != size:
            raise ValueError(f"Expected RGBA PNG {size}, got {image.format}/{image.mode}/{image.size}: {path}")
        low, high = image.getchannel("A").getextrema()
        if low != 0 or high != 255:
            raise ValueError(f"Expected visible opaque content and transparent background: {path}")


def write_meta(path: Path, project: Path, template: str) -> None:
    meta_path = Path(str(path) + ".meta")
    if meta_path.exists():
        return  # Unity/user owns existing importer settings and GUIDs.
    guid = uuid.uuid5(GUID_NAMESPACE, path.relative_to(project).as_posix()).hex
    text = re.sub(r"(?m)^guid: [0-9a-f]{32}$", f"guid: {guid}", template)
    # Exclusive creation preserves a .meta Unity might have created meanwhile.
    try:
        with meta_path.open("x", encoding="utf-8", newline="\n") as stream:
            stream.write(text)
    except FileExistsError:
        pass


def source_plan(source: Path) -> tuple[list[dict], dict]:
    manifest = json.loads((source / "manifest.json").read_text(encoding="utf-8"))
    validation = json.loads((source / "validation.json").read_text(encoding="utf-8"))
    entries = manifest.get("characters", [])
    if len(entries) != len(APPROVED) or {role["slug"] for role in entries} != set(APPROVED):
        raise ValueError("The source manifest must contain exactly the eight approved characters.")
    if validation.get("status") != "passed" or validation.get("verified_frames") != 256:
        raise ValueError("The source validation must have passed all 256 movement frames.")
    roles = {role["slug"]: role for role in entries}
    validations = {role["slug"]: role for role in validation["characters"]}
    plan = []
    for slug in APPROVED:
        role = roles[slug]
        result = validations.get(slug)
        if not result or result.get("status") != "passed":
            raise ValueError(f"Missing passed independent validation: {slug}")
        approved_hashes = {item["path"]: item["sha256"] for item in result["artifacts"]}
        if role.get("cell_size") != [512, 512] or role.get("foot_origin_top_left") != [256, 471]:
            raise ValueError(f"Unexpected cell size or feet anchor: {slug}")
        if role.get("frame_count") != 32 or set(role["directions"]) != set(DIRECTIONS):
            raise ValueError(f"Expected eight directions and 32 movement frames: {slug}")
        portrait = f"{slug}/portrait.png"
        if role["portrait"] != portrait:
            raise ValueError(f"Unexpected portrait path: {slug}")
        files = [(portrait, f"{slug}/portrait.png", (1024, 1024), "portrait")]
        frame_exports = []
        for direction in DIRECTIONS:
            expected_frames = [f"{slug}/walk/{direction}/{frame:02d}.png" for frame in range(1, 5)]
            if role["directions"][direction]["frames"] != expected_frames:
                raise ValueError(f"Expected exactly four approved sequential frames: {slug}/{direction}")
            strip_name = f"{slug}/walk/{direction}/strip.png"
            files.append((strip_name, f"{slug}/walk_{direction}.png", (2048, 512), direction))
            # Recut the authoritative strip on exact 512px boundaries. Compare
            # all RGBA bytes against its separately accepted source frame.
            pixel_hashes = set()
            with Image.open(source / strip_name) as strip:
                strip.load()
                for frame, source_name in enumerate(expected_frames):
                    source_frame = source / source_name
                    check_image(source_frame, (512, 512))
                    source_digest = sha256(source_frame)
                    if approved_hashes.get(source_name) != source_digest:
                        raise ValueError(f"Unapproved source frame: {source_name}")
                    box = [frame * 512, 0, (frame + 1) * 512, 512]
                    crop = strip.crop(box)
                    with Image.open(source_frame) as accepted:
                        if crop.mode != "RGBA" or crop.tobytes() != accepted.tobytes():
                            raise ValueError(f"Strip crop does not match accepted independent frame: {source_name}")
                    pixel_digest = hashlib.sha256(crop.tobytes()).hexdigest()
                    pixel_hashes.add(pixel_digest)
                    encoded = io.BytesIO()
                    crop.save(encoded, format="PNG")
                    frame_exports.append({
                        "slug": slug, "name_zh": role["name_zh"],
                        "source": source_name,
                        "source_sha256": source_digest,
                        "crop_source": strip_name, "crop_box": box,
                        "destination": (RESOURCE_ROOT / source_name).as_posix(),
                        "size": [512, 512], "kind": "walk_frame",
                        "direction": direction, "frame": frame + 1,
                        "pixel_sha256": pixel_digest,
                        "sha256": hashlib.sha256(encoded.getvalue()).hexdigest(),
                    })
            if len(pixel_hashes) != 4:
                raise ValueError(f"Each direction requires four distinct RGBA poses: {slug}/{direction}")
        for source_name, destination_name, size, kind in files:
            path = source / source_name
            check_image(path, size)
            digest = sha256(path)
            if approved_hashes.get(source_name) != digest:
                raise ValueError(f"Source no longer matches independent acceptance SHA256: {source_name}")
            plan.append({
                "slug": slug,
                "name_zh": role["name_zh"],
                "source": source_name,
                "destination": (RESOURCE_ROOT / destination_name).as_posix(),
                "size": list(size),
                "kind": kind,
                "sha256": digest,
            })
        plan.extend(frame_exports)
    return plan, {
        "manifest_sha256": sha256(source / "manifest.json"),
        "validation_sha256": sha256(source / "validation.json"),
    }


def verify_destinations(project: Path, plan: list[dict]) -> int:
    expected = {item["destination"] for item in plan}
    actual = {path.relative_to(project).as_posix() for path in (project / RESOURCE_ROOT).rglob("*.png")}
    idle_extensions = set()
    for slug in DEDICATED_IDLE_CHARACTERS:
        complete_set = {(RESOURCE_ROOT / slug / "idle" / f"{direction}.png").as_posix() for direction in DIRECTIONS}
        present = actual & complete_set
        if present and present != complete_set:
            raise ValueError(f"Incomplete dedicated idle set for {slug}. Missing={sorted(complete_set - present)}")
        idle_extensions.update(present)
    allowed = expected | idle_extensions
    if actual != allowed:
        raise ValueError(f"Unexpected imported PNG set. Missing={sorted(expected - actual)}, extra={sorted(actual - allowed)}")
    guids = set()
    paths = [project / RESOURCE_ROOT] + sorted(path for path in (project / RESOURCE_ROOT).rglob("*") if path.is_dir())
    frame_groups = {}
    for item in plan:
        path = project / item["destination"]
        check_image(path, tuple(item["size"]))
        if sha256(path) != item["sha256"]:
            raise ValueError(f"Imported bytes differ from the validated copy/crop export: {path}")
        if item["kind"] == "walk_frame":
            with Image.open(path) as frame:
                digest = hashlib.sha256(frame.tobytes()).hexdigest()
            if digest != item["pixel_sha256"]:
                raise ValueError(f"Independent frame pixels changed: {path}")
            frame_groups.setdefault((item["slug"], item["direction"]), set()).add(digest)
        paths.append(path)
    if len(frame_groups) != 64 or any(len(hashes) != 4 for hashes in frame_groups.values()):
        raise ValueError("Expected 64 directions, each with exactly four distinct independent frames.")
    for relative in sorted(idle_extensions):
        path = project / relative
        check_image(path, (512, 512))
        paths.append(path)
    for path in paths:
        meta = Path(str(path) + ".meta").read_text(encoding="utf-8")
        match = re.search(r"(?m)^guid: ([0-9a-f]{32})$", meta)
        if not match or match.group(1) in guids:
            raise ValueError(f"Missing or duplicate Unity asset GUID: {path}")
        guids.add(match.group(1))
        if path.suffix == ".png":
            required = ("TextureImporter:", "enableMipMap: 0", "textureCompression: 0", "alphaIsTransparency: 1", "textureType: 0", "filterMode: 1", "wrapU: 1", "wrapV: 1", "maxTextureSize: 4096")
            if any(value not in meta for value in required):
                raise ValueError(f"Unexpected texture import settings: {path}")
    return len(idle_extensions)


def create_review_sheets(project: Path, output: Path) -> None:
    """Technical contact sheets; generated exclusively from imported frame files."""
    output.mkdir(parents=True, exist_ok=True)
    font_path = Path("C:/Windows/Fonts/msyh.ttc")
    font = ImageFont.truetype(str(font_path), 20) if font_path.exists() else ImageFont.load_default()
    for slug in APPROVED:
        sheet = Image.new("RGB", (1024, 48 + 8 * 286), "#ecf0ed")
        draw = ImageDraw.Draw(sheet)
        draw.text((12, 12), f"{slug} | 8 directions x 4 independent poses", fill="#213c32", font=font)
        for row, direction in enumerate(DIRECTIONS):
            for column in range(4):
                x, y = column * 256, 48 + row * 286
                draw.rectangle((x, y, x + 255, y + 285), outline="#bac7bd")
                draw.text((x + 10, y + 5), f"{direction} / {column + 1:02d}", fill="#213c32", font=font)
                with Image.open(project / RESOURCE_ROOT / slug / "walk" / direction / f"{column + 1:02d}.png") as frame:
                    preview = frame.resize((256, 256), Image.Resampling.LANCZOS)
                    sheet.paste(preview, (x, y + 30), preview)
        sheet.save(output / f"{slug}-four-frame-review.jpg", quality=94)


def main() -> None:
    project_default = Path(__file__).resolve().parents[1]
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path, default=project_default.parent / "image/qdao_chibi_roster_v11")
    parser.add_argument("--project", type=Path, default=project_default)
    parser.add_argument("--verify-only", action="store_true", help="Audit imported PNGs and .meta without modifying files.")
    parser.add_argument("--review-dir", type=Path, help="Write eight technical 4-column/8-row contact sheets.")
    args = parser.parse_args()
    source, project = args.source.resolve(), args.project.resolve()
    if not (project / "ProjectSettings/ProjectVersion.txt").is_file():
        raise ValueError(f"Not a Unity project: {project}")
    plan, evidence = source_plan(source)  # Validate everything before copying.
    if not args.verify_only:
        texture_template = (project / "Assets/Resources/World/Characters/QdaoHeadbandBoy/walk_N.png.meta").read_text(encoding="utf-8")
        folder_template = (project / "Assets/Resources/World/Characters/QdaoHeadbandBoy.meta").read_text(encoding="utf-8")
        root = project / RESOURCE_ROOT
        root.mkdir(parents=True, exist_ok=True)
        write_meta(root, project, folder_template)
        for slug in APPROVED:
            folder = root / slug
            folder.mkdir(exist_ok=True)
            write_meta(folder, project, folder_template)
        for item in plan:
            target = project / item["destination"]
            target.parent.mkdir(parents=True, exist_ok=True)
            for folder in reversed(target.parents):
                if folder == root or root in folder.parents:
                    write_meta(folder, project, folder_template)
            write_meta(target, project, texture_template)
            if not target.exists() or sha256(target) != item["sha256"]:
                if item["kind"] == "walk_frame":
                    with Image.open(source / item["crop_source"]) as strip:
                        strip.crop(item["crop_box"]).save(target, format="PNG")
                else:
                    shutil.copyfile(source / item["source"], target)
    idle_count = verify_destinations(project, plan)
    if not args.verify_only:
        report = {
            "status": "passed",
            "source_root": str(source),
            "resource_root": RESOURCE_ROOT.as_posix(),
            "characters": len(APPROVED),
            "portraits": 8,
            "direction_strips": 64,
            "distinct_frames_per_direction": 4,
            "movement_frames": 256,
            "independent_frame_pngs": 256,
            "total_pngs": 328,
            "frame_resource_path": "<slug>/walk/<DIR>/01.png through 04.png",
            "frame_export": "Exact 512px crop from the accepted direction strip; RGBA pixels match the accepted independent source frame.",
            "cell_size": [512, 512],
            "foot_origin_top_left": [256, 471],
            "reference_frame_duration_ms": 120,
            "idle": "Use a contact frame from each walk direction as a static fallback; no authored idle art in this pack.",
            "rejected_excluded": ["24_crane_hermit"],
            "checks": ["Explicit eight-character allowlist", "Passed source validation SHA256", "328 RGBA PNG sizes and real alpha", "72 byte-exact accepted portraits and strips", "256 newly recut frames match accepted RGBA pixels", "Four distinct pixel SHA256 values in every one of 64 directions", "328 export SHA256 matches", "409 unique asset and folder GUIDs", "Uncompressed, no mipmaps, bilinear, clamp, alpha import settings"],
            **evidence,
            "files": plan,
        }
        report_path = project / REPORT_PATH
        report_path.parent.mkdir(parents=True, exist_ok=True)
        report_path.write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    if args.review_dir:
        create_review_sheets(project, args.review_dir.resolve())
    print("PASS: 8 characters, 8 portraits, 64 strips, 256 independent frame PNGs; 328 export SHA256 matches, 64 four-pose uniqueness checks, and 409 valid .meta GUIDs.")
    if idle_count:
        print(f"PASS: {idle_count} optional dedicated idle PNGs form complete approved-character sets with valid dimensions, alpha, import settings and unique GUIDs.")
    print(f"Resources: {project / RESOURCE_ROOT}")
    if not args.verify_only:
        print(f"Report: {project / REPORT_PATH}")


if __name__ == "__main__":
    main()
