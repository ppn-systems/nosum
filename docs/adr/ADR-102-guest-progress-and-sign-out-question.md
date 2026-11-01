# ADR-102: Guest progress from Profile, a sign-out question for guests, and hair-style names

## Status

Accepted

Amends [ADR-069](ADR-069-google-and-microsoft-sign-in-only.md) (D-section on the guest upgrade: the link is offered from Profile at any time, not only after
the first quest) and [ADR-082](ADR-082-remembered-sign-in-and-start-screen.md) (sign-out from Profile). No wire change: `NosumProtocol.Version` does not move,
and the server is unchanged. The link already keeps the player (`LinkExternalIdentityAsync` keeps the player id and state, revokes the guest token).

## Context

The account round of [docs/design/critique-account-settings.md](../design/critique-account-settings.md) left three gaps. A guest could only reach the link
through the session bar's invitation, which waits for the first quest, and Profile said nothing about what linking keeps (section 5). Signing out as a guest
looked the same as signing out of a registered account, although the server keeps no way back to a guest's farm and coins after the session token is revoked
(section 8). The wardrobe showed bare numbers 1 to 8 for the hair styles, and the names existed only as tooltips (section 3).

## Decision

1. **Save progress from Profile.** A signed-in guest sees a "Lưu tiến trình" block in Profile (Settings, Account): one paragraph saying that linking a Google or
   Microsoft account keeps the farm, coins and belongings, because it is the same player with one more way to sign in, then the button
   "Lưu tiến trình bằng tài khoản", which opens the existing link form (`AccountFormMode.Upgrade`). The button is offered at any time for a guest
   (`AccountFormViewModel.CanSaveProgress`). The session bar's invitation keeps its rule (after the first quest).
2. **Sign-out asks a guest first.** Sign-out in Profile opens a `ConfirmDialog` for a guest, not a direct sign-out. It says what is lost: the farm, the coins and
   the belongings, with no way back after signing out. Three answers: "Liên kết tài khoản trước" (opens the link form, primary), "Đăng xuất" (signs out) and
   "Ở lại chơi" (keeps playing). `ConfirmDialog` takes an optional first answer (`ThirdText`, `OnThird`); without it the dialog is unchanged. A registered player
   signs out at once, as before.
3. **Hair styles have names.** Each of the eight hair styles has a readable name in both languages (`Wardrobe_HairStyle_Name_1` to `_8`, by key, so the
   names are game data read by id). The buttons still show the number, with the name as the tooltip. The panel shows "Đang đeo: <name>" under the buttons, so the
   name is visible without a hover. The names follow the silhouettes of `assets/farmer/hair.png` (row 1); an art review may rename a style, not renumber it.

## Consequences

- A guest is one tap from keeping the progress. The Profile block and the link form use the same flow and server path, so nothing new crosses the wire.
- A guest who signs out without linking loses that progress for good. The warning says so, and `AccountServiceTests` checks that the revoked token cannot resume.
- Sign-in with Google or Microsoft needs real client ids and is not played in this container. The tests use the fake verifier from `ExternalSignInTestKit`; no client id is
  committed.

## References

- docs/adr/ADR-069 (sign-in, the guest link), docs/adr/ADR-082 (remembered session), docs/design/ui-standard.md section 5 (Confirm).
- `wasm/Nosum.Wasm.ViewModels/Account/AccountFormViewModel.cs`, `wasm/Nosum.Wasm.Controls/Profile/ProfilePanel.razor`, `wasm/Nosum.Wasm.Controls/Common/ConfirmDialog.razor`,
  `wasm/Nosum.Wasm.ViewModels/Looks/WardrobeViewModel.cs`.
- Tests: `tests/Nosum.UnitTests/Wasm/AccountFormViewModelTests.cs`, `tests/Nosum.UnitTests/Wasm/WardrobeTextTests.cs`, `tests/Nosum.IntegrationTests/Server/AccountServiceTests.cs`.
