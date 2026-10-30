// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Threading.Tasks;
using Nalix.Abstractions.Injection;
using Nalix.Abstractions.Networking;
using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Security;
using Nosum.Abstractions.Look;
using Nosum.Abstractions.Pets;
using Nosum.Contracts.Pet;
using Nosum.Contracts.Pet.C2S;
using Nosum.Contracts.Pet.S2C;
using Nosum.Handlers.Session;

namespace Nosum.Handlers.Pets;

/// <summary>
/// The pet companion (docs/adr/ADR-075 D3-D5). The identity comes from the connection; the service decides. An adoption re-announces the
/// farmer's look, so the players who can see them see the new companion at once.
/// </summary>
[PacketHandler("Nosum.Pet")]
public static partial class PetHandlers
{
    [Inject] private static IPetService s_pets = null!;
    [Inject] private static ILookService s_looks = null!;

    /// <summary>Replies with the player's pet.</summary>
    /// <param name="context">The request.</param>
    /// <returns>A task that completes when the reply was sent.</returns>
    [PacketOpcode(PetOpCode.GetPet)]
    [PacketPermission(PermissionLevel.USER)]
    [PacketRateLimit(4, 5, PolicyId = "pet-read")]
    public static async ValueTask HandleGetPetAsync(IPacketContext<GetPet> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ushort sequence = context.Packet.SequenceId;
        Guid playerId = context.Connection.GetPlayer().PlayerId;

        PetView view = await s_pets.GetAsync(playerId, context.CancellationToken).ConfigureAwait(false);
        await Replies.ReplyAsync<GetPet, PetState>(context, sequence, state => Fill(state, view)).ConfigureAwait(false);
    }

    /// <summary>Adopts a cat or a dog with a name.</summary>
    /// <param name="context">The request.</param>
    /// <returns>A task that completes when the reply was sent.</returns>
    [PacketOpcode(PetOpCode.AdoptPet)]
    [PacketPermission(PermissionLevel.USER)]
    [PacketRateLimit(2, 5, PolicyId = "pet-adopt")]
    public static async ValueTask HandleAdoptPetAsync(IPacketContext<AdoptPet> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ushort sequence = context.Packet.SequenceId;
        IConnection connection = context.Connection;
        Guid playerId = connection.GetPlayer().PlayerId;

        PetView view = await s_pets.AdoptAsync(playerId, context.Packet.Kind, context.Packet.Name, context.CancellationToken).ConfigureAwait(false);
        if (view.Status == PetStatus.Ok)
        {
            await s_looks.AnnounceAsync(connection, playerId, context.CancellationToken).ConfigureAwait(false);
        }

        await Replies.ReplyAsync<AdoptPet, PetState>(context, sequence, state => Fill(state, view)).ConfigureAwait(false);
    }

    /// <summary>Pets the player's pet, once a game day.</summary>
    /// <param name="context">The request.</param>
    /// <returns>A task that completes when the reply was sent.</returns>
    [PacketOpcode(PetOpCode.PetPet)]
    [PacketPermission(PermissionLevel.USER)]
    [PacketRateLimit(2, 5, PolicyId = "pet-pet")]
    public static async ValueTask HandlePetPetAsync(IPacketContext<PetPet> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ushort sequence = context.Packet.SequenceId;
        Guid playerId = context.Connection.GetPlayer().PlayerId;

        PetView view = await s_pets.PetAsync(playerId, context.CancellationToken).ConfigureAwait(false);
        await Replies.ReplyAsync<PetPet, PetState>(context, sequence, state => Fill(state, view)).ConfigureAwait(false);
    }

    private static void Fill(PetState state, PetView view)
    {
        state.Status = view.Status;
        state.Kind = view.Kind;
        state.Name = view.Name;
        state.Mood = view.Mood;
        state.PettedToday = view.PettedToday;
    }
}
