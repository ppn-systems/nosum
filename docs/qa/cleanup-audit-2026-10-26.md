# Source cleanup audit — 2026-10-26

Audit of unused code, resource keys, scripts, static files, docs and project metadata at `master` (`0005e14`),
run before the cleanup PR on `chore/source-cleanup`. Verdicts: **REMOVE** (deleted in the cleanup PR),
**KEEP** (with the reason), **ASK** (the owner decides; not touched).

## Method and limits

- **C# declarations.** Every type and member declared in the 1,135 tracked `.cs` files was counted by name across
  all tracked text files (code, tests, tools, bench, `.razor`, `.js`, `.resx`, docs). A name that occurs only at its
  own declaration is a candidate. Each candidate was then checked by hand: attributed members (`[PacketHandler]`,
  `[JSExport]`, `[JSImport]`, `[Fact]`, `[Inject]`), EF configurations and migrations, interface and framework
  overrides, and extension classes were kept. Result: 2,068 candidates; 1,889 carry attributes (handler and
  test methods, framework entry points); 179 have none, of which 111 are test classes or test-kit members
  (xUnit discovery or test use), 52 are EF configurations, migrations, the context and its factory, and 16 are
  other code, reviewed one by one below.
- **Blind spot.** Name counting cannot see a dead member whose name is also used elsewhere (`Reset`, `Value`,
  `Name`). Private members are covered by the same count, but a name shared with live code can still hide one.
- **Resource keys.** Every key in each `.resx` (not `.en.resx`, which mirrors it) was searched in code and markup.
  Keys built at run time (`"Item_" + key`, `$"Quest_{key}_Title"`, `message.Kind + ".Subject"`) were kept.
- **JavaScript.** Every exported and internal function of the wwwroot scripts was checked for a C# `[JSImport]`,
  an `import`, a `Js.InvokeAsync("import", ...)` or a call inside its own file.
- **Packages and projects.** Every `PackageVersion` was searched for a `PackageReference`; every `ProjectReference`
  was checked for a type of the referenced project used in the referencing project's folder.

## C# members and types

