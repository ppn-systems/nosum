#!/usr/bin/env python3
"""Rebuilds the replaced art files under assets/ from CC0 packs (docs/adr/ADR-040, docs/art/sourcing.md).

    python3 tools/art_repack/repack_cc0_art.py --sources <dir with the unzipped packs> [--repo <root>] [--check] [--docs]
    python3 tools/art_repack/repack_cc0_art.py --only farmer|ui [--repo <root>] [--check]    # drawn files only, no packs needed

Each replaced file keeps the path, the pixel size and the grid of the early file it replaces; the repack places CC0 sprites (and a few
pictures drawn in this folder) into the cells the game draws. The same sources always give the same bytes. --check writes nothing and
exits 1 when a file on disk differs; the UI package also writes the palette block of nosum-theme.css (ui.texts); --docs rewrites docs/art/provenance.md from the table in provenance.py.
Python 3 standard library only.
"""
import argparse
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)

import animals  # noqa: E402
import buildings  # noqa: E402
import crops  # noqa: E402
import farmer  # noqa: E402
import furniture  # noqa: E402
import icons  # noqa: E402
import trees  # noqa: E402
import ui  # noqa: E402
import pngio  # noqa: E402
import provenance  # noqa: E402
import sources  # noqa: E402
import tools  # noqa: E402


def size_of(path):
    img = pngio.read_png(path)
    return img.width, img.height


def build_all(src, repo, only=None):
    """{path under assets/: Image} for every replaced file (only='farmer': the files drawn without any pack)."""
    assets = os.path.join(repo, 'assets')

    def size(rel):
        return size_of(os.path.join(assets, rel))

    out = {}
    if only == 'ui':
        return ui.build(size)
    out.update(farmer.build(size))
    if only == 'farmer':
        return out
    out['sprites/crops.png'] = crops.build_crops(src, repo)
    out['sprites/soil.png'] = crops.build_soil(*size('sprites/soil.png'))
    out['sprites/tools.png'], out['sprites/weapons.png'] = tools.build(src, repo, size('sprites/tools.png'), size('sprites/weapons.png'))
    out.update(buildings.build(src, size))
    out.update(trees.build(size))
    out.update(furniture.build(size))
    out['sprites/items.png'] = icons.build(src, size)
    out.update(animals.build(size))
    out.update(ui.build(size))
    return out


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('--sources')
    parser.add_argument('--only', choices=['farmer', 'ui'])
    parser.add_argument('--repo', default=os.path.abspath(os.path.join(HERE, '..', '..')))
    parser.add_argument('--check', action='store_true')
    parser.add_argument('--docs', action='store_true')
    args = parser.parse_args()

    if not args.only and not args.sources:
        parser.error('--sources is required unless --only is given')
    src = sources.Sources(args.sources) if args.sources else None
    images = build_all(src, args.repo, args.only)
    stale = []
    for rel, img in sorted(images.items()):
        target = os.path.join(args.repo, 'assets', rel)
        if args.check:
            current = pngio.read_png(target)
            if current.px != img.px:
                stale.append(rel)
        else:
            pngio.write_png(target, img)
            print('wrote', rel)
    for rel, text in sorted(ui.texts(args.repo).items()):
        target = os.path.join(args.repo, rel)
        with open(target, encoding='utf-8', newline='') as f:
            same = f.read() == text
        if args.check:
            if not same:
                stale.append(rel)
        elif not same:
            with open(target, 'w', encoding='utf-8', newline='') as f:
                f.write(text)
            print('wrote', rel)
    if args.docs:
        provenance.write(args.repo, sorted(provenance.ENTRIES))
    if args.check:
        for rel in stale:
            print('differs:', rel)
        return 1 if stale else 0
    return 0


if __name__ == '__main__':
    sys.exit(main())
