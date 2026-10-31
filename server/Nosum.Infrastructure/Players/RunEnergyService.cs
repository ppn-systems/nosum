// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;
using Nosum.Abstractions.Inventory;
using Nosum.Abstractions.Player;
using Nosum.Abstractions.Progress;
using Nosum.Gameplay.Skills;

namespace Nosum.Infrastructure.Players;

/// <summary>
/// <see cref="IRunEnergy"/> on the player's energy row (docs/adr/ADR-023, docs/adr/ADR-085 D2): one unit of work spends the energy and the new
/// energy is pushed to the player when the unit is saved, as every other energy charge is.
/// </summary>
public sealed class RunEnergyService : IRunEnergy
{
    private readonly IInventoryService _inventory;
    private readonly IPlayerProgress _progress;

    /// <summary>Initializes the service.</summary>
    /// <param name="inventory">Runs the unit of work.</param>
    /// <param name="progress">Spends the energy.</param>
    public RunEnergyService(IInventoryService inventory, IPlayerProgress progress)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(progress);

        _inventory = inventory;
        _progress = progress;
    }

    /// <inheritdoc/>
    public async Task<bool> TrySpendAsync(Guid playerId, int energy, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(energy, 1);

        IProgressCharge? charge = null;
        try
        {
            InventoryChange change = await _inventory.ApplyAsync(
                playerId,
                [],
                "player.run",
                async (unit, token) =>
                {
                    charge = await _progress.ChargeEnergyAsync(unit, playerId, energy, SkillKind.None, 0, token).ConfigureAwait(false);
                    return charge.Allowed;
                },
                cancellationToken: cancellationToken).ConfigureAwait(false);
            if (!change.IsOk || charge is null)
            {
                return false;
            }

            await charge.PublishAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        finally
        {
            charge?.Dispose();
        }
    }
}
