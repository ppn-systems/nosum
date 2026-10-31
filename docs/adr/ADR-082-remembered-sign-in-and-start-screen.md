# ADR-082: Remembered sign-in on this browser, and the start screen

## Status

Accepted

## Context

A player who signed in with Google or Microsoft (docs/adr/ADR-069) had to finish the provider's popup on every visit. The server already issues a
session token at sign-in, rolls it on every resume and expires it after 30 days idle (docs/adr/ADR-009, docs/adr/ADR-032 D5), and the
`ResumeSession` request signs a connection in with it. Nothing kept the token in the browser, so every visit started at the sign-in buttons.

The start screen also drew a saturated sky and sea behind a wooden window, which read as glaring. The provider buttons sat inside the game's
wooden button frame, while each provider asks for its own look.

## Decision

1. **Remember the session, never the identity token.** After a successful sign-in (Google, Microsoft or guest) and after a guest links an
   identity, the client stores one JSON record under the `localStorage` key `nosum.remembered-session`: the session token the server issued, its
   expiry, the nickname and the kind (`guest`, `google`, `microsoft`). It never stores the provider's ID token. The token is a bearer credential:
   it is not logged, and it is replaced by the rolled token each time a resume succeeds.
2. **Continue without signing in again.** On the next visit the start screen shows the card "Tiếp tục với <name>" (a guest sees "Khách"). Its
   button calls `ResumeSession` with the stored token; no provider window opens. A resume that succeeds keeps the new token it returns.
3. **A refused session falls back to sign-in.** A resume the server refuses (expired, revoked, replaced, banned) forgets the record and shows the
   sign-in buttons with the server's reason. A connection failure keeps the record: the server was not asked to refuse it.
4. **Expiry is checked on this side too.** A record past its expiry is forgotten when the page loads, so the card is never offered for it.
5. **Sign out, or use another account.** "Đăng xuất / dùng tài khoản khác" revokes the remembered session on the server when it answers (a resume,
   then a logout) and forgets the record either way. Signing out from the game does the same locally.
6. **Storage that refuses is not an error.** A browser that blocks `localStorage` reads as "nothing remembered", and a write that fails is ignored;
   the sign-in still works for the visit.
7. **Start screen.** The backdrop is `art/own/ui/start_meadow.png`, a daytime meadow composed from Kenney Tiny Town tiles (CC0) by
   `tools/art_repack/start_meadow.py`; it replaces the sky-and-sea backdrop of docs/adr/ADR-017. The window keeps the shared `GamePanel`.
8. **Provider buttons are not framed.** Google's button is drawn by Google (Google Identity Services) at the width of its slot. Microsoft's is
   drawn to Microsoft's brand guidelines: white, a grey edge, the four-square mark and the label in Segoe UI. Neither sits in the game's wooden
   frame. The brand colours and the 1 px edge are the one exception to the token rule of docs/design/ui-standard.md section 1, because the brand
   sets them.

No wire change: `ResumeSession` and `Logout` already exist, and the protocol version does not move. The server is unchanged.

## Consequences

- A player stays signed in on a browser for up to 30 days of inactivity, with no sign-in in between. On a shared computer the player uses
  "Đăng xuất / dùng tài khoản khác" to leave.
- The token sits in `localStorage`, as the server key does (docs/adr/ADR-032 D1). A script running on the game's origin could read it, so the
  game keeps third-party scripts to the two identity providers it already loads, and no others.
- The continue card's text and the Microsoft label have Vietnamese and English strings (`Language.resx`, `Language.en.resx`).
- The Google button can only be checked with a client id set and the provider reachable; the QA captures show it off (no client id in the
  repository's `appsettings.json`).

## References

- docs/adr/ADR-009 (session tokens, resume), docs/adr/ADR-017 (the start backdrop this replaces), docs/adr/ADR-032 D1 and D5 (stored key, rolled
  session token), docs/adr/ADR-069 (Google and Microsoft sign-in).
- `wasm/Nosum.Wasm.ViewModels/Account/RememberedSessionViewModel.cs`, `wasm/Nosum.Wasm/Account/BrowserRememberedSessionStore.cs`,
  `wasm/Nosum.Wasm/Components/Account/RememberedSessionCard.razor`, `MicrosoftSignInButton.razor`.
- `tests/Nosum.UnitTests/Wasm/RememberedSessionViewModelTests.cs`; captures in `docs/qa/signin/` (`tests/browser/signin-capture.cjs`).
