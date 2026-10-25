// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using Nosum.Gameplay.Movement;

namespace Nosum.Gameplay.Nodes;

/// <summary>
/// Every resource node of one map instance, held in memory in 16 × 16-tile chunk buckets (docs/adr/ADR-022 D5) and by tile. Not thread-safe:
/// the owner serializes access with one lock per instance. <see cref="BuildGrid"/> is how nodes join the grid players walk on, so server
/// validation and client prediction use the same blocked tiles.
/// </summary>
public sealed class NodeField
{
    private readonly Dictionary<int, ResourceNode> _byId = [];
    private readonly Dictionary<(int X, int Y), int> _byTile = [];
    private readonly Dictionary<(int Cx, int Cy), HashSet<int>> _chunks = [];

    /// <summary>Gets the number of nodes.</summary>
    public int Count => _byId.Count;

    /// <summary>Gets every node, in no particular order.</summary>
    public IEnumerable<ResourceNode> All => _byId.Values;

    /// <summary>Returns the chunk coordinate of a tile coordinate.</summary>
    /// <param name="tile">Tile x or y.</param>
    /// <returns>The chunk x or y.</returns>
    public static int ChunkOf(int tile) => tile >> 4;

    /// <summary>Adds a node.</summary>
    /// <param name="node">The node; its id and tile must be free.</param>
    /// <exception cref="InvalidOperationException">The id or the tile is taken.</exception>
    public void Add(in ResourceNode node)
    {
        if (_byId.ContainsKey(node.Id) || _byTile.ContainsKey((node.X, node.Y)))
        {
            throw new InvalidOperationException("The node id or its tile is already in use.");
        }

        _byId.Add(node.Id, node);
        _byTile.Add((node.X, node.Y), node.Id);
        (int, int) chunk = (ChunkOf(node.X), ChunkOf(node.Y));
        if (!_chunks.TryGetValue(chunk, out HashSet<int>? ids))
        {
            ids = [];
            _chunks.Add(chunk, ids);
        }

        _ = ids.Add(node.Id);
    }

    /// <summary>Replaces the state of a node that stays on its tile.</summary>
    /// <param name="node">The node's new state; same id and tile as before.</param>
    /// <returns><see langword="true"/> when the node existed.</returns>
    public bool Replace(in ResourceNode node)
    {
        if (!_byId.TryGetValue(node.Id, out ResourceNode old) || old.X != node.X || old.Y != node.Y)
        {
            return false;
        }

        _byId[node.Id] = node;
        return true;
    }

    /// <summary>Removes a node.</summary>
    /// <param name="id">Node id.</param>
    /// <returns><see langword="true"/> when the node existed.</returns>
    public bool Remove(int id)
    {
        if (!_byId.Remove(id, out ResourceNode node))
        {
            return false;
        }

        _ = _byTile.Remove((node.X, node.Y));
        (int, int) chunk = (ChunkOf(node.X), ChunkOf(node.Y));
        if (_chunks.TryGetValue(chunk, out HashSet<int>? ids) && ids.Remove(id) && ids.Count == 0)
        {
            _ = _chunks.Remove(chunk);
        }

        return true;
    }

    /// <summary>Finds a node by id.</summary>
    /// <param name="id">Node id.</param>
    /// <param name="node">The node when found.</param>
    /// <returns><see langword="true"/> when found.</returns>
    public bool TryGet(int id, out ResourceNode node) => _byId.TryGetValue(id, out node);

    /// <summary>Finds the node standing on a tile.</summary>
    /// <param name="x">Tile x.</param>
    /// <param name="y">Tile y.</param>
    /// <param name="node">The node when found.</param>
    /// <returns><see langword="true"/> when a node stands there.</returns>
    public bool TryGetAt(int x, int y, out ResourceNode node)
    {
        if (_byTile.TryGetValue((x, y), out int id))
        {
            node = _byId[id];
            return true;
        }

        node = default;
        return false;
    }

    /// <summary>Returns whether any node stands on a tile.</summary>
    /// <param name="x">Tile x.</param>
    /// <param name="y">Tile y.</param>
    /// <returns><see langword="true"/> when the tile is taken.</returns>
    public bool HasNodeAt(int x, int y) => _byTile.ContainsKey((x, y));

    /// <summary>Copies the nodes of one chunk: O(the chunk), not O(the map).</summary>
    /// <param name="chunkX">Chunk x.</param>
    /// <param name="chunkY">Chunk y.</param>
    /// <param name="into">List the nodes are appended to.</param>
    public void CopyChunk(int chunkX, int chunkY, List<ResourceNode> into)
    {
        ArgumentNullException.ThrowIfNull(into);

        if (!_chunks.TryGetValue((chunkX, chunkY), out HashSet<int>? ids))
        {
            return;
        }

        foreach (int id in ids)
        {
            into.Add(_byId[id]);
        }
    }

    /// <summary>Returns the grid players walk on: the map's grid with every blocking node's tile blocked.</summary>
    /// <param name="mapGrid">The map's own grid; never changed.</param>
    /// <returns><paramref name="mapGrid"/> itself when no node blocks, otherwise a copy.</returns>
    public WalkGrid BuildGrid(WalkGrid mapGrid)
    {
        ArgumentNullException.ThrowIfNull(mapGrid);

        WalkGrid? grid = null;
        foreach (ResourceNode node in _byId.Values)
        {
            if (node.Blocks)
            {
                grid ??= mapGrid.Clone();
                grid.Block(node.X, node.Y, 1, 1);
            }
        }

        return grid ?? mapGrid;
    }
}
