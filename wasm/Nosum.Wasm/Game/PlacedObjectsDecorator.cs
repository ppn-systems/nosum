// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;
using Nosum.Core;
using Nosum.Gameplay.Maps;
using Nosum.Gameplay.Placeables;
using Nosum.Rendering;
using Nosum.Wasm.Socket.Placeable;

namespace Nosum.Wasm.Game;

/// <summary>
/// Draws what the player placed (furniture) as depth-sorted world sprites, anchored at the bottom of each footprint so a farmer
/// walking behind a piece is drawn first, and the ghost of the piece being placed or carried. Reads the mirror under its lock and
/// draws structs; allocation-free (docs/adr/ADR-015).
/// </summary>
internal sealed class PlacedObjectsDecorator : IMapDecorator
{
    // Multiply tint of a ghost that cannot be placed there (RRGGBBAA).
    private static readonly int s_invalidTint = unchecked((int)0xFF7070FF);

    private readonly string _mapId;
    private readonly PlaceableChannel _placed;
    private readonly PlacementActions _actions;
    private readonly int _sheetTexture;
    private readonly int _tileSize;

    /// <summary>Initializes the decorator.</summary>
    /// <param name="mapId">The map it decorates.</param>
    /// <param name="placed">The mirrored pieces.</param>
    /// <param name="actions">Where the ghost is.</param>
    /// <param name="sheetTexture">Texture id of the furniture sheet.</param>
    /// <param name="tileSize">Map tile size, pixels.</param>
    public PlacedObjectsDecorator(string mapId, PlaceableChannel placed, PlacementActions actions, int sheetTexture, int tileSize)
    {
        _mapId = mapId;
        _placed = placed;
        _actions = actions;
        _sheetTexture = sheetTexture;
        _tileSize = tileSize;
    }

    /// <inheritdoc/>
    public void AddToWorld(WorldSpriteBatch world)
    {
        using (_placed.Read())
        {
            if (!string.Equals(_placed.MapId, _mapId, StringComparison.Ordinal))
            {
                return;
            }

            for (int i = 0; i < _placed.Count; i++)
            {
                PlacedObjectInfo piece = _placed.At(i);
                this.Add(world, piece.ItemId, piece.TileX, piece.TileY, piece.Rotation, placedId: piece.Id, ghost: false, valid: true);
            }
        }

        if (_actions.GhostVisible)
        {
            this.Add(world, _actions.GhostItemId, _actions.GhostTileX, _actions.GhostTileY, _actions.GhostRotation, placedId: 0, ghost: true, _actions.GhostValid);
        }
    }

    private void Add(WorldSpriteBatch world, int itemId, int tileX, int tileY, int rotation, int placedId, bool ghost, bool valid)
    {
        if (!PlaceableCatalog.TryGet(itemId, out PlaceableDefinition? definition))
        {
            return;
        }

        PlaceableView view = definition.View(Math.Min(rotation, definition.Rotations - 1));
        int baseY = (tileY + view.FootprintHeight) * _tileSize;
        int left = tileX * _tileSize;
        int tint = ghost && !valid ? s_invalidTint : RenderCommandBuffer.NoTint;

        // A ghost is drawn in front of everything at its spot; a carried piece's old place keeps drawing until it is set down.
        world.Add(_sheetTexture, view.SourceX, view.SourceY, view.Width, view.Height, left, baseY - view.Height, ghost ? baseY + _tileSize : baseY, left, placedId, view.FlipX ? RenderCommandBuffer.FlipX : 0, tint);
    }
}

/// <summary>Builds the <see cref="PlacedObjectsDecorator"/> of every map where pieces can be placed.</summary>
internal sealed class PlacedObjectsDecoratorProvider : IMapDecoratorProvider
{
    private const string TileSheetsUrl = "assets/TileSheets/";

    private readonly PlaceableChannel _placed;
    private readonly PlacementActions _actions;

    /// <summary>Initializes the provider.</summary>
    /// <param name="placed">The mirrored pieces.</param>
    /// <param name="actions">Where the ghost is.</param>
    public PlacedObjectsDecoratorProvider(PlaceableChannel placed, PlacementActions actions)
    {
        _placed = placed;
        _actions = actions;
    }

    /// <inheritdoc/>
    public async Task<IMapDecorator?> CreateAsync(MapSpec spec, TileMap map, TextureCache textures, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(textures);

        if (!spec.Placeable)
        {
            return null;
        }

        int sheet = await textures.LoadAsync(TileSheetsUrl + PlaceableCatalog.FurnitureSheet + ".png").ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return new PlacedObjectsDecorator(spec.Id, _placed, _actions, sheet, map.TileWidth);
    }
}
