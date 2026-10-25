# wasm/

The Nosum browser client, Blazor WebAssembly. Blazor renders the UI overlay (inventory, shop,
chat); the game world is drawn on a `<canvas>` by the engine
([ADR-002](../docs/adr/ADR-002-batched-render-interop.md)).

| Project | Holds |
| :--- | :--- |
| `Nosum.Wasm` | Blazor host: `Program.cs`, `App.razor`, pages by domain, `wwwroot/` |
| `Nosum.Wasm.ViewModels` | MVVM view models, by domain; no `.razor` files |
| `Nosum.Wasm.Socket` | The single Nalix `WebSocketSession`: `GameSocket`/`IGameSocket` (connect, handshake, reconnect with backoff, typed requests, push subscriptions that survive reconnects) and `Player/PlayerChannel` (join, 10 Hz move reports, hand-off of the server's place (`TryTakeEntry`: after a join and after every `MapChanged`) and of corrections to the frame loop) |

`Directory.Build.props` here turns AOT off and trimming on, and keeps `.pdb`/`.xml` out of the
published bundle (everything in it is downloaded by every player).

Skills: [`blazor-ui`](../.claude/skills/blazor-ui/SKILL.md),
[`render-interop`](../.claude/skills/render-interop/SKILL.md),
[`nalix-client-sdk`](../.claude/skills/nalix-client-sdk/SKILL.md).

Account UI: `Components/Account/AccountForm.razor` is a reusable login/register form bound to
`AccountFormViewModel` (`Nosum.Wasm.ViewModels/Account`). Its text is in
`Nosum.Wasm.ViewModels/Resources/Language.resx` (Vietnamese) and `Language.en.resx`. `Pages/Home.razor`
shows it first; the game starts after sign-in (the canvas listens to the whole window's keys).
`GameSocketAccountClient` implements `IAccountClient` over `IGameSocket`; `AccountSessionKeeper`
applies the account language on sign-in and signs the connection in again (resume token, kept in
memory only) after every reconnect.

Multiplayer ([ADR-008](../docs/adr/ADR-008-snapshot-movement-sync.md)): the server address lives in
`GameSocketOptions` (same host as the page, port 57300, `/ws/`; `ForPageHost`). `Program.cs` creates
one `GameSocket` and one `PlayerChannel`. `Game/MapScene` predicts the local farmer, hands its state
to `PlayerChannel.Report` (no await, no allocation), snaps back on a `MoveCorrection`, and adds the
other farmers (`Game/RemotePlayers`: a `SnapshotBuffer` from `Nosum.Core.Interpolation`, drawn 200 ms
in the past) to the same `WorldSpriteBatch` so they y-sort with the local farmer and the buildings.
Without a server the farm still plays offline. Remote farmers are drawn from the looks the server sent (`LookChannel`, resolved by `Game/LookSprites` and `LookPalette`; ADR-014). `ConnectionBadge` shows the connection state in the current language.

Playable now: `Pages/Home.razor` starts `Game/GameLoop`, which waits (up to 10 s) for the server's join answer, loads
that map and the farmer sheets, then runs `Game/MapScene`: walk with WASD or the arrow keys, the camera follows,
layers draw as ground (`TileLayerPlan`) → decorators on the ground → depth-sorted world (farmer, other farmers,
decorators' buildings) → decorators in front (roof parts) → front layers. Animated tiles and a water ripple run from
the scene clock.

Many maps ([ADR-012](../docs/adr/ADR-012-maps-and-warps.md)): `Game/MapLoader` loads a map of `GameMaps` once
(`.tmx`, tilesheets through `Game/TextureCache`, one texture id per image file) into a `MapContent`, in the background;
it prefetches the maps the current one leads to. The server decides map changes: on `MapChanged` the scene fades out,
freezes the farmer (no reports), waits without blocking until the map is loaded, swaps and fades in. Map-specific
drawing plugs in as an `IMapDecorator` made by an `IMapDecoratorProvider` registered where `GameLoop` builds the loader
(`PropDecoratorProvider` draws a map's buildings from its `MapSpec`; `FarmDecoratorProvider` adds soil, crops and the target cursor on `MapSpec.Farmable` maps). The maps
and tilesheets the client can load are linked from `assets/Maps/` in `Nosum.Wasm.csproj`; building sheets from
`assets/Buildings/`. Without a server the farm still plays offline (no map changes).

Farming ([ADR-011](../docs/adr/ADR-011-inventory-farming-data-model.md)): `Nosum.Wasm.Socket` has `InventoryChannel`, `FarmChannel` and `QuestChannel`, client mirrors of the
server's state loaded after sign-in (inventory, quests) and after every join or map change (farm) and kept current by pushes. `FarmChannel` also estimates the server clock; the
farm decorator computes each crop's stage with `FarmRules` from the mirrored timestamps. Keys reach the frame as batched edges (`InputEdges`, packed with the held directions by `InputPacking`;
`renderer.js` ignores keys typed into form fields): `E` or `Space` uses the held item on the tile in front (`Game/PlayerActions`), `1`-`9` select a hotbar slot, `I` opens the inventory.
View models (`InventoryViewModel`, `QuestTrackerViewModel`, `GameMessagesViewModel`) localize everything; item, crop and quest text is in `Resources/Catalogue.resx` by catalogue key.
`Nosum.Wasm.Controls` is the reusable Razor library: `Hotbar`, `ItemSlotButton`, `InventoryPanel`, `QuestTracker`, `GameMessage`, all following a view model through `ObservingComponent`.
Their scoped CSS reaches the page through `Nosum.Wasm.styles.css`. Item pictures are `Game/ItemIcons` (sheet and cell), crop frames `Game/CropSprites`.
Social layer ([ADR-013](../docs/adr/ADR-013-economy-coins-escrow-market-trade.md), [ADR-014](../docs/adr/ADR-014-coops-and-looks.md)): channels `WalletChannel`, `MarketChannel`, `LookChannel`, `TradeChannel`, `CoopChannel`
and `Player/NearbyPlayers` (the other players on the map, for pickers) mirror the server; `WalletViewModel`, `MarketViewModel`, `WardrobeViewModel`, `TradeViewModel`, `CoopViewModel` and `ToolbarViewModel`
(all `PanelViewModel`: text by key, a status line) localize them. The panels (`MarketPanel`, `WardrobePanel`, `TradePanel`, `CoopPanel`) and the coin badge/toolbar are built only from shared controls in
`Nosum.Wasm.Controls/Common` (`GamePanel`, `GameButton`, `GameInput`, `GameRow`, `GameStack`, `GameSection`, `ItemGrid`, `ConfirmDialog`, `PlayerPicker`, `PriceInput`, `Pager`, `TabBar`): panels
add no colours or borders of their own, so the look of the whole UI is the CSS of those controls and the `--nosum-*` variables. Keys: `M` market, `C` wardrobe, `T` trade, `G` co-op, `H` help, `Esc` close; `E` facing the shipping bin with
a crop held sells the stack. Text is in `Language.resx` / `Language.en.resx`.
`TrimmerRoots.xml` keeps this assembly whole because Razor components are found by reflection.

Resource nodes ([ADR-021](../docs/adr/ADR-021-tools-resource-nodes-ground-items.md)): `NodeChannel` (in `Nosum.Wasm.Socket/Node`) mirrors the trees, stones, twigs, weeds and grass of the player's current place
(`GetNodes` after every join or map change, then `NodeChanged` pushes; it notes when a node lost a hit so the scene can shake it) and the water left in the watering can (`ToolState`).
`Game/NodeDecorator` draws them as depth-sorted world sprites from the one table in `Game/NodeSprites` (a grown tree is two sprites, canopy and trunk, so the farmer walks behind it and in front of it);
`MapScene` adds the blocking ones to the grid the farmer walks on (same `NodeField.BuildGrid` as the server). Using a tool is still the one `UseItem` intent: the server decides what a swing does.
The can's water is a meter on its slot (`SlotDisplay.Meter`). The farmer has no swing animation: the layered shirt, hair and hat sheets have no tool-use frames to match the body's.

Visiting ([ADR-019](../docs/adr/ADR-019-visiting-private-instances.md)): `VisitChannel` (`Nosum.Wasm.Socket/Visit`) reads the player's allow-list and sends the edits and the visit request; `VisitorsViewModel` (`ViewModels/Visit`) and `VisitorsPanel`
(`Controls/Visit`, shared controls only) are the "Visitors" panel: visit a friend by nickname, edit who may enter my farm, send a visitor out, switch co-op visits. It has a toolbar button and no hotkey yet (the toolbar hides an empty key). The server's refusals
(`Visit_Status_*`, `Farm_Status_NotYourFarm`, `Place_Status_NotYourPlace`, `Market_Status_NotYourBin`) are shown through the existing message lines. A guest is typed as "Khách 1234" / "Guest 1234" and sent as `guest-1234`.

Furniture ([ADR-015](../docs/adr/ADR-015-private-instances-and-placeables.md)): `PlaceableChannel` (in `Nosum.Wasm.Socket/Placeable`) mirrors the pieces of the player's current place (loaded with the farm after every join or map change)
and sends place, move and pick-up requests. `Game/PlacedObjectsDecorator` draws them (and the ghost of the piece being placed) as `WorldSpriteBatch` sprites; `MapScene` adds their footprints to the grid the farmer
walks on (same `PlacedSet` as the server). `Game/PlacementActions` is the keys, active only on a `MapSpec.Placeable` map: `E` places the held piece (or sets down a carried one), `R` rotates, `F` picks up the piece in front to
move it (again to cancel), `Q` puts it back in the bag. `PlacementViewModel` relays the same actions from the arrange panel (`Controls/Placement/PlacementPanel`, built from the shared `GamePanel` and `GameButton`;
styled only through their shared CSS classes). Refusals reach the message line as `Place_Status_*` text.

Ground items ([ADR-021](../docs/adr/ADR-021-tools-resource-nodes-ground-items.md)): `GroundItemChannel` (`Nosum.Wasm.Socket/GroundItem`) mirrors the items lying in the player's current place (loaded after every join or map change)
from `GroundItemSpawned` / `GroundItemTaken` pushes; a pickup also leaves a short `FlyingItem` toward where the taker stood. There is nothing to send: the server picks items up when the player walks near.
`Game/GroundItemsDecorator` draws them as y-sorted `WorldSpriteBatch` sprites with a bob, a hop on arrival and the flight, cut from the objects sheet by `ItemIcons.TryGetGroundCell` (items pictured on other sheets are not drawn yet);
it reads the mirror under its lock and allocates nothing. No UI panel.

UI theme and layout ([ADR-016](../docs/adr/ADR-016-ui-theme-and-hud-layout.md), amended by [ADR-017](../docs/adr/ADR-017-game-menu-and-world-pixel-ui.md)): one pixel-art theme lives in `Nosum.Wasm.Controls`.
`wwwroot/nosum-theme.css` holds the variables and the font (`--u` is one UI pixel and equals the world pixel: `round(width / 416)` like `MapScene.ScaleFor`, computed by `nosum-theme.js`; `--tp` is one pixel of the VT323 UI font,
`fonts/`, OFL; colours `--nosum-*`; type scale title, body, small) and `wwwroot/nosum-theme.js` cuts the sprites listed in its table out of `MenuTiles.png`, `Cursors.png` and `textBox.png` (linked from `assets/` in the Controls csproj, placeholder art
per ADR-007) into `--nosum-img-*` variables used as `border-image`, adds a few own glyphs, and shows the one tooltip for any element with `data-tip`. `index.html` links both and starts Blazor by hand with `loadAllSatelliteResources` (so English
appears after sign-in); the controls' scoped CSS comes through `Nosum.Wasm.styles.css`. Every screen element is a shared control (`GamePanel`, `GameButton`, `GameInput`/`GameField`, `ItemSlotButton`/`ItemGrid`, `GameSection`, `GameSwatch`, `GameKey`,
`GameIcon`, `GameIconButton`, `GameNotice`, `GameBadge`, `GameSpinner`, `GameMenu`/`MenuPage`, `CharacterPreview`, ...); panels add layout only, no colours. `Layout/GameHud` is the layout: dock regions (top-left quest tracker, top-center notices,
top-right coins and the gear, center = the game menu, bottom-center hotbar with the message line, overlay for dialogs). **One game menu** (`Controls/Menu/GameMenu`): the inventory, wardrobe, market, trade, co-op, profile and settings pages
(and a shop page while one is open) are its tabs; `Pages/Home.razor` maps `PanelKind` to a page, `ToolbarViewModel.Items` lists the tabs (a new tab = one entry there, one `case` in `Home.razor`, one sprite in `nosum-theme.js`). `Esc` closes the menu or opens it
on the last tab, `I M C T G N` open a tab, `H` / `?` and the gear open settings (account, sign out, the vi / en language toggle stored on the account with `SetLanguage`, key help: `Controls/Settings/SettingsPanel`, `HelpViewModel`). The window is as tall as its page (64 to 186 UI pixels, top edge fixed so the tabs never move); a page may pin controls with `MenuPage Footer`. The start screen (`Components/Account/StartScreen`) wraps the account form; the loading
state is an overlay panel until `GameLoop.StartAsync` returns, and the HUD appears after it.

Seasons ([ADR-018](../docs/adr/ADR-018-seasons-and-server-calendar.md)): `CalendarChannel` (Socket) reads the epoch and game day length once per sign-in and estimates the server clock; `CalendarViewModel` builds the shared `GameCalendar` and
The Town board ([ADR-030](../docs/adr/ADR-030-community-goals-and-market-limits.md)): the action key at the board counter opens `Controls/Goals/GoalsPanel` as a shop-style menu tab (`PanelKind.Goals`, `ToolbarViewModel.GoalsItem`; not a toolbar button). `GoalChannel` reads the goals and applies the `GoalProgress` pushes every player in the Town gets; `GoalsViewModel` shows a `GameProgress` bar per goal, the player's part and an amount to deliver. The market panel's `PriceHistoryCard` shows a `GameSparkline` of an item's daily median unit price with the latest day's lowest, median and highest. `GameProgress` and `GameSparkline` are shared controls in `Controls/Common`.

`Controls/Calendar/CalendarClock` shows season, day and time in the top-right region (the time moves in ten game-minute steps, a 5 s timer re-renders only when the text changes). The farm decorator computes each crop stage and wet soil with `FarmRules` and that calendar (growth counts watered game days, [ADR-021](../docs/adr/ADR-021-tools-resource-nodes-ground-items.md) D7; nothing is drawn until the calendar has loaded) and does not draw crops that withered at a season change. `FarmChannel` drops tiles the server pushes as removed (bare soil that reverted). `DevChannel.AdvanceClock` asks a Development server to skip game days.
The season icon is a flat colour swatch until the icon sheet has season art; the world is not tinted by the time of day yet.


Shops, nicknames and tester tools ([ADR-013 amendments](../docs/adr/ADR-013-economy-coins-escrow-market-trade.md), [ADR-009 amendments](../docs/adr/ADR-009-postgresql-persistence-and-accounts.md)): next to one of the Town's three shop counters (seed, clothing, furniture) the action key (`E`) opens the menu's shop page `ShopPanel`
(takes a `ShopId`; `ShopViewModel`, `ShopChannel`; catalogue and prices are the shared `ShopCatalog`, the server prices and checks every purchase). `N` opens the profile page `ProfilePanel` (`ProfileViewModel`: show and change the nickname once a day; a guest sees a note that registering unlocks it);
the registration and upgrade form asks for a nickname (`GameField`). `NicknameFormatter` shows a nickname (a guest's `guest-1234` as "Khách 1234" / "Guest 1234"); `LookChannel` caches nicknames by map player id and
`Game/NicknameLabels` uploads each name once as an engine label (`LabelRegistry`, `renderer.js registerLabel`) that `MapScene` draws above every farmer. Tester tools exist only in a build made with
`-p:NosumDevTools=true`: from the browser console, `(await getDotnetRuntime(0).getAssemblyExports("Nosum.Wasm.dll")).Nosum.Wasm.Game.DevTools.Grant(300, 5, 0)` asks a Development server for 5 oak chairs
(item id, quantity, coins).

```bash
dotnet build wasm/Nosum.Wasm.slnx
dotnet run --project wasm/Nosum.Wasm
```
