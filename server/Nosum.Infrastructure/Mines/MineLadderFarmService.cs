// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;
using Nalix.Abstractions.Networking;
using Nosum.Abstractions.Farm;
using Nosum.Abstractions.Mines;
using Nosum.Contracts.Farm;

namespace Nosum.Infrastructure.Mines;

/// <summary>
/// An <see cref="IFarmService"/> that lets a tool use end in a ladder (docs/adr/ADR-026 D3): the farm and node layers answer
/// <see cref="FarmAction.Descended"/> when the tile in front holds a ladder, and this wrapper then takes the player down through <see cref="IMineService"/>. It
/// keeps the node layer free of any dependency on moving players between maps.
/// </summary>
public sealed class MineLadderFarmService : IFarmService
{
    private readonly IFarmService _inner;
    private readonly IMineService _mine;

    /// <summary>Initializes the wrapper.</summary>
    /// <param name="inner">The farm service that decides a tool use.</param>
    /// <param name="mine">Moves the player down.</param>
    public MineLadderFarmService(IFarmService inner, IMineService mine)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(mine);

        _inner = inner;
        _mine = mine;
    }

    /// <inheritdoc/>
    public async Task<FarmUseOutcome> UseAsync(IConnection connection, int hotbarSlot, int tileX, int tileY, CancellationToken cancellationToken = default)
        => await this.DescendIfLadderAsync(connection, await _inner.UseAsync(connection, hotbarSlot, tileX, tileY, cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);

    /// <inheritdoc/>
    public async Task<FarmUseOutcome> UseAreaAsync(IConnection connection, int hotbarSlot, int tileX, int tileY, int length, CancellationToken cancellationToken = default)
        => await this.DescendIfLadderAsync(connection, await _inner.UseAreaAsync(connection, hotbarSlot, tileX, tileY, length, cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);

    /// <inheritdoc/>
    public Task<FarmSnapshotView> GetFarmAsync(IConnection connection, CancellationToken cancellationToken = default) => _inner.GetFarmAsync(connection, cancellationToken);

    /// <inheritdoc/>
    public void Forget(Nosum.Gameplay.Maps.MapInstance instance) => _inner.Forget(instance);

    private async Task<FarmUseOutcome> DescendIfLadderAsync(IConnection connection, FarmUseOutcome outcome, CancellationToken cancellationToken)
    {
        if (outcome.Action != FarmAction.Descended)
        {
            return outcome;
        }

        return await _mine.DescendAsync(connection, cancellationToken).ConfigureAwait(false)
            ? outcome
            : new FarmUseOutcome(FarmStatus.Blocked, FarmAction.None);
    }
}
