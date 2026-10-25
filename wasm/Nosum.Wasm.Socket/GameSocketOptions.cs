// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Nosum.Wasm.Socket;

/// <summary>
/// Where the game server is. The one place the address lives: change the defaults here (or build
/// the options in <c>Program.cs</c>) and every feature follows.
/// </summary>
public sealed class GameSocketOptions
{
    /// <summary>Server WebSocket port (<c>server/Nosum.Host/Program.cs</c>).</summary>
    public const ushort DefaultPort = 57300;

    /// <summary>Server WebSocket path; must equal the server's listener path.</summary>
    public const string DefaultPath = "/ws/";

    /// <summary>Gets or sets the server host name or address.</summary>
    public string Host { get; set; } = "127.0.0.1";

    /// <summary>Gets or sets the server port.</summary>
    public ushort Port { get; set; } = DefaultPort;

    /// <summary>Gets or sets the WebSocket path.</summary>
    public string Path { get; set; } = DefaultPath;

    /// <summary>Gets or sets whether to use <c>wss://</c>.</summary>
    public bool UseTls { get; set; }

    /// <summary>
    /// Gets or sets the pinned server X25519 public key (hex). While <see langword="null"/>, the client
    /// trusts the key the server presents on first use, which is acceptable only against a local
    /// development server. TODO: pin it for any real deployment (docs/adr/ADR-003).
    /// </summary>
    public string? ServerPublicKey { get; set; }

    /// <summary>Gets or sets the first delay before a reconnect attempt.</summary>
    public TimeSpan ReconnectBaseDelay { get; set; } = TimeSpan.FromMilliseconds(500);

    /// <summary>Gets or sets the longest delay between reconnect attempts.</summary>
    public TimeSpan ReconnectMaxDelay { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Builds options for the server that serves the page: same host, the default port, TLS when
    /// the page itself is https (a plain <c>ws://</c> socket is blocked from an https page).
    /// </summary>
    /// <param name="pageAddress">The page's base address.</param>
    /// <returns>The options.</returns>
    public static GameSocketOptions ForPageHost(Uri pageAddress)
    {
        ArgumentNullException.ThrowIfNull(pageAddress);

        return new GameSocketOptions
        {
            Host = pageAddress.Host,
            UseTls = string.Equals(pageAddress.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal),
        };
    }
}
