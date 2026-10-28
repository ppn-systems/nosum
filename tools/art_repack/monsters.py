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
from pngio import Image

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


def poof_block(sheet):
    """Extracts a 64 x 48 4-column x 3-row block of death effects from Effect0:
    Row 0: smoke poof (row 9, cols 0..3)
    Row 1: dust burst (row 10, cols 0..3)
    Row 2: fire/spark explosion (row 6, cols 0..3)
    """
    block = Image(64, 48)
    for col in range(4):
        block.paste(sheet.cell(col, 9), col * 16, 0)
        block.paste(sheet.cell(col, 10), col * 16, 16)
        block.paste(sheet.cell(col, 6), col * 16, 32)
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
