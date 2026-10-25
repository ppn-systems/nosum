---
name: nalix-packets
description: Use when defining or changing a packet class or opcode in contracts/ — [Packet], [GenerateFormatter], [SerializePackable], [SerializeOrder], StaticOpCode, opcode classes — or renting/returning packets via PacketFactory<T>/PacketScope<T>. Wire-compatibility rules.
---

# Nalix packets (contracts/)

**Version: Nalix 14.3.7** (`Directory.Packages.props`; all `Nalix.*` packages move together).
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
XML doc. Never reassign or reuse a value.

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
