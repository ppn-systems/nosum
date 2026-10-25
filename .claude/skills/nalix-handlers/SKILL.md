---
name: nalix-handlers
description: Use when writing or debugging a server packet handler in server/Nosum.Handlers — [PacketHandler]/[PacketOpcode], IPacketContext<T>, ReplyAsync vs SendAsync, [Inject], permissions, rate limits; or a handler never fires, WithHandler throws, data corrupts after await.
---

# Nalix handlers (server/Nosum.Handlers)

**Version: Nalix 14.3.7.** Registration in the host: `nalix-server-hosting`. Packet classes:
`nalix-packets`. What a handler must check: `gameplay-rules`.

## Non-negotiables

- **`[PacketHandler("Nosum.<Domain>")]` on the class**, `static partial` preferred. Without it
  the generator skips the class and registration throws `InternalErrorException`.
- **`[PacketOpcode(...)]` on every handler method; opcodes unique per dispatcher** (a duplicate
  throws at registration).
- **Register the class** with `.MapHandlers(typeof(XHandlers))` in
  `server/Nosum.Host/Program.cs`. There is no assembly scanning — an unregistered handler never
  runs, silently.
- **`IPacketContext<T>` is pooled and recycled when the handler returns.** Never store it or
  capture it in a lambda; copy `context.Packet` fields and `context.Connection` out before any
  `await` that outlives the method.
- **Reply with `context.Sender.ReplyAsync(...)`** — it echoes the request's `SequenceId`, which
  the client's `RequestAsync` matches on. `SendAsync` is for unsolicited pushes only; a reply
  sent with it is dropped by the client.
- **Responses are pooled**: `PacketFactory<T>.Acquire()` +
  `ReplyAsync(pkt).DisposeOnCompletionAsync(lease)`, with `try/catch { lease.Dispose(); throw; }`.
  Never `new` a response packet.
- **`[PacketPermission]` only works when `PermissionMiddleware` is registered**, and is then
  fail-closed: a handler without it is denied; public handlers declare
  `[PacketPermission(PermissionLevel.NONE)]`.
- **`[Inject]` static fields are filled only for classes registered through the host.**
- **Handlers reference `Nosum.Abstractions`, never `Nosum.Infrastructure`.**
- Rate-limit spam-able actions with `[PacketRateLimit(...)]` (enforced by `RateLimitMiddleware`).
- **A push to "the players of the map" is a choice (docs/adr/ADR-022): chunk by default, instance when map-wide.**
  Something that changed *at a place* (a plot, a placed object, a resource node, a ground item, a player's look)
  goes through `IChunkPublisher.PublishAsync(instance, chunk, packet)` and reaches only the players who can see that
  chunk; something that concerns the whole map whatever the distance (an announcement, chat) uses
  `PublishInstanceAsync(instance, packet)`; something for one player is a reply or a notifier push, not a publish.
  Never `IMapPresence.MulticastAsync` for a world event: it ignores distance. A feature that keeps per-chunk state
  also implements `IChunkStateContributor`, so the state of a chunk is sent once when the chunk enters a player's
  interest set.

## Shape

```csharp
[PacketHandler("Nosum.Farm")]
public static partial class FarmHandlers
{
    [Inject] private static IFarmService s_farms = null!;

    [PacketOpcode(FarmOpCode.PlantSeed)]
    [PacketPermission(PermissionLevel.USER)]
    public static async ValueTask HandlePlantSeedAsync(IPacketContext<PlantSeed> context)
    {
        ArgumentNullException.ThrowIfNull(context);
        int plot = context.Packet.PlotIndex;          // copy out before awaiting
        int seed = context.Packet.SeedItemId;

        PlantResult result = await s_farms.PlantAsync(context.Connection, plot, seed, context.CancellationToken)
            .ConfigureAwait(false);

        PacketScope<PlotState> lease = PacketFactory<PlotState>.Acquire();
        try
        {
            /* fill lease.Value from result */
            await context.Sender.ReplyAsync(lease.Value).DisposeOnCompletionAsync(lease).ConfigureAwait(false);
        }
        catch { lease.Dispose(); throw; }
    }
}
```

Usings: `Nalix.Abstractions.Injection` (`[Inject]`) · `Nalix.Abstractions.Networking`
(`IConnection`) · `Nalix.Abstractions.Networking.Packets` (`IPacketContext<T>`) ·
`Nalix.Abstractions.Security` (`PermissionLevel`) · `Nalix.Codec.Extensions` (`DisposeOnCompletionAsync`) · `Nalix.Codec.Pooling` (`PacketScope<T>`,
`PacketFactory<T>`) · `Nalix.Runtime.Extensions`.

## Gotchas

- A missing `[PacketHandler]` fails at registration, not at compile time.
- Out-of-order or replayed frames are dropped by per-connection sequence validation without a
  log line — "the packet vanished" can be expected behavior.
