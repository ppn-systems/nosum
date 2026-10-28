---
name: nalix-server-hosting
description: Use when changing server/Nosum.Host — NetworkApplication.CreateBuilder, Use* feature macros, MapHandlers, MapWebSocket/WithPath/OnPort, middleware registration, lifecycle; or a handler never fires, Build() throws, the WebSocket handshake 404s.
---

# Nalix hosting (server/Nosum.Host)

**Version: Nalix 14.3.8.** Handler bodies: `nalix-handlers`.

## Non-negotiables

- **One composition root.** The builder chain lives in `Program.BuildApplication`
  (`server/Nosum.Host/Program.cs`); `Main` only runs it. Tests build the same app through that
  method (`tests/Nosum.IntegrationTests/Server/HostWebSocketTests.cs`) — keep it the single
  place listeners and handlers are registered.
- **No assembly scanning.** Every handler class is registered with `.MapHandlers(typeof(X))`.
- **`WithPath` must equal the client's `WebSocketTransportOptions.Path`** (`/ws/`), or the
  WebSocket handshake fails with 404.
- **Never call `Bootstrap.Initialize()`** — it is a `[ModuleInitializer]` and already ran.
- **A `Configure<T>` callback runs twice** — keep it idempotent.
- `RunAsync(ct)` activates, waits for cancellation, then deactivates; no manual teardown.

## Builder shape

```csharp
NetworkApplication.CreateBuilder()
    .UseGroups()                 // connection groups (map instances)
    .UseTimeSync()               // PING/PONG + time sync handlers
    .UseSystemControl()          // DISCONNECT, CIPHER_UPDATE, REKEY …
    .UseSecureConnections(ServerKey.ConfiguredPath()) // X25519 handshake + server certificate
    .Configure<ConnectionQuotaOptions>(AbuseLimits.Apply)
    .MapHandlers(typeof(PlayerHandlers))
    .MapHandlers(typeof(AccountHandlers))
    // ... all domain handler classes (Inventory, Farm, Quest, Market, Look, Trade, Coop, Placeable, Crafting, Chest, Furnace, Building, Node, Visit, Shop, Blacksmith, Mine, Combat, Goal, Calendar, GroundItem, Chat, Friend, Mail, Admin, Request; plus DevHandlers in Development)
    .ConfigureDispatchOptions(o =>
    {
        o.WithMiddleware(new MetricsMiddleware(...));
        o.WithMiddleware(new InputBoundsMiddleware());
        o.WithMiddleware(new PermissionMiddleware());
        if (!LoadTestMode()) o.WithMiddleware(new RateLimitMiddleware());
    })
    .MapWebSocket<DefaultProtocol>()
        .OnPort(webSocketPort)
        .WithPath("/ws/")
        .Bind()
    .Build();
```

Usings: `Nalix.Hosting` (`NetworkApplication`) · `Nalix.Hosting.Protocols` (`DefaultProtocol`)
· `Nalix.Runtime.Groups` (`UseGroups`) · `Nalix.Runtime.Middleware.Standard` (standard middleware).

Current state: `UseGroups()`, time sync, system control, `UseSecureConnections(ServerKey.ConfiguredPath())`,
`ConnectionQuotaOptions` abuse limits, all domain handlers (`PlayerHandlers`, `AccountHandlers`, `InventoryHandlers`,
`FarmHandlers`, `QuestHandlers`, `MarketHandlers`, `LookHandlers`, `TradeHandlers`, `CoopHandlers`, `PlaceableHandlers`,
`CraftingHandlers`, `ChestHandlers`, `FurnaceHandlers`, `BuildingHandlers`, `NodeHandlers`, `VisitHandlers`,
`ShopHandlers`, `BlacksmithHandlers`, `MineHandlers`, `CombatHandlers`, `GoalHandlers`, `CalendarHandlers`,
`GroundItemHandlers`, `ChatHandlers`, `FriendHandlers`, `MailHandlers`, `AdminHandlers`, `RequestHandlers`, and `DevHandlers` in Development),
`MetricsMiddleware`, `InputBoundsMiddleware`, `PermissionMiddleware`, `RateLimitMiddleware` and the WebSocket listener.
Services handlers `[Inject]` are registered in `HostServices.Register` and `Program.RegisterServices` before the builder runs.

## Gotchas

- `IConnectionHub` and `IConnectionGuard` are required by `Build()`; defaults are created
  automatically unless that is disabled.
- Inbound path: listener → frame processor (decrypt/decompress, sequence check) → protocol →
  dispatch → middleware → handler. A frame dropped by the sequence check is not logged.
- Nalix keeps its server key, `server.ini` and block lists under `NALIX_BASE_PATH` / `NALIX_CONFIG_PATH` (default `~/.local/share/Nalix`); a container must mount a volume
  there. `server.ini` is created with every default on first start and keeps the values already in it; `deploy/host-entrypoint.sh` sets `[ForwardedHeaders]` and
  `AllowedOrigins` in it. Behind a proxy without those, every player shares the proxy's address and its rate limits.
- Operations live in `Nosum.Host/Ops/` (docs/adr/ADR-031): the health and metrics `HttpListener` (port 57301), the host command line (`Main(string[] args)`: `admin ...`,
  `healthcheck`), `MetricsMiddleware` (registered first, so it times the permission and rate-limit checks too) and `OpsServices.Register` (one call from `RegisterServices`).
  Keep `Program.cs` edits to one-liners that call into that folder.
- `NOSUM_LOAD_TEST=1` leaves `RateLimitMiddleware` out for the load test; the host refuses to start with it outside Staging and Development. Never set it anywhere else.
- A handler gated `PermissionLevel.SYSTEM_ADMINISTRATOR` is an admin intent: the connection gets that level only when `AccountOutcome.IsAdmin` (the `players.role` column), and the
  service behind it re-checks the role on every call.
