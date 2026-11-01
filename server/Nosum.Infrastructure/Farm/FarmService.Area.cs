// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;
using Nalix.Abstractions.Networking;
using Nosum.Abstractions.Farm;
using Nosum.Abstractions.Inventory;
using Nosum.Abstractions.Presence;
using Nosum.Contracts.Farm;
using Nosum.Core;
using Nosum.Gameplay.Inventory;
using Nosum.Gameplay.Items;

namespace Nosum.Infrastructure.Farm;

/// <summary>The wide use of an upgraded hoe or watering can (docs/adr/ADR-026 D1).</summary>
public sealed partial class FarmService
{
    /// <inheritdoc/>
    public async Task<FarmUseOutcome> UseAreaAsync(IConnection connection, int hotbarSlot, int tileX, int tileY, int length, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        if (length == 1)
        {
            return await this.UseAsync(connection, hotbarSlot, tileX, tileY, cancellationToken).ConfigureAwait(false);
        }

        if ((uint)hotbarSlot >= (uint)InventoryLimits.HotbarSlots)
        {
            return Refused(FarmStatus.InvalidSlot);
        }

        if (!_presence.TryGet(connection, out PlayerPresence? player))
        {
            return Refused(FarmStatus.NotOnFarm);
        }

        // The claim is checked against the item in the slot, as the server sees it: a client cannot widen a basic hoe.
        InventorySlotView held = await _inventory.PeekSlotAsync(player.AccountId, hotbarSlot, cancellationToken).ConfigureAwait(false);
        _ = ItemCatalog.TryGet(held.ItemId, out ItemDefinition? item);
        if (ToolRules.ValidateArea(item, length) != AreaVerdict.Ok)
        {
            return Refused(FarmStatus.InvalidArea);
        }

        Facing facing;
        lock (player.Gate)
        {
            facing = (Facing)player.Motion.Facing;
        }

        // The first tile starts the swing and is the only one that has to be within reach; the others follow it in a straight line, so they are at most
        // length - 1 tiles beyond it. A tile that cannot be worked (a stone, the map's edge) is skipped, the rest are still worked.
        FarmUseOutcome result = await this.UseCoreAsync(connection, hotbarSlot, tileX, tileY, tail: false, cancellationToken).ConfigureAwait(false);
        if (result.Status is FarmStatus.Cooldown or FarmStatus.InvalidSlot or FarmStatus.NotOnFarm or FarmStatus.OutOfReach or FarmStatus.NotYourFarm or FarmStatus.ItemMissing or FarmStatus.NoEnergy or FarmStatus.CanEmpty)
        {
            return result;
        }

        // Every tile that worked is charged its own energy (docs/adr/ADR-026 D1), so the count is what the client shows as the energy spent (docs/adr/ADR-080).
        int worked = result.Status == FarmStatus.Ok && result.Action != FarmAction.Descended ? 1 : 0;
        FarmUseOutcome best = result;
        for (int i = 1; i < length; i++)
        {
            (int x, int y) = ToolRules.LineTile(tileX, tileY, facing, length, i);
            FarmUseOutcome next = await this.UseCoreAsync(connection, hotbarSlot, x, y, tail: true, cancellationToken).ConfigureAwait(false);
            if (next.Action == FarmAction.Descended)
            {
                // A ladder at the end of a line is not used by a hoe passing by.
                continue;
            }

            if (next.Status == FarmStatus.Ok)
            {
                worked++;
            }

            if (best.Status != FarmStatus.Ok && next.Status == FarmStatus.Ok)
            {
                best = next;
            }

            if (next.Status is FarmStatus.CanEmpty or FarmStatus.NoEnergy)
            {
                break;
            }
        }

        return best with { Worked = worked };
    }
}
