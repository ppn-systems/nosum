# terrain/ — sources

Wang (corner) tilesets, 32 × 32 px, sliced one PNG per tile from the PixelLab sheet using each
tile's `bounding_box` from the tileset metadata. File name: `<set>_<NW><NE><SW><SE>`, one letter
per corner naming its terrain, so the corner data used for autotiling is in the name. All three
sets share the same grass (chained through the grass base tile
`12075806-b53e-484c-97ac-0ba99d15f67b`).

| Files | Letters | Generator | Prompt (lower → upper; transition) | Date | PixelLab id |
| :--- | :--- | :--- | :--- | :--- | :--- |
| `soil_grass_*.png` (16) | `s` soil, `g` grass | `create_topdown_tileset`, standard, 32 px, highly detailed, detailed shading, transition 0.25 | rich dark farm soil, neatly tilled rows with small clods and pebbles → soft meadow grass with varied green tufts, tiny clover and scattered small flowers; grass edge with short overhanging blades and a thin darker soil rim | 2026-10-25 | tileset `7c5ff2ab-f655-4422-b8fc-577a18073b96` |
| `water_grass_*.png` (16) | `w` water, `g` grass | same settings, transition 0.25 | clear teal pond water with soft ripples and light reflections → (same grass); sandy pebbly bank with reeds | 2026-10-25 | tileset `44b92a3a-825e-4cc5-9633-2824c4d49fd4` |
| `path_grass_*.png` (16) | `p` path, `g` grass | same settings, no transition | worn packed dirt footpath with small stones → (same grass) | 2026-10-25 | tileset `0480c6d2-1780-42bd-ab8a-e581cda02ef5` |
