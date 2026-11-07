"""Import complete formal festival batches and verify them without an art checkout.

The approved 1024 PNGs are copied exactly. The fallback layout is assembled from
those same runtime tiles, while highest-native PNGs remain in the art repository.
Only JSON provenance is frozen in Docs; normal artwork is never invented.
"""
from __future__ import annotations

import argparse
import base64
from collections import defaultdict
import hashlib
import io
import json
import math
from pathlib import Path
import re
import uuid

from PIL import Image

PROJECT = Path(__file__).resolve().parents[1]
DELIVERY = Path("Assets/Resources/World/QdaoMaps20261102")
EVIDENCE = Path("Docs/VerificationEvidence/festival-maps-20261106")
RESOURCE_ROOT = "World/QdaoMaps20261102/"
REVISION = "festival20261106"
SCOPE = {("21003", "spring"): (3, 3), ("21003", "mid-autumn"): (3, 3),
         ("28007", "spring"): (3, 2), ("35004", "spring"): (3, 2),
         ("35004", "mid-autumn"): (3, 2)}
SHA = re.compile(r"^[0-9a-f]{64}$")


def require(value, message):
    if not value:
        raise ValueError(message)


def digest(data):
    return hashlib.sha256(data).hexdigest()


def json_bytes(value):
    return (json.dumps(value, ensure_ascii=False, indent=2) + "\n").encode("utf-8")


def read_json(path):
    return json.loads(Path(path).read_bytes().decode("utf-8-sig"))


def normalized_appearance(value):
    return "mid-autumn" if value == "midautumn" else value


def descriptor_path(value):
    return value.get("path") or value.get("file")


def checked_bytes(value):
    data = Path(descriptor_path(value)).read_bytes()
    require(digest(data) == value["sha256"] and len(data) == value["bytes"],
            f"Source bytes changed: {descriptor_path(value)}")
    return data


def inspect_png(data, dimensions, label):
    try:
        with Image.open(io.BytesIO(data)) as image:
            require(image.format == "PNG" and list(image.size) == list(dimensions), f"{label}: PNG dimensions")
            image.verify()
        with Image.open(io.BytesIO(data)) as image:
            image.load()
            require(image.mode in ("RGB", "RGBA"), f"{label}: RGB/RGBA PNG required")
            if image.mode == "RGBA":
                require(image.getextrema()[3] == (255, 255), f"{label}: nonopaque map PNG")
    except (OSError, SyntaxError) as error:
        raise ValueError(f"{label}: PNG decode failed") from error


def guid(path, project):
    return uuid.uuid5(uuid.NAMESPACE_URL,
                      "mmorpg-client/festival-maps/" + path.relative_to(project).as_posix()).hex


def meta_bytes(path, project):
    header = f"fileFormatVersion: 2\nguid: {guid(path, project)}\n"
    if path.is_dir():
        return (header + "folderAsset: yes\nDefaultImporter:\n  externalObjects: {}\n").encode()
    if path.suffix != ".png":
        return (header + "TextScriptImporter:\n  externalObjects: {}\n").encode()
    limit = 8192 if path.name == "layout.png" else 1024
    body = ("TextureImporter:\n  externalObjects: {}\n  serializedVersion: 13\n"
            "  mipmaps:\n    enableMipMap: 0\n    sRGBTexture: 1\n"
            "  isReadable: 0\n  textureType: 0\n  textureShape: 1\n"
            f"  maxTextureSize: {limit}\n  nPOTScale: 0\n"
            "  textureSettings:\n    serializedVersion: 2\n    filterMode: 1\n"
            "    aniso: 1\n    wrapU: 1\n    wrapV: 1\n    wrapW: 1\n"
            "  textureCompression: 0\n  alphaIsTransparency: 0\n  platformSettings:\n"
            "  - serializedVersion: 3\n    buildTarget: DefaultTexturePlatform\n"
            f"    maxTextureSize: {limit}\n    resizeAlgorithm: 0\n    textureFormat: 3\n"
            "    textureCompression: 0\n    compressionQuality: 100\n    overridden: 1\n")
    return (header + body).encode()


def write_meta(path, project):
    Path(str(path) + ".meta").write_bytes(meta_bytes(path, project))


def asset_directory(path, project):
    root = project / DELIVERY
    require(path == root or root in path.parents, "Asset output escaped festival root")
    pending = []
    parent = path
    while parent != root.parent:
        pending.append(parent)
        parent = parent.parent
    for folder in reversed(pending):
        folder.mkdir(exist_ok=True)
        if not Path(str(folder) + ".meta").exists():
            write_meta(folder, project)


