// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Nosum.Core;
using Nosum.Gameplay.Maps;
using Nosum.Gameplay.Placeables;
using Nosum.Rendering;
using Nosum.Wasm.Socket.Building;
using Nosum.Wasm.Socket.Furnace;
using Nosum.Wasm.Socket.Placeable;

namespace Nosum.Wasm.Game;

/// <summary>
/// Draws what the player placed (furniture, chests, fences, paths, sprinklers, furnaces: each from its own sheet) as depth-sorted world sprites, anchored at the bottom of each footprint so a farmer
/// walking behind a piece is drawn first, and the ghost of the piece being placed or carried. Reads the mirror under its lock and
/// draws structs; allocation-free (docs/adr/ADR-015).
/// </summary>
internal sealed class PlacedObjectsDecorator : IMapDecorator
{
    // Multiply tint of a ghost that cannot be placed there (RRGGBBAA): a strong red, so it reads as "no" next to a placed piece.
    private static readonly int s_invalidTint = Rgba.From(0xFF, 0x5A, 0x5A, 0xFF);

    // Multiply tint of a ghost that can be placed there: a clear green, so the valid spot does not look like the piece already placed (ADR-015 D10).
    private static readonly int s_validTint = Rgba.From(0xB4, 0xFF, 0xB4, 0xFF);

    // Multiply tint of a building that is still being built: dull and bluish, so it reads as scaffolding (docs/adr/ADR-028 D1).
    private static readonly int s_scaffoldTint = Rgba.From(0x8C, 0x94, 0xB4, 0xFF);

    private readonly string _mapId;
    private readonly PlaceableChannel _placed;
    private readonly PlacementActions _actions;
    private readonly IReadOnlyDictionary<int, int> _textureOfItem;
    private readonly FurnaceChannel? _furnaces;
    private readonly BuildingChannel? _buildings;
    private readonly int _tileSize;

    /// <summary>Initializes the decorator.</summary>
    /// <param name="mapId">The map it decorates.</param>
    /// <param name="placed">The mirrored pieces.</param>
    /// <param name="actions">Where the ghost is.</param>
    /// <param name="textureOfItem">Texture id of the sheet of every placeable item.</param>
    /// <param name="furnaces">Says which furnaces are smelting (drawn lit); <see langword="null"/> draws them all unlit.</param>
    /// <param name="buildings">Says which buildings are still being built (drawn dull); <see langword="null"/> draws them all finished.</param>
    /// <param name="tileSize">Map tile size, pixels.</param>
    public PlacedObjectsDecorator(string mapId, PlaceableChannel placed, PlacementActions actions, IReadOnlyDictionary<int, int> textureOfItem, FurnaceChannel? furnaces, BuildingChannel? buildings, int tileSize)
    {
        _mapId = mapId;
        _placed = placed;
        _actions = actions;
        _textureOfItem = textureOfItem;
        _furnaces = furnaces;
        _buildings = buildings;
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

            if (_actions.GhostVisible)
            {
                this.Add(world, _actions.GhostItemId, _actions.GhostTileX, _actions.GhostTileY, _actions.GhostRotation, placedId: 0, ghost: true, _actions.GhostValid);
            }
        }
    }

    private void Add(WorldSpriteBatch world, int itemId, int tileX, int tileY, int rotation, int placedId, bool ghost, bool valid)
    {
        if (!PlaceableCatalog.TryGet(itemId, out PlaceableDefinition? definition) || !_textureOfItem.TryGetValue(itemId, out int texture))
        {
            return;
        }

        PlaceableView view = definition.View(Math.Min(rotation, definition.Rotations - 1));
        int baseY = (tileY + view.FootprintHeight) * _tileSize;
        int left = tileX * _tileSize;
        int tint = ghost && !valid ? s_invalidTint
            : ghost ? s_validTint
            : definition.Kind == PlaceableKind.Building && !ghost && _buildings is not null && _buildings.IsUnderConstruction(placedId) ? s_scaffoldTint
            : RenderCommandBuffer.NoTint;

        // A fence joins its four cardinal fence neighbours (16 auto-tile cells in an 8 x 2 strip); a working furnace is the next cell of its sheet (lit).
        int fenceMask = definition.Kind == PlaceableKind.Fence ? this.FenceMask(tileX, tileY) : 0;
        int sourceX = definition.Kind == PlaceableKind.Fence
            ? view.SourceX + ((fenceMask & 7) * view.Width)
            : view.SourceX + (definition.Kind == PlaceableKind.Furnace && !ghost && _furnaces is not null && _furnaces.IsWorking(placedId) ? view.Width : 0);
        int sourceY = definition.Kind == PlaceableKind.Fence
            ? view.SourceY + ((fenceMask >> 3) * view.Height)
            : view.SourceY;
        int sortY = !definition.BlocksWalking ? tileY * _tileSize : ghost ? baseY + _tileSize : baseY;

        // A ghost is drawn in front of everything at its spot; a carried piece's old place keeps drawing until it is set down.
        world.Add(texture, sourceX, sourceY, view.Width, view.Height, left, baseY - view.Height, sortY, left, placedId, view.FlipX ? RenderCommandBuffer.FlipX : 0, tint);
    }

    private int FenceMask(int tileX, int tileY)
    {
        int mask = 0;
        for (int i = 0; i < _placed.Count; i++)
        {
            PlacedObjectInfo other = _placed.At(i);
            if (!PlaceableCatalog.TryGet(other.ItemId, out PlaceableDefinition? def) || def.Kind != PlaceableKind.Fence)
            {
                continue;
            }

            mask |= NeighborBit(tileX, tileY, other.TileX, other.TileY);
        }

        if (_actions.GhostVisible && _actions.GhostValid && PlaceableCatalog.TryGet(_actions.GhostItemId, out PlaceableDefinition? ghostDef) && ghostDef.Kind == PlaceableKind.Fence)
        {
            mask |= NeighborBit(tileX, tileY, _actions.GhostTileX, _actions.GhostTileY);
        }

        return mask;
    }

    private static int NeighborBit(int x, int y, int ox, int oy)
        => ox == x && oy == y - 1 ? 1
            : ox == x + 1 && oy == y ? 2
            : ox == x && oy == y + 1 ? 4
            : ox == x - 1 && oy == y ? 8 : 0;
}