| Candidate | Evidence | Category | Verdict |
|:--|:--|:--|:--|
| `SoundCues.AmbientLoop` (`engine/Nosum.Rendering/SoundCues.cs:50`) | `git grep -w`: declaration only | C# member, unreferenced | REMOVE |
| `ToolRules.MaxAreaLength` (`gameplay/Nosum.Gameplay/Items/ToolRules.cs:31`) | declaration only | C# const, unreferenced | REMOVE |
| `GameMaps.MineFloorId` (`gameplay/Nosum.Gameplay/Maps/GameMaps.cs:158`) | declaration only | C# method, unreferenced | REMOVE |
| `WalkGrid.FromLayer` (`gameplay/Nosum.Gameplay/Movement/WalkGrid.cs:53`) | declaration only | C# method, unreferenced | REMOVE |
| `NodeLimits.ChunkSize` (`gameplay/Nosum.Gameplay/Nodes/NodeLimits.cs:9`) | declaration only | C# const, unreferenced | REMOVE |
| `SprinklerRules.Reach` (`gameplay/Nosum.Gameplay/Placeables/SprinklerRules.cs:15`) | declaration only | C# field, unreferenced | REMOVE |
| `TradeSession.Unconfirm` (`gameplay/Nosum.Gameplay/Trade/TradeSession.cs:161`) | declaration only; not an interface member | C# method, unreferenced | REMOVE |
| `PlacementViewModel.RotateLabel`, `PickUpLabel` (`wasm/Nosum.Wasm.ViewModels/Placement/PlacementViewModel.cs:66,72`) | declaration only; no `.razor` binding | C# property, unreferenced | REMOVE (and their two keys, see below) |
| `GameAudio.Cues` (`wasm/Nosum.Wasm/Game/GameAudio.cs:44`) | declaration only | C# property, unreferenced | REMOVE |
| `MapScene.Fade`, `MapScene.IsChangingMap` (`wasm/Nosum.Wasm/Game/MapScene.cs:141,144`) | declaration only | C# property, unreferenced | REMOVE |
| `ChunkTestKit.BytesReceived`, `EntriesReceived` (`tests/Nosum.IntegrationTests/Server/ChunkTestKit.cs:177,182`) | declaration only | Test helper, unreferenced | REMOVE |
| `DbCommandCounter.RoundTrips` (`tests/Nosum.IntegrationTests/Server/DbBudget/DbCommandCounter.cs:224`) | declaration only | Test helper, unreferenced | REMOVE |
| `FarmWireTests.WalkUntilPickedUpAsync` (`tests/Nosum.IntegrationTests/Server/FarmWireTests.cs:730`) | declaration only | Test helper, unreferenced | REMOVE |
| `SocialClient.GetCoopAsync` (`tests/Nosum.IntegrationTests/Server/SocialClient.cs:352`) | declaration only | Test helper, unreferenced | REMOVE |
| `RoomOfFloor` (`gameplay/Nosum.Gameplay/Maps/GameMaps.cs:123,201`) | called at line 123 (`RoomOfFloor(1)`); two declarations look alike to the name count | C# method | KEEP — used |
| `SeasonSets` (`gameplay/Nosum.Gameplay/Calendar/SeasonSet.cs:31`) | static class of extension methods; `Contains` is used on `SeasonSet` | C# extension class | KEEP — members used |
| `BuildingViewMapping` (`server/Nosum.Abstractions/Buildings/BuildingViewMapping.cs:9`) | extension `ToRow` used on `BuildingView` / `AnimalView` | C# extension class | KEEP — members used |
| `PlayerConnectionExtensions` (`server/Nosum.Handlers/Session/PlayerConnectionExtensions.cs:14`) | extension methods `AttachPlayer`, `GetPlayer`, `DetachPlayer` used by handlers | C# extension class | KEEP — members used |
| `BudgetPlayerExtensions.ProbeAsync` (`tests/.../DbBudget/BudgetRig.cs:481`) and `EncryptedRequestExtensions.RequestAsync` (`tests/.../EncryptedRequestExtensions.cs:16`) | `ProbeAsync` 143 and encrypted `RequestAsync` 285 mentions in tests | test extension class | KEEP — used |
| `DbCommandCounter` interceptor methods (`TransactionStarting`, `TransactionStartingAsync`, `SavingChanges`, `SavingChangesAsync`) | implement EF `DbCommandInterceptor` / `IDbTransactionInterceptor` / `ISaveChangesInterceptor` | Framework interface | KEEP — called by EF |
| 108 `Handle*Async` methods in `server/Nosum.Handlers/` | `[PacketHandler]` / `[PacketOpcode]` scanned by Nalix at start-up | Framework entry point | KEEP — `nalix-handlers` |
| 52 EF `*Configuration` classes, `CommunityGoalsAndPriceHistory` and `PasswordRecovery` migrations, `NosumDbContextFactory` | found by `ApplyConfigurationsFromAssembly`, EF migrations, EF design-time tooling | Framework / data model | KEEP — `data-model-ef-core`, never touched |
| xUnit test classes (about 70 `…Tests` classes) | discovered by the test runner | Test framework | KEEP |
| `NosumDbContext` `DbSet`s `PlayerAppearances`, `BlockedPlayers`, `Friendships`, `FriendRequests` (`server/Nosum.Infrastructure/Persistence/NosumDbContext.cs:96,146,155,158`) | properties never read; entities are configured through `OnModelCreating` | Data model surface | ASK — the data model is the data-model owner's decision; removing a `DbSet` does not change the schema, but it changes the context's public surface |

## Resource keys (`.resx`)

