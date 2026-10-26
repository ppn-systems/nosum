# ADR-042 — Own map layouts and removal of the former early content's traces before the public release

**Status:** Accepted (coordinator, under the owner's delegation, 2026-10-26). Supersedes ADR-040 D9 (layouts kept) and the placeholder parts of ADR-007 and

ADR-040. Builds on ADR-012 (maps and warps), ADR-015 and ADR-019 (private instances), ADR-020 (co-op land), ADR-026 (mine floors), ADR-028 (building
interiors), ADR-034 (trader counter), ADR-037 (outdoor maps).

## Context

The owner is publishing the project as a public repository. The public repository must contain only Nosum's own work, and nothing in it may show which game
the early content (ADR-007) came from. A measurement on `master` (`b0310b3`) shows three kinds of remaining derived material:

1. **Map layouts.** All 53 `.tmx` files under `art/own/Maps` keep the early content's level layouts. 43 are byte-identical to the early maps: `Barn`,
   `Coop`, `FarmHouse`, `Mine` and 39 of the 40 mine floors. The other 10 (`Backwoods`, `Beach`, `BusStop`, `Farm`, `Farm_Foraging`, `Forest`, `Mines/20`,
   `Mountain`, `Town`, `Woods`) had only their `Back` layer re-authored (ADR-040). The release gate does not see any of this, because it checks only
   the 95 listed paths under `assets/` (ADR-040 D8).
2. **Painted sheets and names.** The map sheets in `art/own/Maps/*.png` were painted in code, but from floor plans read off the early content's sheets, and
   they keep the early content's sheet names, grids and served paths. The same holds for `assets/` paths and the tileset names inside the maps (for example
   the item, crop, town and interior sheets). Item ids are Nosum's own scheme (`ItemCatalog`), but sprite cells index the early content's sheet grids.
3. **Text and tools.** About 41 mentions of the source game's name across code comments, XML docs, docs, ADRs, skills and workflows. Fingerprints and
   inventory (`docs/art/placeholders.sha256`, `docs/art/inventory.md`, `docs/art/provenance.md`). Two tools that exist only for the transition
   (`tools/Nosum.AssetInventory` and `tools/Nosum.MapReauthor`, with the `NOSUM_PLACEHOLDER_ROOT` paths), the early release gate
   (`deploy/check-placeholder-art.sh` and its workflow jobs), and `tests/Nosum.UnitTests/Maps/PlaceholderContent.cs` with the tests that read the early.

## Decision

- **D1 — Every map gets an own layout.** All 53 `.tmx` files are replaced. A replacement keeps the file's map id. It draws only from own tilesets (D3), and
  no layer derives from the early map. Byte-identity is not the test: copying a layout cell by cell onto new tiles counts as derived.

- **D2 — The gameplay contract stays, or moves with its code.** A replacement keeps every point the code and tests read, unless the same PR changes that
  code and those tests and lists each move:
  - spawn tiles, exits (the `Warp` property, extra and dropped warps), shop and counter tiles, gates and bridges: `GameMaps.cs`, `MapSpec.cs`,
    `MineWorld.cs`, `OutdoorWorld.cs`, `CoopLand.cs`;
  - the co-op land size (80 x 65) and the unlock rectangles in `CoopUpgrades.cs`, and the three Farm building footprints in `FarmProps.cs`;
  - the tile traits the rules read (`Water`, `Impassable`, `Diggable`, `Passable`), and the `Paths` node-site and forage marks (`NodeSite.cs`);
  - the mine contract: 40 floors, a ladder under one stone on floors 1–39, an elevator at the arrival tile every fifth floor, and ore by depth
    (`MineRules.cs`, `MineFloorPlanner.cs`); the arrival tiles in `MineLayouts.cs`;
  - the interior doors, spawns, animal areas and bench rows (`BuildingCatalog.cs`).

  Tests that pin tiles or counts (`GameMapsTests`, `MineMapsTests`, `OutdoorMapsTests`, `CoopLandMapTests`, `MineRulesTests`, `NodeSitesTests`,
  `ForageTests`, `MapChangeTests`, `CoopUpgradeTests`, `CoopLandTests`, `ChunkInterestTests`, `ChunkWorldTests`, `VisitWireTests`) change only together
  with the layout, in the same PR.

- **D3 — Own tilesets with own names.** The maps use own sheets with Nosum names, for example `terrain`, `interior`, `town`, `mine` → `cave`. Each sheet is
  drawn from scratch (in code like `own_terrain`, or by hand/PixelLab under ADR-040 D4), not from a floor plan of an early sheet. The old painted
  sheets are deleted. Tile traits live in Nosum's own tileset data. The client and server link sheets by their new names; links to old served paths are
  removed.

