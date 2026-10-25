// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;

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
    /// Gets or sets a pinned server X25519 public key (hex). Kept for single-key setups; it counts like the first entry of
    /// <see cref="ServerPublicKeys"/>.
    /// </summary>
    public string? ServerPublicKey { get; set; }

    /// <summary>
    /// Gets the pinned server public keys (hex), at most <see cref="Security.ServerKeys.MaxPinned"/>: the current key and, during a rotation window,
    /// the next one (docs/adr/ADR-032 D1). The handshake is refused unless the server proves it holds one of them. When none is pinned the client
    /// falls back to trust on first use (<see cref="KeyStore"/>), which is for development only.
    /// </summary>
    public IList<string> ServerPublicKeys { get; } = [];

    /// <summary>
    /// Gets or sets whether a pinned key is mandatory. A deployment sets it next to its keys so that a boot configuration that lost them stops the
    /// client instead of quietly falling back to trust on first use.
    /// </summary>
    public bool RequirePin { get; set; }

    /// <summary>
    /// Gets or sets where a development build remembers the key it trusted on first use. Without it (and without a pin) the handshake trusts whatever key the
    /// server presents, as before. Ignored while a key is pinned.
    /// </summary>
    public Security.IServerKeyStore? KeyStore { get; set; }

    /// <summary>Gets or sets the first delay before a reconnect attempt.</summary>
    public TimeSpan ReconnectBaseDelay { get; set; } = TimeSpan.FromMilliseconds(500);

    /// <summary>Gets or sets the longest delay between reconnect attempts.</summary>
    public TimeSpan ReconnectMaxDelay { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Applies the boot configuration the deploy publishes (<c>wwwroot/appsettings.json</c>): <c>Server:PublicKeys:0</c> and <c>Server:PublicKeys:1</c> are the
    /// pinned server keys, <c>Server:RequirePin</c> makes a pin mandatory. When no key is pinned, a development build trusts the server on first use and
    /// remembers the key in the store the factory creates.
    /// </summary>
    /// <param name="read">Reads a configuration value by key.</param>
    /// <param name="keyStore">Creates the store for the key trusted on first use; called only when no key is pinned.</param>
    /// <returns>These options.</returns>
    /// <exception cref="FormatException">A configured key is not a 64-digit hex key.</exception>
    public GameSocketOptions ApplyBootConfiguration(Func<string, string?> read, Func<Security.IServerKeyStore>? keyStore = null)
    {
        ArgumentNullException.ThrowIfNull(read);
        foreach (string key in Security.ServerKeys.FromConfiguration(read))
        {
            this.ServerPublicKeys.Add(key);
        }

        this.RequirePin = string.Equals(read("Server:RequirePin"), "true", StringComparison.OrdinalIgnoreCase);
        if (this.ServerPublicKeys.Count == 0 && keyStore is not null)
        {
            this.KeyStore = keyStore();
        }

        return this;
    }

    /// <summary>
    /// Builds options for the server that serves the page: same host. A page served over https sits behind the reverse proxy
    /// (docs/adr/ADR-031 D2), which serves the client and proxies <see cref="DefaultPath"/> on the page's own port, so the socket uses
    /// that port with TLS; an http page is a local development server and the socket goes to <see cref="DefaultPort"/> directly
    /// (a plain <c>ws://</c> socket is blocked from an https page, which is why the two cases differ).
    /// </summary>
    /// <param name="pageAddress">The page's base address.</param>
    /// <returns>The options.</returns>
    public static GameSocketOptions ForPageHost(Uri pageAddress)
    {
        ArgumentNullException.ThrowIfNull(pageAddress);

        bool tls = string.Equals(pageAddress.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal);
        return new GameSocketOptions
        {
            Host = pageAddress.Host,
            UseTls = tls,
            Port = tls ? (ushort)pageAddress.Port : DefaultPort,
        };
    }
}
