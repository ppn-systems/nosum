---
name: nalix-server-hosting
description: Use when changing server/Nosum.Host — NetworkApplication.CreateBuilder, Use* feature macros, MapHandlers, MapWebSocket/WithPath/OnPort, middleware registration, lifecycle; or a handler never fires, Build() throws, the WebSocket handshake 404s.
---

# Nalix hosting (server/Nosum.Host)

**Version: Nalix 14.3.7.** Handler bodies: `nalix-handlers`.

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
    .UseTimeSync()               // PING/PONG + time sync handlers
    .UseSystemControl()          // DISCONNECT, CIPHER_UPDATE, REKEY …
    // .UseSecureConnections()   // X25519 handshake + server certificate — required before clients can HandshakeAsync()
    // .UseSessions()            // session save/resume
    // .MapHandlers(typeof(FarmHandlers))
    // .ConfigureDispatchOptions(o => o.WithMiddleware(new PermissionMiddleware(...)))
    .MapWebSocket<DefaultProtocol>()
        .OnPort(webSocketPort)
        .WithPath("/ws/")
        .Bind()
    .Build();
```

Usings: `Nalix.Hosting` (`NetworkApplication`) · `Nalix.Hosting.Protocols` (`DefaultProtocol`)
· `Nalix.Runtime.Middleware.Standard` (standard middleware).

Current state: time sync, system control and the WebSocket listener only. Adding
`UseSecureConnections()` brings a certificate store; decide where its key lives before
enabling it (never in the repository).

## Gotchas

- `IConnectionHub` and `IConnectionGuard` are required by `Build()`; defaults are created
  automatically unless that is disabled.
- Inbound path: listener → frame processor (decrypt/decompress, sequence check) → protocol →
  dispatch → middleware → handler. A frame dropped by the sequence check is not logged.
