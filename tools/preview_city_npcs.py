"""Compose a placement-review image from production tiles and unchanged NPC textures.

This is a deterministic layout preview, not a Unity screenshot. The real engine
uses the same feet, scale and names, with its own shadows, text and foregrounds.
"""
import argparse
import base64
import hashlib
import json
import math
import re
from collections import deque
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[1]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, default=ROOT / "Docs/VerificationEvidence/city-npcs-20261009")
    args = parser.parse_args()
    args.output.mkdir(parents=True, exist_ok=True)
    path = ROOT / "Assets/Resources/World/Tianyong/Npcs/placements.json"
    manifest = json.loads(path.read_text(encoding="utf-8-sig"))
    entries = manifest["entries"]
    tile_root = ROOT / "Assets/Resources/World/Tianyong/SceneTiles6x6/Tiles"
    base = Image.new("RGB", (6144, 6144))
    map_hashes = []
    for row in range(6):
        for col in range(6):
            tile = tile_root / f"tianyong_r{row+1:02}_c{col+1:02}.png"
            with Image.open(tile) as image:
                assert image.size == (1024, 1024)
                base.paste(image.convert("RGB"), (col * 1024, row * 1024))
            map_hashes.append(dict(file=tile.relative_to(ROOT).as_posix(), sha256=hashlib.sha256(tile.read_bytes()).hexdigest()))
    canvas = base.convert("RGBA")
    ppu = 20.48
    sprites, names = [], []
    for npc in sorted(entries, key=lambda n: -n["z"]):
        with Image.open(ROOT / "Assets/Resources" / (npc["spriteResource"] + ".png")) as source:
            frame_size = round(npc["visibleHeight"] / npc["visibleHeightFraction"] * ppu)
            body = source.resize((frame_size, frame_size), Image.Resampling.LANCZOS)
        x, y = (npc["x"] - 50) * ppu, (300 - npc["z"]) * ppu
        left = round(x - frame_size * npc["pivotX"])
        top = round(y - frame_size * (1 - npc["pivotY"]))
        canvas.alpha_composite(body, (left, top))
        names.append((x, y + 1.9 * ppu, npc["displayName"]))
        bounds = body.getchannel("A").getbbox()
        sprites.append(dict(id=npc["id"], bounds=[left + bounds[0], top + bounds[1], left + bounds[2], top + bounds[3]]))
    font = ImageFont.truetype("C:/Windows/Fonts/simkai.ttf", 26)
    draw = ImageDraw.Draw(canvas)
    for x, y, name in names:
        box = draw.textbbox((x, y), name, font=font, anchor="mm")
        draw.rounded_rectangle((box[0]-7, box[1]-3, box[2]+7, box[3]+3), radius=4, fill=(9,12,11,220))
        draw.text((x, y), name, font=font, anchor="mm", fill=(255,233,184), stroke_width=1, stroke_fill=(12,16,14))
    overview = canvas.convert("RGB").resize((2048, 2048), Image.Resampling.LANCZOS)
    overview.save(args.output / "npc-layout-overview.jpg", quality=94)
    for name, box in {
        "west-market": (200, 1870, 1950, 3700),
        "north-districts": (1300, 650, 5650, 2350),
        "south-gardens": (550, 3700, 5700, 5200),
    }.items():
        view = canvas.crop(box).convert("RGB")
        view.thumbnail((2000, 1500), Image.Resampling.LANCZOS)
        view.save(args.output / (name + ".jpg"), quality=95)

    source = ROOT / "Assets/Scripts/World/Tianyong/TianyongPaintedCity.cs"
    code = source.read_text(encoding="utf-8-sig")
    encoded = "".join(re.findall(r'"([A-Za-z0-9+/=]+)"', code.split("private const string WalkMaskBase64", 1)[1]))
    mask = base64.b64decode(encoded)
    def walkable(cell):
        wx, wz = 2 * cell[0] + 1, 2 * cell[1] + 1
        cx, cy = math.floor((wx - 50)/2), math.floor((300 - wz)/2)
        if not 0 <= cx < 150 or not 0 <= cy < 150:
            return False
        index = cy * 150 + cx
        return bool(mask[index >> 3] & (0x80 >> (index & 7)))
    seen = {(100, 90)}
    queue = deque(seen)
    assert walkable((100, 90))
    while queue:
        x, z = queue.popleft()
        for dx, dz in ((1,0),(-1,0),(0,1),(0,-1),(1,1),(1,-1),(-1,1),(-1,-1)):
            nxt = x+dx, z+dz
            if nxt in seen or not walkable(nxt):
                continue
            if dx and dz and (not walkable((x+dx,z)) or not walkable((x,z+dz))):
                continue
            seen.add(nxt)
            queue.append(nxt)
    checks = [dict(id=n["id"], displayName=n["displayName"], x=n["x"], z=n["z"],
                   reachable=(math.floor(n["x"]/2), math.floor(n["z"]/2)) in seen) for n in entries]
    intersections = []
    for i, a in enumerate(sprites):
        for b in sprites[i+1:]:
            aa, bb = a["bounds"], b["bounds"]
            if max(aa[0],bb[0]) < min(aa[2],bb[2]) and max(aa[1],bb[1]) < min(aa[3],bb[3]):
                intersections.append([a["id"],b["id"]])
    report = dict(previewType="Deterministic composite, not an engine screenshot", npcCount=len(entries),
                  allReachable=all(c["reachable"] for c in checks), positions=checks,
                  bodyBoundingBoxIntersections=intersections, mapTiles=map_hashes,
                  placementSha256=hashlib.sha256(path.read_bytes()).hexdigest(),
                  navigationSha256=hashlib.sha256(source.read_bytes()).hexdigest())
    (args.output / "layout-validation.json").write_text(json.dumps(report, ensure_ascii=False, indent=2)+"\n", encoding="utf-8")
    print(json.dumps({"npcCount":len(entries), "allReachable":report["allReachable"], "bodyIntersections":intersections}))
    assert report["allReachable"]


if __name__ == "__main__":
    main()
