"""Import the delivered town cast unchanged; keep placements independent of map art."""
import argparse
import hashlib
import json
import re
import shutil
import uuid
from pathlib import Path

from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
RESOURCE = ROOT / "Assets/Resources/World/Tianyong/Npcs"


def write_meta(path, template):
    meta = Path(str(path) + ".meta")
    if not meta.exists():
        guid = uuid.uuid5(uuid.NAMESPACE_URL, path.relative_to(ROOT).as_posix()).hex
        meta.write_text(re.sub(r"guid: [0-9a-f]+", "guid: " + guid, template), encoding="utf-8")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path, default=ROOT.parent / "image/designs/city-npcs-20260924")
    parser.add_argument("--positions", type=Path, default=RESOURCE / "placements.json")
    args = parser.parse_args()
    source_manifest = json.loads((args.source / "manifest.json").read_text(encoding="utf-8-sig"))
    positions_data = json.loads(args.positions.read_text(encoding="utf-8-sig"))
    positions = positions_data if isinstance(positions_data, list) else positions_data["entries"]
    positions = {str(p["id"]).zfill(2): p for p in positions}
    assert len(positions) == source_manifest["npcCount"] == 23
    sprite_dir = RESOURCE / "Sprites"
    sprite_dir.mkdir(parents=True, exist_ok=True)
    folder_meta = (ROOT / "Assets/Resources/World/Tianyong.meta").read_text(encoding="utf-8")
    texture_meta = (ROOT / "Assets/Resources/World/Characters/QdaoHeadbandBoy/idle_S.png.meta").read_text(encoding="utf-8")
    for folder in (RESOURCE, sprite_dir):
        write_meta(folder, folder_meta)
    entries, sources = [], []
    for npc in source_manifest["entries"]:
        export = next(e for e in npc["export"] if e["size"] == 1024)
        source = args.source / export["file"]
        sha = hashlib.sha256(source.read_bytes()).hexdigest()
        assert sha == export["sha256"], f"Changed source: {source}"
        with Image.open(source) as image:
            assert image.mode == "RGBA" and image.size == (1024, 1024)
            assert image.getchannel("A").getextrema() == (0, 255)
        dest = sprite_dir / source.name
        shutil.copyfile(source, dest)
        write_meta(dest, texture_meta)
        p = positions[npc["id"]]
        # Scale children and large divine figures relative to the existing player.
        height = {"06": 8.5, "14": 8.2, "15": 6.8, "20": 6.2}.get(npc["id"], 7.6)
        bbox = export["visibleBBox"]
        entry = dict(id=npc["id"], displayName=p["displayName"],
                     spriteResource="World/Tianyong/Npcs/Sprites/" + source.stem,
                     x=p["x"], z=p["z"], visibleHeight=p.get("visibleHeight", height),
                     pivotX=p.get("pivotX", 0.5), pivotY=p.get("pivotY", 0.12),
                     visibleHeightFraction=(bbox[3] - bbox[1]) / 1024,
                     location=p["location"])
        entries.append(entry)
        sources.append(dict(id=npc["id"], name=npc["name"], source=source.as_posix(),
                            destination=dest.relative_to(ROOT).as_posix(), sha256=sha,
                            sourceGenerationRecord=(args.source / npc["generationRecord"]).as_posix(),
                            referenceCompleteness=npc["referenceCompleteness"],
                            actualModel=npc["actualModel"], actualQuality=npc["actualQuality"]))
    manifest = dict(schemaVersion=1, coordinateSystem="Unity X/Z feet points, current painted city",
                    scope="23 static visual NPCs; independent of ground, navigation and server actors",
                    entries=entries)
    placements = RESOURCE / "placements.json"
    placements.write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    write_meta(placements, "fileFormatVersion: 2\nguid: 00000000000000000000000000000000\nTextScriptImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n")
    records = ROOT / "Docs/CityNpcs.sources.json"
    records.write_text(json.dumps(dict(npcCount=23, imageOperation="byte-identical copy", entries=sources),
                                  ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"Imported {len(entries)} unchanged transparent NPCs; placements: {placements}")


if __name__ == "__main__":
    main()
