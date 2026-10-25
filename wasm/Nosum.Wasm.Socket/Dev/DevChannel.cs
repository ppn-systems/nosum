// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using Nalix.Codec.Pooling;
using Nosum.Contracts.Dev.C2S;
using Nosum.Contracts.Dev.S2C;

namespace Nosum.Wasm.Socket.Dev;

/// <summary>
/// Tester tool: asks a Development server to grant items or coins. A server running in Production does not know the opcode and never answers; the
/// request is only sent from a client built with <c>-p:NosumDevTools=true</c> (<c>NOSUM_DEV_TOOLS</c>), so a normal build contains no way to ask.
/// </summary>
public sealed class DevChannel
{
    private readonly IGameSocket _socket;

    /// <summary>Initializes the channel.</summary>
    /// <param name="socket">The game connection.</param>
    public DevChannel(IGameSocket socket)
    {
        ArgumentNullException.ThrowIfNull(socket);
        _socket = socket;
    }

    /// <summary>Asks the server for items and/or coins.</summary>
    /// <param name="itemId">The item, or zero for coins only.</param>
    /// <param name="quantity">How many items.</param>
    /// <param name="coins">Coins to add.</param>
    public void Grant(int itemId, int quantity, long coins) => _ = this.GrantAsync(itemId, quantity, coins);

    [SuppressMessage("Design", "CA1031", Justification = "A tester tool: a missing answer (Production) is not an error.")]
    private async Task GrantAsync(int itemId, int quantity, long coins)
    {
        try
        {
            using PacketScope<DevGrant> request = PacketFactory<DevGrant>.Acquire();
            request.Value.ItemId = itemId;
            request.Value.Quantity = quantity;
            request.Value.Coins = coins;
            _ = await _socket.RequestAsync<DevGrantResult>(request.Value).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // No answer: not a Development server.
        }
    }
}
