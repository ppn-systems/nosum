# ADR-010 — Internationalization (i18n)

**Status:** Accepted (owner decision, 2026-10-25)

## Context

Nosum is played in Vietnamese and English from the start, and more languages may follow. The
same stack in a sibling product (AtmosOS) already settled how to do this: UI text in `.resx`,
codes on the wire, a localized-string envelope for player- or admin-authored content, and real
culture data in the WASM bundle. Nosum follows the same rules so code and habits carry over.

## Decision

- **D1 — Languages.** Vietnamese is the default; English ships too. A language is the
  `GameLanguage` enum (`Unspecified = 0` meaning Vietnamese, `Vietnamese = 1`, `English = 2`;
  `contracts/Nosum.Contracts/Common`) on the wire, in the database (smallint) and in code. New
  languages are appended with the next number, never renumbered, and the server refuses undefined
  values. `GameLanguages` is the one mapping to BCP-47 tags (`vi`, `en`), used only where a tag is
  required: `.resx` culture names, `CultureInfo`, and the `i18n{}` content envelope keys.
- **D2 — No display text on the wire.** Packets carry ids, enums and status/error codes. The
  client turns them into text. Logs, exceptions and code comments stay English and are never
  localized.
- **D3 — UI text in `.resx`.** Every user-visible string in `wasm/` is read through
  `IStringLocalizer` from `Resources/Language.resx` (`vi`, neutral) and `Language.en.resx` in the
  project that owns it — reusable components keep their own resources. No hard-coded display
  strings in `.razor` or view models.
- **D4 — Game data names are keys.** Items, crops, buildings, maps and NPCs are referred to by a
  stable key (`crop.parsnip`); their display names and descriptions live in a localization
  catalogue (`.resx` in a shared project both client and server can read), not in packets or
  database rows.
- **D5 — Authored content uses an envelope.** Text typed by players or admins that must show in
  several languages (co-op descriptions, market notes, server announcements) is stored as one
  string; a value starting with `i18n{` holds a JSON object of `{ "<tag>": "<text>" }`, any other
  value is plain text shown as is. Resolution: exact UI locale → base language → `en` → first
  segment, never empty. The server validates envelopes on save and rejects malformed ones; it
  never rewrites them. (Same convention as AtmosOS ADR-58.)
- **D6 — Culture.** The player's chosen language is stored on the account and applied to
  `CultureInfo.CurrentUICulture`/`CurrentCulture` at client start-up. Dates and numbers are
  formatted with the current culture for display and `InvariantCulture` on the wire. The WASM
  client does not enable `InvariantGlobalization`.

## Alternatives considered

- **Server sends localized text** — couples the server to every language and breaks caching.
- **Per-language columns (`NameVi`, `NameEn`)** — caps the number of languages and needs a
  migration per language.

## Consequences

- Every new feature adds its strings to both `.resx` files in the same change.
- Content from the early game data (ADR-007) is not localized by this system; it is
  replaced together with the art.

## Revisit when

A language needs right-to-left layout or plural rules `.resx` cannot express.
