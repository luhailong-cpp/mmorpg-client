#!/usr/bin/env python3
"""Compare complete decoded RGBA rasters without resizing or tolerance.

Usage: python verify_pixels.py REFERENCE ACTUAL REPORT.json [--diff DIFF.png]
Exit codes: 0 = identical pixels, 1 = different pixels, 2 = invalid comparison.
An exact result does not establish capture provenance or functional acceptance.
Requires Pillow. No input image is modified.
"""

import argparse
from datetime import datetime, timezone
from functools import reduce
import hashlib
import io
import json
from pathlib import Path
import sys

try:
    from PIL import Image, ImageChops
except ImportError:
    Image = ImageChops = None


SCOPE = (
    "Complete decoded 8-bit RGBA raster comparison only. No resizing, alignment, "
    "masking, tolerance, color-profile conversion, or EXIF orientation correction. "
    "An exact result does not prove runtime capture provenance or functional acceptance."
)


def load_rgba(path, report, prefix):
    data = path.read_bytes()
    report[prefix + "SHA256"] = hashlib.sha256(data).hexdigest()
    if Image is None:
        raise ValueError("Pillow is required; use a Python environment containing Pillow.")
    # Pillow may reduce 16-bit RGB PNG to 8-bit on load. Refuse this lossy path.
    if data.startswith(b"\x89PNG\r\n\x1a\n") and len(data) > 24 and data[24] > 8:
        raise ValueError(f"Higher-than-8-bit PNG is unsupported: {path}")
    with Image.open(io.BytesIO(data)) as source:
        report[prefix + "Size"] = list(source.size)
        if getattr(source, "n_frames", 1) != 1:
            raise ValueError(f"Multi-frame images are unsupported: {path}")
        if source.mode not in {"1", "L", "LA", "P", "RGB", "RGBA"}:
            raise ValueError(f"Unsupported image mode {source.mode!r}: {path}")
        source.load()
        return source.convert("RGBA")


def verify(reference, actual, output, diff_path=None):
    reference, actual, output = (Path(p).resolve() for p in (reference, actual, output))
    diff_path = Path(diff_path).resolve() if diff_path is not None else None
    report = {
        "schemaVersion": 1,
        "checkedAtUtc": datetime.now(timezone.utc).isoformat(),
        "reference": str(reference),
        "actual": str(actual),
        "referenceSHA256": None,
        "actualSHA256": None,
        "referenceSize": None,
        "actualSize": None,
        "comparisonMode": "RGBA8",
        "status": "error",
        "changedPixelCount": None,
        "maxChannelDelta": None,
        "changedBoundingBoxXYXY": None,
        "scope": SCOPE,
    }
    outputs = [output] + ([diff_path] if diff_path is not None else [])
    try:
        # Avoid accidental replacement even through an existing hard-link alias.
        for target in outputs:
            for source in (reference, actual):
                if target == source or (target.exists() and source.exists() and target.samefile(source)):
                    raise ValueError("Output paths must not overwrite either input image.")
        if diff_path is not None and (
            diff_path == output
            or (diff_path.exists() and output.exists() and diff_path.samefile(output))
        ):
            raise ValueError("JSON and difference-image outputs must be different files.")
        expected = load_rgba(reference, report, "reference")
        observed = load_rgba(actual, report, "actual")
        if expected.size != observed.size:
            raise ValueError("Image dimensions differ; no resizing or alignment is permitted.")
        difference = ImageChops.difference(expected, observed)
        # Include all four channels: alpha-only and transparent-RGB changes count.
        channel_max = reduce(ImageChops.lighter, difference.split())
        histogram = channel_max.histogram()
        changed = expected.width * expected.height - histogram[0]
        report.update({
            "status": "exact" if changed == 0 else "different",
            "changedPixelCount": changed,
            "maxChannelDelta": channel_max.getextrema()[1],
            "changedBoundingBoxXYXY": channel_max.getbbox(),
        })
        if diff_path is not None:
            diff_path.parent.mkdir(parents=True, exist_ok=True)
            marked = Image.new("RGB", expected.size, "black")
            marked.paste((255, 0, 0), mask=channel_max.point(lambda value: 255 if value else 0))
            marked.save(diff_path, format="PNG")
            report["differenceImage"] = str(diff_path)
            report["differenceImageMeaning"] = "Red = any nonzero RGBA difference; black = identical."
    except Exception as error:
        report["status"] = "error"
        report["error"] = f"{type(error).__name__}: {error}"

    # Invalid destination aliases must never be written, including on errors.
    safe_output = all(
        output != source and not (output.exists() and source.exists() and output.samefile(source))
        for source in (reference, actual)
    )
    if safe_output:
        try:
            output.parent.mkdir(parents=True, exist_ok=True)
            output.write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
        except OSError as error:
            report["status"] = "error"
            report["error"] = f"Cannot write report: {error}"
    print(json.dumps(report, ensure_ascii=False))
    return {"exact": 0, "different": 1, "error": 2}[report["status"]]


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("reference", help="Approved original image")
    parser.add_argument("actual", help="Image to compare at the original dimensions")
    parser.add_argument("output", help="JSON report path")
    parser.add_argument("--diff", help="Optional PNG locating every differing pixel in red")
    args = parser.parse_args(argv)
    return verify(args.reference, args.actual, args.output, args.diff)


if __name__ == "__main__":
    sys.exit(main())
