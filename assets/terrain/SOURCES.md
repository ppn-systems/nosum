# terrain/ — sources

Wang (corner) tilesets, 32 × 32 px, sliced one PNG per tile from the PixelLab sheet using each
tile's `bounding_box` from the tileset metadata. File name: `<set>_<NW><NE><SW><SE>`, one letter
per corner naming its terrain, so the corner data used for autotiling is in the name.

| Files | Letters | Generator | Prompt (lower → upper) | Date | PixelLab id |
| :--- | :--- | :--- | :--- | :--- | :--- |
| `soil_grass_*.png` (16) | `s` soil, `g` grass | `create_topdown_tileset`, standard, 32 px, medium detail, basic shading | tilled farm soil, dark brown earth with furrows → lush green grass | 2026-10-25 | tileset `5bb35746-b93a-4e57-a95f-30e839c0f10e` |
| `water_grass_*.png` (16) | `w` water, `g` grass | `create_topdown_tileset`, standard, 32 px, medium detail, basic shading, upper chained to the grass base tile `65f87c9b-0cb8-454d-b4c3-974b5511327e` | clear blue pond water with gentle ripples → lush green grass | 2026-10-25 | tileset `9b6f1b46-7647-46a4-8329-d838b1324145` |
