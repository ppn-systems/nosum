// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;
using Nosum.Abstractions.Maps;
using Nosum.Abstractions.Nodes;
using Nosum.Abstractions.Placeables;
using Nosum.Gameplay.Maps;
using Nosum.Gameplay.Movement;

namespace Nosum.Infrastructure.Placeables;

/// <summary>
/// The one <see cref="IInstanceWorld"/> movement validation asks: it joins the layers of an instance (placed furniture, resource nodes) so the
/// grid a player walks on is the same everywhere. A map has one layer today: furniture where players may place it, nodes where the map file marks them.
/// </summary>
public sealed class InstanceWorld : IInstanceWorld
{
    private readonly IInstanceWorld _placeables;
    private readonly IResourceNodeService _nodes;

    /// <summary>Initializes the world.</summary>
    /// <param name="placeables">The placed-objects layer.</param>
    /// <param name="nodes">The resource-node layer.</param>
    public InstanceWorld(IInstanceWorld placeables, IResourceNodeService nodes)
    {
        ArgumentNullException.ThrowIfNull(placeables);
        ArgumentNullException.ThrowIfNull(nodes);

        _placeables = placeables;
        _nodes = nodes;
    }

    /// <inheritdoc/>
    public async ValueTask PrepareAsync(MapInstance instance, CancellationToken cancellationToken = default)
    {
        await _placeables.PrepareAsync(instance, cancellationToken).ConfigureAwait(false);
        await _nodes.PrepareAsync(instance, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public WalkGrid GridFor(MapInstance instance, MapDefinition map)
    {
        ArgumentNullException.ThrowIfNull(map);
        return map.Placeable ? _placeables.GridFor(instance, map) : _nodes.GridFor(instance, map);
    }
}
