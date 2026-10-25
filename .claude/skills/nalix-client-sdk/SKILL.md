---
name: nalix-client-sdk
description: Use when writing or debugging wasm/Nosum.Wasm.Socket or any client transport code — WebSocketSession construction, connect-then-handshake order, On<T> subscriptions and leaks, RequestAsync correlation, resume, disconnect events. UI code goes through view models (blazor-ui).
---

# Nalix client SDK (wasm/Nosum.Wasm.Socket)

**Version: Nalix 14.3.7.** Server side: `nalix-server-hosting`.

## Non-negotiables

- **Browser = `WebSocketSession` only.** `TcpSession` and `UdpSession` are
  `[UnsupportedOSPlatform("browser")]`. On `WebSocketSession`, never call the synchronous
  `Send(ReadOnlySpan<byte>, bool)` (also browser-unsupported); use the `SendAsync` overloads.
- **One session for the app's lifetime**, owned by `Nosum.Wasm.Socket`. Pages and view models
  never construct or hold a session.
- **Order:** construct → subscribe `On<T>()` → `ConnectAsync()` → `HandshakeAsync()` → use →
  `DisconnectGracefullyAsync()` → `Dispose()`. `ConnectAsync` only opens the socket;
  `HandshakeAsync` derives the session key and is never automatic.
- **Pin the server key.** Set `TransportOptions.ServerPublicKey` (X25519, hex) for any real
  deployment. With it unset, `AllowTofu` (default `true`) trusts whatever key the server
  presents on first use — acceptable only against a local dev server. The server must run
  `UseSecureConnections()` for `HandshakeAsync` to succeed.
- **Every `On<T>()` returns an `IDisposable`; dispose it** on teardown, or the handler fires
  forever.
- **`RequestAsync<TResponse>`** stamps the request's `SequenceId` and matches the reply on it;
  the server must answer with `ReplyAsync`. Retry only idempotent requests — a timeout does
  not cancel the server-side work.
- **`On<T>` callbacks run off the Blazor render context**; view models marshal with
  `InvokeAsync(StateHasChanged)` or their own change notification.
- `OnConnected` is the raw socket open (before handshake); `OnReady` fires after handshake or
  resume. `OnDisconnected` fires once per real close, before teardown completes.
- A 14.3.7 peer cannot read a 14.3.6-or-older peer's encrypted frames: server and client
  always use the same `Nalix.*` version.

## Shape

```csharp
WebSocketSession session = new(
    new TransportOptions { Address = host, Port = 443, ServerPublicKey = pin },
    new WebSocketTransportOptions { Path = "/ws/", UseTls = true });

IDisposable sub = session.On<PlotState>(OnPlotState);   // before connecting
await session.ConnectAsync().ConfigureAwait(false);
await session.HandshakeAsync().ConfigureAwait(false);
```

Usings: `Nalix.SDK.Options` · `Nalix.SDK.Transport` · `Nalix.SDK.Transport.Extensions`.
The URI is `{ws|wss}://{host}:{port}{Path}`.
