// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Threading;
using System.Threading.Tasks;
using Nalix.Abstractions.Networking;
using Nosum.Contracts.Emote;

namespace Nosum.Abstractions.Emotes;

/// <summary>
/// The emote wheel (docs/adr/ADR-075 D1-D2): shows an emote above a farmer for the players near them. The server checks the code and the
/// per-player cooldown; the sender's own bubble is shown by the client from the result.
/// </summary>
public interface IEmoteService
{
    /// <summary>Shows an emote above the connection's farmer for every player who can see them.</summary>
    /// <param name="connection">The sender's connection; the place is taken from it.</param>
    /// <param name="emote">The emote on the wheel, 0 to 11.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The outcome.</returns>
    ValueTask<EmoteStatus> SendAsync(IConnection connection, byte emote, CancellationToken cancellationToken = default);
}
