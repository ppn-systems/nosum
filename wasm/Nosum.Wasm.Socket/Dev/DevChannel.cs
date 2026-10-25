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

    /// <summary>Asks the server to move its game clock forward by whole game days (crops grow, bare soil may revert). The change reaches the screen as pushed tiles.</summary>
    /// <param name="days">Game days, 1 to 60.</param>
    public void AdvanceClock(int days) => _ = this.AdvanceClockAsync(days);

    [SuppressMessage("Design", "CA1031", Justification = "A tester tool: a missing answer (Production) is not an error.")]
    private async Task AdvanceClockAsync(int days)
    {
        try
        {
            using PacketScope<DevAdvanceClock> request = PacketFactory<DevAdvanceClock>.Acquire();
            request.Value.Days = days;
            _ = await _socket.RequestAsync<DevAdvanceClockResult>(request.Value).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // No answer: not a Development server.
        }
    }

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
