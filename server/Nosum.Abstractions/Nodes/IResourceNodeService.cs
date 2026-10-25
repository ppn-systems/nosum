// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Nalix.Abstractions.Networking;
using Nosum.Abstractions.Farm;
using Nosum.Abstractions.Inventory;
using Nosum.Abstractions.Maps;
using Nosum.Abstractions.Placeables;
using Nosum.Abstractions.Presence;
using Nosum.Gameplay.Nodes;

namespace Nosum.Abstractions.Nodes;

/// <summary>
/// Resource nodes: trees, stumps, twigs, stones, ore nodes, weeds and grass of every map instance (docs/adr/ADR-021). It is also the node
/// layer of <see cref="IInstanceWorld"/>: nodes that block walking are part of the grid movement is validated against. The server decides
/// every outcome from the tool, the tile and the node; tools reach it through <see cref="IFarmService.UseAsync"/>.
/// </summary>
public interface IResourceNodeService : IInstanceWorld
{
    /// <summary>Reads every node of the place the connection's player stands in, with the state of their watering can.</summary>
    /// <param name="connection">The player's connection.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The nodes; empty (and an empty map id) when the player is on no map or the map has none.</returns>
    Task<NodeSnapshotView> GetAsync(IConnection connection, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lets the node layer handle one tool use: a swing at a node, a refill at water, planting a tree seed. The farm layer has already checked the
    /// slot, the map and the reach, and has started the swing cooldown.
    /// </summary>
    /// <param name="request">The use.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The outcome, or <see langword="null"/> when the use is not the node layer's business (the farm layer decides).</returns>
    Task<FarmUseOutcome?> TryUseAsync(NodeUseRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// A game day ended: trees grow, and public maps regrow debris (docs/adr/ADR-021 D4). The calendar (docs/adr/ADR-018) or, until it exists,
    /// <see cref="IGameDayClock"/> calls this.
    /// </summary>
    /// <param name="day">The number of the day that ended; days only increase.</param>
    /// <param name="seasonChanged">Whether the day that starts begins a new season; private places then regrow weeds.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when every loaded instance was brought up to date.</returns>
    Task AdvanceDayAsync(long day, bool seasonChanged, CancellationToken cancellationToken = default);
}

/// <summary>One tool use handed to the node layer.</summary>
/// <param name="Player">The player, as the server records them.</param>
/// <param name="Map">The player's map.</param>
/// <param name="HotbarSlot">The hotbar slot used.</param>
/// <param name="Held">What the slot holds.</param>
/// <param name="TileX">Target tile x.</param>
/// <param name="TileY">Target tile y.</param>
/// <param name="TileWorked">Whether the farm layer has tilled soil on the target tile.</param>
public readonly record struct NodeUseRequest(PlayerPresence Player, MapDefinition Map, int HotbarSlot, InventorySlotView Held, int TileX, int TileY, bool TileWorked);

/// <summary>The nodes of one place and the player's watering can.</summary>
/// <param name="MapId">The map key; empty when the player is on no map.</param>
/// <param name="WaterLeft">Water left in the player's can.</param>
/// <param name="WaterCapacity">Largest amount the can holds.</param>
/// <param name="Nodes">Every node.</param>
public sealed record NodeSnapshotView(string MapId, int WaterLeft, int WaterCapacity, IReadOnlyList<ResourceNode> Nodes);
