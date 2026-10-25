// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Threading.Tasks;

namespace Nosum.Wasm.Socket.Security;

/// <summary>
/// Remembers the server key a development build trusted on first use (docs/adr/ADR-032 D1). The browser implementation keeps it in
/// <c>localStorage</c>; a deployment that ships a pin never reads it.
/// </summary>
public interface IServerKeyStore
{
    /// <summary>Reads the remembered key.</summary>
    /// <returns>The key as hex, or <see langword="null"/> when none was remembered (or storage is unavailable).</returns>
    ValueTask<string?> GetAsync();

    /// <summary>Remembers a key.</summary>
    /// <param name="keyHex">The key as hex.</param>
    /// <returns>A task that completes when the key is stored (a storage that is unavailable is not an error).</returns>
    ValueTask SetAsync(string keyHex);
}