def write_asset(path, data, project):
    asset_directory(path.parent, project)
    path.write_bytes(data)
    write_meta(path, project)


def tile_id(row, column):
    return f"r{row:02d}_c{column:02d}"


class BatchConfig:
    """An explicit frozen scope; the default preserves the original five deliveries."""

    def __init__(self, document=None):
        self.document = document
        if document is None:
            self.scope, self.evidence, self.revision = dict(SCOPE), EVIDENCE, REVISION
            return
        require(document.get("schemaVersion") == 1, "Invalid festival batch config")
        revision = document.get("revision", "")
        require(isinstance(revision, str) and re.fullmatch(r"[A-Za-z0-9_-]+", revision), "Invalid batch revision")
        directory = document.get("evidenceDirectory", "")
        require(isinstance(directory, str) and "\\" not in directory, "Invalid batch evidence directory")
        evidence = Path(directory)
        require(not evidence.is_absolute() and evidence.parts[:2] == ("Docs", "VerificationEvidence")
                and len(evidence.parts) >= 3 and ".." not in evidence.parts,
                "Batch evidence must stay below Docs/VerificationEvidence")
        scope = {}
        entries = document.get("scope")
        require(isinstance(entries, list) and entries, "A nonempty explicit batch scope is required")
        scenes = {}
        for entry in entries:
            require(isinstance(entry, dict), "Invalid batch scope entry")
            map_id, appearance = str(entry.get("mapId", "")), normalized_appearance(entry.get("appearance"))
            columns, rows = entry.get("columns"), entry.get("rows")
            require(map_id.isascii() and map_id.isdigit() and 0 < int(map_id) <= 0xffffffff
                    and appearance in ("normal", "spring", "lantern", "mid-autumn"), "Invalid batch identity")
            require(type(columns) is int and type(rows) is int and columns > 0 and rows > 0
                    and columns * rows <= 128 and max(columns, rows) * 1024 <= 8192, "Invalid batch grid")
            identity = map_id, appearance
            require(identity not in scope, "Duplicate batch appearance")
            require(int(map_id) not in scenes or scenes[int(map_id)] == map_id, "Duplicate numeric map identity")
            scenes[int(map_id)] = map_id
            scope[identity] = columns, rows
        self.scope, self.evidence, self.revision = scope, evidence, revision


def resolve_batch(value=None):
    if isinstance(value, BatchConfig):
        return value
    return BatchConfig(read_json(value) if value is not None and not isinstance(value, dict) else value)


def default_appearance(appearances):
    return next(value for value in ("normal", "spring", "mid-autumn", "lantern") if value in appearances)


def validate_selection(selection, batch=None):
    scope = resolve_batch(batch).scope
    require(selection.get("schemaVersion") == 1 and isinstance(selection.get("sets"), list), "Invalid frozen selection")
    seen = set()
    for item in selection["sets"]:
        identity = str(item["mapId"]), normalized_appearance(item["appearance"])
        require(identity in scope and identity not in seen, "Unknown or duplicate festival appearance")
        seen.add(identity)
        columns, rows = scope[identity]
        require((item["grid"]["columns"], item["grid"]["rows"]) == (columns, rows)
                and item["grid"]["canvasPixels"] == [columns * 1024, rows * 1024], "Frozen grid mismatch")
        require(item.get("formalArtAccepted") is True and len(item["tiles"]) == columns * rows,
                "Only complete formal festival appearances are eligible")
        occupied = set()
        for tile in item["tiles"]:
            row, column = tile["row"], tile["column"]
            require(type(row) is int and type(column) is int and 1 <= row <= rows and 1 <= column <= columns,
                    "Frozen tile coordinates out of range")
            require((row, column) not in occupied and tile["tile"] == tile_id(row, column), "Duplicate frozen tile")
            occupied.add((row, column))
            require(tile["imageRectHalfOpen"] == [(column - 1) * 1024, (row - 1) * 1024, column * 1024, row * 1024],
                    "Frozen tile image rectangle mismatch")
            require(tile.get("formalArtAccepted") is True and tile["formalAcceptance"].get("formalAccepted") is True,
                    "Tile has no formal art acceptance")
            for key, dimensions in (("native", [1254, 1254]), ("runtime", [1024, 1024])):
                value = tile[key]
                require(SHA.fullmatch(value["sha256"]) and value["bytes"] > 0 and value["pixels"] == dimensions,
                        "Frozen PNG provenance invalid")
    require(seen == set(scope), "Frozen selection must contain exactly the authorized batch appearances")


