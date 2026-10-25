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
/// that open to walkers get a plank each. Placeholder art (docs/adr/ADR-007): the end plank of the Beach sheet, repeated.
/// </summary>
internal sealed class BridgeDecorator : IMapDecorator
{
    // The "untitled tile sheet" of Beach.tmx starts at gid 65 and has 17 columns; the bridge end plank is gid 141, so local tile 76: column 8, row 4.
    private const int PlankSrcX = 8 * 16;
    private const int PlankSrcY = 4 * 16;
    private const int Tile = 16;

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
    private const string BridgeSheet = "spring_beach.png";

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
