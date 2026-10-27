# ADR-044 — Standalone panels, HUD action bar and Settings separation

**Status:** Proposed (2026-10-27). Amends [ADR-017](ADR-017-game-menu-and-world-pixel-ui.md) D1 and D5, [ADR-033](ADR-033-client-settings-touch-onboarding.md) D1, and [ADR-043](ADR-043-npc-merchants-and-the-menu-split.md) D4.

## Context

Under ADR-017 and ADR-043, the top-right HUD held only the Mail icon and the Settings cog, while all eleven player and system features (`Inventory`, `Wardrobe`, `Crafting`, `Trade`, `Coop`, `Visitors`, `Friends`, `Mail`, `Skills`, `Profile`, `Settings`) were attached as tabs to a single `GameMenu` window. As a result:

1. A player using the mouse had no HUD button for `Inventory`, `Wardrobe`, `Crafting`, `Skills`, `Trade`, `Coop`, `Visitors`, `Friends`, or `Profile` and had to click the `Settings` cog to open any of them.
2. Opening `Settings` or `Mail` from the HUD still opened the 11-tab window where gameplay, social, and settings panels were mixed together.
3. Inside `SettingsPanel`, the `General` tab combined account management (signed-in status, guest upgrade, sign out), language selection, onboarding hints, and the 26-entry open-source art credits list, while `ProfilePanel` showed only the player's nickname and told guests to visit Settings to register.

The owner required every feature to be separated rather than routed through or stuffed into Settings.

## Decision

- **D1 — Standalone windows for every panel.** Every panel in `PanelKind` (`Inventory`, `Wardrobe`, `Crafting`, `Skills`, `Profile`, `Trade`, `Coop`, `Visitors`, `Friends`, `Mail`, `Settings`, alongside counter/world dialogs `Shop`, `Market`, `Goals`, `Chest`, `Furnace`) opens in `Center` as its own standalone `TradeDialog` window with a single title bar, close button, and page body — never with a row of unrelated tabs attached to the top edge.
- **D2 — Separated HUD action bar.** `TopRight` in `Home.razor` renders two clean rows:
  1. The status and system row: `CalendarClock`, `CoinBadge`, `Mail` (`tab-mail` with unread badge), and `Settings` (`tab-settings`).
  2. The player and social action bar (`role="toolbar"`): one `GameIconButton` per player/social entry in `ToolbarViewModel.Items` (`Inventory`, `Wardrobe`, `Crafting`, `Trade`, `Coop`, `Visitors`, `Friends`, `Skills`, `Profile`), with `Active` state highlighting the currently open window and tooltips showing the localized name and key binding.
- **D3 — Account in Profile; Credits in its own Settings tab.**
  - `ProfilePanel` receives `AccountFormViewModel` and renders the `Account` section (signed-in status, guest upgrade button, and sign out) directly below the nickname section.
  - `SettingsPanel` holds only client/game settings across five focused tabs: `General` (language and tutorial hints), `Display` (UI zoom, names, reduced motion, high contrast), `Sound` (music volume, sound effects volume, mute), `Controls` (key bindings), and `Credits` (`ArtCreditsList`).

## Alternatives considered

- **Keep the 11-tab `GameMenu` and only add an Inventory icon to the HUD** — still mixes `Settings` and `Mail` with `Inventory`, `Coop`, and `Friends` on the same window.
- **Leave Account and Art Credits inside `Settings -> General`** — leaves `Profile` incomplete for guests and clutters `General` with a 26-item license list.

## Consequences

- Clicking any HUD button (or pressing its key) opens only that feature's window; clicking the active HUD button, its close button, or `Escape` closes it; clicking another HUD button switches directly to that window.
- `ToolbarViewModel.Items` keeps the canonical list of player and system items and adds `TitleOf(PanelKind)` for standalone window titles.

## Revisit when

New player panels are added and the HUD action bar needs collapsible grouping on narrow viewports.