def acceptance_identity(document, native_sha, tile=None, appearance=None):
    native = document.get("native") or document.get("source")
    bound = isinstance(native, dict) and native.get("sha256") == native_sha
    if not bound and tile is not None:
        cells = document.get("inputCells", [])
        bound = isinstance(cells, list) and sum(isinstance(cell, dict) and cell.get("id") == tile
                                               and cell.get("sha256") == native_sha for cell in cells) == 1
    require(bound,
            "Formal acceptance is bound to another native PNG")
    require(document.get("formalAccepted") is True or document.get("formalCredit") == 1,
            "Formal acceptance document is not a PASS")
    if tile is not None and (document.get("coordinate") or document.get("tile")):
        require((document.get("coordinate") or document.get("tile")) == tile, "Formal acceptance coordinate mismatch")
    if appearance is not None and document.get("appearance"):
        require(normalized_appearance(document["appearance"]) == normalized_appearance(appearance),
                "Formal acceptance appearance mismatch")


def capture_evidence(item, tile, selection_sha):
    documents = []
    roles = {role: tile.get(role) for role in
             ("formalAcceptance", "generationRecord", "runtimeGenerationRecord", "localQA", "runtimeUseSizeReview", "selectionEvidence")}
    reuse = tile.get("reuse", {})
    if reuse.get("reusedNative"):
        require(reuse.get("sameGridAndCoordinate") is True and reuse.get("newPhysicalCalls") == 0,
                "Reuse must bind the original coordinate without a new image call")
        require(reuse.get("source", {}).get("sha256") == tile["native"]["sha256"], "Reuse native source mismatch")
        require(reuse.get("evidence") and reuse.get("sourceFormalAcceptance"), "Reuse adoption evidence missing")
        roles.update(reusePlan=reuse["evidence"], reuseSourceFormalAcceptance=reuse["sourceFormalAcceptance"])
    for role, value in roles.items():
        if not value:
            continue
        data = checked_bytes(value)
        if role == "formalAcceptance":
            acceptance_identity(json.loads(data.decode("utf-8-sig")), tile["native"]["sha256"], tile["tile"], item["appearance"])
        if role == "reuseSourceFormalAcceptance":
            acceptance_identity(json.loads(data.decode("utf-8-sig")), tile["native"]["sha256"],
                                reuse["sourceCoordinate"], reuse["sourceAppearance"])
        documents.append(dict(role=role, originalPath=descriptor_path(value), originalSha256=digest(data),
                              originalBytes=len(data), utf8Text=data.decode("utf-8")))
    evidence = dict(schemaVersion=1, mapId=str(item["mapId"]), appearance=normalized_appearance(item["appearance"]),
                    tile=tile["tile"], sourceSelectionSha256=selection_sha, native=tile["native"], runtime=tile["runtime"],
                    formalArtAccepted=True, navigationAcceptanceClaimed=False, originalDocuments=documents)
    for key in ("reuse", "originalToolEvent", "sourceCoordinateMapping", "originalRuntime"):
        if key in tile:
            evidence[key] = tile[key]
    return evidence


def geometry_matches(source, item):
    columns, rows = item["grid"]["columns"], item["grid"]["rows"]
    world, effective, image = (source[k] for k in ("worldRect", "effectiveWorldRect", "imageRect"))
    for rect in (world, effective, image):
        require(all(isinstance(rect[k], (int, float)) and math.isfinite(rect[k]) for k in ("x", "y", "width", "height"))
                and rect["width"] > 0 and rect["height"] > 0, "Nonfinite or empty geometry")
    scale = world["width"] / (columns * 1024)
    require(abs(world["height"] / (rows * 1024) - scale) < 1e-8, "Map artwork must keep a uniform world scale")
    expected = dict(x=world["x"] + image["x"] * scale,
                    y=world["y"] + world["height"] - (image["y"] + image["height"]) * scale,
                    width=image["width"] * scale, height=image["height"] * scale)
    require(all(abs(effective[k] - expected[k]) < 1e-5 for k in expected), "Image/world mapping mismatch")
    require(0 <= image["x"] and 0 <= image["y"] and image["x"] + image["width"] <= columns * 1024
            and image["y"] + image["height"] <= rows * 1024, "Effective image exceeds raster")
    require(source["referenceWidth"] > 0 and source["referenceHeight"] > 0 and
            abs(image["width"] * source["referenceHeight"] - image["height"] * source["referenceWidth"])
            <= max(source["referenceWidth"], source["referenceHeight"]), "Reference aspect mismatch")


