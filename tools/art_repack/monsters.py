"""Package 11 (ADR-045, ADR-047): sprites/monsters.png (64 x 576), twelve 4-column x 3-row directional blocks of 16 x 16 frames.

Each block is 64 x 48 (four walk frames across, rows Down, Right, Up; Left is the Right row mirrored by MonstersDecorator):
  Block 0 (y =   0): GreenSlime           <- oga_animated-slimes (Slime_Small_Green.png)
  Block 1 (y =  48): FrostSlime           <- oga_animated-slimes (Slime_Small_Blue.png)
  Block 2 (y =  96): MagmaSlime           <- oga_animated-slimes (Slime_Small_Red.png)
  Block 3 (y = 144): CaveBat              <- oga_trash-mobs (columns 4..5, rows 0/2/3)
  Block 4 (y = 192): SporeShroom          <- oga_trash-mobs (columns 6..7, rows 0/2/3)
  Block 5 (y = 240): GoblinScout          <- oga_tiny-creatures (tile 0, 1)
  Block 6 (y = 288): SkeletonWarrior      <- oga_tiny-creatures (tile 1, 0)
  Block 7 (y = 336): StoneGolem           <- oga_tiny-creatures (tile 4, 1)
  Block 8 (y = 384): BossBroodmother      <- oga_trash-mobs (columns 2..3, rows 0/2/3)
  Block 9 (y = 432): BossGoblinChieftain  <- oga_tiny-creatures (tile 2, 1)
  Block 10(y = 480): BossMagmaColossus    <- oga_trash-mobs (columns 12..13, rows 0/2/3)
  Block 11(y = 528): BossGolemKing        <- oga_tiny-creatures (tile 4, 2)
"""
from animals import chicken_frames
from draw import Canvas
from pngio import Image
from trees import blob

WIDTH = 64
HEIGHT = 624


def slime_block(sheet):
    """Extracts a 64 x 48 4-column x 3-row (Down, Right, Up) block from a 64 x 128 Slime_Small sheet (32 x 32 cells)."""
    block = Image(64, 48)
    # Slime_Small rows: 0 = Down, 1 = Up, 2 = Right, 3 = Left; each row has 2 frames in 32 x 32 cells with the 16 x 16 sprite at (+8, +4).
    source_rows = (0, 2, 1)
    for out_row, src_row in enumerate(source_rows):
        f0 = sheet.cell(0, src_row).crop(8, 4, 16, 16)
        f1 = sheet.cell(1, src_row).crop(8, 4, 16, 16)
        for col, frame in enumerate((f0, f1, f0, f1)):
            block.paste(frame, col * 16, out_row * 16)
    return block


def trash_mob_block(sheet, base_col):
    """Extracts a 64 x 48 4-column x 3-row (Down, Right, Up) block from oga_trash-mobs.png (2 frames per direction)."""
    block = Image(64, 48)
    # oga_trash-mobs rows: 0 = Down, 1 = Left, 2 = Right, 3 = Up
    source_rows = (0, 2, 3)
    for out_row, src_row in enumerate(source_rows):
        f0 = sheet.cell(base_col, src_row)
        f1 = sheet.cell(base_col + 1, src_row)
        for col, frame in enumerate((f0, f1, f0, f1)):
            block.paste(frame, col * 16, out_row * 16)
    return block


SMOKE = ((135, 145, 165), (195, 205, 220), (242, 246, 252))
DUST = ((155, 115, 75), (210, 170, 115), (245, 220, 165))
FIRE = ((185, 55, 35), (245, 130, 45), (255, 225, 95))


def _fx_frame(tones, col, kind):
    c = Canvas(16, 16)
    if col == 0:
        blob(c, 8, 8, 3.2, 3.2, tones)
        c.rect(7, 7, 2, 2, tones[2])
    elif col == 1:
        blob(c, 8, 8, 4.6, 4.4, tones)
        blob(c, 6, 6, 2.4, 2.4, (tones[1], tones[2], tones[2]))
    elif col == 2:
        for cx, cy, r in ((6, 6, 2.8), (10, 6, 2.6), (6, 10, 2.5), (10, 10, 2.7)):
            blob(c, cx, cy, r, r, tones)
        if kind == 'fire':
            c.rect(7, 7, 2, 2, (255, 250, 180))
    else:
        for cx, cy in ((4, 5), (11, 5), (5, 11), (11, 11)):
            c.rect(cx, cy, 2, 2, tones[1])
            c.dot(cx, cy, tones[2])
        if kind != 'smoke':
            c.dot(8, 3, tones[2])
            c.dot(8, 12, tones[1])
    return c.outline()


def poof_block(_sheet):
    """Builds a 64 x 48 4-column x 3-row block of 16 x 16 combat/death effects:
    Row 0: smoke poof (cols 0..3)
    Row 1: dust burst (cols 0..3)
    Row 2: fire/spark explosion (cols 0..3)
    """
    block = Image(64, 48)
    for col in range(4):
        block.paste(_fx_frame(SMOKE, col, 'smoke'), col * 16, 0)
        block.paste(_fx_frame(DUST, col, 'dust'), col * 16, 16)
        block.paste(_fx_frame(FIRE, col, 'fire'), col * 16, 32)
    return block


def build(src):
    out = Image(WIDTH, HEIGHT)
    creatures = src.sheet('creatures')
    trash = src.sheet('trash_mobs')
    effects = src.sheet('dawn_effects')
    blocks = [
        slime_block(src.sheet('slime_green')),
        slime_block(src.sheet('slime_blue')),
        slime_block(src.sheet('slime_red')),
        trash_mob_block(trash, 4),
        trash_mob_block(trash, 6),
        chicken_frames(creatures.cell(0, 1)),
        chicken_frames(creatures.cell(1, 0)),
        chicken_frames(creatures.cell(4, 1)),
        trash_mob_block(trash, 2),
        chicken_frames(creatures.cell(2, 1)),
        trash_mob_block(trash, 12),
        chicken_frames(creatures.cell(4, 2)),
        poof_block(effects),
    ]
    for i, block in enumerate(blocks):
        out.paste(block, 0, i * 48)
    return {'sprites/monsters.png': out}
