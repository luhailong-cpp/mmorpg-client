"""Validate one complete runtime acceptance run, then package its review evidence."""
import argparse
import collections
import csv
import hashlib
import json
import math
import os
import re
import shutil
from pathlib import Path

from PIL import Image, ImageDraw, ImageStat

DIRECTIONS = ("N", "NE", "E", "SE", "S", "SW", "W", "NW")
EDGES = ("w", "e", "s", "n", "sw", "se", "nw", "ne")
ZOOM_SAMPLES = ("base", "out_f1", "out_006", "out_settled", "in_f1", "in_006", "in_settled")
NAMEPLATE_CASES = ("short", "long_cjk", "empty", "disabled", "restored")
CSV_COLUMNS = (
    "frame,rendered_frame,offscreen_render,mode,realtime,phase,actual_size,requested_size,aspect,focus_x,focus_z,"
    "camera_x,camera_y,camera_z,map_x_min,map_z_min,map_x_max,map_z_max,"
    "view_x_min,view_z_min,view_x_max,view_z_max,overflow,pass"
).split(",")


class ValidationError(ValueError):
    pass


def require(condition, message):
    if not condition:
        raise ValidationError(message)


def same_path(left, right):
    return os.path.normcase(str(Path(left).resolve())) == os.path.normcase(str(Path(right).resolve()))


def required_shots():
    names = {"00_runtime_hud", "09_zoom_restored", "07_occluder_west_before_foreground"}
    names.update("00_nameplate_" + case for case in NAMEPLATE_CASES)
    names.update(f"03_stop_{direction}_{sample}" for direction in DIRECTIONS for sample in ("a", "b"))
    names.update(f"02_run_{direction}_b" for direction in DIRECTIONS)
    names.update(f"09_zoom_{edge}_{sample}" for edge in EDGES for sample in ZOOM_SAMPLES)
    names.update(f"10_behind_prop_{index}" for index in range(6))
    names.update(("01_idle_a", "01_idle_b", "01_idle_c", "04_click_a", "04_click_arrive_b",
                  "05_click_roof_arrive", "06_restart_stopped", "06_restart_go_b",
                  "07_occluder_west_behind", "07_occluder_south_front", "07_occluder_east",
                  "07_near_pillar", "07_temple_front", "08_edge_w", "08_edge_w_arrive",
                  "08_edge_e", "08_edge_s", "08_edge_n"))
    return names


def expected_phase(name):
    if name == "09_zoom_restored" or name.startswith("10_behind_prop_"):
        return "zoom_restored"
    match = re.fullmatch(r"09_zoom_(w|e|s|n|sw|se|nw|ne)_(base|out_.+|in_.+)", name)
    if match:
        return "zoom_" + match[1] + "_" + match[2].split("_")[0]
    return "startup"


