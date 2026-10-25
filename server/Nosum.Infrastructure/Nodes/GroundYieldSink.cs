// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Nosum.Abstractions.Data;
using Nosum.Abstractions.GroundItems;
using Nosum.Abstractions.Maps;
using Nosum.Abstractions.Nodes;
using Nosum.Abstractions.Presence;
using Nosum.Gameplay.GroundItems;
using Nosum.Gameplay.Inventory;

namespace Nosum.Infrastructure.Nodes;

/// <summary>
/// <see cref="INodeYieldSink"/> that pops a node's yield onto the ground beside its tile (docs/adr/ADR-021 D5), in the same transaction as the
/// node's change, then lets the breaker pick up what landed within reach (D6). A full inventory no longer blocks breaking: the items wait on the ground.
/// </summary>
public sealed class GroundYieldSink : INodeYieldSink
{
    private readonly IGroundItems _groundItems;
    private readonly IUnitOfWorkFactory _unitOfWork;
    private readonly IMapPresence _presence;
    private readonly IMapCatalog _maps;

    /// <summary>Initializes the sink.</summary>
    /// <param name="groundItems">Where the yield lands.</param>
    /// <param name="unitOfWork">Creates the unit of work the node's change and the drop share.</param>
    /// <param name="presence">Finds the breaker, to pop the yield toward them and let them take it at once.</param>
    /// <param name="maps">The tile size of the map.</param>
    public GroundYieldSink(IGroundItems groundItems, IUnitOfWorkFactory unitOfWork, IMapPresence presence, IMapCatalog maps)
    {
        ArgumentNullException.ThrowIfNull(groundItems);
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(presence);
        ArgumentNullException.ThrowIfNull(maps);

        _groundItems = groundItems;
        _unitOfWork = unitOfWork;
        _presence = presence;
        _maps = maps;
    }

    /// <inheritdoc/>
    public async Task<NodeYieldStatus> DeliverAsync(NodeYieldRequest request, NodeYieldEnlistment? enlist = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!_maps.TryGet(request.Instance.MapId, out MapDefinition? map))
        {
            return NodeYieldStatus.Refused;
        }

        PlayerPresence? breaker = this.Find(request);
        double centreX = (request.TileX + 0.5) * map.Grid.TileSize;
        double centreY = (request.TileY + 0.5) * map.Grid.TileSize;
        double feetX = centreX;
        double feetY = centreY;
        if (breaker is not null)
        {
            lock (breaker.Gate)
            {
                feetX = breaker.Motion.X;
                feetY = breaker.Motion.Y;
            }
        }

        List<PendingGroundDrop> pending = [];
        IUnitOfWork unit = _unitOfWork.Create();
        await using (unit.ConfigureAwait(false))
        {
            if (enlist is not null && !await enlist(unit, cancellationToken).ConfigureAwait(false))
            {
                return NodeYieldStatus.Refused;
            }

            foreach (ItemSlot item in request.Items)
            {
                GroundItemRules.PopPosition(centreX, centreY, feetX, feetY, out double x, out double y);
                if (_groundItems.Stage(unit, new GroundDrop(request.Instance, (int)x, (int)y, item.ItemId, item.Quantity, request.PlayerId)) is { } staged)
                {
                    pending.Add(staged);
                }
            }

            try
            {
                _ = await unit.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (DbUpdateException ex) when (unit.IsUniqueViolation(ex) || unit.IsConcurrencyConflict(ex))
            {
                return NodeYieldStatus.Conflict;
            }
        }

        foreach (PendingGroundDrop drop in pending)
        {
            await _groundItems.CompleteAsync(drop, cancellationToken).ConfigureAwait(false);
        }

        if (breaker is not null && pending.Count > 0)
        {
            await _groundItems.OnPlayerMovedAsync(breaker).ConfigureAwait(false);
        }

        return NodeYieldStatus.Ok;
    }

    private PlayerPresence? Find(NodeYieldRequest request)
    {
        List<PlayerPresence> players = [];
        _presence.CopyPlayers(request.Instance.Key, players);
        foreach (PlayerPresence player in players)
        {
            if (player.AccountId == request.PlayerId)
            {
                return player;
            }
        }

        return null;
    }
}
