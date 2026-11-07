"""从人工审过的正式地图地面多边形生成客户端导航，不从颜色猜路。

像素坐标以完整运行图左上角为原点；世界坐标以西南角为原点。
build_navigation 返回 mask(bytes)、source(运行清单几何字段)和 stats。
服务端场景映射、传送点和 Recast 发布不由本模块推断或修改。
"""

from __future__ import annotations

import argparse
import base64
from collections import deque
import hashlib
import heapq
import json
import math
from pathlib import Path


def _finite(value):
    return isinstance(value, (int, float)) and not isinstance(value, bool) and math.isfinite(value)


def _point(value):
    if not isinstance(value, list) or len(value) != 2 or not all(_finite(v) for v in value):
        raise ValueError("导航点必须是有限的像素坐标二元组。")
    return value[0], value[1]


def _on_segment(point, start, end):
    cross = (point[0] - start[0]) * (end[1] - start[1]) - (point[1] - start[1]) * (end[0] - start[0])
    return abs(cross) <= 1e-7 and min(start[0], end[0]) - 1e-7 <= point[0] <= max(start[0], end[0]) + 1e-7 and min(start[1], end[1]) - 1e-7 <= point[1] <= max(start[1], end[1]) + 1e-7


def _inside(point, polygon):
    inside = False
    for a, b in zip(polygon, polygon[1:] + polygon[:1]):
        if _on_segment(point, a, b):
            return True
        if (a[1] > point[1]) != (b[1] > point[1]):
            intersection = (b[0] - a[0]) * (point[1] - a[1]) / (b[1] - a[1]) + a[0]
            if point[0] < intersection:
                inside = not inside
    return inside


def _segments_intersect(a, b, c, d):
    def cross(p, q, r):
        return (q[0] - p[0]) * (r[1] - p[1]) - (q[1] - p[1]) * (r[0] - p[0])
    values = cross(a, b, c), cross(a, b, d), cross(c, d, a), cross(c, d, b)
    if values[0] * values[1] < 0 and values[2] * values[3] < 0:
        return True
    return any(abs(value) < 1e-7 and _on_segment(point, start, end) for value, point, start, end in (
        (values[0], c, a, b), (values[1], d, a, b), (values[2], a, c, d), (values[3], b, c, d)))


def _obstacle_overlaps(polygon, box):
    x0, y0, x1, y1 = box
    corners = [(x0, y0), (x1, y0), (x1, y1), (x0, y1)]
    if any(_inside(p, polygon) for p in corners) or any(x0 <= x <= x1 and y0 <= y <= y1 for x, y in polygon):
        return True
    return any(_segments_intersect(a, b, c, d) for a, b in zip(polygon, polygon[1:] + polygon[:1]) for c, d in zip(corners, corners[1:] + corners[:1]))


def pixel_to_world(spec, pixel):
    px, py = _point(pixel)
    world = spec["worldRect"]
    width, height = spec["canvasPixels"]
    return {"x": world["x"] + px * world["width"] / width, "y": 0.0,
            "z": world["y"] + (height - py) * world["height"] / height}


def _validated_spec(plan, map_id):
    if plan.get("schemaVersion") != 1 or str(map_id) not in plan.get("maps", {}):
        raise ValueError("导航计划版本或地图ID错误。")
    spec = plan["maps"][str(map_id)]
    canvas = spec.get("canvasPixels", [])
    if len(canvas) != 2 or any(not isinstance(v, int) or isinstance(v, bool) or v <= 0 or v > 131072 for v in canvas):
        raise ValueError("完整运行画布尺寸错误。")
    for name in ("worldRect", "imageRect"):
        rect = spec.get(name, {})
        if set(rect) != {"x", "y", "width", "height"} or not all(_finite(v) for v in rect.values()) or rect["width"] <= 0 or rect["height"] <= 0:
            raise ValueError("导航矩形必须有限且宽高为正。")
    world, image = spec["worldRect"], spec["imageRect"]
    if image["x"] < 0 or image["y"] < 0 or image["x"] + image["width"] > canvas[0] or image["y"] + image["height"] > canvas[1]:
        raise ValueError("有效图像范围超出完整画布。")
    if abs(world["width"] / canvas[0] - world["height"] / canvas[1]) > 1e-9:
        raise ValueError("运行块必须等比例映射到世界，不能拉伸。")
    cell = spec.get("cellSize")
    if not _finite(cell) or cell < .5 or math.ceil(world["width"] / cell) * math.ceil(world["height"] / cell) > 4000000:
        raise ValueError("导航单元尺寸或总数错误。")
    for name in ("walkablePolygons", "obstaclePolygons"):
        polygons = spec.get(name)
        if not isinstance(polygons, list) or (name == "walkablePolygons" and not polygons):
            raise ValueError("必须显式提供道路及阻挡多边形。")
        for polygon in polygons:
            if not isinstance(polygon, list) or len(polygon) < 3:
                raise ValueError("道路或阻挡多边形至少需要三个点。")
            points = [_point(p) for p in polygon]
            if any(x < 0 or x > canvas[0] or y < 0 or y > canvas[1] for x, y in points):
                raise ValueError("导航多边形超出画布。")
            area = sum(a[0] * b[1] - b[0] * a[1] for a, b in zip(points, points[1:] + points[:1]))
            if abs(area) < 1e-7:
                raise ValueError("退化多边形不能作为导航。")
    _point(spec.get("spawnPixel"))
    reference = spec.get("referencePixels", [])
    if len(reference) != 2 or any(not isinstance(value, int) or isinstance(value, bool) or value <= 0 for value in reference):
        raise ValueError("必须提供实际参考图像尺寸。")
    return spec


