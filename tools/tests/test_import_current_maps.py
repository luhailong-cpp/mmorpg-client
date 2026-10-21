"""Navigation geometry, wire format and portable delivery regression checks."""
from pathlib import Path
import shutil
import sys
import tempfile
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import import_current_maps as maps


class CurrentMapImportTests(unittest.TestCase):
    def test_msb_encoding_and_last_padding(self):
        self.assertEqual(maps.pack_mask([True, False, True, False, False, False, False, True, True]),
                         bytes([0b10100001, 0b10000000]))
        self.assertEqual(len(maps.pack_mask([True] * 22500)), 2813)
        self.assertEqual(maps.pack_mask([True] * 22500)[-1], 0xF0)

    def test_world_grid_matches_top_down_mask_and_offset(self):
        self.assertEqual(maps.spawn_cell((51, 0, 299)), (0, 0))
        self.assertEqual(maps.spawn_cell((349, 0, 1)), (149, 149))
        for _, (_, _, _, _, spawn) in maps.MAPS.items():
            col, row = maps.spawn_cell(spawn)
            self.assertEqual((51 + 2 * col, 0, 299 - 2 * row), spawn)

    def test_four_neighbour_connectivity_rejects_diagonal_islands(self):
        self.assertEqual(maps.connected_component([True, False, False, True], 2, 2, (0, 0)),
                         [True, False, False, False])
        with self.assertRaisesRegex(ValueError, 'Spawn'):
            maps.connected_component([False] * 4, 2, 2, (0, 0))

    def test_whole_cell_sampling_rejects_obstacle_even_off_centre(self):
        annotation = {
            'annotationWidth': 10, 'annotationHeight': 10,
            'walkablePolygons': [{'points': [[0, 0], [9, 0], [9, 9], [0, 9]]}],
            'obstaclePolygons': [{'points': [[2, 0], [2, 1], [2, 0]]}],
        }
        # 5x5 cells: a one-pixel obstacle at the left edge of cell (1,0)
        # must block that cell although its centre is clear.
        packed, kept, original = maps.build_mask(annotation, (80, 0, 270), resolution=5)
        self.assertTrue(packed[0] & 0x80)
        self.assertFalse(packed[0] & 0x40)
        self.assertEqual((kept, original), (24, 24))

    def test_delivery_verifies_without_art_checkout_and_detects_changed_mask(self):
        with tempfile.TemporaryDirectory() as directory:
            project = Path(directory)
            # Native tile payloads are independently tested and can be several
            # gigabytes; this test only exercises the existing layout/navigation.
            shutil.copytree(maps.PROJECT / maps.DELIVERY, project / maps.DELIVERY,
                            ignore=shutil.ignore_patterns('tiles', 'tiles.meta', 'tiles.json',
                                                          'tiles.json.meta', 'tile-evidence',
                                                          'tile-evidence.meta'))
            result = maps.verify(project)
            self.assertEqual({r['key']: r['walkableCells'] for r in result},
                             {'main': 6651, 'village': 4524, 'island': 4018})
            mask_file = project / maps.DELIVERY / 'main/walkmask.txt'
            changed = bytearray(mask_file.read_bytes())
            changed[0] = ord('A') if changed[0] != ord('A') else ord('B')
            mask_file.write_bytes(changed)
            with self.assertRaisesRegex(ValueError, 'mask'):
                maps.verify(project)


if __name__ == '__main__':
    unittest.main()
