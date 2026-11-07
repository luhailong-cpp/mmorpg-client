"""Small synthetic fixtures only; no production images receive acceptance."""

import contextlib
import io
import json
from pathlib import Path
import tempfile
import unittest

from PIL import Image, PngImagePlugin

import verify_pixels


class StrictPixelTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="synthetic-test-", dir=Path(__file__).parent)
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.reference = self.root / "reference.png"
        self.actual = self.root / "actual.png"
        self.output = self.root / "report.json"
        self.image = Image.new("RGBA", (3, 2), (10, 20, 30, 40))
        self.image.save(self.reference)

    def run_compare(self, **kwargs):
        with contextlib.redirect_stdout(io.StringIO()):
            code = verify_pixels.verify(self.reference, self.actual, self.output, **kwargs)
        return code, json.loads(self.output.read_text(encoding="utf-8"))

    def test_same_pixels_with_different_encoding(self):
        metadata = PngImagePlugin.PngInfo()
        metadata.add_text("fixture", "different PNG bytes, same RGBA pixels")
        self.image.save(self.actual, pnginfo=metadata, compress_level=0)
        code, result = self.run_compare()
        self.assertEqual(code, 0)
        self.assertEqual(result["status"], "exact")
        self.assertEqual(result["changedPixelCount"], 0)
        self.assertEqual(result["maxChannelDelta"], 0)
        self.assertNotEqual(result["referenceSHA256"], result["actualSHA256"])

    def test_single_pixel_single_level_difference(self):
        self.image.putpixel((1, 0), (11, 20, 30, 40))
        self.image.save(self.actual)
        diff = self.root / "difference.png"
        code, result = self.run_compare(diff_path=diff)
        self.assertEqual(code, 1)
        self.assertEqual(result["status"], "different")
        self.assertEqual(result["changedPixelCount"], 1)
        self.assertEqual(result["maxChannelDelta"], 1)
        self.assertEqual(result["changedBoundingBoxXYXY"], [1, 0, 2, 1])
        with Image.open(diff) as marked:
            self.assertEqual(marked.getpixel((1, 0)), (255, 0, 0))
            self.assertEqual(marked.getpixel((0, 0)), (0, 0, 0))

    def test_dimension_mismatch_is_error(self):
        Image.new("RGBA", (2, 3)).save(self.actual)
        code, result = self.run_compare()
        self.assertEqual(code, 2)
        self.assertEqual(result["status"], "error")
        self.assertEqual(result["referenceSize"], [3, 2])
        self.assertEqual(result["actualSize"], [2, 3])
        self.assertIsNone(result["changedPixelCount"])

    def test_alpha_difference_fails(self):
        self.image.putpixel((0, 1), (10, 20, 30, 41))
        self.image.save(self.actual)
        code, result = self.run_compare()
        self.assertEqual(code, 1)
        self.assertEqual(result["changedPixelCount"], 1)
        self.assertEqual(result["maxChannelDelta"], 1)

    def test_missing_file_is_error(self):
        code, result = self.run_compare()
        self.assertEqual(code, 2)
        self.assertEqual(result["status"], "error")
        self.assertIn("FileNotFoundError", result["error"])

    def test_input_cannot_be_overwritten(self):
        before = self.reference.read_bytes()
        self.image.save(self.actual)
        with contextlib.redirect_stdout(io.StringIO()):
            code = verify_pixels.verify(self.reference, self.actual, self.reference)
        self.assertEqual(code, 2)
        self.assertEqual(self.reference.read_bytes(), before)


if __name__ == "__main__":
    unittest.main(verbosity=2)