def build_overview(tile_bytes, columns, rows):
    canvas = Image.new("RGB", (columns * 1024, rows * 1024))
    for row, column, data in tile_bytes:
        with Image.open(io.BytesIO(data)) as tile:
            canvas.paste(tile.convert("RGB"), ((column - 1) * 1024, (row - 1) * 1024))
    output = io.BytesIO()
    canvas.save(output, format="PNG")
    return output.getvalue()


def import_overview(item, tile_bytes, columns, rows):
    """Keep reviewed PNG bytes, while proving every displayed pixel is a runtime tile."""
    if not item.get("derivedOverview"):
        return build_overview(tile_bytes, columns, rows)
    data = checked_bytes(item["derivedOverview"])
    inspect_png(data, [columns * 1024, rows * 1024], "reviewed formal overview")
    with Image.open(io.BytesIO(data)) as overview:
        for row, column, runtime in tile_bytes:
            with Image.open(io.BytesIO(runtime)) as tile:
                box = ((column - 1) * 1024, (row - 1) * 1024, column * 1024, row * 1024)
                require(overview.crop(box).convert("RGB").tobytes() == tile.convert("RGB").tobytes(),
                        "Reviewed overview differs from actual runtime tiles")
    return data


def navigation_result(plan, map_id):
    from festival_map_navigation import build_navigation
    result = build_navigation(plan, str(map_id))
    mask = result.get("mask", result.get("maskBytes"))
    require(isinstance(mask, bytes), "Navigation builder did not return packed bytes")
    require(result["planSha256"] == digest(Path(plan).read_bytes()), "Navigation plan SHA mismatch")
    return result, mask


def navigation_layout_binding(plan_path, map_id, appearance, layout_sha):
    reviewed = read_json(plan_path).get("visuallyReviewedFormalAssemblies", [])
    matches = [r for r in reviewed if str(r.get("mapId")) == map_id and r.get("appearance") == appearance]
    require(len(matches) == 1 and matches[0].get("sha256") == layout_sha,
            "Navigation plan was reviewed against a different formal tile assembly")


