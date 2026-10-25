// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Nosum.Gameplay.Maps;

/// <summary>
/// One running copy of a map: the map plus, for a private map, the player it belongs to (docs/adr/ADR-015).
/// Everything that is "per place" on the server (who is present, the farm's plots, placed furniture) is keyed by
/// <see cref="Key"/>. A shared map has one instance for everybody; a private map has one per owner. An allow-list
/// (visiting) can be added later without changing the key: the owner stays part of it and entry is checked by
/// <c>IInstanceAccessPolicy</c>.
/// </summary>
/// <param name="MapId">Stable id of the map in <see cref="GameMaps"/>.</param>
/// <param name="OwnerId">The owning player's account id, or <see cref="Guid.Empty"/> for a shared map.</param>
public readonly record struct MapInstance(string MapId, Guid OwnerId)
{
    /// <summary>Gets whether this instance belongs to one player.</summary>
    public bool IsPrivate => this.OwnerId != Guid.Empty;

    /// <summary>
    /// Gets the string that names the instance for presence groups and in-memory caches: the map id for a shared
    /// map (so code written against map ids keeps working), <c>mapid@ownerid</c> for a private one.
    /// </summary>
    public string Key => this.IsPrivate ? this.MapId + "@" + this.OwnerId.ToString("N") : this.MapId;

    /// <summary>Returns the one instance of a shared map.</summary>
    /// <param name="mapId">Map id.</param>
    /// <returns>The instance.</returns>
    public static MapInstance Shared(string mapId)
    {
        ArgumentException.ThrowIfNullOrEmpty(mapId);
        return new MapInstance(mapId, Guid.Empty);
    }

    /// <summary>Returns the instance of a map a player enters: their own copy of a private map, the shared one otherwise.</summary>
    /// <param name="map">The map.</param>
    /// <param name="playerId">The entering player's account id.</param>
    /// <returns>The instance.</returns>
    public static MapInstance Of(MapSpec map, Guid playerId)
    {
        ArgumentNullException.ThrowIfNull(map);
        return map.IsPrivate ? new MapInstance(map.Id, playerId) : Shared(map.Id);
    }

    /// <summary>Returns the instance of a map a player enters, from the map id only.</summary>
    /// <param name="mapId">Map id; a map the catalogue does not know is treated as shared.</param>
    /// <param name="playerId">The entering player's account id.</param>
    /// <returns>The instance.</returns>
    public static MapInstance Of(string mapId, Guid playerId)
        => GameMaps.TryGet(mapId, out MapSpec? map) ? Of(map, playerId) : Shared(mapId);
}
