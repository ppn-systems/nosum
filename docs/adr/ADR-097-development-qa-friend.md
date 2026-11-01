# ADR-097: Development QA friend for the two-player browser check

## Status

Accepted

## Context

The social critique (`docs/design/critique-social.md`, findings 8 and the "not browser-played" note) could not play a friend in the browser: a
friends list needs registered accounts on both sides (docs/adr/ADR-029 D4, ADR-069), and Development has no Google or Microsoft sign-in, so the
browser only ever had guests. Friends, presence, a visit to a friend's farm and a trade or co-op with a friend stayed unverified.

The Development QA hook (docs/adr/ADR-079) is the place for a tester tool that reaches such a state in one call. The rules stay fixed: the server
decides every outcome, Production maps no tester opcode, and the wire is append-only.

## Decision

- **D1 — One Development opcode.** `DevBefriend` (C2S `0xF006`, reply `DevBefriendResult` `0xF046`) in the `DevOpCode` band. `DevHandlers` maps it,
  and the host maps the handler only in Development. `NosumProtocol.Version` moves from 31 to 32; no existing packet, opcode or field changed.
- **D2 — The sender becomes a registered test account.** A friends list is for registered accounts only, so a guest sender is made registered
  (`IsGuest = false`, the nickname is kept). This is a Development-only change of privilege with no identity linked; it exists so the tester can
  see the friends list, presence and visits from its own account.
- **D3 — A new registered test player.** The server starts a guest through `IAccountService.StartGuestAsync`, which gives a session token, then gives
  the player a registered name `QaFriend` plus four digits that no other player has. The reply carries the nickname and that session token, so a
  second client can sign in as the test player (the remembered session of docs/adr/ADR-082).
- **D4 — The friendship is the real one.** The test player sends a friend request to the sender, and the sender asks back, which the friend service
  already treats as accepting (`IFriendService.RequestAsync`). The rows, the caps, the blocks and the presence pushes are the ones players use.
- **D5 — The client and the hook.** `DevChannel.BefriendAsync` asks the server; `DevTools.Befriend` (JSExport) resolves to the nickname and the token
  separated by a tab; `window.nosumQa.befriend()` exposes it on a page opened with `?qa=1`. Compiled only into the tester build (ADR-079 D6).
- **D6 — Budget.** `DevBefriend` is a tester tool with no budget row in docs/adr/ADR-035 D1. Its cost is the friend request and its accept, which are
  measured under the social rows; `DbBudgetRun` records it as a skipped Development tool with that reason.

## Consequences

- A Development host can reach a two-player state (friends, presence, chat, visit, trade, co-op) from one call and a second browser context.
- The sender's account stops being a guest in Development after the first call. Production is unaffected: the opcode is not mapped there
  (`DevQaWireTests.DevQaOpcodes_InProduction_AreNotMappedAndNothingAnswers` probes it and expects no answer).
- The session token of the test player crosses the wire to a Development client only. It is not logged, and it is a test account with no identity.
- Protocol version 32 is a wire change for every client built from this commit; clients and server ship together, as before.

## References

- docs/adr/ADR-029 D4 (friendships), ADR-069 (sign-in), ADR-079 (Development QA hook), ADR-035 (round-trip budget), ADR-082 (remembered session).
- `server/Nosum.Infrastructure/Dev/DevSocialService.cs`, `server/Nosum.Handlers/Dev/DevHandlers.cs`, `contracts/Nosum.Contracts/Dev/`,
  `wasm/Nosum.Wasm.Socket/Dev/DevChannel.cs`, `wasm/Nosum.Wasm/Game/DevTools.cs`, `tests/browser/social-round3-play.cjs`.
