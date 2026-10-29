---
name: i18n
description: Use when adding or changing any user-visible text, error/status codes sent to clients, game data names (items, crops, maps), player-authored text, language selection, or date/number formatting. Vietnamese default + English, .resx, codes on the wire, i18n{} envelope.
---

# Internationalization

Decision: `docs/adr/ADR-010-internationalization.md`.

## Non-negotiables

- **No display text on the wire.** Replies carry an enum code (`AccountStatus.InvalidIdToken`),
  ids and keys; the client maps them to text. Never put a sentence in a packet or a DB column.
- **Every visible string through `.resx`.** `Resources/Language.resx` (Vietnamese, neutral) and
  `Resources/Language.en.resx` in the project that owns the component; read via
  `IStringLocalizer<…>` in the view model or component. Add both languages in the same change.
- **A language is the `GameLanguage` enum** (`Nosum.Contracts.Common`) on the wire, in the
  database and in code — never a tag string. `GameLanguages.ToTag/FromTag` is the only mapping to
  BCP-47, for culture names and the `i18n{}` envelope. New languages are appended, never
  renumbered; the server rejects undefined values with `AccountStatus.InvalidLanguage`;
  `Unspecified` means Vietnamese.
- **Game data by key.** Items, crops, buildings, maps, NPCs: stable keys (`crop.parsnip`); names
  and descriptions come from the localization catalogue.
- **Authored multi-language text** uses the `i18n{"vi":"…","en":"…"}` envelope (ADR-010 D5):
  validate on save, resolve when mapping to a view model, never show the raw envelope.
- **Formatting.** Display with the current culture; wire and storage with
  `CultureInfo.InvariantCulture` (CA1305 is on). Never enable `InvariantGlobalization` in `wasm/`.
- **English only** for logs, exceptions, code comments and XML docs.

## Checklist

- [ ] New UI text exists in both `Language.resx` and `Language.en.resx`.
- [ ] New server outcome is an enum value, mapped to text on the client.
- [ ] New game data uses a key with catalogue entries in both languages.