def _neighbors(index, walkable, columns, rows):
    row, column = divmod(index, columns)
    for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (1, -1), (-1, 1), (-1, -1)):
        x, y = column + dx, row + dy
        if not 0 <= x < columns or not 0 <= y < rows:
            continue
        next_index = y * columns + x
        if not walkable[next_index]:
            continue
        if dx and dy and (not walkable[row * columns + x] or not walkable[y * columns + column]):
            continue
        yield next_index


def _component_labels(walkable, columns, rows):
    labels = [-1] * len(walkable)
    sizes = []
    for index, value in enumerate(walkable):
        if not value or labels[index] != -1:
            continue
        label = len(sizes)
        labels[index] = label
        queue = deque([index])
        size = 0
        while queue:
            current = queue.popleft()
            size += 1
            for next_index in _neighbors(current, walkable, columns, rows):
                if labels[next_index] == -1:
                    labels[next_index] = label
                    queue.append(next_index)
        sizes.append(size)
    return labels, sizes


def _route_distance(start, end, walkable, columns, rows, cell_size):
    distances = {start: 0.0}
    queue = [(0.0, start)]
    while queue:
        distance, current = heapq.heappop(queue)
        if distance != distances[current]:
            continue
        if current == end:
            return distance
        y, x = divmod(current, columns)
        for next_index in _neighbors(current, walkable, columns, rows):
            ny, nx = divmod(next_index, columns)
            candidate = distance + math.hypot(nx - x, ny - y) * cell_size
            if candidate < distances.get(next_index, math.inf):
                distances[next_index] = candidate
                heapq.heappush(queue, (candidate, next_index))
    raise ValueError("关键目标路径不存在。")