| Candidate | Evidence | Category | Verdict |
|:--|:--|:--|:--|
| `Market_Title`, `Market_SellPick`, `Wardrobe_Worn_Heading`, `Wardrobe_Plain`, `Wardrobe_TakeOff`, `Wardrobe_Clothes_Heading`, `Wardrobe_NoClothes`, `Wardrobe_Hint`, `Trade_Title`, `Coop_Title`, `Profile_Title`, `Toolbar_Help`, `Settings_Accessibility`, `Menu_Label` (`wasm/Nosum.Wasm.ViewModels/Resources/Language.resx` and `Language.en.resx`) | no literal in code or markup; no run-time prefix matches (`Wardrobe_`, `Market_`, … are not built at run time) | Resource key, unused | REMOVE (both languages) |
| `Placement_Rotate`, `Placement_PickUp` (same two files) | only read by `RotateLabel` / `PickUpLabel`, which are removed | Resource key, unused after removal | REMOVE (both languages) |
| 600 keys matched by a run-time prefix (`Item_`, `Quest_`, `Goal_`, `Building_`, `Animal_`, `Season_`, `Action_`, `Account_Status_`, …) | keys built as `"Item_" + key + "_Name"`, `$"Quest_{key}_Title"` and the like | Resource key, dynamic | KEEP — built at run time |
| `PasswordReset.Subject`, `PasswordReset.Body` (`server/Nosum.Infrastructure/Resources/EmailText.resx`) | read as `message.Kind + ".Subject"` in `SmtpEmailSender` | Resource key, dynamic | KEEP — built at run time |

## JavaScript (`wwwroot`)

| Candidate | Evidence | Category | Verdict |
|:--|:--|:--|:--|
| `renderer.js`: `setKeyBindings`, `loadTexture`, `registerLabel`, `start`, `submitFrame` | `[JSImport]` in `engine/Nosum.Rendering.Web/WebRenderBackend.cs` | Exported, bound | KEEP |
| `audio.js`: `loadAudio`, `setAudioLevels`, `setUiClickSound` (re-exported by `renderer.js`), `audioCommand` (imported by `renderer.js`) | `[JSImport]` and `renderer.js` | Exported, bound | KEEP |
| `nosum-settings.js`: `readSettings`, `writeSettings`, `applyDisplay`, `applyScale` | `[JSImport]` in `wasm/Nosum.Wasm/Settings/BrowserSettings.cs` | Exported, bound | KEEP |
| `nosum-theme.js`: `unitFor`, `setUnitChoice` | `setUnitChoice` imported by `nosum-settings.js`; `unitFor` called inside the module | Exported, used | KEEP |
| `chat-input.js`: `init`, `setEnabled`, `focusInput`, `dispose` | `InvokeVoidAsync` in `ChatLine.razor` | Exported, bound | KEEP |
| Non-exported helpers in all five scripts | each called at least once in its file | Internal | KEEP |

## Static files

| Candidate | Evidence | Category | Verdict |
|:--|:--|:--|:--|
| `wasm/Nosum.Wasm/wwwroot/audio/*.wav` (22 files) | each file name is referenced in the repository | Static, loaded | KEEP |
| `assets/` (27 files) | loaded by code: `FarmProps.cs`, `BuildingCatalog.cs`, `AnimalCatalog.cs`, `AnimalsDecorator.cs`, the layered farmer sheets | Static, loaded | KEEP |
| `art/own/Maps` and tilesheets | linked into the client and server by the `.csproj` files and loaded by map id | Static, loaded | KEEP |
| `art/own/reports/**` | generated per-map reports, referenced from `art/own/SOURCES.md` provenance | Provenance | KEEP |
| `art/own/source/pixellab/**` | source images for the own terrain, provenance | Source | KEEP |
| `tests/browser/*.js` (five scripts) | named in `docs/qa/acceptance-2026-10-25.md` and `acceptance-2026-10-26-final.md`; run by hand (`tests/README.md`) | Test tool | KEEP |
| `tools/synth_music.py`, `tools/synth_sounds.py`, `tools/art_repack/*.py` | the generators of the shipped audio and art (ADR-039, `docs/art/provenance.md`) | Generator | KEEP |

