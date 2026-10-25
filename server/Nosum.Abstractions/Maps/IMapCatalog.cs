// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Diagnostics.CodeAnalysis;

namespace Nosum.Abstractions.Maps;

/// <summary>The maps this server can host. Ids are the keys clients name in a join request.</summary>
public interface IMapCatalog
{
    /// <summary>Looks up a map.</summary>
    /// <param name="mapId">Map key sent by the client; never trusted as a path.</param>
    /// <param name="map">The map when found.</param>
    /// <returns><see langword="true"/> when the server hosts that map.</returns>
    bool TryGet(string mapId, [NotNullWhen(true)] out MapDefinition? map);
}
