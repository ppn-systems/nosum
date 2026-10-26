// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;
using Nosum.Core;
using Nosum.Gameplay.Maps;
using Nosum.Rendering;
using Nosum.Wasm.Socket.Node;

namespace Nosum.Wasm.Game;

/// <summary>
/// Draws the resource nodes of the map (trees, stones, twigs, weeds) as depth-sorted world sprites, so a farmer walks behind a tree and in front
/// of a stone (docs/adr/ADR-005, ADR-021). Reads the mirror under its lock and adds only what is near the camera; allocation-free.
/// </summary>
internal sealed class NodeDecorator : IMapDecorator
{
    // Margin around the view for a tree's 3 × 6-tile frame, pixels.
    private const int CullMarginX = 32;
    private const int CullMarginTop = 96;

    private readonly string _mapId;
    private readonly NodeChannel _nodes;
    private readonly NodeTextures _textures;
    private readonly int _tileSize;
    private int _left;
    private int _top;
    private int _right = int.MaxValue;
    private int _bottom = int.MaxValue;

    /// <summary>Initializes the decorator.</summary>
    /// <param name="mapId">The map it decorates.</param>
    /// <param name="nodes">The mirrored nodes.</param>
    /// <param name="textures">The sheets.</param>
    /// <param name="tileSize">Map tile size, pixels.</param>
    public NodeDecorator(string mapId, NodeChannel nodes, NodeTextures textures, int tileSize)
    {
        _mapId = mapId;
        _nodes = nodes;
        _textures = textures;
        _tileSize = tileSize;
    }

    /// <inheritdoc/>
    public void SetView(in Camera2D camera)
    {
        _left = camera.Left - CullMarginX;
        _top = camera.Top;
        _right = camera.Left + camera.Width + CullMarginX;
        _bottom = camera.Top + camera.Height + CullMarginTop;
    }

    /// <inheritdoc/>
    public void AddToWorld(WorldSpriteBatch world)
    {
        long now = Environment.TickCount64;
        using (_nodes.Read())
        {
            if (!string.Equals(_nodes.MapId, _mapId, StringComparison.Ordinal))
            {
                return;
            }

            for (int i = 0; i < _nodes.Count; i++)
            {
                NodeInfo node = _nodes.At(i);
                int x = node.X * _tileSize;
                int baseY = (node.Y + 1) * _tileSize;

                // A tree's frame rises 96 px above its base, so a node is in view when its base is up to that far below the top edge.
                if (x < _left || x > _right || baseY < _top || baseY - CullMarginTop > _bottom)
                {
                    continue;
                }

                NodeSprites.Add(world, node, _textures, _tileSize, NodeSprites.ShakeOffset(node.HitAtMs, now));
            }
        }
    }
}

/// <summary>Loads the sheets the resource nodes are drawn from and builds the <see cref="NodeDecorator"/> of every map that has nodes.</summary>
internal sealed class NodeDecoratorProvider : IMapDecoratorProvider
{
    private const string TreesUrl = "assets/sprites/trees/";
    private const string ObjectsUrl = "assets/sprites/items.png";
    private const string MineUrl = "assets/Maps/Mines/mine.png";

    private readonly NodeChannel _nodes;
    private NodeTextures? _textures;

    /// <summary>Initializes the provider.</summary>
    /// <param name="nodes">The mirrored nodes.</param>
    public NodeDecoratorProvider(NodeChannel nodes) => _nodes = nodes;

    /// <inheritdoc/>
    public async Task<IMapDecorator?> CreateAsync(MapSpec spec, TileMap map, TextureCache textures, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(textures);

        // Maps without a Paths layer (a room) have no nodes, except the floors of the mine, whose nodes the server builds (docs/adr/ADR-026 D3).
        if (spec.Placeable || (map.FindLayer(Nosum.Gameplay.Nodes.NodeSites.LayerName) is null && !spec.IsMineFloor))
        {
            return null;
        }

        if (_textures is null)
        {
            int oak = await textures.LoadAsync(TreesUrl + "oak.png").ConfigureAwait(false);
            int maple = await textures.LoadAsync(TreesUrl + "maple.png").ConfigureAwait(false);
            int pine = await textures.LoadAsync(TreesUrl + "pine.png").ConfigureAwait(false);
            int objects = await textures.LoadAsync(ObjectsUrl).ConfigureAwait(false);
            int mine = await textures.LoadAsync(MineUrl).ConfigureAwait(false);
            _textures = new NodeTextures(oak, maple, pine, objects, mine);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return new NodeDecorator(spec.Id, _nodes, _textures.Value, map.TileWidth);
    }
}
