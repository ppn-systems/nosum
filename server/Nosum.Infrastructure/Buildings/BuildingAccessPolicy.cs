// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;
using Nosum.Abstractions.Buildings;
using Nosum.Abstractions.Coop;
using Nosum.Abstractions.Presence;
using Nosum.Gameplay.Buildings;
using Nosum.Gameplay.Maps;

namespace Nosum.Infrastructure.Buildings;

/// <summary>
/// <see cref="IInstanceAccessPolicy"/> that adds the inside of farm buildings to another policy (docs/adr/ADR-028 D2): an interior is entered by whoever may enter the land its building
/// stands on, once the building is finished. On a player's farm that is the owner, and the visitors of docs/adr/ADR-019 (they may look, not tend); on co-op land it is the members of that co-op.
/// Every other instance is the inner policy's decision.
/// </summary>
public sealed class BuildingAccessPolicy : IInstanceAccessPolicy
{
    private readonly IInstanceAccessPolicy _inner;
    private readonly IBuildingDirectory _buildings;
    private readonly ICoopMembership _coops;
    private readonly TimeProvider _time;

    /// <summary>Initializes the policy.</summary>
    /// <param name="inner">Decides for farms, the farmhouse and shared maps (visiting).</param>
    /// <param name="buildings">Finds the building an interior instance belongs to.</param>
    /// <param name="coops">Co-op membership, asked for buildings on co-op land.</param>
    /// <param name="time">The server clock: whether the building is finished.</param>
    public BuildingAccessPolicy(IInstanceAccessPolicy inner, IBuildingDirectory buildings, ICoopMembership coops, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(buildings);
        ArgumentNullException.ThrowIfNull(coops);
        ArgumentNullException.ThrowIfNull(time);

        _inner = inner;
        _buildings = buildings;
        _coops = coops;
        _time = time;
    }

    /// <inheritdoc/>
    public async ValueTask<bool> CanEnterAsync(MapInstance instance, Guid playerId, CancellationToken cancellationToken = default)
    {
        if (!GameMaps.TryGet(instance.MapId, out MapSpec? spec) || !spec.IsInterior)
        {
            return await _inner.CanEnterAsync(instance, playerId, cancellationToken).ConfigureAwait(false);
        }

        BuildingInfo? building = await _buildings.FindAsync(instance.OwnerId, cancellationToken).ConfigureAwait(false);
        if (building is null || building.Kind != spec.InteriorOf || building.StateAt(_time.GetUtcNow().ToUnixTimeMilliseconds()) != BuildingState.Finished)
        {
            return false;
        }

        return await this.MayEnterLandAsync(building.Land, playerId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Tells whether a player may be in a building's land and so in its interior: the owner and permitted visitors on a farm, the members on co-op land.</summary>
    /// <param name="land">The land the building stands on.</param>
    /// <param name="playerId">The player's account id.</param>
    /// <param name="cancellationToken">Cancels the check.</param>
    /// <returns><see langword="true"/> when the player may be there.</returns>
    public async ValueTask<bool> MayEnterLandAsync(MapInstance land, Guid playerId, CancellationToken cancellationToken = default)
    {
        if (land.IsCoopLand)
        {
            CoopMembershipInfo? member = await _coops.FindAsync(playerId, cancellationToken).ConfigureAwait(false);
            return member is not null && member.CoopId == land.OwnerId;
        }

        return await _inner.CanEnterAsync(land, playerId, cancellationToken).ConfigureAwait(false);
    }
}
