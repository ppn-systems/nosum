// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Threading;
using System.Threading.Tasks;
using Nosum.Core;
using Nosum.Gameplay.Maps;

namespace Nosum.Wasm.Game;

/// <summary>
/// Creates the <see cref="IMapDecorator"/> a map needs, loading its textures first. <see cref="MapLoader"/> asks every
/// provider for every map it loads; a provider returns <see langword="null"/> for a map it has nothing to add to.
/// Register providers where <see cref="GameLoop"/> builds the loader.
/// </summary>
internal interface IMapDecoratorProvider
{
    /// <summary>Loads what the map needs and returns its decorator.</summary>
    /// <param name="spec">The map.</param>
    /// <param name="map">The parsed map.</param>
    /// <param name="textures">Texture loading shared with every other map.</param>
    /// <param name="cancellationToken">Cancels loading.</param>
    /// <returns>The decorator, or <see langword="null"/> when there is nothing to draw for this map.</returns>
    Task<IMapDecorator?> CreateAsync(MapSpec spec, TileMap map, TextureCache textures, CancellationToken cancellationToken);
}
