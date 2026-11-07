"""验证正式节庆地图导航的编码、阻挡及必要路径，避免视觉通过即当可走。"""

import hashlib
import json
import math
from pathlib import Path
import sys
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from festival_map_navigation import build_navigation, pixel_to_world


ROOT = Path(__file__).resolve().parents[2]
PLAN_PATH = ROOT / "Docs/VerificationEvidence/festival-maps-20261106/navigation-plan.json"


def accepts(result, world):
    """按消费者的MSB位图/北南行序读取，而非读取生成器多边形。"""
    s = result["source"]
    bounds = s["effectiveWorldRect"]
    if not bounds["x"] <= world["x"] < bounds["x"] + bounds["width"] or not bounds["y"] <= world["z"] < bounds["y"] + bounds["height"]:
        return False
    x = math.floor((world["x"] - s["worldRect"]["x"]) / s["cellSize"])
    row = s["walkmaskRows"] - 1 - math.floor((world["z"] - s["worldRect"]["y"]) / s["cellSize"])
    index = row * s["walkmaskColumns"] + x
    return bool(result["mask"][index // 8] & (128 >> (index % 8)))


def fixture():
    return {"schemaVersion": 1, "maps": {"test": {
        "canvasPixels": [40, 40], "worldRect": {"x": 50, "y": -20, "width": 4, "height": 4},
        "imageRect": {"x": 0, "y": 0, "width": 40, "height": 40}, "referencePixels": [40, 40],
        "cellSize": 1, "spawnPixel": [35, 35],
        "walkablePolygons": [[[0, 0], [40, 0], [40, 40], [0, 40]]], "obstaclePolygons": []}}}


class FestivalMapNavigationTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.plan = json.loads(PLAN_PATH.read_text(encoding="utf-8"))
        cls.results = {map_id: build_navigation(PLAN_PATH, map_id) for map_id in cls.plan["maps"]}

    def test_three_maps_have_explicit_navigation_and_no_engine_or_server_claim(self):
        self.assertEqual(set(self.results), {"21003", "28007", "35004"})
        for map_id, result in self.results.items():
            with self.subTest(map=map_id):
                self.assertGreater(result["stats"]["walkableCells"], 500)
                self.assertLess(result["stats"]["walkableCells"], result["stats"]["totalCells"] / 2)
                self.assertFalse(result["stats"]["engineAccepted"])
                self.assertFalse(result["stats"]["serverNavigationPublished"])

    def test_half_open_image_bounds_are_not_misread_as_width_height(self):
        rect = self.results["21003"]["source"]["effectiveWorldRect"]
        for name, value in {"x": 67.2, "y": 85.6, "width": 172.8, "height": 129.6}.items():
            self.assertAlmostEqual(rect[name], value)
        self.assertFalse(accepts(self.results["21003"], {"x": 260, "z": 200}))

    def test_current_masks_use_north_to_south_msb_order_and_zero_tail_bits(self):
        for map_id, result in self.results.items():
            spec = self.plan["maps"][map_id]
            s = result["source"]
            bits = s["walkmaskColumns"] * s["walkmaskRows"]
            self.assertEqual(len(result["mask"]), (bits + 7) // 8)
            if bits % 8:
                self.assertEqual(result["mask"][-1] & ((1 << (8 - bits % 8)) - 1), 0)
            self.assertTrue(accepts(result, pixel_to_world(spec, spec["spawnPixel"])))
            for pixel in spec["blockedLandmarkPixels"].values():
                self.assertFalse(accepts(result, pixel_to_world(spec, pixel)))

    def test_north_wall_and_south_floor_prove_not_vertically_mirrored(self):
        result = self.results["21003"]
        self.assertFalse(accepts(result, {"x": 153.6, "z": 196.2}))
        self.assertTrue(accepts(result, {"x": 153.6, "z": 107.2}))

    def test_furniture_staircase_and_padding_are_blocked_in_main_hall(self):
        result = self.results["28007"]
        spec = self.plan["maps"]["28007"]
        for name in ("outside_black", "closed_bedroom", "offering_table", "east_stairs", "foyer_table", "entry_bollard_west", "entry_bollard_east", "dais_bolster"):
            with self.subTest(point=name):
                self.assertFalse(accepts(result, pixel_to_world(spec, spec["blockedLandmarkPixels"][name])))

    def test_three_cloud_platforms_remain_separate(self):
        stats = self.results["35004"]["stats"]
        self.assertEqual(stats["componentCount"], 3)
        labels = [stats["landmarks"][name]["component"] for name in ("spawn", "west_platform", "east_platform")]
        self.assertEqual(len(set(labels)), 3)

    def test_all_listed_paths_exist_and_are_within_explicit_short_route_budget(self):
        for result in self.results.values():
            self.assertGreater(len(result["stats"]["routeMeasurements"]), 0)
            self.assertLess(result["stats"]["maximumListedRouteSecondsAt18"], 40)
            for route in result["stats"]["connectedRoutes"]:
                self.assertEqual(*[result["stats"]["landmarks"][name]["component"] for name in route])

    def test_file_input_binds_entire_plan_bytes(self):
        expected = hashlib.sha256(PLAN_PATH.read_bytes()).hexdigest()
        self.assertEqual({r["planSha256"] for r in self.results.values()}, {expected})

    def test_generation_is_deterministic_for_shared_seasonal_geometry(self):
        f = fixture()
        a = build_navigation(f, "test")
        b = build_navigation(json.loads(json.dumps(f)), "test")
        self.assertEqual(a, b)

    def test_thin_obstacle_touching_cell_edge_blocks_both_neighbors(self):
        f = fixture()
        f["maps"]["test"]["obstaclePolygons"] = [[[9.8, 0], [10.2, 0], [10.2, 40], [9.8, 40]]]
        result = build_navigation(f, "test")
        self.assertFalse(accepts(result, {"x": 50.5, "z": -18.5}))
        self.assertFalse(accepts(result, {"x": 51.5, "z": -18.5}))
        self.assertTrue(accepts(result, {"x": 52.5, "z": -18.5}))

    def test_diagonal_contact_does_not_create_invisible_bridge(self):
        f = fixture()
        spec = f["maps"]["test"]
        spec.update(cellSize=2, spawnPixel=[10, 10], walkablePolygons=[[[0, 0], [20, 0], [20, 20], [0, 20]], [[20, 20], [40, 20], [40, 40], [20, 40]]], expectedConnectedComponents=2)
        self.assertEqual(build_navigation(f, "test")["stats"]["componentCount"], 2)

    def test_bad_spawn_and_distorted_world_are_rejected(self):
        for mutate in (lambda s: s.update(spawnPixel=[100, 100]), lambda s: s["worldRect"].update(width=8), lambda s: s.update(cellSize=math.nan), lambda s: s.update(walkablePolygons=[[[0, 0], [1, 1], [2, 2]]])):
            f = fixture()
            mutate(f["maps"]["test"])
            with self.assertRaises(ValueError):
                build_navigation(f, "test")


if __name__ == "__main__":
    unittest.main()
