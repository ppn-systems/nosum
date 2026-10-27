// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Nosum.Core;
using Nosum.Gameplay.Maps;
using Nosum.Gameplay.Npcs;
using Nosum.Rendering;

namespace Nosum.Wasm.Game;

/// <summary>Sizes of the merchants' world sprites (docs/adr/ADR-043 D1).</summary>
internal static class NpcSprites
{
    /// <summary>Width of one idle frame, pixels.</summary>
    public const int Width = 16;

    /// <summary>Height of one idle frame, pixels; the frame's bottom edge is the merchant's feet.</summary>
    public const int Height = 32;

    /// <summary>Extra pixels around a merchant in which a click still counts, so a tap with a finger lands.</summary>
    public const int ClickSlack = 4;

    /// <summary>How long one idle frame shows, milliseconds.</summary>
    public const int FrameMs = 700;
}

/// <summary>
/// Draws the merchants of a map (docs/adr/ADR-043 D1) as depth-sorted world sprites with a two-frame idle, so the farmer walks in front of and behind them.
/// The merchants are only a picture of a counter: they have no collision and the server knows nothing of them. Draws structs; allocation-free.
/// </summary>
internal sealed class NpcDecorator : IMapDecorator
{
    private readonly IReadOnlyList<NpcDefinition> _merchants;
    private readonly int[] _textures;
    private readonly int _tileSize;

    /// <summary>Initializes the decorator.</summary>
    /// <param name="merchants">The merchants of the map.</param>
    /// <param name="textures">Texture of each merchant's sheet, in the same order.</param>
    /// <param name="tileSize">Tile size, pixels.</param>
    public NpcDecorator(IReadOnlyList<NpcDefinition> merchants, int[] textures, int tileSize)
    {
        _merchants = merchants;
        _textures = textures;
        _tileSize = tileSize;
    }

    /// <inheritdoc/>
    public void AddToWorld(WorldSpriteBatch world)
    {
        long clock = Environment.TickCount64;
        for (int i = 0; i < _merchants.Count; i++)
        {
            NpcDefinition npc = _merchants[i];

            // Each merchant breathes out of step with the next; with reduced motion the first frame stays.
            int frame = ViewOptions.ReduceMotion ? 0 : (int)(((clock / NpcSprites.FrameMs) + i) % 2);
            int feetY = (npc.TileY + 1) * _tileSize;
            int left = npc.TileX * _tileSize;
            world.Add(_textures[i], frame * NpcSprites.Width, 0, NpcSprites.Width, NpcSprites.Height, left, feetY - NpcSprites.Height, feetY, left + (_tileSize / 2));
        }
    }
}

/// <summary>Loads the sheets of a map's merchants and builds its <see cref="NpcDecorator"/>.</summary>
internal sealed class NpcDecoratorProvider : IMapDecoratorProvider
{
    private const string NpcsUrl = "assets/sprites/npcs/";

    /// <inheritdoc/>
    public async Task<IMapDecorator?> CreateAsync(MapSpec spec, TileMap map, TextureCache textures, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(textures);

        IReadOnlyList<NpcDefinition> merchants = NpcCatalog.OnMap(spec.Id);
        if (merchants.Count == 0)
        {
            return null;
        }

        int[] sheets = new int[merchants.Count];
        for (int i = 0; i < merchants.Count; i++)
        {
            sheets[i] = await textures.LoadAsync(NpcsUrl + merchants[i].SpriteKey + ".png").ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
        }

        return new NpcDecorator(merchants, sheets, map.TileWidth);
    }
}