def validate_run(shots, log_path):
    log = log_path.read_text(encoding="utf-8-sig", errors="strict")
    drive = [match[1].strip() for line in log.splitlines()
             if (match := re.search(r"\[SandboxDrive\] (.*)", line))]
    results = [line for line in drive if line.startswith("RESULT=")]
    quits = [line for line in drive if line.startswith("quit exit_code=")]
    require(len(results) == 1 and len(quits) == 1,
            "Expected exactly one completed RESULT and quit; log may be truncated or contain multiple runs")
    require(len(drive) >= 2 and drive[-2:] == [results[0], quits[0]], "RESULT and quit are not the final drive events")
    result = re.fullmatch(
        r"RESULT=PASS shots=(\d+)/(\d+) cameraSamples=(\d+) cameraFailures=0 failures=0 "
        r"offscreenRenders=(\d+) capture=offscreen_render zoomCases=8/8 behindCases=6/6 dir=(.+)", results[0])
    require(result is not None, "Final RESULT is not a complete passing acceptance run: " + results[0])
    require(same_path(result[5], shots), "Final RESULT directory does not match --shots")
    require(int(result[4]) == int(result[3]), "Completed offscreen renders differ from camera sample count")
    quit_match = re.fullmatch(r"quit exit_code=0 shots=(.+)", quits[0])
    require(quit_match is not None, "Final quit did not report exit_code=0")
    require(not re.search(r"\[(?:SandboxDrive|SandboxHud)\].*(?:RESULT=FAIL|verification failure:|nameplate=FAIL)", log),
            "Runtime failure appears in log")
    require(not re.search(r"NullReferenceException|MissingReferenceException|Unhandled Exception|error CS\d+", log),
            "Runtime exception or compiler error appears in log")
    require(not re.search(r"ReadPixels[^\r\n]*(?:system frame buffer|not inside drawing|outside)", log),
            "ReadPixels framebuffer error: screenshots are not valid rendered evidence")
    begins = [re.fullmatch(r"begin screen=(\d+)x(\d+) shotDir=(.+) spawn=\(.+\)", line)
              for line in drive if line.startswith("begin ")]
    require(len(begins) == 1 and begins[0] is not None, "Missing or duplicated run metadata")
    begin = begins[0]
    width, height = int(begin[1]), int(begin[2])
    require(width > 0 and height > 0 and same_path(begin[3], shots), "Run dimensions or screenshot directory mismatch")
    hud_lines = [line for line in log.splitlines() if "[SandboxHud]" in line]
    require(any(f"layout=PASS screen={width}x{height} " in line for line in hud_lines), "HUD layout acceptance missing")
    require(any("capture=offscreen_actual_canvas " in line and f"target={width}x{height}" in line for line in hud_lines),
            "Actual HUD was not routed through the offscreen capture camera")
    require(sum(line.startswith(f"camera verification mode=offscreen_render size={width}x{height} ")
                for line in drive) == 1, "Offscreen camera render metadata missing or duplicated")
    require(any("nameplate=PASS cases=short,long_cjk,empty,disabled,restored" in line for line in hud_lines),
            "Nameplate acceptance is incomplete")
    for case in NAMEPLATE_CASES:
        require(sum(f"nameplate_case={case} RESULT=PASS " in line for line in hud_lines) == 1,
                f"Missing or duplicate nameplate case: {case}")

    trace_path = shots / "camera-frames.csv"
    with trace_path.open(encoding="utf-8-sig", newline="") as handle:
        reader = csv.DictReader(handle)
        require(reader.fieldnames == CSV_COLUMNS, "Camera CSV schema mismatch")
        rows = list(reader)
    require(len(rows) == int(result[3]) and bool(rows), "CSV sample count does not match final cameraSamples")
    phases = collections.defaultdict(list)
    by_frame = {}
    previous_frame = None
    previous_time = None
    for sample_index, row in enumerate(rows):
        require(None not in row and all(value is not None for value in row.values()), "Malformed or truncated CSV row")
        frame = int(row["frame"])
        values = {key: float(row[key]) for key in CSV_COLUMNS
                  if key not in ("frame", "rendered_frame", "offscreen_render", "mode", "phase", "pass")}
        require(row["mode"] == "offscreen_render" and int(row["offscreen_render"]) == sample_index + 1,
                f"Missing or out-of-order completed offscreen render at frame {frame}")
        require(int(row["rendered_frame"]) >= 0, f"Invalid informational rendered_frame at frame {frame}")
        require(all(math.isfinite(value) for value in values.values()), f"Non-finite camera data at frame {frame}")
        require(previous_frame is None or frame == previous_frame + 1, f"Camera frame gap or duplicate: {previous_frame} -> {frame}")
        require(previous_time is None or values["realtime"] >= previous_time, f"Camera time moved backwards at frame {frame}")
        require(row["pass"] == "1", f"Camera failure at frame {frame}")
        require(values["actual_size"] > 0 and values["requested_size"] > 0,
                f"Invalid camera size at frame {frame}")
        require(abs(values["aspect"] - width / height) < 0.00001, f"Camera aspect differs from run dimensions at frame {frame}")
        require(values["map_x_min"] < values["map_x_max"] and values["map_z_min"] < values["map_z_max"]
                and values["view_x_min"] < values["view_x_max"] and values["view_z_min"] < values["view_z_max"],
                f"Invalid map or viewport bounds at frame {frame}")
        overflow = max(0, values["map_x_min"] - values["view_x_min"], values["view_x_max"] - values["map_x_max"],
                       values["map_z_min"] - values["view_z_min"], values["view_z_max"] - values["map_z_max"])
        require(0 <= values["overflow"] <= 0.001 and overflow <= 0.0011
                and abs(overflow - values["overflow"]) <= 0.0001, f"Camera overflow contradicts pass at frame {frame}")
        by_frame[frame] = row
        phases[row["phase"]].append(row)
        previous_frame, previous_time = frame, values["realtime"]
    expected_phases = {"startup", "zoom_restored"} | {f"zoom_{edge}_{phase}" for edge in EDGES for phase in ("base", "out", "in")}
    require(set(phases) == expected_phases, "Camera phase coverage mismatch: missing=" + str(sorted(expected_phases - set(phases)))
            + " extra=" + str(sorted(set(phases) - expected_phases)))
    for phase, samples in phases.items():
        requested = 30 if phase.endswith("_out") else 12 if phase.endswith(("_base", "_in")) else 27
        require(all(abs(float(row["requested_size"]) - requested) < 0.00001 for row in samples),
                "Camera requested zoom does not match phase: " + phase)
        actual = [float(row["actual_size"]) for row in samples]
        if phase.endswith("_base"):
            require(all(abs(size - 12.0) <= 0.05 for size in actual),
                    "Camera actual base zoom is not 12: " + phase)
        elif phase.endswith(("_out", "_in")):
            require(abs(actual[-1] - requested) <= 0.05,
                    f"Camera actual zoom did not settle at {requested}: {phase} last={actual[-1]}")
            increasing = phase.endswith("_out")
            require(all((right >= left - 0.0001 if increasing else right <= left + 0.0001)
                        for left, right in zip(actual, actual[1:])),
                    "Camera actual zoom moved in the wrong direction: " + phase)
            # Capture must include real easing, not only a renamed frozen view
            # or a target endpoint. One world unit is well above float noise.
            require(max(actual) - min(actual) >= 1.0,
                    "Camera actual zoom span is insufficient: " + phase)

    shot_events = []
    for line in drive:
        if not line.startswith("shot="):
            continue
        match = re.fullmatch(r"shot=(\S+) file=(\S+) frame=(\d+) renderedFrame=(\d+) "
                             r"offscreenRender=(\d+) capture=offscreen_render realtime=\S+ "
                             r".*actualSize=(\S+) requestedSize=(\S+)", line)
        require(match is not None, "Malformed screenshot event: " + line)
        name, filename, frame = match[1], match[2], int(match[3])
        require(filename == f"{len(shot_events):02d}_{name}.png", "Screenshot filename or sequence mismatch: " + filename)
        require(frame in by_frame, "Screenshot has no corresponding camera sample: " + name)
        row = by_frame[frame]
        require(row["phase"] == expected_phase(name), "Screenshot phase mismatch: " + name)
        require(int(match[4]) == int(row["rendered_frame"]) and int(match[5]) == int(row["offscreen_render"]),
                "Screenshot does not reference the matching completed offscreen render: " + name)
        require(abs(float(match[6]) - float(row["actual_size"])) < 0.00001
                and abs(float(match[7]) - float(row["requested_size"])) < 0.00001,
                "Screenshot camera settings differ from CSV: " + name)
        shot_events.append((name, filename, frame))
    count = len(shot_events)
    require(count == int(result[1]) == int(result[2]) and count > 0, "Screenshot event count differs from final shots count")
    names = [event[0] for event in shot_events]
    require(len(names) == len(set(names)), "Duplicate screenshot names")
    require(names == quit_match[1].split(","), "Final quit screenshot list differs from screenshot events")
    require(required_shots() <= set(names), "Required screenshots missing: " + str(sorted(required_shots() - set(names))))
    require(all(left[2] < right[2] for left, right in zip(shot_events, shot_events[1:])), "Screenshot frame order is not increasing")
    candidates = {path.name: path for path in shots.glob("*.png")}
    require(set(candidates) == {event[1] for event in shot_events}, "PNG files differ from the runtime screenshot list (missing or stale files)")
    for filename, path in candidates.items():
        with path.open("rb") as handle:
            require(handle.read(8) == b"\x89PNG\r\n\x1a\n", "Invalid PNG signature: " + filename)
        with Image.open(path) as image:
            require(image.format == "PNG" and image.size == (width, height), "PNG dimensions differ from run: " + filename)
            image.verify()
        with Image.open(path) as image:
            image.load()  # Decode all image data; a valid header alone is insufficient.
            sample = image.convert("RGB").resize((128, 72), Image.Resampling.NEAREST)
            colors = sample.getcolors(128 * 72)
            uniform_fraction = max(count for count, _ in colors) / (128 * 72)
            require(uniform_fraction < 0.90 and max(ImageStat.Stat(sample).stddev) > 2.0,
                    f"Blank or nearly uniform PNG: {filename} dominant_color={uniform_fraction:.1%}")

    summary = {
        "validation": "PASS", "source_shots": str(shots.resolve()), "source_log": str(log_path.resolve()),
        "samples": len(rows), "offscreen_renders": len(rows), "capture_mode": "offscreen_render", "first_frame": int(rows[0]["frame"]), "last_frame": int(rows[-1]["frame"]),
        "failed_frames": 0, "max_overflow_world_units": max(float(row["overflow"]) for row in rows),
        "aspects": sorted({round(float(row["aspect"]), 5) for row in rows}),
        "screen": [width, height], "png_count": count, "runtime_pass": True,
        "png_validation": "Every logged PNG decoded at the declared resolution; blank and nearly uniform images rejected. This is not a pixel-wise map-edge leak detector.",
        "camera_geometry_only": True,
        "scope": "Every explicit offscreen Camera.Render in the contiguous captured Unity frame interval is matched to a viewport-versus-painting bounds check, tolerance 0.001 world units. PNGs sample those offscreen renders and include the actual production HUD canvases; this does not certify OS window presentation or arbitrary uncaptured rendering configurations.",
        "phases": {name: {"samples": len(items), "min_actual_size": min(float(row["actual_size"]) for row in items),
                          "max_actual_size": max(float(row["actual_size"]) for row in items), "failed_frames": 0}
                   for name, items in phases.items()},
        "hud_checks": hud_lines,
    }
    return summary, {name: candidates[filename] for name, filename, _ in shot_events}


