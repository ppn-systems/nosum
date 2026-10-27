"""The packs the repack reads (CC0, plus CC-BY DawnLike), kept under art/source/ (docs/art/library.md), with the sheet files pinned by SHA-256 so a run is reproducible.

The packs are committed under art/source/<directory> (the name in PACKS): run repack_cc0_art.py --sources art/source.
Archives are untrusted data: this code only decodes the PNG sheets and never runs anything from a pack.
"""
import hashlib
import os

import pngio

# directory under --sources -> (title, page, license as verified, author)
PACKS = {
    'kenney_tiny-town': ('Kenney Tiny Town 1.1', 'https://kenney.nl/assets/tiny-town', 'CC0 1.0 (License.txt inside the archive)', 'Kenney'),
    'kenney_tiny-farm': ('Kenney Tiny Farm 1.0', 'https://kenney.nl/assets/tiny-farm', 'CC0 1.0 (License.txt inside the archive)', 'Kenney'),
    'kenney_tiny-dungeon': ('Kenney Tiny Dungeon 1.0', 'https://kenney.nl/assets/tiny-dungeon', 'CC0 1.0 (License.txt inside the archive)', 'Kenney'),
    'oga_tiny-creatures': ('Tiny Creatures 1.0', 'https://opengameart.org/content/tiny-creatures', 'CC0 1.0 (License.txt inside the archive)', 'Clint Bellanger'),
    'dawnlike': ('DawnLike 16x16 Universal Rogue-like tileset v1.81', 'https://opengameart.org/content/dawnlike-16x16-universal-rogue-like-tileset-v181', 'CC-BY 4.0 (README.txt inside the archive); credit DragonDePlatino and DawnBringer (palette)', 'DragonDePlatino'),
    'kenney_ui-pack-pixel-adventure': ('Kenney UI Pack - Pixel Adventure 2.0', 'https://kenney.nl/assets/ui-pack-pixel-adventure', 'CC0 1.0 (License.txt inside the archive; archive SHA-256 0b0ed4802ebcfff5e44e370f394baa1d751862a5a4a7612ac4ce84e85faa0627)', 'Kenney'),
    'kenney_pixel-ui-pack': ('Kenney Pixel UI pack', 'https://kenney.nl/assets/pixel-ui-pack', 'CC0 1.0 (License.txt inside the archive; archive SHA-256 b76f2f60f2be76eb8e66511038fa4d48faec11e638316fa37d06084878cdf0c7)', 'Kenney, with Lynn Evers'),
    'josehzz_farming-crops-16x16': ('Farming crops 16x16', 'https://opengameart.org/content/farming-crops-16x16', 'CC0 (stated on the OpenGameArt page; the archive holds no license file)', 'josehzz'),
}

# key -> (pack directory, path inside it, sha256, cell size, margin between cells)
SHEETS = {
    'crops': ('josehzz_farming-crops-16x16', 'Crop_Spritesheet.png', 'c98fe9cd765698738678f4a64c5ac51c713c63624ca73da05bea7be995946c46', 16, 0),
    'farm': ('kenney_tiny-farm', 'Tilemap/tilemap_packed.png', '0c4b3b4058cacf6a9bf021666838156d46da9ce27500d4c13e30cd5a92b722f4', 16, 0),
    'creatures': ('oga_tiny-creatures', 'tiny-creatures/Tilemap/tilemap_packed.png', '1fd68a8434e3269bb10a76e50491dc61ac45b1da4ad543c7588970f2c582ca81', 16, 0),
    'dawn_food': ('dawnlike', 'Items/Food.png', '4b52adbe3f4d17d94080755ab365571622824a838dbe80f170c4b4ae9ce4beec', 16, 0),
    'dawn_rock': ('dawnlike', 'Items/Rock.png', 'bc25f942b5ed2c0548366536fa2f7896549033a96e76b399e5f195c94e4eedfe', 16, 0),
    'dungeon': ('kenney_tiny-dungeon', 'Tilemap/tilemap_packed.png', 'd24e60a41e4ac7a745c0304dfde121143688557f40215f23221c29cfe683825f', 16, 0),
    'town': ('kenney_tiny-town', 'Tilemap/tilemap_packed.png', '3a54d99ecde790d4fdea207a3644cf130fc56fa838f1beb1507c185a95b8e902', 16, 0),
}


