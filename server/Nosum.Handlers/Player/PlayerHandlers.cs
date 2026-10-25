// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Threading.Tasks;
using Nalix.Abstractions.Injection;
using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Security;
using Nalix.Codec.Extensions;
using Nalix.Codec.Pooling;
using Nalix.Runtime.Extensions;
using Nosum.Abstractions.Inventory;
using Nosum.Abstractions.Player;
using Nosum.Abstractions.Progress;
using Nosum.Handlers.Session;
using Nosum.Contracts.Player;
using Nosum.Contracts.Player.C2S;
using Nosum.Contracts.Player.S2C;

namespace Nosum.Handlers.Player;

/// <summary>Entering the world and walking on a map. The server keeps the positions and changes maps itself; these handlers only relay intents.</summary>
[PacketHandler("Nosum.Player")]
public static partial class PlayerHandlers
{
    [Inject] private static IPlayerService s_players = null!;
    [Inject] private static IPlayerProgress s_progress = null!;
    [Inject] private static IInventoryService s_inventory = null!;

    /// <summary>Puts the player on the map the server chooses and replies with their id, that map and their spawn.</summary>
    /// <param name="context">The request.</param>
    /// <returns>A task that completes when the reply was sent.</returns>
    [PacketOpcode(PlayerOpCode.JoinMap)]
    [PacketPermission(PermissionLevel.USER)]
    [PacketRateLimit(4, 5, PolicyId = "player-join")]
    public static async ValueTask HandleJoinMapAsync(IPacketContext<JoinMap> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ushort sequence = context.Packet.SequenceId;
        string mapId = context.Packet.MapId;
        PlayerIdentity player = context.Connection.GetPlayer();

        JoinOutcome outcome = await s_players.JoinAsync(context.Connection, player.PlayerId, mapId, context.CancellationToken).ConfigureAwait(false);

        PacketScope<JoinMapResult> lease = PacketFactory<JoinMapResult>.Acquire();
        try
        {
            lease.Value.SequenceId = sequence;
            lease.Value.Status = outcome.Status;
            lease.Value.PlayerId = outcome.PlayerId;
            lease.Value.SpawnX = outcome.SpawnX;
            lease.Value.SpawnY = outcome.SpawnY;
            lease.Value.MapId = outcome.MapId;
            lease.Value.Facing = outcome.Facing;
            await context.Sender.ReplyAsync(lease.Value).DisposeOnCompletionAsync(lease).ConfigureAwait(false);
        }
        catch
        {
            lease.Dispose();
            throw;
        }

        if (outcome.Status != JoinStatus.Ok)
        {
            return;
        }

        // Accounts made before the axe, pickaxe and scythe existed get them once (docs/adr/ADR-023), then the player learns their energy and skills.
        _ = await s_inventory.BackfillStarterToolsAsync(player.PlayerId, context.CancellationToken).ConfigureAwait(false);
        await s_progress.PushAsync(player.PlayerId, context.CancellationToken).ConfigureAwait(false);
    }

    /// <summary>Eats the food in a hotbar slot; the server removes one and restores its energy (docs/adr/ADR-023 D2).</summary>
    /// <param name="context">The request.</param>
    /// <returns>A task that completes when the reply was sent.</returns>
    [PacketOpcode(PlayerOpCode.Eat)]
    [PacketPermission(PermissionLevel.USER)]
    [PacketRateLimit(8, 3, PolicyId = "player-eat")]
    public static async ValueTask HandleEatAsync(IPacketContext<Eat> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ushort sequence = context.Packet.SequenceId;
        int slot = context.Packet.Slot;
        PlayerIdentity player = context.Connection.GetPlayer();

        EatOutcome outcome = await s_progress.EatAsync(player.PlayerId, slot, context.CancellationToken).ConfigureAwait(false);

        PacketScope<EatResult> lease = PacketFactory<EatResult>.Acquire();
        try
        {
            lease.Value.SequenceId = sequence;
            lease.Value.Status = outcome.Status;
            await context.Sender.ReplyAsync(lease.Value).DisposeOnCompletionAsync(lease).ConfigureAwait(false);
        }
        catch
        {
            lease.Dispose();
            throw;
        }
    }

    /// <summary>Validates the claimed position; on refusal, pushes the position the server keeps.</summary>
    /// <param name="context">The intent.</param>
    /// <returns>A task that completes when the intent was handled.</returns>
    [PacketOpcode(PlayerOpCode.MoveIntent)]
    [PacketPermission(PermissionLevel.USER)]
    [PacketRateLimit(30, 1.0, PolicyId = "player-move")]
    public static async ValueTask HandleMoveIntentAsync(IPacketContext<MoveIntent> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        MoveIntent intent = context.Packet;
        uint sequence = intent.Sequence;

        MoveOutcome outcome = await s_players.MoveAsync(context.Connection, intent.X, intent.Y, intent.Facing, intent.Moving, context.CancellationToken).ConfigureAwait(false);
        if (outcome.Status != MoveStatus.Refused)
        {
            return;
        }

        PacketScope<MoveCorrection> lease = PacketFactory<MoveCorrection>.Acquire();
        try
        {
            lease.Value.Sequence = sequence;
            lease.Value.Reason = outcome.Reason;
            lease.Value.X = outcome.X;
            lease.Value.Y = outcome.Y;
            await context.Sender.SendAsync(lease.Value).DisposeOnCompletionAsync(lease).ConfigureAwait(false);
        }
        catch
        {
            lease.Dispose();
            throw;
        }
    }
}
