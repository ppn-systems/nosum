// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Nalix.Abstractions.Injection;
using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Security;
using Nosum.Abstractions.Buildings;
using Nosum.Contracts.Building;
using Nosum.Contracts.Building.C2S;
using Nosum.Contracts.Building.S2C;
using Nosum.Gameplay.Animals;
using Nosum.Gameplay.Buildings;
using Nosum.Handlers.Session;

namespace Nosum.Handlers.Building;

/// <summary>
/// Farm buildings and animals (docs/adr/ADR-028 D6). The client names what it wants (a building kind, an animal, an inventory slot); the connection says who and where, and the server
/// decides the rest: the position, the coins, the day, what the animal gives.
/// </summary>
[PacketHandler("Nosum.Building")]
public static partial class BuildingHandlers
{
    [Inject] private static IBuildingService s_buildings = null!;

    /// <summary>Replies with the buildings of the land the player stands on, or of their own lands.</summary>
    /// <param name="context">The request.</param>
    /// <returns>A task that completes when the reply was sent.</returns>
    [PacketOpcode(BuildingOpCode.GetBuildings)]
    [PacketPermission(PermissionLevel.USER)]
    [PacketRateLimit(4, 5, PolicyId = "building-read")]
    public static async ValueTask HandleGetBuildingsAsync(IPacketContext<GetBuildings> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ushort sequence = context.Packet.SequenceId;
        bool mine = context.Packet.Mine;
        IReadOnlyList<BuildingView> views = await s_buildings.ListAsync(context.Connection, mine, context.CancellationToken).ConfigureAwait(false);
        await Replies.ReplyAsync<GetBuildings, BuildingsSnapshot>(context, sequence, r =>
        {
            r.Mine = mine;
            r.Buildings.Clear();
            foreach (BuildingView view in views)
            {
                r.Buildings.Add(view.ToRow());
            }
        }).ConfigureAwait(false);
    }

    /// <summary>Buys a building's blueprint at the carpenter's counter.</summary>
    /// <param name="context">The request.</param>
    /// <returns>A task that completes when the reply was sent.</returns>
    [PacketOpcode(BuildingOpCode.CommissionBuilding)]
    [PacketPermission(PermissionLevel.USER)]
    [PacketEncryption(true)]
    [PacketRateLimit(4, 4, PolicyId = "building-buy")]
    public static async ValueTask HandleCommissionBuildingAsync(IPacketContext<CommissionBuilding> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ushort sequence = context.Packet.SequenceId;
        BuildingOutcome outcome = await s_buildings.CommissionAsync(context.Connection, (BuildingKind)context.Packet.Kind, context.CancellationToken).ConfigureAwait(false);
        await ReplyAsync(context, sequence, outcome).ConfigureAwait(false);
    }

    /// <summary>Buys an animal at the carpenter's counter.</summary>
    /// <param name="context">The request.</param>
    /// <returns>A task that completes when the reply was sent.</returns>
    [PacketOpcode(BuildingOpCode.BuyAnimal)]
    [PacketPermission(PermissionLevel.USER)]
    [PacketEncryption(true)]
    [PacketRateLimit(4, 4, PolicyId = "building-buy")]
    public static async ValueTask HandleBuyAnimalAsync(IPacketContext<BuyAnimal> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ushort sequence = context.Packet.SequenceId;
        Guid buildingId = context.Packet.BuildingId;
        AnimalKind kind = (AnimalKind)context.Packet.Kind;
        string name = context.Packet.Name;
        BuildingOutcome outcome = await s_buildings.BuyAnimalAsync(context.Connection, buildingId, kind, name, context.CancellationToken).ConfigureAwait(false);
        await ReplyAsync(context, sequence, outcome).ConfigureAwait(false);
    }

