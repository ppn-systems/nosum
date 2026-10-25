// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Nalix.Abstractions.Networking;
using Nosum.Contracts.Farm;
using Nosum.Gameplay.Farm;
using Nosum.Gameplay.Maps;

namespace Nosum.Abstractions.Farm;

/// <summary>
/// Farming on the maps that allow it: use an item on a tile (till, plant, water, harvest) and read the
/// worked tiles of a map. The server decides everything: the player's position, the held item, the
/// tile and the clock never come from the client.
/// </summary>
public interface IFarmService
{
    /// <summary>Uses the item in a hotbar slot on a tile of the map the connection's player stands on.</summary>
    /// <param name="connection">The player's connection.</param>
    /// <param name="hotbarSlot">Hotbar slot, zero to eight.</param>
    /// <param name="tileX">Target tile x.</param>
    /// <param name="tileY">Target tile y.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>What was done, or why not.</returns>
    Task<FarmUseOutcome> UseAsync(IConnection connection, int hotbarSlot, int tileX, int tileY, CancellationToken cancellationToken = default);

    /// <summary>Reads every worked tile of the map the connection's player stands on.</summary>
    /// <param name="connection">The player's connection.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The map id, the server clock and the tiles; an empty map id when the player is on no map.</returns>
    Task<FarmSnapshotView> GetFarmAsync(IConnection connection, CancellationToken cancellationToken = default);

    /// <summary>
    /// Drops the in-memory tiles of an instance. Called after the instance's rows were deleted (a dissolved co-op's land, docs/adr/ADR-020),
    /// so a stale copy is never served.
    /// </summary>
    /// <param name="instance">The place whose tiles to forget.</param>
    void Forget(MapInstance instance);
}

/// <summary>Outcome of <see cref="IFarmService.UseAsync"/>.</summary>
/// <param name="Status">Ok, or why nothing happened.</param>
/// <param name="Action">What was done; <see cref="FarmAction.None"/> on a refusal.</param>
public readonly record struct FarmUseOutcome(FarmStatus Status, FarmAction Action);

/// <summary>One worked tile with its position.</summary>
/// <param name="X">Tile x.</param>
/// <param name="Y">Tile y.</param>
/// <param name="State">The tile's stored state.</param>
public readonly record struct FarmTileView(int X, int Y, FarmTile State);

/// <summary>The worked tiles of one map.</summary>
/// <param name="MapId">The map key.</param>
/// <param name="ServerNowMs">The server clock when read, Unix milliseconds.</param>
/// <param name="Tiles">The tilled tiles.</param>
public sealed record FarmSnapshotView(string MapId, long ServerNowMs, IReadOnlyList<FarmTileView> Tiles);
