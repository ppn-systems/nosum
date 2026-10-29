// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using Nosum.Core;

namespace Nosum.Rendering;

/// <summary>
/// Draws placed buildings and props from data (<see cref="PropPlacement"/>), for any map: the base
/// goes into the depth-sorted <see cref="WorldSpriteBatch"/> anchored at the footprint's bottom
/// edge, the roof part (<see cref="PropDefinition.FrontHeight"/>) is drawn over everything.
/// </summary>
public sealed class PropLayer
{
    private readonly PropPlacement[] _placements;
    private readonly int[] _textures;
    private readonly int _tileSize;

    /// <summary>Initializes the layer.</summary>
    /// <param name="placements">The props.</param>
    /// <param name="textureOf">Maps <see cref="PropDefinition.Sheet"/> to a backend texture id; negative skips the prop. Called once per prop here.</param>
    /// <param name="tileSize">Map tile size, pixels.</param>
    public PropLayer(IReadOnlyList<PropPlacement> placements, Func<string, int> textureOf, int tileSize)
    {
        ArgumentNullException.ThrowIfNull(placements);
        ArgumentNullException.ThrowIfNull(textureOf);

        _placements = [.. placements];
        _tileSize = tileSize;
        _textures = new int[_placements.Length];
        for (int i = 0; i < _placements.Length; i++)
        {
            _textures[i] = textureOf(_placements[i].Definition.Sheet);
        }
    }

    /// <summary>Gets the props.</summary>
    public IReadOnlyList<PropPlacement> Placements => _placements;

    /// <summary>Adds each prop's base to the depth-sorted world batch anchored at the footprint bottom. The roof is not sorted: <see cref="DrawFront"/> draws it over everything.</summary>
    /// <param name="batch">The world batch.</param>
    public void AddToBatch(WorldSpriteBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);

        for (int i = 0; i < _placements.Length; i++)
        {
            if (_textures[i] < 0)
            {
                continue;
            }

            PropDefinition d = _placements[i].Definition;
            Origin(_placements[i], out int left, out int top, out int anchorY);
            int sortX = left + (d.Width / 2);
            batch.Add(_textures[i], d.SrcX, d.SrcY + d.FrontHeight, d.Width, d.Height - d.FrontHeight, left, top + d.FrontHeight, anchorY, sortX, id: i);
        }
    }

    /// <summary>Records the roof of each prop after the world, so eaves and chimneys cover players walking behind them.</summary>
    /// <param name="commands">Where draws are recorded.</param>
    /// <param name="camera">The camera.</param>
    public void DrawFront(RenderCommandBuffer commands, in Camera2D camera)
    {
        ArgumentNullException.ThrowIfNull(commands);

        for (int i = 0; i < _placements.Length; i++)
        {
            PropDefinition d = _placements[i].Definition;
            if (_textures[i] < 0 || d.FrontHeight == 0)
            {
                continue;
            }

            Origin(_placements[i], out int left, out int top, out _);
            if (left >= camera.Left + camera.Width || top >= camera.Top + camera.Height || left + d.Width <= camera.Left || top + d.FrontHeight <= camera.Top)
            {
                continue;
            }

            commands.Draw(_textures[i], d.SrcX, d.SrcY, d.Width, d.FrontHeight, left - camera.Left, top - camera.Top);
        }
    }

    private void Origin(in PropPlacement p, out int left, out int top, out int anchorY)
    {
        PropDefinition d = p.Definition;
        left = (p.TileX * _tileSize) + (((d.FootprintWidth * _tileSize) - d.Width) / 2);
        anchorY = (p.TileY + d.FootprintHeight) * _tileSize;
        top = anchorY - d.Height;
    }
}
