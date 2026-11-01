// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Nalix.Abstractions.Networking.Packets;
using Nalix.Codec.DataFrames;
using Nalix.Codec.Pooling;
using Nosum.Contracts.Mine;
using Nosum.Contracts.Mine.C2S;
using Nosum.Contracts.Mine.S2C;

namespace Nosum.Wasm.Socket.Mine;

/// <summary>The player's place in the mine and how the last request ended.</summary>
/// <param name="Status">The outcome of the last request.</param>
/// <param name="Floor">The floor the player stands on; zero on the entrance cave and outside the mine.</param>
/// <param name="Deepest">The deepest floor the player reached.</param>
public readonly record struct MineInfo(MineStatus Status, int Floor, int Deepest);

/// <summary>
/// Talks to the mine's elevator (docs/adr/ADR-026 D3): reads how deep the player has been and rides to a stop. Going down a ladder is a tool use on its tile,
/// not part of this channel. The answer to each request arrives as <see cref="Updated"/>; the map change that follows a ride arrives as the usual map-change push.
/// </summary>
public sealed class MineChannel
{
    private readonly IGameSocket _socket;
    private int _deepestFloor;

    /// <summary>Initializes the channel.</summary>
    /// <param name="socket">The game connection.</param>
    public MineChannel(IGameSocket socket)
    {
        ArgumentNullException.ThrowIfNull(socket);
        _socket = socket;
    }

    /// <summary>Raised with the server's answer to a request and the state after it. May run on any thread.</summary>
    public event Action<MineInfo>? Updated;

    /// <summary>Gets the deepest mine floor the player has reached, from the last answer; zero until the first answer.</summary>
    public int DeepestFloor => Volatile.Read(ref _deepestFloor);

    /// <summary>Reads the player's mine progress.</summary>
    public void Load() => _ = this.SendAsync<GetMine>(static _ => { });

    /// <summary>Rides the elevator to a stop.</summary>
    /// <param name="floor">0 for the entrance cave, or an elevator floor the player has reached.</param>
    public void Ride(int floor) => _ = this.SendAsync<RideElevator>(r => r.Floor = floor);

    [SuppressMessage("Design", "CA1031", Justification = "A lost request changes nothing; the player tries again.")]
    private async Task SendAsync<TRequest>(Action<TRequest> fill)
        where TRequest : PacketBase<TRequest>, IPacketStaticOpcode, new()
    {
        try
        {
            using PacketScope<TRequest> request = PacketFactory<TRequest>.Acquire();
            fill(request.Value);
            MineState reply = await _socket.RequestAsync<MineState>(request.Value).ConfigureAwait(false);
            Volatile.Write(ref _deepestFloor, reply.Deepest);
            this.Updated?.Invoke(new MineInfo(reply.Status, reply.Floor, reply.Deepest));
        }
        catch (Exception)
        {
            // Connection dropped.
        }
    }
}
