// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Wasm.Socket;

/// <summary>Where the game connection is in its life.</summary>
public enum GameSocketState
{
    /// <summary>Not started, or stopped.</summary>
    Offline = 0,

    /// <summary>Opening the socket and handshaking for the first time.</summary>
    Connecting = 1,

    /// <summary>Handshake done; requests and pushes work.</summary>
    Ready = 2,

    /// <summary>The connection was lost or failed; waiting to try again.</summary>
    Reconnecting = 3,

    /// <summary>The server's key is not the pinned one, or not the one trusted earlier: the server may be an impostor. The client refuses to talk to it (docs/adr/ADR-032 D1).</summary>
    KeyMismatch = 4,
}
