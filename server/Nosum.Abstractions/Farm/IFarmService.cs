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

    /// <summary>
    /// Uses a hoe or a watering can on a line of tiles with one swing (docs/adr/ADR-026 D1): the tile in front of the player and the ones after it, the way the
    /// server knows the player faces. One swing, one cooldown; every tile is worked like a single use (and costs what a single use costs).
    /// </summary>
    /// <param name="connection">The player's connection.</param>
    /// <param name="hotbarSlot">Hotbar slot, zero to eight.</param>
    /// <param name="tileX">First tile x.</param>
    /// <param name="tileY">First tile y.</param>
    /// <param name="length">How many tiles in a line: 1, 3 or 5; the held tool's tier must allow it.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>Ok when at least one tile was worked, otherwise why the first tile was not.</returns>
    Task<FarmUseOutcome> UseAreaAsync(IConnection connection, int hotbarSlot, int tileX, int tileY, int length, CancellationToken cancellationToken = default)
        => length == 1
            ? this.UseAsync(connection, hotbarSlot, tileX, tileY, cancellationToken)
            : Task.FromResult(new FarmUseOutcome(FarmStatus.InvalidArea, FarmAction.None));

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

/// <summary>The worked tiles of an instance, for layers that must keep off them (docs/adr/ADR-024 D3: nothing is placed on tilled soil).</summary>
public interface IFarmTiles
{
    /// <summary>Returns which tiles of a rectangle are tilled.</summary>
    /// <param name="instance">The place.</param>
    /// <param name="tileX">Left tile of the rectangle.</param>
    /// <param name="tileY">Top tile of the rectangle.</param>
    /// <param name="width">Width in tiles.</param>
    /// <param name="height">Height in tiles.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The tilled tiles inside the rectangle; empty on a map that cannot be farmed.</returns>
    Task<IReadOnlySet<(int X, int Y)>> WorkedTilesAsync(MapInstance instance, int tileX, int tileY, int width, int height, CancellationToken cancellationToken = default);
}

/// <summary>Outcome of <see cref="IFarmService.UseAsync"/>.</summary>
/// <param name="Status">Ok, or why nothing happened.</param>
/// <param name="Action">What was done; <see cref="FarmAction.None"/> on a refusal.</param>
/// <param name="Subject">What the action was done to when that matters to others: for <see cref="FarmAction.Broke"/> the <c>NodeKind</c> of the node that broke (docs/adr/ADR-036 D3); otherwise zero.</param>
public readonly record struct FarmUseOutcome(FarmStatus Status, FarmAction Action, int Subject = 0);

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
