# ADR-071: Emote wheel and pet companion

**Status:** Accepted (agent-written, 2026-10-29). Builds on [ADR-029](ADR-029-chat-friends-mail.md) (chat fan-out, profanity mask, rate rules), [ADR-022](ADR-022-chunked-interest-management.md) (who sees whom), [ADR-006](ADR-006-layered-character-customization.md) (looks as ids on the wire) and [ADR-028](ADR-028-farm-buildings-and-animals.md) (animals walk the farm and are petted).

## Context

Players can talk to each other with chat, but they cannot show a feeling without typing. The social pillar asks for small non-verbal signals and a companion that makes the farm feel lived in. Both must stay cheap on the wire, and the server must still decide who sees what.

## Decision

- **D1 — Emote wheel: twelve codes, shown for three seconds.** `Nosum.Gameplay.Emotes.EmoteRules`: heart, laugh, wave, question, exclamation, zzz, music, angry, sad, thumbs up, coin, flower, in the order of the icon sheet `assets/ui/emotes.png`. Only the code travels (a byte); the client draws the icon above the farmer for `EmoteRules.ShownMs` (3 s). `SendEmote` (C2S `0x9400`) replies `EmoteResult` (`0x9440`, `EmoteStatus`); the push `EmoteShown` (`0x9441`) goes to the players in the sender's chunk interest set (ADR-022), the same reach as local chat. The sender's own bubble is shown from the `Ok` reply, so the sender is not pushed to.
- **D2 — Cooldown and rate.** One emote per `EmoteRules.MinIntervalMs` (1.5 s) per player, enforced exactly by `EmoteCooldown` in `EmoteService`; the opcode is also throttled by `[PacketRateLimit]`. A refused emote costs nothing and changes nothing.
- **D3 — Pet: one cat or dog, named by the player.** `PetKind` (None, Cat, Dog) and a `PlayerPet` row (`player_pets`: kind, name, mood, last petted game day, adoption time). `AdoptPet` (`0x9501`) takes a kind and a name; the name goes through `PetRules.CleanName`, which is the chat cleaning and profanity mask (`ChatRules.Prepare`) cut to 12 characters. One pet per player; a second adoption is refused with `AlreadyHasPet`. The offer is open whenever the player has no pet, and the client shows it on the player's own farm, which is where a player starts their first farm day. The kind and the name travel in `LookRow` (`PetKind`, `PetName`, protocol 13), so every player who can see the farmer sees the pet, and an adoption re-announces the look (`ILookService.AnnounceAsync`).
- **D4 — Petting: once a game day, a small bonus.** `PetPet` (`0x9502`) raises the mood by `PettingMood` (10, capped at 100) and gives `PettingEnergy` (5) energy through `IPlayerProgress.RestoreEnergyAsync`, once per game day (`PetRules.CanPet` against `GameCalendar.DayNumberAt`). A refused petting changes nothing. The energy call is a separate change after the pet row is saved; it is the same capped restore a potion uses.
- **D5 — Wire.** Band `0x9400-0x94FF` (emote) and `0x9500-0x95FF` (pet), Trade band sub-ranges (ADR-029 D6). Replies: `PetState` (`0x9540`) carries the status, kind, name, mood and petted-today flag for every pet request. `NosumProtocol.Version` is 13 (`LookRow` gained two appended fields; the new packets are additive).
- **D6 — Pet follow and pose are the client's, from the owner's position.** The server does not simulate the pet's steps: each client draws the pet from the owner's look and position. A pet trails its owner by a fixed distance and sits when the owner has not moved for about a second and a half. It sleeps by the bed when the owner is in their farmhouse at night, using the same calendar the farm uses. Only the owner's mood and petting are server state. Everyone who can see the farmer sees the same pet, because they all follow the same owner position stream.
- **D7 — Art.** The cat and dog are drawn in-repo as Nosum's own art (`tools/art_repack/pets_emotes.py`, provenance in `docs/art/provenance.md`): no new downloads, no pack source. Their sheet layout is in the pet draw code.

## Alternatives considered

- **Pet position on the server.** Rejected for now: it would add a wire push every step for every pet, and the follow is cosmetic. The server still owns the state that matters: adoption, the name and petting.
- **Free text emotes.** Rejected: an emote is a code, so no player text reaches other players and nothing needs the chat filter beyond the pet name.
- **A pet that dies or needs feeding.** Out of scope; the pet has mood only.

## Consequences

- Visitors see a player's pet without a new fan-out: it rides the look path that already reaches every visible player.
- Protocol 13 is a breaking wire change for older clients in the sense of `LookRow` fields, so the server and client ship together as before.
- The energy restore was renamed from `RestoreEnergyFromPotionAsync` to `RestoreEnergyAsync`, because the petting bonus uses the same operation.
