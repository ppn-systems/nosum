# ADR-026 — Tool upgrades, the blacksmith and the mine

**Status:** Accepted (coordinator, 2026-10-25, owner delegation). Roadmap M2. Builds on ADR-021 (tools, nodes),
ADR-023 (mining skill), ADR-024 (bars from the furnace), ADR-012/015 (maps and instances).

## Context

Ore and bars need a sink and a source that scales with progress; the genre's answer is tool upgrades paid in bars and
coins, and a mine whose deeper floors hold better ore.

## Decision

- **D1 — Tiers.** Basic → copper → iron (gold/iridium later). `ToolTier` on the tool item (separate item ids per tier,
  appended in 1–99). Effects: axe/pickaxe damage per hit = tier + 1; hoe and watering can affect a line of
  1/3/5 tiles when the use is held (charged use: the client sends the tier's area intent, the server validates it);
  watering can capacity 40/55/70. Energy cost per tile (ADR-023).
- **D2 — Blacksmith counter** in Town (the `Blacksmith` building door, tile recorded in `MapSpec.Shops`): upgrade
  copper = 2 000 coins + 5 copper bars, iron = 5 000 + 5 iron bars; also sells coal and ore at high prices. The
  upgrade takes two game days (the tool is held by the blacksmith; collect intent) — keeps the genre's trade-off.
- **D3 — The mine.** A shared public map `Mine` (entrance from Town or Mountain when it exists; Town edge for now)
  leading to floors built from `Maps/Mines/*.tmx` layouts. Floors are **per player instances** (`mine<N>@<player>`) so
  nobody's stones are taken by others; floors 1–40 in M2. Each floor spawns stones, copper (1–39) and iron (20–40)
  nodes and one ladder hidden under a stone (breaking it reveals the ladder to the next floor). Floor layouts are
  rebuilt every game day. An elevator every 5 floors remembers the deepest floor reached (persisted per player).
- **D4 — No combat in M2.** Monsters are an M3+ topic with their own ADR.
- **D5 — Wire.** Mine floors reuse warps, nodes and chunks; blacksmith uses the shop band (0xC000) append-only.

## Consequences

Per-player floor instances multiply node state; floors are kept only in memory, created on entry and dropped
after the player leaves (only the deepest floor is persisted). The chunk system (ADR-022) applies unchanged.