def import_maps(project, selection_path, navigation_path, batch=None):
    batch = resolve_batch(batch)
    scope, evidence_directory, revision = batch.scope, batch.evidence, batch.revision
    project = Path(project).resolve()
    selection_data = Path(selection_path).read_bytes()
    selection = json.loads(selection_data.decode("utf-8-sig"))
    validate_selection(selection, batch)
    selection_sha = digest(selection_data)
    catalog_path = project / DELIVERY / "catalog.json"
    catalog = read_json(catalog_path) if catalog_path.exists() else dict(schemaVersion=2, maps=[])
    require(catalog.get("schemaVersion") == 2 and isinstance(catalog.get("maps"), list), "Existing catalog schema")
    requested = defaultdict(set)
    for map_id, appearance in scope:
        requested["map" + map_id].add(appearance)
    for existing in catalog["maps"]:
        key, scene = existing["key"], existing["sceneId"]
        for requested_key in requested:
            require(scene != int(requested_key[3:]) or key == requested_key,
                    "Existing numeric sceneId belongs to a different map key")
        if key in requested:
            require(scene == int(key[3:]), "Existing map key has another sceneId")
            require({a["id"] for a in existing["appearances"]}.issubset(requested[key]),
                    "Batch scope must cover all existing appearances of a managed map")
    docs = project / evidence_directory
    docs.mkdir(parents=True, exist_ok=True)
    if batch.document is not None:
        (docs / "batch.json").write_bytes(json_bytes(batch.document))
    selection_copy = docs / "selection.json"
    selection_copy.write_bytes(selection_data)
    nav_copy = docs / "navigation-plan.json"
    if Path(navigation_path).resolve() != nav_copy.resolve():
        nav_copy.write_bytes(Path(navigation_path).read_bytes())
    nav_sha = digest(nav_copy.read_bytes())
    nav_results = {map_id: navigation_result(nav_copy, map_id) for map_id in sorted({m for m, _ in scope})}
    registrations = defaultdict(list)
    for item in selection["sets"]:
        map_id, appearance = str(item["mapId"]), normalized_appearance(item["appearance"])
        key = "map" + map_id
        nav, mask = nav_results[map_id]
        geometry_matches(nav["source"], item)
        columns, rows = scope[map_id, appearance]
        files = []
        for tile in item["tiles"]:
            native_data = checked_bytes(tile["native"])
            inspect_png(native_data, [1254, 1254], tile["tile"] + " native")
            runtime_data = checked_bytes(tile["runtime"])
            inspect_png(runtime_data, [1024, 1024], tile["tile"] + " runtime")
            files.append((tile["row"], tile["column"], runtime_data))
        overview = import_overview(item, files, columns, rows)
        layout_sha = digest(overview)
        navigation_layout_binding(nav_copy, map_id, appearance, layout_sha)
        resource_root = f"{RESOURCE_ROOT}{key}/{revision}/{appearance}/{layout_sha}/"
        folder = project / "Assets/Resources" / resource_root
        asset_directory(folder / "tiles", project)
        evidence_refs, records = [], []
        for tile, (_, _, data) in zip(item["tiles"], files):
            name = tile["tile"]
            evidence = capture_evidence(item, tile, selection_sha)
            evidence_rel = evidence_directory / "source-evidence" / key / appearance / (name + ".json")
            evidence_path = project / evidence_rel
            evidence_path.parent.mkdir(parents=True, exist_ok=True)
            evidence_data = json_bytes(evidence)
            evidence_path.write_bytes(evidence_data)
            reference = dict(path=evidence_rel.as_posix(), sha256=digest(evidence_data))
            evidence_refs.append(reference)
            write_asset(folder / "tiles" / (name + ".png"), data, project)
            records.append(dict(row=tile["row"], column=tile["column"], resourcePath=resource_root + "tiles/" + name,
                                sha256=digest(data), bytes=len(data), nativeSha256=tile["native"]["sha256"],
                                nativePixels=1254, evidencePath=reference["path"], evidenceSha256=reference["sha256"]))
        bundle = dict(schemaVersion=1, key=key, sceneId=int(map_id), revision=revision, appearance=appearance,
                      layoutSha256=layout_sha, sourceSelectionSha256=selection_sha, navigationPlanSha256=nav_sha,
                      sourceLayoutPreview=item["layoutSource"], selectionAuthority=item["selectionAuthority"],
                      tileEvidence=evidence_refs, formalArtAccepted=True, authoritativeServerRegistered=False)
        bundle_rel = evidence_directory / "source-evidence" / key / appearance / "delivery.json"
        bundle_data = json_bytes(bundle)
        (project / bundle_rel).write_bytes(bundle_data)
        navigation = dict(schemaVersion=1, key=key, sceneId=int(map_id), revision=revision,
                          navigationPlanPath=(evidence_directory / "navigation-plan.json").as_posix(),
                          navigationPlanSha256=nav_sha, source=nav["source"], stats=nav["stats"],
                          runtimeEngineAcceptance=False, authoritativeServerRegistered=False)
        nav_data = json_bytes(navigation)
        mask_text = base64.b64encode(mask)
        source = dict(nav["source"])
        source.update(schemaVersion=2, key=key, sceneId=int(map_id), name="地图 " + map_id,
                      revision=revision, appearance=appearance, resourceRoot=resource_root,
                      clientOnly=True, authoritativeServerRegistered=False, artStatus="formal_tiles_overview",
                      layoutPixels=dict(width=columns * 1024, height=rows * 1024), layoutSha256=layout_sha,
                      sourceSelectionPath=selection_copy.relative_to(project).as_posix(), sourceSelectionSha256=selection_sha,
                      sourceEvidencePath=bundle_rel.as_posix(), sourceEvidenceSha256=digest(bundle_data),
                      navigationPlanPath=(evidence_directory / "navigation-plan.json").as_posix(), navigationPlanSha256=nav_sha,
                      navigationSha256=digest(nav_data), walkmaskSha256=digest(mask_text),
                      walkmaskPackedSha256=digest(mask), navigationStatus="conservative_visible_ground_client_only",
                      runtimeEngineAcceptance=False)
        manifest = dict(schemaVersion=2, deliveryStatus="formal_runtime_tiles", key=key, sceneId=int(map_id),
                        revision=revision, appearance=appearance, resourceRoot=resource_root, layoutSha256=layout_sha,
                        columns=columns, rows=rows, tilePixels=1024, tiles=records,
                        deliveredTileCount=len(records), deliveredImageBytes=sum(t["bytes"] for t in records),
                        sourceSelectionSha256=selection_sha, sourceEvidenceSha256=digest(bundle_data),
                        navigationPlanSha256=nav_sha, wholeMapRuntimeAccepted=False)
        for field in ("worldRect", "effectiveWorldRect", "imageRect", "referenceWidth", "referenceHeight"):
            manifest[field] = source[field]
        for name, data in (("layout.png", overview), ("source.json", json_bytes(source)),
                           ("tiles.json", json_bytes(manifest)), ("navigation.json", nav_data), ("walkmask.txt", mask_text)):
            write_asset(folder / name, data, project)
        registrations[key].append(dict(id=appearance, resourceRoot=resource_root,
                                       layoutSha256=layout_sha, artStatus="formal_art_delivery"))
    managed = set(registrations)
    retained = [entry for entry in catalog["maps"] if entry["key"] not in managed]
    for key in sorted(managed):
        retained.append(dict(key=key, sceneId=int(key[3:]), name="地图 " + key[3:], revision=revision,
                             clientOnly=True, defaultAppearance=default_appearance({a["id"] for a in registrations[key]}),
                             appearances=registrations[key]))
    catalog["maps"] = retained
    write_asset(catalog_path, json_bytes(catalog), project)
    return verify(project, batch)


