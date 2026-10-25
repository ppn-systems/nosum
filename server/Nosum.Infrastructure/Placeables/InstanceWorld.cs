// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Nosum.Abstractions.Buildings;
using Nosum.Abstractions.Maps;
using Nosum.Abstractions.Nodes;
using Nosum.Abstractions.Placeables;
using Nosum.Gameplay.Maps;
using Nosum.Gameplay.Movement;

namespace Nosum.Infrastructure.Placeables;

/// <summary>
/// The one <see cref="IInstanceWorld"/> movement validation asks: it joins the layers of an instance (placed furniture, resource nodes) so the
/// grid a player walks on is the same everywhere. A map has up to two layers: nodes where the map file marks them and placed objects where players may place them (the farm has both;
/// docs/adr/ADR-024). Where both exist the joined grid is built once and kept until either layer replaces its grid, so a move validation allocates nothing.
/// </summary>
public sealed class InstanceWorld : IInstanceWorld
{
    private readonly IInstanceWorld _placeables;
    private readonly IResourceNodeService _nodes;
    private readonly IBuildingDirectory? _buildings;
    private readonly ConcurrentDictionary<string, Joined> _joined = new(StringComparer.Ordinal);

    /// <summary>Initializes the world.</summary>
    /// <param name="placeables">The placed-objects layer.</param>
    /// <param name="nodes">The resource-node layer.</param>
    /// <param name="buildings">The farm buildings of the land, loaded when a land is entered so their doors are known on the movement path (docs/adr/ADR-028); <see langword="null"/> for none.</param>
    public InstanceWorld(IInstanceWorld placeables, IResourceNodeService nodes, IBuildingDirectory? buildings = null)
    {
        ArgumentNullException.ThrowIfNull(placeables);
        ArgumentNullException.ThrowIfNull(nodes);

        _placeables = placeables;
        _nodes = nodes;
        _buildings = buildings;
    }

    /// <inheritdoc/>
    public async ValueTask PrepareAsync(MapInstance instance, CancellationToken cancellationToken = default)
    {
        await _placeables.PrepareAsync(instance, cancellationToken).ConfigureAwait(false);
        await _nodes.PrepareAsync(instance, cancellationToken).ConfigureAwait(false);
        if (_buildings is not null)
        {
            await _buildings.PrepareAsync(instance, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    public WalkGrid GridFor(MapInstance instance, MapDefinition map)
    {
        ArgumentNullException.ThrowIfNull(map);

        if (!map.Placeable)
        {
            return _nodes.GridFor(instance, map);
        }

        WalkGrid placed = _placeables.GridFor(instance, map);
        if (!map.HasNodes)
        {
            return placed;
        }

        // A layer that has nothing loaded yet answers with the map's own grid; the other one is then the whole answer.
        WalkGrid nodes = _nodes.GridFor(instance, map);
        if (ReferenceEquals(nodes, map.Grid))
        {
            return placed;
        }

        if (ReferenceEquals(placed, map.Grid))
        {
            return nodes;
        }

        string key = instance.Key;
        if (_joined.TryGetValue(key, out Joined? known) && ReferenceEquals(known.Nodes, nodes) && ReferenceEquals(known.Placed, placed))
        {
            return known.Grid;
        }

        WalkGrid grid = nodes.Clone();
        grid.BlockWhereBlocked(placed);
        _joined[key] = new Joined(nodes, placed, grid);
        return grid;
    }

    private sealed record Joined(WalkGrid Nodes, WalkGrid Placed, WalkGrid Grid);
}
