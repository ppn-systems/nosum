// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;
using Nosum.Core;
using Nosum.Gameplay.Maps;
using Nosum.Rendering;

namespace Nosum.Wasm.Game;

/// <summary>
/// Draws the planks of a map's gated bridge (<see cref="MapSpec.Gates"/>, docs/adr/ADR-037 D4) once its unlock is known: the file draws the bridge broken, and the tiles
/// that open to walkers get a plank each. The plank is <see cref="OutdoorWorld.BridgePlankTile"/> of the own <c>town</c> sheet (docs/adr/ADR-042 P5c).
/// </summary>
internal sealed class BridgeDecorator : IMapDecorator
{
    // The <c>town</c> sheet is 16 columns of 16 px tiles (art/own/source/tilesets/town.tileset).
    private const int Columns = 16;
    private const int Tile = 16;
    private const int PlankSrcX = (OutdoorWorld.BridgePlankTile % Columns) * Tile;
    private const int PlankSrcY = (OutdoorWorld.BridgePlankTile / Columns) * Tile;

    private readonly MapGate _gate;
    private readonly int _texture;
    private readonly Func<string, bool> _unlocked;

    /// <summary>Initializes the decorator.</summary>
    /// <param name="gate">The gate whose tiles get planks.</param>
    /// <param name="texture">The texture of the tile sheet the plank is cut from.</param>
    /// <param name="unlocked">Whether an unlock key is set.</param>
    public BridgeDecorator(MapGate gate, int texture, Func<string, bool> unlocked)
    {
        ArgumentNullException.ThrowIfNull(gate);
        ArgumentNullException.ThrowIfNull(unlocked);

        _gate = gate;
        _texture = texture;
        _unlocked = unlocked;
    }

    /// <inheritdoc/>
    public void AddToWorld(WorldSpriteBatch world)
    {
        if (!_unlocked(_gate.Key))
        {
            return;
        }

        // Anchored on the top edge of the tile, so a walker standing on the plank is drawn over it.
        for (int i = 0; i < _gate.Tiles.Count; i++)
        {
            (int x, int y) = _gate.Tiles[i];
            world.Add(_texture, PlankSrcX, PlankSrcY, Tile, Tile, x * Tile, y * Tile, y * Tile, (x * Tile) + (Tile / 2), 0);
        }
    }
}

/// <summary>Loads the sheet a map's bridge planks come from and builds its <see cref="BridgeDecorator"/>.</summary>
internal sealed class BridgeDecoratorProvider : IMapDecoratorProvider
{
    private const string MapsUrl = "assets/Maps/";
    private const string BridgeSheet = "town.png";

    private readonly Func<string, bool> _unlocked;

    /// <summary>Initializes the provider.</summary>
    /// <param name="unlocked">Whether the server said an unlock key is set.</param>
    public BridgeDecoratorProvider(Func<string, bool> unlocked)
    {
        ArgumentNullException.ThrowIfNull(unlocked);

        _unlocked = unlocked;
    }

    /// <inheritdoc/>
    public async Task<IMapDecorator?> CreateAsync(MapSpec spec, TileMap map, TextureCache textures, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(textures);

        if (spec.Gates.Count == 0)
        {
            return null;
        }

        int texture = await textures.LoadAsync(MapsUrl + BridgeSheet).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return new BridgeDecorator(spec.Gates[0], texture, _unlocked);
    }
}
