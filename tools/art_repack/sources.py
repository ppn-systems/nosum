"""The CC0 packs the repack reads, with the sheet files pinned by SHA-256 so a run is reproducible.

Download each archive from its page, unzip it into <sources>/<directory> (the name in PACKS), and run repack_cc0_art.py --sources <sources>.
Archives are untrusted data: this code only decodes the PNG sheets and never runs anything from a pack.
"""
import hashlib
import os

import pngio

# directory under --sources -> (title, page, license as verified, author)
PACKS = {
    'kenney_tiny-town': ('Kenney Tiny Town 1.1', 'https://kenney.nl/assets/tiny-town', 'CC0 1.0 (License.txt inside the archive)', 'Kenney'),
    'kenney_tiny-farm': ('Kenney Tiny Farm 1.0', 'https://kenney.nl/assets/tiny-farm', 'CC0 1.0 (License.txt inside the archive)', 'Kenney'),
    'josehzz_farming-crops-16x16': ('Farming crops 16x16', 'https://opengameart.org/content/farming-crops-16x16', 'CC0 (stated on the OpenGameArt page; the archive holds no license file)', 'josehzz'),
}

# key -> (pack directory, path inside it, sha256, cell size, margin between cells)
SHEETS = {
    'crops': ('josehzz_farming-crops-16x16', 'Crop_Spritesheet.png', 'c98fe9cd765698738678f4a64c5ac51c713c63624ca73da05bea7be995946c46', 16, 0),
    'farm': ('kenney_tiny-farm', 'Tilemap/tilemap_packed.png', '0c4b3b4058cacf6a9bf021666838156d46da9ce27500d4c13e30cd5a92b722f4', 16, 0),
    'town': ('kenney_tiny-town', 'Tilemap/tilemap_packed.png', '3a54d99ecde790d4fdea207a3644cf130fc56fa838f1beb1507c185a95b8e902', 16, 0),
}


class Sheet:
    """A sprite sheet on a regular grid; cell(column, row) returns one cell as an Image."""

    def __init__(self, image, size, margin):
        self.image = image
        self.size = size
        self.margin = margin

    def cell(self, column, row, w=1, h=1):
        step = self.size + self.margin
        return self.image.crop(column * step, row * step, w * self.size + (w - 1) * self.margin, h * self.size + (h - 1) * self.margin)


class Sources:
    def __init__(self, root):
        self.root = root
        self._sheets = {}

    def sheet(self, key):
        if key not in self._sheets:
            pack, path, digest, size, margin = SHEETS[key]
            full = os.path.join(self.root, pack, path)
            with open(full, 'rb') as f:
                actual = hashlib.sha256(f.read()).hexdigest()
            if actual != digest:
                raise SystemExit(f'{full}: SHA-256 {actual} differs from the pinned {digest}; the pack changed or is not the one in docs/art/sourcing.md')
            self._sheets[key] = Sheet(pngio.read_png(full), size, margin)
        return self._sheets[key]
