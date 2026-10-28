"""The packs the repack reads (CC0, plus CC-BY DawnLike and Tiny 16), kept under art/source/ (docs/art/library.md), with the sheet files pinned by SHA-256 so a run is reproducible.

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
    'kenney_tiny-battle': ('Kenney Tiny Battle', 'https://kenney.nl/assets/tiny-battle', 'CC0 1.0 (License.txt inside the archive)', 'Kenney'),
    'kenney_roguelike-rpg-pack': ('Kenney Roguelike/RPG Pack', 'https://kenney.nl/assets/roguelike-rpg-pack', 'CC0 1.0 (License.txt inside the archive)', 'Kenney'),
    'kenney_roguelike-characters': ('Kenney Roguelike Characters', 'https://kenney.nl/assets/roguelike-characters', 'CC0 1.0 (License.txt inside the archive)', 'Kenney'),
    'kenney_roguelike-indoors': ('Kenney Roguelike Indoors', 'https://kenney.nl/assets/roguelike-indoors', 'CC0 1.0 (License.txt inside the archive)', 'Kenney'),
    'kenney_roguelike-caves-dungeons': ('Kenney Roguelike Caves & Dungeons', 'https://kenney.nl/assets/roguelike-caves-dungeons', 'CC0 1.0 (License.txt inside the archive)', 'Kenney'),
    'kenney_roguelike-modern-city': ('Kenney Roguelike Modern City', 'https://kenney.nl/assets/roguelike-modern-city', 'CC0 1.0 (License.txt inside the archive)', 'Kenney'),
    'kenney_rpg-urban-pack': ('Kenney RPG Urban Pack', 'https://kenney.nl/assets/rpg-urban-pack', 'CC0 1.0 (License.txt inside the archive)', 'Kenney'),
    'kenney_1-bit-pack': ('Kenney 1-Bit Pack', 'https://kenney.nl/assets/1-bit-pack', 'CC0 1.0 (License.txt inside the archive)', 'Kenney'),
    'oga_tiny-creatures': ('Tiny Creatures 1.0', 'https://opengameart.org/content/tiny-creatures', 'CC0 1.0 (License.txt inside the archive)', 'Clint Bellanger'),
    'oga_trash-mobs': ('16x16 Fantasy RPG Trash Mobs (animated)', 'https://opengameart.org/content/16x16-fantasy-rpg-trash-mobs-animated', 'CC0 (stated on the OpenGameArt page)', 'Emcee Flesher'),
    'oga_animated-slimes': ('16x16 Animated Slimes', 'https://opengameart.org/content/16x16-animated-slimes', 'CC0 (stated on the OpenGameArt page)', 'stealthix'),
    'oga_16x16-dungeon-tiles': ('16x16 Dungeon Tiles', 'https://opengameart.org/content/16x16-dungeon-tiles', 'CC0 (stated on the OpenGameArt page)', 'ETTiNGRiNDER'),
    'oga_puny-dungeon': ('16x16 Puny Dungeon Tileset v1', 'https://opengameart.org/content/16x16-puny-dungeon-tileset', 'CC0 (stated on the OpenGameArt page)', 'Shade'),
    'oga_assorted-rpg-icons': ('16x16 Assorted RPG Icons', 'https://opengameart.org/content/16x16-assorted-rpg-icons', 'CC0 (stated on the OpenGameArt page)', 'Shade'),
    'oga_weapon-rpg-icons': ('16x16 Weapon RPG Icons', 'https://opengameart.org/content/16x16-weapon-rpg-icons', 'CC0 (stated on the OpenGameArt page)', 'Shade'),
    'oga_gold-treasure-icons': ('Gold treasure icons 16x16', 'https://opengameart.org/content/gold-treasure-icons-16x16', 'CC0 (stated on the OpenGameArt page)', 'Bonsaiheldin'),
    'oga_16x16-rpg-items': ('16x16 RPG items', 'https://opengameart.org/content/16x16-rpg-items', 'CC0 (stated on the OpenGameArt page)', 'Jetrel'),
    'oga_random-animals': ('Random animals', 'https://opengameart.org/content/random-animals', 'CC0 (stated on the OpenGameArt page)', 'hdst'),
    'oga_pixel-animals-16x16': ('Pixel Animals 16x16', 'https://opengameart.org/content/pixel-animals-16x16', 'CC0 (stated on the OpenGameArt page)', 'GrumpyDiamond'),
    'oga_6-color-dungeon': ('6 Color Dungeon 16x16', 'https://opengameart.org/content/6-color-dungeon-16x16', 'CC0 (stated on the OpenGameArt page)', 'HorusKDI'),
    'dawnlike': ('DawnLike 16x16 Universal Rogue-like tileset v1.81', 'https://opengameart.org/content/dawnlike-16x16-universal-rogue-like-tileset-v181', 'CC-BY 4.0 (README.txt inside the archive); credit DragonDePlatino and DawnBringer (palette)', 'DragonDePlatino (palette: DawnBringer)'),
    'tiny16': ('Tiny 16 (Basic, Dungeon, Overworld sheets)', 'https://opengameart.org/content/tiny-16-basic', 'CC-BY 4.0 (stated on the OpenGameArt page); credit Sharm (Lanea Zimmerman)', 'Sharm (Lanea Zimmerman)'),
    'kenney_ui-pack-pixel-adventure': ('Kenney UI Pack - Pixel Adventure 2.0', 'https://kenney.nl/assets/ui-pack-pixel-adventure', 'CC0 1.0 (License.txt inside the archive; archive SHA-256 0b0ed4802ebcfff5e44e370f394baa1d751862a5a4a7612ac4ce84e85faa0627)', 'Kenney'),
    'kenney_pixel-ui-pack': ('Kenney Pixel UI pack', 'https://kenney.nl/assets/pixel-ui-pack', 'CC0 1.0 (License.txt inside the archive; archive SHA-256 b76f2f60f2be76eb8e66511038fa4d48faec11e638316fa37d06084878cdf0c7)', 'Kenney, with Lynn Evers'),
    'josehzz_farming-crops-16x16': ('Farming crops 16x16', 'https://opengameart.org/content/farming-crops-16x16', 'CC0 (stated on the OpenGameArt page; the archive holds no license file)', 'josehzz'),
}

# key -> (pack directory, path inside it, sha256, cell size, margin between cells[, optional key_color tuple])
SHEETS = {
    'crops': ('josehzz_farming-crops-16x16', 'Crop_Spritesheet.png', 'c98fe9cd765698738678f4a64c5ac51c713c63624ca73da05bea7be995946c46', 16, 0),
    'farm': ('kenney_tiny-farm', 'Tilemap/tilemap_packed.png', '0c4b3b4058cacf6a9bf021666838156d46da9ce27500d4c13e30cd5a92b722f4', 16, 0),
    'creatures': ('oga_tiny-creatures', 'tiny-creatures/Tilemap/tilemap_packed.png', '1fd68a8434e3269bb10a76e50491dc61ac45b1da4ad543c7588970f2c582ca81', 16, 0),
    'dawn_food': ('dawnlike', 'Items/Food.png', '4b52adbe3f4d17d94080755ab365571622824a838dbe80f170c4b4ae9ce4beec', 16, 0),
    'dawn_rock': ('dawnlike', 'Items/Rock.png', 'bc25f942b5ed2c0548366536fa2f7896549033a96e76b399e5f195c94e4eedfe', 16, 0),
    'dungeon': ('kenney_tiny-dungeon', 'Tilemap/tilemap_packed.png', 'd24e60a41e4ac7a745c0304dfde121143688557f40215f23221c29cfe683825f', 16, 0),
    'town': ('kenney_tiny-town', 'Tilemap/tilemap_packed.png', '3a54d99ecde790d4fdea207a3644cf130fc56fa838f1beb1507c185a95b8e902', 16, 0),
    'battle': ('kenney_tiny-battle', 'Tilemap/tilemap_packed.png', 'd8b164ef0e8db6429407dca2e68669c76f57df08ddb3e8ec4278eb26556f5656', 16, 0),
    'roguelike_rpg': ('kenney_roguelike-rpg-pack', 'Spritesheet/roguelikeSheet_transparent.png', '808c8d734480bc949b284f3bcc252e87e1ee53b24178791a6838cce6a319c439', 16, 1),
    'roguelike_chars': ('kenney_roguelike-characters', 'Spritesheet/roguelikeChar_transparent.png', '771d51ca84b2c5b412027ba80c6a6b8a98bb5a983b2c58ac4f0ccf08ae4d10f2', 16, 1),
    'roguelike_indoors': ('kenney_roguelike-indoors', 'Tilesheets/roguelikeIndoor_transparent.png', 'ade984d5cbe3f16d9e23e6dc516246649de1a7eec2540cf31d53852b690c446f', 16, 1),
    'roguelike_caves': ('kenney_roguelike-caves-dungeons', 'Spritesheet/roguelikeDungeon_transparent.png', '3abd8d294c523fe78078e2123146a2b9d2c0f2dd3cfbbd60dec8f508c3fe8c2c', 16, 1),
    'modern_city': ('kenney_roguelike-modern-city', 'Tilemap/tilemap_packed.png', 'e8b653bd3f32a537a2634eaa55d6abb97f20e09fd974bc1278a2ea55c1cda59f', 16, 0),
    'rpg_urban': ('kenney_rpg-urban-pack', 'Tilemap/tilemap_packed.png', '305e71fa778444a0858c33988d45a6b79ea923f500f96079f4378347cf6ad389', 16, 0),
    'one_bit': ('kenney_1-bit-pack', 'Tilesheet/colored-transparent_packed.png', '801243b8b35bcfde727bd52447bcae5c2abf36b0ae2f3ac7ee54f91791575e74', 16, 0),
    'trash_mobs': ('oga_trash-mobs', 'oga_trash-mobs.png', '21d50e7c6d0c7f546569bddbd36cc7b46e33faf51d4a91770053a79bcbf16fd6', 16, 0),
    'slime_green': ('oga_animated-slimes', 'Slime_Small_Green.png', '9a5bad4dda4ac370430d814dbe20dcd164aa7e42bd1ab1cabf9235aec2efb254', 32, 0),
    'slime_blue': ('oga_animated-slimes', 'Slime_Small_Blue.png', 'f1d75d3c35fc4a6ec81735c24a0355f4e34c00c7f68728eb0044e65e1bdcdfea', 32, 0),
    'slime_red': ('oga_animated-slimes', 'Slime_Small_Red.png', '9f19829aacae6cdd8fee9ab97c514a33b8babb878463a9141793c5a729c2450b', 32, 0),
    'dungeon_diamond': ('oga_16x16-dungeon-tiles', 'diamond_1.png', '8ecc7e434b59d0f77c0065c54e873498c498ec604de639d9daa168983e2bf9b7', 16, 0, (14, 12, 16)),
    'puny_dungeon': ('oga_puny-dungeon', 'PUNY_DUNGEON_v1/punyworld-dungeon-tileset.png', '0bec8a1262360c8828e450760551754d7cb734ae51b952df9e7100a656b656a8', 16, 0),
    'rpg_potions': ('oga_assorted-rpg-icons', '16x16 Assorted RPG Icons/potions.png', 'd3fc2937aec92152f071597c194c9fb8ad8303d6739805770490a3b1c747b937', 16, 0),
    'rpg_books': ('oga_assorted-rpg-icons', '16x16 Assorted RPG Icons/books.png', 'afa280ed05c1e2392d5872e1734c82d60562b3787b9b9e3d2df56eb0f6e7046c', 16, 0),
    'weapons_bronze': ('oga_weapon-rpg-icons', '16x16 Weapons RPG Icons/bronze-weapons.png', 'b2eb15ab6ab9e85f16817c0458b3a3e762819ea73dfc36acad36d5c1d5adf9e5', 16, 0),
    'weapons_iron': ('oga_weapon-rpg-icons', '16x16 Weapons RPG Icons/iron-weapons.png', '9f520f628e59e578cfd91fbc614cd5a9b8f4b982a6d45354ebb03e88ad6dcf2c', 16, 0),
    'weapons_steel': ('oga_weapon-rpg-icons', '16x16 Weapons RPG Icons/steel-weapons.png', '257ef941fec1bfd8a3a86f080d91a9d4a2a100de292f32e59e35960a52356bc7', 16, 0),
    'weapons_gold': ('oga_weapon-rpg-icons', '16x16 Weapons RPG Icons/gold-weapons.png', '020cf63a509a31a59df2142e319b25382433859580b5b3df2a27e549d7b013bd', 16, 0),
    'gold_treasure': ('oga_gold-treasure-icons', 'sheet.png', '76ac74ac1634473247c7fa1c1acc4a05f4ba237b7caa68f538038cef994c9eb6', 16, 0),
    'rpg_items': ('oga_16x16-rpg-items', 'oga_16x16-rpg-items.png', '46281291a03802fd6517194813f47227f0e138a855709a243e910f2d9ac9bf33', 16, 0, (111, 119, 109)),
    'animal_bee': ('oga_random-animals', 'bee.png', 'd241ee7e8da5ecf0aeb16c5baf4eb45418f997208597bec217faf059d856ac27', 16, 0),
    'pixel_animals': ('oga_pixel-animals-16x16', 'oga_pixel-animals-16x16.png', 'f48431946c9e5d8ff0b7671b4abd032a90f22d3f6fdfc1c7d492588687ecf9c8', 16, 0),
    'six_color_dungeon': ('oga_6-color-dungeon', 'oga_6-color-dungeon.png', '6ce8628fb4337669fbd4346d6e2ded3908982a3cc9aa100f9f1df5bee9ade92d', 16, 0, (46, 36, 64)),
    'tiny16_dungeon': ('tiny16', 'dungeon.png', '272a421c38b3f0f7da77cd59975d6ceeb7d14cdbb3f0fd082f9d35d2dac698db', 16, 0, (117, 113, 97)),
    'dawn_armor': ('dawnlike', 'Items/Armor.png', '633032aafe520932c71802da6e9abfee0be711fa2e3313a60a45f71689a5e256', 16, 0),
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
            entry = SHEETS[key]
            pack, path, digest, size, margin = entry[:5]
            key_color = entry[5] if len(entry) > 5 else None
            full = os.path.join(self.root, pack, path)
            with open(full, 'rb') as f:
                actual = hashlib.sha256(f.read()).hexdigest()
            if actual != digest:
                raise SystemExit(f'{full}: SHA-256 {actual} differs from the pinned {digest}; the pack changed or is not the one in docs/art/sourcing.md')
            img = pngio.read_png(full)
            if key_color is not None:
                img = img.map_pixels(lambda p: pngio.TRANSPARENT if p[:3] == key_color else p)
            self._sheets[key] = Sheet(img, size, margin)
        return self._sheets[key]
