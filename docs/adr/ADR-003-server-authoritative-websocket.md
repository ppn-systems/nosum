# ADR-003 — Server-authoritative game over one Nalix WebSocket

**Status:** Accepted (owner decision, 2026-10-25)

## Context

Browsers cannot open raw TCP or UDP sockets, and anything in a WebAssembly bundle can be read
and modified by the player. Nalix ships a WebSocket listener on the server
(`MapWebSocket<TProtocol>()` in `Nalix.Hosting`) and a browser-capable client transport
(`WebSocketSession` in `Nalix.SDK`; `TcpSession` and `UdpSession` are
`[UnsupportedOSPlatform("browser")]`).

## Decision

- **D1** — Every client connects through one Nalix WebSocket listener
  (`server/Nosum.Host/Program.cs`, path `/ws/`). No REST API.
- **D2** — Client and server share packet definitions from `contracts/Nosum.Contracts`; the
  wire format is opcode plus `[SerializeOrder]`.
- **D3** — The server is authoritative. Clients send intents; the server validates them with
  `Nosum.Game`, applies them, persists the result and replies or pushes the new state. Time,
  currency and inventory are never taken from the client.
- **D4** — Crop growth and other timers are computed from stored timestamps on demand, not
  simulated per tick on the server.
- **D5** — The client keeps exactly one `WebSocketSession` for the app's lifetime, owned by
  `wasm/Nosum.Wasm.Socket`; UI code reaches it only through ViewModels.

## Alternatives considered

- **SignalR (Blazor Server or ASP.NET Core hub)** — mature, but a second realtime stack next
  to Nalix and no shared packet model with the engine.
- **REST plus polling** — simple, but no server push and higher latency for a social game.

## Consequences

- A packet change can break old clients: server and client ship together, and
  `NosumProtocol.Version` is bumped on a breaking wire change.
- The server must validate every intent and rate-limit spam-able actions.

## Revisit when

A real-time shared world (many players moving in one area) needs unreliable, low-latency
delivery that WebSocket cannot provide.