- **D4 — How the layouts are made.**
  - **Mines and the cave:** `tools/Nosum.MapGen mine` generates the 40 floors from a fixed seed per floor. Arrival tiles come from `MineLayouts`. The D2
    mine contract holds by construction and is then checked. The output is byte-stable, so `--check` fails a stale file. The cave entrance is hand-authored
    like an interior.
  - **Interiors and outdoor maps:** each layout is a text grid, `art/own/source/layouts/<Map>.txt` (one character per tile, with a legend), compiled into
    the `.tmx` by `tools/Nosum.MapGen layout`. The `.tmx` is generated, never edited. Layouts are new designs around the D2 points; mirroring, rotating or
    re-tiling an early layout is not allowed.

- **D5 — Sprite sheets and paths get own names.** Every file under `assets/` moves to a Nosum path and name, with a cell grid of Nosum's choosing. The code
  references (sprite indices in `ItemDefinition`, `CropDefinition`, `PlaceableCatalog`, the farmer layers, UI) follow in the same PR. Wire ids and item ids
  do not change: the wire format is append-only and the ids are already Nosum's.

- **D6 — The transition tooling and its traces are removed.** After D1–D5: delete `tools/Nosum.AssetInventory`, `tools/Nosum.MapReauthor`,
  `deploy/check-placeholder-art.sh` with its workflow jobs and MSBuild target, `docs/art/placeholders.sha256`, `docs/art/inventory.md`, the
  early sections of `docs/art/provenance.md`, `PlaceholderContent.cs` and the tests that read the early content. ADR-007 and ADR-040 are condensed into
  a short record of Nosum's own art pipeline: what the own sources are and how they are built. Every remaining mention of the source game in code, docs,
  skills and workflows is rewritten neutrally or deleted. A unit test replaces the gate: every tileset a map names and every file under `assets/` is listed
  in `art/own/SOURCES.md` with its own source, a tool or a licensed pack.

- **D7 — External provenance audit, not an in-repo gate.** The repository carries no fingerprints of the source content. Before publishing, the
  coordinator runs a one-off audit outside the repository against the private archive. It checks that no file in the tree is byte-identical to a former
  early file, and that no map layer matches a former map's layer cell by cell. The result is reported to the owner, not committed.

- **D8 — The public repository starts from fresh history.** Old commit messages and files name the source content, so stripping blobs is not enough. The
  current repository is renamed and stays private as the archive. The new public repository `nosum` starts with one commit of the cleaned tree. Secrets,
  variables, the GHCR package access and the GitHub App are set up again on the new repository (owner steps).

## Phasing

Each phase is one PR, small enough for one session. P1 lands first; after it, the map phases can run in parallel because they touch different files.

| Phase | Content | Done when |
|:--|:--|:--|
| P1 | `tools/Nosum.MapGen`: the layout compiler and the mine generator with `--check`, unit tests; the own tileset data format (D3) | The tool builds, its tests pass, no map changes |
| P2 | Own sheets `terrain`, `interior`, `cave`, `town` and the rest of D3 (code-drawn) | Sheets listed in `SOURCES.md`, no map uses them yet |
| P3 | Mines: 40 generated floors and the cave | Mine tests pass unchanged or with listed moves |
| P4 | Interiors: FarmHouse, Coop, Barn | Interior and building tests pass |
| P5a / P5b / P5c | Farm and co-op land / Town, BusStop, Backwoods / Forest, Mountain, Beach, Woods | Map, node and forage tests pass with listed moves |
| P6 | Sprite sheets and paths renamed (D5); the old painted sheets deleted | No link to an old path or sheet name remains |
| P7 | Tooling and trace removal (D6); the `SOURCES.md` test | `git grep -i` for the source game's name returns nothing |
| P8 | Full browser acceptance on the final master; external audit (D7); publish (D8) | Audit clean, acceptance passes, the owner flips the repository public |

**Definition of done:** every phase is merged; all 8 `.slnx` build with 0 warnings; `tests/Nosum.Tests.slnx` passes twice; the browser acceptance passes at
1280x720 and 1920x1080 in vi and en with no console errors; the D7 audit is clean.

## Alternatives considered

- **Keep the layouts, only rewrite history:** the current tree itself still holds the derived maps and names. Rejected.
- **Commit a fingerprint list and an in-repo hash gate:** the list itself reveals the source. Rejected for D7.
- **Rewrite history with blob stripping on this repository:** old PR refs stay on GitHub, and commit messages still name the source. Rejected for D8.
- **Random generation for every map:** the town, the farm and the interiors have named points that players and code rely on. Generation is used for the
  mines only.

## Consequences

- The public release waits for P1–P8. The game keeps running from the private repository meanwhile.
- Test pins on tiles and counts change phase by phase, each listed in its PR.
- The transition tools and the early gate disappear; `SOURCES.md` and its test become the provenance record.
- After D8, PR numbers and issues start again in the new repository.

## Revisit when

A phase shows the D2 contract is too strict; the owner wants the derived painted sheets kept; the publication plan (D8) changes.
