// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using Nalix.Abstractions.Networking.Packets;
using Nalix.Codec.DataFrames;
using Nalix.Codec.Pooling;
using Nosum.Contracts.Inventory;
using Nosum.Contracts.Inventory.C2S;
using Nosum.Contracts.Inventory.S2C;

namespace Nosum.Wasm.Socket.Inventory;

/// <summary>The backpack's size and the next upgrade, and how the last request ended.</summary>
/// <param name="Status">The outcome of the last request.</param>
/// <param name="Capacity">Slots the backpack has now.</param>
/// <param name="NextCapacity">Slots the next size has; zero when it is the largest.</param>
/// <param name="NextPrice">Coins the next size costs.</param>
public readonly record struct BackpackInfo(BackpackStatus Status, int Capacity, int NextCapacity, int NextPrice);

/// <summary>
/// Talks to the tailor about the backpack (docs/adr/ADR-081): reads the size and the next upgrade, and buys it. The size and the price come from the
/// server's rules; the answer to each request arrives as <see cref="Updated"/>.
/// </summary>
public sealed class BackpackChannel
{
    private readonly IGameSocket _socket;

    /// <summary>Initializes the channel.</summary>
    /// <param name="socket">The game connection.</param>
    public BackpackChannel(IGameSocket socket)
    {
        ArgumentNullException.ThrowIfNull(socket);
        _socket = socket;
    }

    /// <summary>Raised with the server's answer to a request and the state after it. May run on any thread.</summary>
    public event Action<BackpackInfo>? Updated;

    /// <summary>Reads the backpack's size and the next upgrade.</summary>
    public void Load() => _ = this.SendAsync<GetBackpack>(static _ => { });

    /// <summary>Buys the next size.</summary>
    public void Upgrade() => _ = this.SendAsync<UpgradeBackpack>(static _ => { });

    [SuppressMessage("Design", "CA1031", Justification = "A lost request changes nothing; the player tries again.")]
    private async Task SendAsync<TRequest>(Action<TRequest> fill)
        where TRequest : PacketBase<TRequest>, IPacketStaticOpcode, new()
    {
        try
        {
            using PacketScope<TRequest> request = PacketFactory<TRequest>.Acquire();
            fill(request.Value);
            BackpackState reply = await _socket.RequestAsync<BackpackState>(request.Value).ConfigureAwait(false);
            this.Updated?.Invoke(new BackpackInfo(reply.Status, reply.Capacity, reply.NextCapacity, reply.NextPrice));
        }
        catch (Exception)
        {
            // Connection dropped.
        }
    }
}
