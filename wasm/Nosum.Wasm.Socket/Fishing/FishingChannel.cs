// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;
using Nalix.Codec.Pooling;
using Nosum.Contracts.Fishing.C2S;
using Nosum.Contracts.Fishing.S2C;

namespace Nosum.Wasm.Socket.Fishing;

/// <summary>
/// Sends casts and reel-ins to the server (docs/adr/ADR-071 D9). The server starts a cast on its own clock and answers with the bite delay and the
/// press window; the reel-in carries only the cast id. Each call waits for the reply that carries its sequence id. A lost connection throws; the view
/// model says the line did not go through.
/// </summary>
public sealed class FishingChannel
{
    private readonly IGameSocket _socket;

    /// <summary>Initializes the channel.</summary>
    /// <param name="socket">The game connection.</param>
    public FishingChannel(IGameSocket socket)
    {
        ArgumentNullException.ThrowIfNull(socket);
        _socket = socket;
    }

    /// <summary>Casts the held fishing rod at a tile.</summary>
    /// <param name="tileX">Tile x of the water.</param>
    /// <param name="tileY">Tile y of the water.</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>The server's answer; an Ok status carries the open cast's id and its timing.</returns>
    public async ValueTask<CastResult> CastAsync(int tileX, int tileY, CancellationToken cancellationToken = default)
    {
        using PacketScope<CastRod> request = PacketFactory<CastRod>.Acquire();
        request.Value.TileX = tileX;
        request.Value.TileY = tileY;
        return await _socket.RequestAsync<CastResult>(request.Value, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Reels the line in during the bite window.</summary>
    /// <param name="castId">The id of the open cast, from the cast's answer.</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>The server's answer, with the fish when one was caught.</returns>
    public async ValueTask<ReelResult> ReelAsync(uint castId, CancellationToken cancellationToken = default)
    {
        using PacketScope<ReelIn> request = PacketFactory<ReelIn>.Acquire();
        request.Value.CastId = castId;
        return await _socket.RequestAsync<ReelResult>(request.Value, cancellationToken).ConfigureAwait(false);
    }
}
