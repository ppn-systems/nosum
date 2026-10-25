// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Nosum.Abstractions.Economy;
using Nosum.Abstractions.Nodes;
using Nosum.Gameplay.Inventory;

namespace Nosum.Infrastructure.Nodes;

/// <summary>
/// <see cref="INodeYieldSink"/> that puts a node's yield into the breaker's inventory through the transfer service (docs/adr/ADR-013), in the
/// same transaction as the node's change. A full inventory refuses the whole delivery, so the node stays. Ground items replace this with drops at the tile.
/// </summary>
public sealed class InventoryYieldSink : INodeYieldSink
{
    private readonly ITransferService _transfers;

    /// <summary>Initializes the sink.</summary>
    /// <param name="transfers">The transfer service.</param>
    public InventoryYieldSink(ITransferService transfers)
    {
        ArgumentNullException.ThrowIfNull(transfers);
        _transfers = transfers;
    }

    /// <inheritdoc/>
    public async Task<NodeYieldStatus> DeliverAsync(NodeYieldRequest request, NodeYieldEnlistment? enlist = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        TransferParty player = TransferParty.Player(request.PlayerId);
        List<TransferLeg> legs = [];
        foreach (ItemSlot item in request.Items)
        {
            legs.Add(new TransferLeg(player, item.ItemId, item.Quantity));
        }

        // A node that gives nothing still commits its change; an empty leg names the player the transaction is for.
        if (legs.Count == 0)
        {
            legs.Add(new TransferLeg(player, 0, 0));
        }

        TransferStatus status = await _transfers.ExecuteAsync(
            legs,
            request.Reason,
            enlist is null ? null : async (unit, token) => await enlist(unit, token).ConfigureAwait(false) ? TransferStatus.Ok : TransferStatus.Refused,
            cancellationToken).ConfigureAwait(false);

        return status switch
        {
            TransferStatus.Ok => NodeYieldStatus.Ok,
            TransferStatus.NoSpace => NodeYieldStatus.NoSpace,
            TransferStatus.Conflict => NodeYieldStatus.Conflict,
            _ => NodeYieldStatus.Refused,
        };
    }
}
