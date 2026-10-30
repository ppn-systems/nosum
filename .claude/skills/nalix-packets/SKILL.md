---
name: nalix-packets
description: Use when defining or changing a packet class or opcode in contracts/ — [Packet], [GenerateFormatter], [SerializePackable], [SerializeOrder], StaticOpCode, opcode classes — or renting/returning packets via PacketFactory<T>/PacketScope<T>. Wire-compatibility rules.
---

# Nalix packets (contracts/)

**Version: Nalix 14.3.8** (`Directory.Packages.props`; all `Nalix.*` packages move together).
Facts from the Nalix source (`src/Nalix.Codec`, generators) as used by Nalix 14.3.x clients.

## Non-negotiables

- **Never change an opcode value or a `[SerializeOrder]` value; never reuse a retired opcode.**
  The wire is opcode + fields in `[SerializeOrder]` order, not type names — renaming a type or
  folder is safe.
- **`[SerializeOrder]` is append-only.** The serializer writes fields back to back with no tags
  or length prefix; numbers are a sort key. New fields get a number higher than every existing
  one. Removing a field or inserting in the middle shifts everything after it — a wire break.
  Duplicate numbers are a defect (both are written, ordered by declaration).
- **A frame one field short or long does not deserialize.** Server and client ship together;
  a breaking change bumps `NosumProtocol.Version` (`contracts/Nosum.Contracts/NosumProtocol.cs`).
- Each packet type is defined once; `StaticOpCode` is unique across the protocol.
- Contracts hold no logic and reference no `gameplay/` or `server/` project.
- **Every client-supplied member of a `…/C2S` packet declares its bounds** (`Nosum.Contracts.Validation`, docs/adr/ADR-032 D3): `[WireMaxLength(n)]` on
  strings and lists, `[WireRange(min, max)]` on `int`/`long`/`float`/`double`, `[WireDefined]` on enums (constants in `WireLimits`). Booleans, bytes, GUIDs
  and unsigned numbers are exempt. `HandlerAuditTests` and `PacketBoundsTests` fail on a missing declaration; the server drops a packet that breaks one.
- Retiring a packet: keep its class and opcode reserved, document it as retired, and remove its handler and client use. Do not mark the class `[Obsolete]`: the generated formatter then fails the build under `TreatWarningsAsErrors`. The Account opcodes `0x11F0` and `0x11EF` (account recovery, ADR-032) and `0x1001`, `0x1002`, `0x1006`, `0x11EE`, `0x11E0` (password accounts, ADR-069) are retired this way. New operations opcodes take `0x1100` upward.

## Define a packet

```csharp
using Nalix.Abstractions.Networking.Packets;   // [Packet], [SerializeOrder], IPacketStaticOpcode
using Nalix.Abstractions.Serialization;        // [SerializePackable], SerializeLayout
using Nalix.Codec.DataFrames;                  // PacketBase<T>

namespace Nosum.Contracts.Farm.C2S;

/// <summary>Asks the server to plant a seed in one plot.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class PlantSeed : PacketBase<PlantSeed>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => FarmOpCode.PlantSeed;

    /// <summary>Target plot.</summary>
    [SerializeOrder(0)] public int PlotIndex { get; set; }

    /// <summary>Seed item to plant.</summary>
    [SerializeOrder(1)] public int SeedItemId { get; set; }
}
```

- The class is `partial`, non-abstract, inherits `PacketBase<TSelf>`, has a public
  parameterless constructor. Without `partial` the generator cannot add `Length` and
  `ResetForPool()`.
- Do not hand-write generated members (`Length`, `ResetForPool`, the formatter). Override
  `ResetForPool()` only for custom reset, calling `base.ResetForPool()` first.
- `[SerializeIgnore]` keeps a member off the wire. `IFixedSizeSerializable` only when every
  field is fixed-size.
- Nullable value types (`int?`, `Guid?`) are legal since Nalix 14.3.0 (1-byte presence flag);
  use one only when "empty" must differ from "absent".

## Opcodes

One static class per domain, `contracts/Nosum.Contracts/{Domain}/{Domain}OpCode.cs`, holding
`public const ushort` values in a band reserved for that domain. Record the band in the class's
XML doc and in `contracts/README.md`. Never reassign or reuse a value.

| Domain | Band |
| :--- | :--- |
| Account | `0x1000-0x10FF`, `0x11E0-0x11F0` (`0x11EF-0x11F0` retired, reserved) |
| Admin | `0x1100-0x11FF` |
| Player (presence, movement, energy/skills) | `0x2000-0x20FF` |
| Farm | `0x3000-0x30FF` |
| Building (buildings & animals) | `0x3100-0x31FF` |
| Market | `0x4000-0x40FF` |
| Goals (community goals) | `0x4100-0x41FF` |
| Inventory | `0x5000-0x50FF` |
| Quest | `0x6000-0x60FF` |
| Requests (daily board) | `0x6100-0x61FF` |
| World / Node / GroundItem / Chunks | `0x7000-0x73FF` |
| Look (wardrobe) | `0x8000-0x80FF` |
| Trade / Chat / Friends / Mail | `0x9000-0x93FF` |
| Coop | `0xA000-0xA0FF` |
| Placeable / Crafting / Chest / Furnace | `0xB000-0xB1FF` |
| Shop / Blacksmith | `0xC000-0xC0FF` |
| Fishing | `0xC300-0xC3FF` |
| Mine | `0xC100-0xC1FF` |
| Combat | `0xC200-0xC2FF` (`0xE100-0xE1FF` reserved) |
| Season (calendar) | `0xD000-0xD0FF` |
| Visit | `0xE000-0xE0FF` |
| Dev (Development only) | `0xF000-0xF0FF` |

## Rent and return

```csharp
PacketScope<PlotState> lease = PacketFactory<PlotState>.Acquire();
try
{
    lease.Value.PlotIndex = index;
    return context.Sender.ReplyAsync(lease.Value).DisposeOnCompletionAsync(lease);
}
catch { lease.Dispose(); throw; }
```

Never keep a reference to a packet after its scope is disposed — it may already be re-rented.
