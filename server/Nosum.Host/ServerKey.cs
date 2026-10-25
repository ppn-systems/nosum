// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;
using Nalix.Runtime.Handlers;

namespace Nosum.Host;

/// <summary>
/// The server's static X25519 identity (docs/adr/ADR-032 D1). The private half lives in a file Nalix creates on first start; the public half is what
/// the deploy publishes to clients as the pin. Losing the file changes the public key and every pinned client refuses the server until the new key is published.
/// </summary>
internal static class ServerKey
{
    /// <summary>Gets the path of the identity file from <c>NOSUM_SERVER_KEY_PATH</c>, or null to use Nalix's default (<c>~/.local/share/Nalix/config/certificate.private</c> on Linux). Mount a persistent volume there in a container.</summary>
    /// <returns>The configured path, or null.</returns>
    internal static string? ConfiguredPath()
    {
        string? path = Environment.GetEnvironmentVariable("NOSUM_SERVER_KEY_PATH");
        return string.IsNullOrWhiteSpace(path) ? null : path;
    }

    /// <summary>Prints the public key (hex) once the listener has loaded the identity, so an operator can copy it into the client's boot configuration. Only the public key is printed.</summary>
    /// <param name="write">Receives the line to print.</param>
    /// <param name="cancellationToken">Stops waiting.</param>
    /// <returns>A task that completes after printing, after 30 seconds without a key, or when cancelled.</returns>
    internal static async Task AnnounceAsync(Action<string> write, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(write);
        try
        {
            for (int attempt = 0; attempt < 120; attempt++)
            {
                if (TryRead(out string key))
                {
                    write("Server public key (publish it as the client pin, docs/adr/ADR-032): " + key);
                    return;
                }

                await Task.Delay(250, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down before the listener came up.
        }
    }

    private static bool TryRead(out string key)
    {
        try
        {
            Nalix.Abstractions.Primitives.Bytes32 value = HandshakeHandlers.ServerPublicKey;
            key = value.IsZero ? string.Empty : value.ToString();
            return key.Length > 0;
        }
        catch (InvalidOperationException)
        {
            // The listener has not loaded the identity yet.
            key = string.Empty;
            return false;
        }
    }
}