    /// <summary>Replies with the animals of the building the player stands in.</summary>
    /// <param name="context">The request.</param>
    /// <returns>A task that completes when the reply was sent.</returns>
    [PacketOpcode(BuildingOpCode.GetAnimals)]
    [PacketPermission(PermissionLevel.USER)]
    [PacketRateLimit(4, 5, PolicyId = "animal-read")]
    public static async ValueTask HandleGetAnimalsAsync(IPacketContext<GetAnimals> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ushort sequence = context.Packet.SequenceId;
        AnimalRoomView? room = await s_buildings.GetAnimalsAsync(context.Connection, context.CancellationToken).ConfigureAwait(false);
        await Replies.ReplyAsync<GetAnimals, AnimalsSnapshot>(context, sequence, r =>
        {
            r.Animals.Clear();
            if (room is null)
            {
                r.BuildingId = Guid.Empty;
                return;
            }

            r.BuildingId = room.BuildingId;
            r.Kind = (byte)room.Kind;
            r.Rations = room.Rations;
            r.MayTend = room.MayTend;
            foreach (AnimalView animal in room.Animals)
            {
                r.Animals.Add(animal.ToRow());
            }
        }).ConfigureAwait(false);
    }

    /// <summary>Puts feed into the feeding bench of the building the player stands in.</summary>
    /// <param name="context">The request.</param>
    /// <returns>A task that completes when the reply was sent.</returns>
    [PacketOpcode(BuildingOpCode.FeedAnimals)]
    [PacketPermission(PermissionLevel.USER)]
    [PacketRateLimit(6, 4, PolicyId = "animal-tend")]
    public static async ValueTask HandleFeedAnimalsAsync(IPacketContext<FeedAnimals> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ushort sequence = context.Packet.SequenceId;
        BuildingOutcome outcome = await s_buildings.FeedAsync(context.Connection, context.Packet.Slot, context.Packet.Quantity, context.CancellationToken).ConfigureAwait(false);
        await ReplyAsync(context, sequence, outcome).ConfigureAwait(false);
    }

    /// <summary>Pets an animal.</summary>
    /// <param name="context">The request.</param>
    /// <returns>A task that completes when the reply was sent.</returns>
    [PacketOpcode(BuildingOpCode.PetAnimal)]
    [PacketPermission(PermissionLevel.USER)]
    [PacketRateLimit(6, 4, PolicyId = "animal-tend")]
    public static async ValueTask HandlePetAnimalAsync(IPacketContext<PetAnimal> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ushort sequence = context.Packet.SequenceId;
        AnimalOutcome outcome = await s_buildings.PetAsync(context.Connection, context.Packet.AnimalId, context.CancellationToken).ConfigureAwait(false);
        await ReplyAsync(context, sequence, outcome).ConfigureAwait(false);
    }

    /// <summary>Collects an animal's produce for the day.</summary>
    /// <param name="context">The request.</param>
    /// <returns>A task that completes when the reply was sent.</returns>
    [PacketOpcode(BuildingOpCode.CollectProduce)]
    [PacketPermission(PermissionLevel.USER)]
    [PacketRateLimit(6, 4, PolicyId = "animal-tend")]
    public static async ValueTask HandleCollectProduceAsync(IPacketContext<CollectProduce> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ushort sequence = context.Packet.SequenceId;
        AnimalOutcome outcome = await s_buildings.CollectAsync(context.Connection, context.Packet.AnimalId, context.Packet.Slot, context.CancellationToken).ConfigureAwait(false);
        await ReplyAsync(context, sequence, outcome).ConfigureAwait(false);
    }

    private static ValueTask ReplyAsync<TRequest>(IPacketContext<TRequest> context, ushort sequence, BuildingOutcome outcome)
        where TRequest : class, IPacket
        => Replies.ReplyAsync<TRequest, BuildingResult>(context, sequence, r =>
        {
            r.Status = outcome.Status;
            r.Id = outcome.Id;
            r.ItemId = outcome.ItemId;
            r.Quantity = outcome.Quantity;
        });

    private static ValueTask ReplyAsync<TRequest>(IPacketContext<TRequest> context, ushort sequence, AnimalOutcome outcome)
        where TRequest : class, IPacket
        => Replies.ReplyAsync<TRequest, AnimalResult>(context, sequence, r =>
        {
            r.Status = outcome.Status;
            r.ItemId = outcome.ItemId;
            r.Quantity = outcome.Quantity;
            r.Friendship = outcome.Friendship;
        });
}
