# art/

Nosum's **own** art, as it replaces the placeholder content in [`assets/`](../assets/README.md)
([ADR-040](../docs/adr/ADR-040-own-art-plan.md)). Nothing here is Stardew Valley content. Every file has a
recorded provenance in [`own/SOURCES.md`](own/SOURCES.md).

| Folder | Holds |
| :--- | :--- |
| `own/Maps/` | Re-authored `.tmx` maps and the own terrain tileset they use. A map here has the same id, size, collision, warps and object groups as its placeholder; only the drawn terrain differs. |
| `own/source/` | The generated source art the maps' tilesets are composed from (PixelLab corner tilesets and their metadata). |
| `own/reports/` | What each re-authored map still draws from placeholder tiles in its ground layer. |

## Which map the game loads

`wasm/Nosum.Wasm/Nosum.Wasm.csproj` and `server/Nosum.Host/Nosum.Host.csproj` link each map file to the path the code
loads (`assets/Maps/<name>.tmx` on the web side, `maps/<name>.tmx` on the server). A map with an own file is linked from
`art/own/Maps/` instead of `assets/Maps/`; nothing else changes. The re-authored `.tmx` still names the placeholder
sheets of the layers that are not terrain (buildings, trees, fences), so the placeholder sheets stay linked.

| Map id | File | Source placeholder |
| :--- | :--- | :--- |
| `farm` | `own/Maps/Farm.tmx` | `assets/Maps/Farm.tmx` |

The tool that produces these files is [`tools/Nosum.MapReauthor`](../tools/README.md).