# The two UI packs are committed (CC0) under art/source/ui/<directory>, only the files the repack reads, so `--only ui` needs no --sources.
# key -> (pack directory under art/source/ui, path inside it, sha256)
UI_FILES = {
    'adventure-small': ('kenney_ui-pack-pixel-adventure', 'Tilesheets/Small tiles/Thin outline/tilemap_packed.png', 'e11a08abb13ddee96f01612ced167c944b30f3df4e22b4e301f0464129c7f254'),
    'pui-sheet': ('kenney_pixel-ui-pack', 'Spritesheet/UIpackSheet_transparent.png', '38cd7cabd9667ace6bdd9f561a50f697359eb9460e80adbcdde7f2a3e71efb1a'),
    'brown': ('kenney_pixel-ui-pack', '9-Slice/Ancient/brown.png', '5223c9086c245cbee564debb9c0bd95cb03d5df1e44859ed5a2fc4ccaf7790ed'),
    'brown_pressed': ('kenney_pixel-ui-pack', '9-Slice/Ancient/brown_pressed.png', '84d338ce6e30b072ca35dc86740eea1d747f7878a70ca8bc311f5b1016e5e1e2'),
    'grey': ('kenney_pixel-ui-pack', '9-Slice/Ancient/grey.png', '4b6eadb1565f2b7890001b19e978eaf64830efa618dae554b43d0af4122fccde'),
    'grey_pressed': ('kenney_pixel-ui-pack', '9-Slice/Ancient/grey_pressed.png', '38068e8040ed94573d85e0c8a11eda017d83562e7d73c81cd5e21803b660c648'),
    'tan': ('kenney_pixel-ui-pack', '9-Slice/Ancient/tan.png', 'f1b2c44c8e25ec31ee2417dbf8491d1a8a01321387e33e6b45f78354a79bfe89'),
    'tan_pressed': ('kenney_pixel-ui-pack', '9-Slice/Ancient/tan_pressed.png', '8c7b35f266e43aad98a70a72bc4aa701a73438ef5ed794d4659ff65c28204a65'),
    'yellow': ('kenney_pixel-ui-pack', '9-Slice/Colored/yellow.png', '43850f322d944faafcd0873c4434b3406dc85e19834a621695088c51a6f829c7'),
    'yellow_pressed': ('kenney_pixel-ui-pack', '9-Slice/Colored/yellow_pressed.png', 'b9958567f3324cea4cd61337922ff3e14c44e086fc3e69eddf7ccea429af8779'),
    'red': ('kenney_pixel-ui-pack', '9-Slice/Colored/red.png', '08f69b6ef9307ae3c39eb4e2c0c97ca72019c0afd4f28c5ba35140edd2d54600'),
    'red_pressed': ('kenney_pixel-ui-pack', '9-Slice/Colored/red_pressed.png', '0e0a2bb45f23cbfd92eae510e8410d4e96434c9a329d9ede23e1a764ec100edc'),
}


def ui_image(repo, key):
    """A committed UI pack file as an Image, checked against its pinned SHA-256."""
    pack, path, digest = UI_FILES[key]
    full = os.path.join(repo, 'art', 'source', 'ui', pack, path)
    with open(full, 'rb') as f:
        actual = hashlib.sha256(f.read()).hexdigest()
    if actual != digest:
        raise SystemExit(f'{full}: SHA-256 {actual} differs from the pinned {digest}')
    return pngio.read_png(full)


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
