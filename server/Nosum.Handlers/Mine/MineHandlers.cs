// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Threading.Tasks;
using Nalix.Abstractions.Injection;
using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Security;
using Nosum.Abstractions.Mines;
using Nosum.Contracts.Mine;
using Nosum.Contracts.Mine.C2S;
using Nosum.Contracts.Mine.S2C;
using Nosum.Handlers.Session;

namespace Nosum.Handlers.Mine;

/// <summary>The mine's elevator and the player's depth (docs/adr/ADR-026 D3). Going down a ladder is a tool use, not a packet of its own.</summary>
[PacketHandler("Nosum.Mine")]
public static partial class MineHandlers
{
    [Inject] private static IMineService s_mine = null!;

    /// <summary>Replies with the floor the player is on and the deepest they reached.</summary>
    /// <param name="context">The request.</param>
    /// <returns>A task that completes when the reply was sent.</returns>
    [PacketOpcode(MineOpCode.GetMine)]
    [PacketPermission(PermissionLevel.USER)]
    [PacketRateLimit(4, 5, PolicyId = "mine-read")]
    public static async ValueTask HandleGetMineAsync(IPacketContext<GetMine> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ushort sequence = context.Packet.SequenceId;
        MineView view = await s_mine.GetAsync(context.Connection, context.CancellationToken).ConfigureAwait(false);
        await ReplyAsync(context, sequence, view).ConfigureAwait(false);
    }

    /// <summary>Rides the elevator to a stop the player has reached.</summary>
    /// <param name="context">The request.</param>
    /// <returns>A task that completes when the reply was sent.</returns>
    [PacketOpcode(MineOpCode.RideElevator)]
    [PacketPermission(PermissionLevel.USER)]
    [PacketRateLimit(4, 4, PolicyId = "mine-elevator")]
    public static async ValueTask HandleRideElevatorAsync(IPacketContext<RideElevator> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ushort sequence = context.Packet.SequenceId;
        int floor = context.Packet.Floor;
        MineView view = await s_mine.RideElevatorAsync(context.Connection, floor, context.CancellationToken).ConfigureAwait(false);
        await ReplyAsync(context, sequence, view).ConfigureAwait(false);
    }

    private static ValueTask ReplyAsync<TRequest>(IPacketContext<TRequest> context, ushort sequence, MineView view)
        where TRequest : class, IPacket
        => Replies.ReplyAsync<TRequest, MineState>(context, sequence, r =>
        {
            r.Status = view.Status;
            r.Floor = view.Floor;
            r.Deepest = view.Deepest;
        });
}
