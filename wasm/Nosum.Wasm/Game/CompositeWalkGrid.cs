// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Nosum.Gameplay.Movement;
using Nosum.Gameplay.Nodes;
using Nosum.Gameplay.Placeables;
using Nosum.Wasm.Socket.Node;
using Nosum.Wasm.Socket.Placeable;

namespace Nosum.Wasm.Game;

/// <summary>
/// Composes the base map <see cref="WalkGrid"/> with blocking resource nodes and placed furniture footprints,
/// rebuilding only when the underlying map or channel version changes (docs/adr/ADR-021 D3).
/// </summary>
internal sealed class CompositeWalkGrid
{
    private readonly NodeChannel? _nodes;
    private readonly PlaceableChannel? _placed;
    private readonly NodeField _nodeField = new();
    private readonly PlacedSet _placedSet = new();

    private WalkGrid? _nodeGrid;
    private MapContent? _nodeGridContent;
    private int _nodeGridVersion = -1;

    private WalkGrid? _placedGrid;
    private MapContent? _placedGridContent;
    private int _placedGridVersion = -1;

    private WalkGrid? _joinedGrid;
    private WalkGrid? _joinedNodes;
    private WalkGrid? _joinedPlaced;

    /// <summary>Initializes the composite walk grid cache.</summary>
    /// <param name="nodes">Resource node channel, or <see langword="null"/> when nodes are not wired.</param>
    /// <param name="placed">Placed furniture channel, or <see langword="null"/> when furniture is not wired.</param>
    public CompositeWalkGrid(NodeChannel? nodes, PlaceableChannel? placed)
    {
        _nodes = nodes;
        _placed = placed;
    }

    /// <summary>Gets the cached set of placed objects on the current map.</summary>
    public PlacedSet PlacedSet => _placedSet;

    /// <summary>Returns the composite walk grid for <paramref name="content"/>, rebuilding only on version or map changes.</summary>
    /// <param name="content">Active map content.</param>
    /// <returns>The composite <see cref="WalkGrid"/>.</returns>
    public WalkGrid GridOf(MapContent content)
    {
        WalkGrid nodes = this.NodeGridOf(content);
        if (_placed is null || !content.Spec.Placeable)
        {
            return nodes;
        }

        int version = _placed.Version;
        if (version != _placedGridVersion || !ReferenceEquals(content, _placedGridContent) || _placedGrid is null)
        {
            _placedSet.Clear();
            using (_placed.Read())
            {
                if (string.Equals(_placed.MapId, content.Spec.Id, StringComparison.Ordinal))
                {
                    for (int i = 0; i < _placed.Count; i++)
                    {
                        PlacedObjectInfo piece = _placed.At(i);
                        _placedSet.Add(new PlacedItem(piece.Id, piece.ItemId, piece.TileX, piece.TileY, piece.Rotation, piece.PlacedAtMs));
                    }
                }
            }

            _placedGrid = _placedSet.BuildGrid(content.Grid);
            _placedGridContent = content;
            _placedGridVersion = version;
        }

        if (ReferenceEquals(nodes, content.Grid))
        {
            return _placedGrid;
        }

        if (_joinedGrid is null || !ReferenceEquals(_joinedNodes, nodes) || !ReferenceEquals(_joinedPlaced, _placedGrid))
        {
            WalkGrid joined = nodes.Clone();
            joined.BlockWhereBlocked(_placedGrid);
            _joinedGrid = joined;
            _joinedNodes = nodes;
            _joinedPlaced = _placedGrid;
        }

        return _joinedGrid;
    }

    private WalkGrid NodeGridOf(MapContent content)
    {
        if (_nodes is null)
        {
            return content.Grid;
        }

        int version = _nodes.Version;
        if (version != _nodeGridVersion || !ReferenceEquals(content, _nodeGridContent) || _nodeGrid is null)
        {
            _nodeField.Clear();
            using (_nodes.Read())
            {
                if (string.Equals(_nodes.MapId, content.Spec.Id, StringComparison.Ordinal))
                {
                    for (int i = 0; i < _nodes.Count; i++)
                    {
                        NodeInfo node = _nodes.At(i);
                        _nodeField.Add(new ResourceNode(node.Id, node.X, node.Y, (NodeKind)node.Kind, node.Variant, node.Stage, node.Hits));
                    }
                }
            }

            _nodeGrid = _nodeField.BuildGrid(content.Grid);
            _nodeGridContent = content;
            _nodeGridVersion = version;
        }

        return _nodeGrid;
    }
}