def package_evidence(shots, log_path, output, summary, images):
    shutil.copy2(shots / "camera-frames.csv", output / "camera-frames.csv")
    shutil.copy2(log_path, output / "player.log")
    selected = [path for name, path in images.items() if any(token in name for token in (
        "00_runtime_hud", "nameplate_long", "nameplate_restored", "03_stop_W_b", "04_click_a",
        "07_occluder_", "10_behind_prop_", "zoom_ne_out_006", "zoom_w_out_f1"))]
    manifest = []
    for path in selected:
        destination = output / path.name
        shutil.copy2(path, destination)
        with Image.open(path) as source:
            image = source.convert("RGB")
            image.thumbnail((1280, 720))
            image.save(destination.with_suffix(".jpg"), quality=86, optimize=True)
        manifest.append({"file": path.name, "sha256": hashlib.sha256(path.read_bytes()).hexdigest(), "bytes": path.stat().st_size})
    (output / "evidence-sha256.json").write_text(json.dumps(manifest, indent=2), encoding="utf-8")
    panel = Image.new("RGB", (4 * 320, 2 * 390), (235, 232, 222))
    draw = ImageDraw.Draw(panel)
    for index, direction in enumerate(DIRECTIONS):
        with Image.open(images[f"03_stop_{direction}_b"]) as source:
            image = source.convert("RGB")
            width, height = image.size
            crop = image.crop((int(width * .375), int(height * .20), int(width * .625), int(height * .73)))
            crop.thumbnail((320, 360))
        x, y = (index % 4) * 320, (index // 4) * 390
        panel.paste(crop, (x + (320 - crop.width) // 2, y + 28))
        draw.text((x + 12, y + 9), direction + " / dedicated idle", fill=(35, 45, 40))
    panel.save(output / "idle-directions-review.jpg", quality=90)
    # Publish the success marker only after both validation and packaging succeed.
    temporary = output / "coverage-summary.json.tmp"
    temporary.write_text(json.dumps(summary, ensure_ascii=False, indent=2, allow_nan=False), encoding="utf-8")
    temporary.replace(output / "coverage-summary.json")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--shots", required=True, type=Path)
    parser.add_argument("--log", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    args = parser.parse_args()
    require(not same_path(args.output, args.shots) and not same_path(args.output / "player.log", args.log),
            "Output must be separate from original evidence")
    args.output.mkdir(parents=True, exist_ok=True)
    summary_path = args.output / "coverage-summary.json"
    diagnostic = args.output / "validation-failure.json"
    summary_path.unlink(missing_ok=True)  # A failed rerun must not leave an older success marker.
    (args.output / "coverage-summary.json.tmp").unlink(missing_ok=True)
    diagnostic.unlink(missing_ok=True)
    try:
        summary, images = validate_run(args.shots, args.log)
        package_evidence(args.shots, args.log, args.output, summary, images)
    except (OSError, ValueError, KeyError, OverflowError, csv.Error) as error:
        summary_path.unlink(missing_ok=True)
        diagnostic.write_text(json.dumps({"validation": "FAIL", "reason": str(error),
                                         "source_shots": str(args.shots.resolve()), "source_log": str(args.log.resolve())},
                                        ensure_ascii=False, indent=2), encoding="utf-8")
        raise SystemExit("Validation failed: " + str(error))
    print(json.dumps({key: value for key, value in summary.items() if key not in ("phases", "hud_checks")}, indent=2))


if __name__ == "__main__":
    main()
