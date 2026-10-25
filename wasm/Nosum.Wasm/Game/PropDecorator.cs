// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Nosum.Core;
using Nosum.Gameplay.Maps;
using Nosum.Rendering;

namespace Nosum.Wasm.Game;

/// <summary>Draws a map's buildings and props (<see cref="MapSpec.Props"/>) with a <see cref="PropLayer"/>.</summary>
internal sealed class PropDecorator : IMapDecorator
{
    private readonly PropLayer _props;

    /// <summary>Initializes the decorator.</summary>
    /// <param name="props">The prop layer.</param>
    public PropDecorator(PropLayer props)
    {
        ArgumentNullException.ThrowIfNull(props);
        _props = props;
    }

    /// <inheritdoc/>
    public void AddToWorld(WorldSpriteBatch world) => _props.AddToBatch(world);

    /// <inheritdoc/>
    public void DrawInFront(RenderCommandBuffer commands, in Camera2D camera) => _props.DrawFront(commands, camera);
}

/// <summary>Loads the sheets of a map's props and builds its <see cref="PropDecorator"/>.</summary>
internal sealed class PropDecoratorProvider : IMapDecoratorProvider
{
    private const string BuildingsUrl = "assets/Buildings/";

    /// <inheritdoc/>
    public async Task<IMapDecorator?> CreateAsync(MapSpec spec, TileMap map, TextureCache textures, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(textures);

        if (spec.Props.Count == 0)
        {
            return null;
        }

        // A prop sheet name is the file name of its image in Buildings/.
        Dictionary<string, int> sheets = new(StringComparer.Ordinal);
        foreach (PropPlacement placement in spec.Props)
        {
            string sheet = placement.Definition.Sheet;
            if (!sheets.ContainsKey(sheet))
            {
                sheets[sheet] = await textures.LoadAsync(BuildingsUrl + sheet + ".png").ConfigureAwait(false);
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        return new PropDecorator(new PropLayer(spec.Props, sheet => sheets.TryGetValue(sheet, out int id) ? id : -1, map.TileWidth));
    }
}
