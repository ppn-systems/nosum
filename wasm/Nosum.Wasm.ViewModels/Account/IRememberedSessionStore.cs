// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Threading;
using System.Threading.Tasks;

namespace Nosum.Wasm.ViewModels.Account;

/// <summary>
/// Keeps the remembered session in this browser (docs/adr/ADR-081). The browser implementation lives in <c>Nosum.Wasm</c>. An implementation
/// never throws: a browser that refuses storage reads as "nothing remembered" and ignores writes, so the game still starts.
/// </summary>
public interface IRememberedSessionStore
{
    /// <summary>Reads the stored session, or <see langword="null"/> when none is stored or the stored record cannot be read.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The remembered session, or <see langword="null"/>.</returns>
    Task<RememberedSession?> LoadAsync(CancellationToken cancellationToken);

    /// <summary>Replaces the stored session.</summary>
    /// <param name="session">The session to remember.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when the write is done (or ignored).</returns>
    Task SaveAsync(RememberedSession session, CancellationToken cancellationToken);

    /// <summary>Forgets the stored session.</summary>
    /// <param name="cancellationToken">Cancels the removal.</param>
    /// <returns>A task that completes when the removal is done (or ignored).</returns>
    Task ClearAsync(CancellationToken cancellationToken);
}