def build_navigation(plan, map_id):
    """生成同地图各外观共用的mask和运行字段；无服务端/引擎副作用。"""
    # 文件入口绑定原始bytes；内存入口绑定规范化后的整个计划。
    if isinstance(plan, dict):
        plan_bytes = json.dumps(plan, sort_keys=True, ensure_ascii=False, separators=(",", ":")).encode("utf-8")
    else:
        plan_bytes = Path(plan).read_bytes()
        plan = json.loads(plan_bytes.decode("utf-8-sig"))
    spec = _validated_spec(plan, map_id)
    world, image = spec["worldRect"], spec["imageRect"]
    canvas_width, canvas_height = spec["canvasPixels"]
    scale = world["width"] / canvas_width
    effective = {"x": world["x"] + image["x"] * scale,
                 "y": world["y"] + (canvas_height - image["y"] - image["height"]) * scale,
                 "width": image["width"] * scale, "height": image["height"] * scale}
    cell = spec["cellSize"]
    columns, rows = math.ceil(world["width"] / cell), math.ceil(world["height"] / cell)
    walkable = [False] * (columns * rows)
    polygons = spec["walkablePolygons"]
    obstacles = spec["obstaclePolygons"]
    for row in range(rows):
        z0, z1 = (rows - 1 - row) * cell, min((rows - row) * cell, world["height"])
        for column in range(columns):
            x0, x1 = column * cell, min((column + 1) * cell, world["width"])
            box = (max(x0 / scale, image["x"]), max(canvas_height - z1 / scale, image["y"]),
                   min(x1 / scale, image["x"] + image["width"]), min(canvas_height - z0 / scale, image["y"] + image["height"]))
            if box[0] >= box[2] or box[1] >= box[3]:
                continue
            epsilon = 1e-5
            samples = [(box[0] + epsilon, box[1] + epsilon), (box[2] - epsilon, box[1] + epsilon),
                       (box[2] - epsilon, box[3] - epsilon), (box[0] + epsilon, box[3] - epsilon),
                       ((box[0] + box[2]) / 2, (box[1] + box[3]) / 2)]
            if all(any(_inside(point, polygon) for polygon in polygons) for point in samples) and not any(_obstacle_overlaps(polygon, box) for polygon in obstacles):
                walkable[row * columns + column] = True
    mask = bytearray((len(walkable) + 7) // 8)
    for index, value in enumerate(walkable):
        if value:
            mask[index >> 3] |= 0x80 >> (index & 7)
    labels, sizes = _component_labels(walkable, columns, rows)

    def cell_for(pixel):
        point = pixel_to_world(spec, pixel)
        col = math.floor((point["x"] - world["x"]) / cell)
        row = rows - 1 - math.floor((point["z"] - world["y"]) / cell)
        inside = effective["x"] <= point["x"] < effective["x"] + effective["width"] and effective["y"] <= point["z"] < effective["y"] + effective["height"]
        if not inside or not 0 <= col < columns or not 0 <= row < rows:
            return None
        return row * columns + col

    spawn_index = cell_for(spec["spawnPixel"])
    if spawn_index is None or not walkable[spawn_index]:
        raise ValueError("人工计划出生点不在可走格内。")
    landmarks = {}
    for name, pixel in spec.get("landmarkPixels", {}).items():
        index = cell_for(pixel)
        if index is None or not walkable[index]:
            raise ValueError(f"导航目标不可走：{name}")
        landmarks[name] = {"pixel": pixel, "world": pixel_to_world(spec, pixel), "component": labels[index]}
    for name, pixel in spec.get("blockedLandmarkPixels", {}).items():
        index = cell_for(pixel)
        if index is not None and walkable[index]:
            raise ValueError(f"本应阻挡的位置变成可走：{name}")
    for start, end in spec.get("connectedLandmarkPairs", []):
        if landmarks[start]["component"] != landmarks[end]["component"]:
            raise ValueError(f"应当相通的道路断开：{start} -> {end}")
    for start, end in spec.get("disconnectedLandmarkPairs", []):
        if landmarks[start]["component"] == landmarks[end]["component"]:
            raise ValueError(f"分离平台被错误接通：{start} -> {end}")
    if len(sizes) != spec.get("expectedConnectedComponents", len(sizes)):
        raise ValueError(f"连通域数量错误：{len(sizes)}，计划{spec.get('expectedConnectedComponents')}")
    routes = [{"from": start, "to": end,
               "gridDistanceWorldUnits": _route_distance(cell_for(spec["landmarkPixels"][start]),
                   cell_for(spec["landmarkPixels"][end]), walkable, columns, rows, cell)}
              for start, end in spec.get("connectedLandmarkPairs", [])]
    return {"mask": bytes(mask), "planSha256": hashlib.sha256(plan_bytes).hexdigest(),
            "source": {"worldRect": world.copy(), "effectiveWorldRect": effective, "cellSize": cell,
                       "imageRect": image.copy(), "referenceWidth": spec["referencePixels"][0],
                       "referenceHeight": spec["referencePixels"][1],
                       "walkmaskColumns": columns, "walkmaskRows": rows, "spawnWorld": pixel_to_world(spec, spec["spawnPixel"])},
            "stats": {"walkableCells": sum(walkable), "totalCells": len(walkable), "componentCount": len(sizes),
                      "componentSizes": sizes, "spawnComponent": labels[spawn_index], "landmarks": landmarks,
                      "blockedLandmarks": list(spec.get("blockedLandmarkPixels", {})),
                      "connectedRoutes": spec.get("connectedLandmarkPairs", []),
                      "routeMeasurements": routes,
                      "maximumListedRouteSecondsAt18": max((r["gridDistanceWorldUnits"] / 18 for r in routes), default=0),
                      "separatedRoutes": spec.get("disconnectedLandmarkPairs", []),
                      "maskSha256": hashlib.sha256(mask).hexdigest(), "maskBytes": len(mask),
                      "engineAccepted": False, "serverNavigationPublished": False}}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--plan", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    plan = json.loads(args.plan.read_text(encoding="utf-8-sig"))
    for map_id in plan["maps"]:
        result = build_navigation(plan, map_id)
        root = args.output / map_id
        root.mkdir(parents=True, exist_ok=True)
        (root / "walkmask.txt").write_text(base64.b64encode(result.pop("mask")).decode("ascii") + "\n", encoding="ascii")
        (root / "navigation.json").write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
        print(json.dumps({"mapId": map_id, **result["stats"]}, ensure_ascii=False))


if __name__ == "__main__":
    main()