/// <summary>Builds the <see cref="PlacedObjectsDecorator"/> of every map where pieces can be placed.</summary>
internal sealed class PlacedObjectsDecoratorProvider : IMapDecoratorProvider
{
    private readonly PlaceableChannel _placed;
    private readonly PlacementActions _actions;
    private readonly FurnaceChannel? _furnaces;
    private readonly BuildingChannel? _buildings;

    /// <summary>Initializes the provider.</summary>
    /// <param name="placed">The mirrored pieces.</param>
    /// <param name="actions">Where the ghost is.</param>
    /// <param name="furnaces">Which furnaces are smelting, or <see langword="null"/>.</param>
    /// <param name="buildings">Which buildings are still being built, or <see langword="null"/>.</param>
    public PlacedObjectsDecoratorProvider(PlaceableChannel placed, PlacementActions actions, FurnaceChannel? furnaces = null, BuildingChannel? buildings = null)
    {
        _placed = placed;
        _actions = actions;
        _furnaces = furnaces;
        _buildings = buildings;
    }

    /// <summary>Returns where a sheet of <see cref="PlaceableDefinition.Sheet"/> is served from (early art, docs/adr/ADR-007).</summary>
    /// <param name="sheet">The sheet name.</param>
    /// <returns>The URL relative to the app root.</returns>
    internal static string UrlOf(string sheet)
        => "assets/sprites/" + sheet + ".png";

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

        // Each distinct sheet is loaded once (the cache keeps one texture id per file).
        Dictionary<string, int> bySheet = new(StringComparer.Ordinal);
        Dictionary<int, int> byItem = [];
        foreach (PlaceableDefinition definition in PlaceableCatalog.All)
        {
            if (!bySheet.TryGetValue(definition.Sheet, out int texture))
            {
                texture = await textures.LoadAsync(UrlOf(definition.Sheet)).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                bySheet[definition.Sheet] = texture;
            }

            byItem[definition.ItemId] = texture;
        }

        return new PlacedObjectsDecorator(spec.Id, _placed, _actions, byItem, _furnaces, _buildings, map.TileWidth);
    }
}