def verify_meta(path, project):
    require(Path(str(path) + ".meta").read_bytes() == meta_bytes(path, project), f"{path.name}: importer metadata changed")


def verify(project, batch=None):
    """Read only delivered assets/Docs. No source art paths are opened here."""
    batch = resolve_batch(batch)
    scope, evidence_directory, revision = batch.scope, batch.evidence, batch.revision
    project = Path(project).resolve()
    selection_path, plan_path = project / evidence_directory / "selection.json", project / evidence_directory / "navigation-plan.json"
    selection_data = selection_path.read_bytes()
    selection, selection_sha = json.loads(selection_data.decode("utf-8-sig")), digest(selection_data)
    validate_selection(selection, batch)
    nav_sha = digest(plan_path.read_bytes())
    nav_results = {map_id: navigation_result(plan_path, map_id) for map_id in sorted({m for m, _ in scope})}
    catalog_path = project / DELIVERY / "catalog.json"
    catalog = read_json(catalog_path)
    verify_meta(catalog_path, project)
    require(catalog.get("schemaVersion") == 2 and isinstance(catalog.get("maps"), list), "Catalog identity")
    keys = [m["key"] for m in catalog["maps"]]
    scenes = [m["sceneId"] for m in catalog["maps"]]
    require(len(keys) == len(set(keys)) and len(scenes) == len(set(scenes)), "Duplicate catalog identity")
    results, shared_masks = [], {}
    for item in selection["sets"]:
        map_id, appearance = str(item["mapId"]), normalized_appearance(item["appearance"])
        key = "map" + map_id
        registration = next((m for m in catalog["maps"] if m["key"] == key), None)
        expected_apps = {a for mid, a in scope if mid == map_id}
        require(registration and registration["sceneId"] == int(map_id) and registration["revision"] == revision
                and registration.get("clientOnly") is True
                and registration.get("defaultAppearance") == default_appearance(expected_apps),
                "Client-only festival registration mismatch")
        require({a["id"] for a in registration["appearances"]} == expected_apps
                and len(registration["appearances"]) == len(expected_apps), "Undelivered normal or duplicate appearance registered")
        delivery = next(a for a in registration["appearances"] if a["id"] == appearance)
        root = delivery["resourceRoot"]
        layout_sha = delivery["layoutSha256"]
        require(SHA.fullmatch(layout_sha) and root == f"{RESOURCE_ROOT}{key}/{revision}/{appearance}/{layout_sha}/"
                and delivery["artStatus"] == "formal_art_delivery", "Catalog source-bound Resources path mismatch")
        navigation_layout_binding(plan_path, map_id, appearance, layout_sha)
        folder = project / "Assets/Resources" / root
        source, manifest = read_json(folder / "source.json"), read_json(folder / "tiles.json")
        identity = dict(schemaVersion=2, key=key, sceneId=int(map_id), revision=revision,
                        appearance=appearance, resourceRoot=root, layoutSha256=layout_sha)
        require(all(source.get(k) == v and manifest.get(k) == v for k, v in identity.items()), "Source/tiles/catalog identity mismatch")
        require(source["artStatus"] == "formal_tiles_overview" and manifest["deliveryStatus"] == "formal_runtime_tiles"
                and source.get("clientOnly") is True and source.get("authoritativeServerRegistered") is False,
                "Formal/runtime/server status mismatch")
        nav, mask = nav_results[map_id]
        require(all(source.get(k) == v for k, v in nav["source"].items()), "Navigation geometry/source mismatch")
        geometry_matches(source, item)
        for field in ("worldRect", "effectiveWorldRect", "imageRect", "referenceWidth", "referenceHeight"):
            require(manifest[field] == source[field], "Manifest geometry mismatch")
        columns, rows = scope[map_id, appearance]
        require((manifest["columns"], manifest["rows"], manifest["tilePixels"]) == (columns, rows, 1024)
                and source["layoutPixels"] == dict(width=columns * 1024, height=rows * 1024), "Runtime raster mismatch")
        require(source["sourceSelectionSha256"] == manifest["sourceSelectionSha256"] == selection_sha
                and source["navigationPlanSha256"] == manifest["navigationPlanSha256"] == nav_sha,
                "Frozen selection/navigation plan binding mismatch")
        require(source["navigationPlanPath"] == (evidence_directory / "navigation-plan.json").as_posix()
                and source["sourceSelectionPath"] == (evidence_directory / "selection.json").as_posix(), "Unsafe provenance path")
        mask_text, nav_data = (folder / "walkmask.txt").read_bytes(), (folder / "navigation.json").read_bytes()
        require(base64.b64decode(mask_text, validate=True) == mask and digest(mask_text) == source["walkmaskSha256"]
                and digest(mask) == source["walkmaskPackedSha256"] and digest(nav_data) == source["navigationSha256"],
                "Navigation mask/plan SHA mismatch")
        navigation = json.loads(nav_data)
        require(navigation["navigationPlanSha256"] == nav_sha and navigation["source"] == nav["source"]
                and navigation["stats"] == nav["stats"] and navigation["key"] == key, "Navigation evidence mismatch")
        if map_id in shared_masks:
            require(shared_masks[map_id] == (mask, nav["source"]), "Festival appearances have different navigation")
        shared_masks[map_id] = mask, nav["source"]
        bundle_rel = evidence_directory / "source-evidence" / key / appearance / "delivery.json"
        require(source["sourceEvidencePath"] == bundle_rel.as_posix(), "Unsafe source evidence path")
        bundle_data = (project / bundle_rel).read_bytes()
        require(digest(bundle_data) == source["sourceEvidenceSha256"] == manifest["sourceEvidenceSha256"], "Source evidence SHA mismatch")
        bundle = json.loads(bundle_data)
        require(bundle["sourceSelectionSha256"] == selection_sha and bundle["navigationPlanSha256"] == nav_sha
                and bundle["layoutSha256"] == layout_sha and bundle["formalArtAccepted"] is True,
                "Source evidence identity mismatch")
        expected_tiles = {(t["row"], t["column"]): t for t in item["tiles"]}
        require(len(manifest["tiles"]) == columns * rows, "Missing runtime tile")
        seen, tile_bytes, total = set(), [], 0
        for entry in manifest["tiles"]:
            coord = entry["row"], entry["column"]
            require(coord in expected_tiles and coord not in seen, "Missing or duplicated tile coordinate")
            seen.add(coord)
            tile = expected_tiles[coord]
            name = tile["tile"]
            require(entry["resourcePath"] == root + "tiles/" + name and entry["nativePixels"] == 1254
                    and entry["nativeSha256"] == tile["native"]["sha256"]
                    and entry["sha256"] == tile["runtime"]["sha256"] and entry["bytes"] == tile["runtime"]["bytes"],
                    "Runtime/native provenance mismatch")
            evidence_rel = evidence_directory / "source-evidence" / key / appearance / (name + ".json")
            require(entry["evidencePath"] == evidence_rel.as_posix(), "Unsafe tile evidence path")
            evidence_data = (project / evidence_rel).read_bytes()
            require(digest(evidence_data) == entry["evidenceSha256"], "Tile evidence SHA mismatch")
            require(dict(path=entry["evidencePath"], sha256=entry["evidenceSha256"]) in bundle["tileEvidence"],
                    "Unlisted formal evidence")
            evidence = json.loads(evidence_data)
            require(evidence["native"] == tile["native"] and evidence["runtime"] == tile["runtime"]
                    and evidence["sourceSelectionSha256"] == selection_sha and evidence["formalArtAccepted"] is True,
                    "Formal evidence source binding mismatch")
            reuse = tile.get("reuse", {})
            if reuse.get("reusedNative"):
                require(evidence.get("reuse") == reuse and reuse.get("sameGridAndCoordinate") is True
                        and reuse.get("newPhysicalCalls") == 0
                        and reuse.get("source", {}).get("sha256") == tile["native"]["sha256"],
                        "Reuse adoption/source identity mismatch")
                require({"reusePlan", "reuseSourceFormalAcceptance"}.issubset(
                    {d["role"] for d in evidence["originalDocuments"]}), "Portable reuse evidence missing")
            found_formal = False
            for document in evidence["originalDocuments"]:
                original = document["utf8Text"].encode("utf-8")
                require(digest(original) == document["originalSha256"] and len(original) == document["originalBytes"],
                        "Original JSON snapshot SHA mismatch")
                if document["role"] == "formalAcceptance":
                    require(document["originalSha256"] == tile["formalAcceptance"]["sha256"], "Formal QA snapshot changed")
                    acceptance_identity(json.loads(document["utf8Text"].lstrip("\ufeff")), tile["native"]["sha256"],
                                        tile["tile"], appearance)
                    found_formal = True
                elif document["role"] == "reuseSourceFormalAcceptance":
                    require(document["originalSha256"] == reuse["sourceFormalAcceptance"]["sha256"],
                            "Reuse source acceptance snapshot changed")
                    acceptance_identity(json.loads(document["utf8Text"].lstrip("\ufeff")), tile["native"]["sha256"],
                                        reuse["sourceCoordinate"], reuse["sourceAppearance"])
                elif document["role"] == "reusePlan":
                    require(document["originalSha256"] == reuse["evidence"]["sha256"], "Reuse plan snapshot changed")
            require(found_formal, "Formal acceptance snapshot absent")
            png = folder / "tiles" / (name + ".png")
            data = png.read_bytes()
            require(digest(data) == entry["sha256"] and len(data) == entry["bytes"], "Runtime PNG SHA mismatch")
            inspect_png(data, [1024, 1024], name)
            verify_meta(png, project)
            tile_bytes.append((*coord, data))
            total += len(data)
        overview_data = (folder / "layout.png").read_bytes()
        require(digest(overview_data) == layout_sha, "Formal assembled layout SHA mismatch")
        inspect_png(overview_data, [columns * 1024, rows * 1024], "layout")
        # Pixel equality avoids making portable verification depend on a Pillow encoder version.
        with Image.open(io.BytesIO(overview_data)) as overview:
            for row, column, data in tile_bytes:
                with Image.open(io.BytesIO(data)) as tile:
                    box = ((column - 1) * 1024, (row - 1) * 1024, column * 1024, row * 1024)
                    require(overview.crop(box).convert("RGB").tobytes() == tile.convert("RGB").tobytes(),
                            "Layout contains preview or pixels from another runtime tile")
        require({p.name for p in (folder / "tiles").glob("*.png")} == {t["tile"] + ".png" for t in item["tiles"]},
                "Unlisted runtime PNG")
        require(manifest["deliveredTileCount"] == len(seen) and manifest["deliveredImageBytes"] == total, "Delivery totals mismatch")
        for name in ("source.json", "tiles.json", "navigation.json", "walkmask.txt", "layout.png"):
            verify_meta(folder / name, project)
        parent = folder / "tiles"
        while parent != (project / DELIVERY).parent:
            verify_meta(parent, project)
            parent = parent.parent
        results.append(dict(key=key, sceneId=int(map_id), appearance=appearance, tiles=len(seen),
                            runtimeImageBytes=total, layoutBytes=len(overview_data), layoutSha256=layout_sha,
                            walkmaskPackedSha256=digest(mask), navigationStats=nav["stats"], clientOnly=True))
    return dict(schemaVersion=1, ok=True, appearances=results, maps=len(shared_masks),
                appearanceCount=len(results), tileCount=sum(r["tiles"] for r in results),
                runtimeImageBytes=sum(r["runtimeImageBytes"] for r in results),
                assembledLayoutBytes=sum(r["layoutBytes"] for r in results),
                sourceSelectionSha256=selection_sha, navigationPlanSha256=nav_sha,
                formalArtSourceBindingsVerified=True, runtimeTilesDecoded=True, layoutPixelsMatchRuntimeTiles=True,
                artCheckoutRequiredForVerification=False, highestNativePNGsCopied=False,
                authoritativeServerRegistered=False, unityRenderedAcceptance=False)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("command", choices=("import", "verify"))
    parser.add_argument("--project", type=Path, default=PROJECT)
    parser.add_argument("--selection", type=Path)
    parser.add_argument("--navigation", type=Path)
    parser.add_argument("--report", type=Path)
    parser.add_argument("--batch-config", type=Path,
                        help="Explicit revision/evidenceDirectory/scope JSON; omit to verify the original five appearances")
    args = parser.parse_args()
    if args.command == "import" and (not args.selection or not args.navigation):
        parser.error("import requires --selection and --navigation")
    batch = resolve_batch(args.batch_config)
    result = import_maps(args.project, args.selection, args.navigation, batch) if args.command == "import" else verify(args.project, batch)
    report = args.report or args.project / batch.evidence / "delivery-verification.json"
    report.parent.mkdir(parents=True, exist_ok=True)
    report.write_bytes(json_bytes(result))
    summary = {key: value for key, value in result.items() if key != "appearances"}
    summary["appearances"] = [{key: appearance[key] for key in
                              ("key", "appearance", "tiles", "layoutSha256", "walkmaskPackedSha256")}
                             for appearance in result["appearances"]]
    print(json.dumps(summary, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
