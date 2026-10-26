"""Tiny drawing helpers for the pictures that no CC0 pack has: shapes in the Kenney palette, then a 1 px outline."""
import pngio
from pngio import Image

# The palette Kenney's 16 px packs share, so hand-drawn pieces sit next to them (ADR-040 D1: one palette, outline = a darker tone).
OUT = (63, 38, 49)
WOOD_D = (119, 56, 51)
WOOD = (189, 108, 74)
WOOD_L = (234, 165, 108)
STEEL_D = (82, 96, 124)
STEEL = (140, 156, 181)
STEEL_L = (192, 203, 220)
RED = (233, 67, 52)
BLUE_D = (0, 100, 160)
BLUE = (0, 154, 220)
BLUE_L = (118, 228, 255)
CREAM = (240, 230, 200)
GREEN_D = (38, 92, 66)
GREEN = (62, 137, 72)
GREEN_L = (99, 199, 77)
GOLD = (254, 174, 52)
GOLD_L = (255, 224, 120)
BROWN_D = (90, 64, 50)


class Canvas(Image):
    def __init__(self, w=16, h=16):
        super().__init__(w, h)

    def dot(self, x, y, c):
        if 0 <= x < self.width and 0 <= y < self.height:
            self.px[y][x] = c + (255,)

    def rect(self, x, y, w, h, c):
        for j in range(h):
            for i in range(w):
                self.dot(x + i, y + j, c)

    def line(self, x0, y0, x1, y1, c):
        n = max(abs(x1 - x0), abs(y1 - y0))
        for i in range(n + 1):
            t = i / n if n else 0
            self.dot(round(x0 + (x1 - x0) * t), round(y0 + (y1 - y0) * t), c)

    def ellipse(self, cx, cy, rx, ry, c):
        for y in range(self.height):
            for x in range(self.width):
                if ((x - cx) / rx) ** 2 + ((y - cy) / ry) ** 2 <= 1:
                    self.dot(x, y, c)

    def art(self, rows, palette, x=0, y=0):
        """ASCII rows; '.' is empty, other characters look up palette."""
        for j, row in enumerate(rows):
            for i, ch in enumerate(row):
                if ch != '.':
                    self.dot(x + i, y + j, palette[ch])

    def outline(self, c=OUT):
        """1 px outline around every opaque pixel (4-neighbourhood), drawn on empty pixels only."""
        add = []
        for y in range(self.height):
            for x in range(self.width):
                if self.px[y][x][3] == 0 and any(
                        0 <= x + dx < self.width and 0 <= y + dy < self.height and self.px[y + dy][x + dx][3]
                        for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1))):
                    add.append((x, y))
        for x, y in add:
            self.px[y][x] = c + (255,)
        return self


def swap(img, mapping):
    """Exact-colour palette swap: mapping {(r, g, b): (r, g, b)}."""
    return img.map_pixels(lambda p: mapping.get(p[:3], p[:3]) + (p[3],))
