// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Nosum.Abstractions.Buildings;
using Nosum.Abstractions.Data;
using Nosum.Domain.Buildings;
using Nosum.Domain.Farm;
using Nosum.Gameplay.Buildings;
using Nosum.Gameplay.Maps;

namespace Nosum.Infrastructure.Buildings;

/// <summary>
/// <see cref="IBuildingDirectory"/>: the buildings of every land in use, loaded once when a land is first entered and replaced whenever a building is added, kept as immutable
/// snapshots so the movement path (is this tile a door?) reads them without a lock or an allocation (docs/adr/ADR-028 D2). A building never moves and never goes away, so a
/// stale entry can only be missing a building that was just placed, which <see cref="ReloadAsync"/> adds.
/// </summary>
public sealed class BuildingDirectory : IBuildingDirectory
{
    private readonly IUnitOfWorkFactory _unitOfWork;
    private readonly ConcurrentDictionary<string, Task<BuildingInfo[]>> _lands = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<Guid, BuildingInfo> _byId = new();

    /// <summary>Initializes the directory.</summary>
    /// <param name="unitOfWork">Creates units of work.</param>
    public BuildingDirectory(IUnitOfWorkFactory unitOfWork)
    {
        ArgumentNullException.ThrowIfNull(unitOfWork);
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc/>
    public async ValueTask PrepareAsync(MapInstance land, CancellationToken cancellationToken = default)
    {
        if (!IsLand(land))
        {
            return;
        }

        Task<BuildingInfo[]> load = _lands.GetOrAdd(land.Key, _ => this.LoadAsync(land));
        try
        {
            _ = await load.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception) when (load.IsFaulted)
        {
            // A failed load is forgotten so the next entry tries again.
            _ = _lands.TryRemove(new KeyValuePair<string, Task<BuildingInfo[]>>(land.Key, load));
            throw;
        }
    }

    /// <inheritdoc/>
    public IReadOnlyList<BuildingInfo> BuildingsOf(MapInstance land)
        => _lands.TryGetValue(land.Key, out Task<BuildingInfo[]>? load) && load.IsCompletedSuccessfully ? load.Result : [];

    /// <inheritdoc/>
    public bool TryFindDoor(MapInstance land, int tileX, int tileY, [NotNullWhen(true)] out BuildingInfo? building)
    {
        if (_lands.TryGetValue(land.Key, out Task<BuildingInfo[]>? load) && load.IsCompletedSuccessfully)
        {
            foreach (BuildingInfo candidate in load.Result)
            {
                if (BuildingCatalog.TryGet(candidate.Kind, out BuildingDefinition? definition)
                    && candidate.TileX + definition.DoorX == tileX
                    && candidate.TileY + definition.DoorY == tileY)
                {
                    building = candidate;
                    return true;
                }
            }
        }

        building = null;
        return false;
    }

    /// <inheritdoc/>
    public async ValueTask<BuildingInfo?> FindAsync(Guid buildingId, CancellationToken cancellationToken = default)
    {
        if (_byId.TryGetValue(buildingId, out BuildingInfo? known))
        {
            return known;
        }

        IUnitOfWork unit = _unitOfWork.Create();
        await using (unit.ConfigureAwait(false))
        {
            FarmBuilding? row = await unit.Repository<FarmBuilding>().FindAsync(buildingId, cancellationToken).ConfigureAwait(false);
            if (row is null)
            {
                return null;
            }

            BuildingInfo? info = await this.InfoOfAsync(unit, row, cancellationToken).ConfigureAwait(false);
            if (info is not null)
            {
                _byId[buildingId] = info;
            }

            return info;
        }
    }

    /// <inheritdoc/>
    public IReadOnlyList<MapInstance> InteriorsOfPlayer(Guid ownerId)
    {
        IReadOnlyList<BuildingInfo> buildings = this.BuildingsOf(new MapInstance(GameMaps.Farm, ownerId));
        if (buildings.Count == 0)
        {
            return [];
        }

        List<MapInstance> interiors = new(buildings.Count);
        foreach (BuildingInfo building in buildings)
        {
            interiors.Add(building.Interior);
        }

        return interiors;
    }

    /// <inheritdoc/>
    public async ValueTask<IReadOnlyList<BuildingInfo>> ReloadAsync(MapInstance land, CancellationToken cancellationToken = default)
    {
        if (!IsLand(land))
        {
            return [];
        }

        BuildingInfo[] loaded = await this.LoadAsync(land).WaitAsync(cancellationToken).ConfigureAwait(false);
        _lands[land.Key] = Task.FromResult(loaded);
        return loaded;
    }

    // A land is a private place where buildings may stand: the farm and co-op land, not an interior or the farmhouse.
    private static bool IsLand(MapInstance land) => land.IsPrivate && GameMaps.TryGet(land.MapId, out MapSpec? spec) && spec.Farmable && !spec.IsInterior;

    private async Task<BuildingInfo[]> LoadAsync(MapInstance land)
    {
        IUnitOfWork unit = _unitOfWork.Create();
        await using (unit.ConfigureAwait(false))
        {
            string mapId = land.MapId;
            Guid ownerId = land.OwnerId;
            List<FarmBuilding> rows = await unit.Repository<FarmBuilding>().ListAsync(b => b.MapId == mapId && b.OwnerId == ownerId, CancellationToken.None).ConfigureAwait(false);
            List<BuildingInfo> infos = new(rows.Count);
            foreach (FarmBuilding row in rows)
            {
                if (await this.InfoOfAsync(unit, row, CancellationToken.None).ConfigureAwait(false) is { } info)
                {
                    infos.Add(info);
                    _byId[info.Id] = info;
                }
            }

            return [.. infos];
        }
    }

    private async Task<BuildingInfo?> InfoOfAsync(IUnitOfWork unit, FarmBuilding row, CancellationToken cancellationToken)
    {
        PlacedObject? placed = await unit.Repository<PlacedObject>().FindAsync(row.ObjectId, cancellationToken).ConfigureAwait(false);
        return placed is null ? null : new BuildingInfo(row.Id, row.ObjectId, (BuildingKind)row.Kind, new MapInstance(row.MapId, row.OwnerId), placed.TileX, placed.TileY, row.CompletesAtMs);
    }
}