## Early-art pipeline (removed in ADR-042 P7)

| Candidate | Evidence | Category | Verdict |
|:--|:--|:--|:--|
| The inventory and re-author tools and their folder variable (removed in ADR-042 P7) | the variable selected the early `assets/` folder for the tools' `--check` modes | Early-art leftover | ASK, handled by ADR-042 P7 (left untouched in this PR) |
| The early-art test helper and its users (removed in ADR-042 P7) | the tests returned early unless the folder variable was set, so they skipped in CI; they reproduced the committed reports and maps | Early-art leftover | ASK, handled by ADR-042 P7 (left untouched in this PR) |
| `docs/ops/README.md:119-121`, the paragraph on the first deploy after the early-content removal | the removal has landed (`docs/design/roadmap.md`, status 2026-10-26); the paragraph still describes it as pending | Stale doc | ASK — whether the first deploy has run is not visible in the repository; the owner should say what the paragraph should now state |

## Stale documentation

| Candidate | Evidence | Category | Verdict |
|:--|:--|:--|:--|
| Paths to deleted early-art files in ADR-004, ADR-006, ADR-007, ADR-021, ADR-040, ADR-041 (e.g. `assets/Maps/paths.png`, `assets/wardrobe/SOURCES.md`) | ADRs record decisions as they were | ADR history | KEEP — ADRs are not edited except for status (`docs-style`) |
| `art/README.md`, "`own/Maps/spring_town.png` is served as `assets/Maps/spring_town.png`" | `wasm/Nosum.Wasm/Nosum.Wasm.csproj` links `art/own/Maps` files to those served paths; the mechanism still exists | Current behaviour | KEEP |
| Short paths in `README.md`, `ARCHITECTURE.md`, `wasm/README.md`, `engine/README.md` (`Nosum.Engine.slnx`, `wwwroot/renderer.js`) | each names a file that exists under its area folder | Shorthand | KEEP |
| `deploy/.env` in `deploy/README.md`, `docs/ops/README.md` | the file is git-ignored by design (`.gitignore`, `deploy/.env.example`) | Current | KEEP |
| Nalix version in `.claude/skills/nalix-packets/SKILL.md` | `Directory.Packages.props` pins `Nalix.Codec` 14.3.7 | Current | KEEP |
| Skill paths in `.claude/skills/*/SKILL.md` (`Nosum.Handlers/Session`, `Nosum.Gameplay/Maps/ChunkCoord`, `Nosum.Host/Ops`, `Nosum.Abstractions/Data`) | each path or type exists (`ChunkCoord` in `Maps/ChunkBounds.cs`) | Current | KEEP |

## Project metadata

| Candidate | Evidence | Category | Verdict |
|:--|:--|:--|:--|
| `PackageVersion` entries in `Directory.Packages.props` (21) | each has a `PackageReference` in a `.csproj` | NuGet | KEEP |
| `ProjectReference` entries (47) | each referencing project uses a type of the referenced project | Project reference | KEEP |
| Commented-out code blocks | `//` lines ending in `;`, `{` or `}` were checked; the hits are prose comments in `GameMaps.cs`, `PlayerService*.cs`, `Program.cs` | Commented code | KEEP — no dead code found |

## Totals

- C#: 14 REMOVE rows covering 17 declarations; 1 ASK row (the four `DbSet`s).
- Resource keys: 16 keys REMOVE in both language files (14 plus the 2 placement keys); 601 dynamic keys KEEP.
- Early-art pipeline (ADR-042 P7): 3 ASK rows.
- Stale docs: 1 ASK row; the rest KEEP with the reason above.
- Packages, project references, commented code: nothing to remove.
- Wire format: no packet, opcode, `[SerializeOrder]` member, enum value, EF migration, model snapshot or entity property is a candidate.
