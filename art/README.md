# art/

Nosum's **own** art, as it replaces the placeholder content in [`assets/`](../assets/README.md)
([ADR-040](../docs/adr/ADR-040-own-art-plan.md)). Nothing here is Stardew Valley content. Every file has a
recorded provenance in [`own/SOURCES.md`](own/SOURCES.md).

| Folder | Holds |
| :--- | :--- |
| `own/Maps/` | Re-authored `.tmx` maps, the own terrain tileset (`own_terrain.png`) and the own tilesheets painted for the drawn layers. A map here has the same id, size, collision, warps and object groups as its placeholder; only the art differs. |
| `own/source/` | The generated source art the maps' tilesets are composed from (PixelLab corner tilesets and their metadata). |
| `own/reports/` | Per map and layer, how many cells come from the own terrain tileset and how many from the painted sheets. |

## Which map the game loads

`wasm/Nosum.Wasm/Nosum.Wasm.csproj` and `server/Nosum.Host/Nosum.Host.csproj` link each map file to the path the code loads
(`assets/Maps/<name>.tmx` on the web side, `maps/<name>.tmx` on the server). **Every hosted map has an own file** in `own/Maps/` (same relative path,
`Mines/3.tmx` included) and is linked from there; nothing in the code changes. The own `.tmx` keeps the placeholder's tilesheet names, and the client links
the own sheet of each name from `own/Maps/` to the placeholder's served path (`own/Maps/spring_town.png` is served as `assets/Maps/spring_town.png`), so every
tile index of every layer draws own art while the layers, tile properties and animations are the placeholder's. The placeholder maps and sheets stay in
`assets/` as the input of the tool; they are no longer linked into the client or the server, and `Nosum.AssetInventory --release` counts a map or a
`Maps/` sheet with an own file as replaced. What is left on a placeholder is the farmer (`assets/Characters/Farmer/`).

The tool that produces these files is [`tools/Nosum.MapReauthor`](../tools/README.md): `reauthor all` writes the maps, the reports and the sheets.
