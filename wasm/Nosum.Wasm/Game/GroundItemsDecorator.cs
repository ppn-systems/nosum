// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;
using Nosum.Core;
using Nosum.Gameplay.Maps;
using Nosum.Rendering;
using Nosum.Wasm.Socket.GroundItem;

namespace Nosum.Wasm.Game;

/// <summary>
/// Draws the items lying on the ground as depth-sorted world sprites (a farmer walking behind one is drawn first) with a small bob, a short
/// pop when one appears, and a quick flight to the player who picks one up (docs/adr/ADR-021 D5-D6). Reads the mirror under its lock and
/// draws structs; allocation-free. Items without a picture on the objects sheet are not drawn.
/// </summary>
internal sealed class GroundItemsDecorator : IMapDecorator
{
    private const int Icon = 16;
    private const long PopMs = 300;
    private const long FlightMs = 180;

    private readonly string _mapId;
    private readonly GroundItemChannel _items;
    private readonly int _sheetTexture;

    /// <summary>Initializes the decorator.</summary>
    /// <param name="mapId">The map it decorates.</param>
    /// <param name="items">The mirrored items.</param>
    /// <param name="sheetTexture">Texture id of the objects sheet.</param>
    public GroundItemsDecorator(string mapId, GroundItemChannel items, int sheetTexture)
    {
        _mapId = mapId;
        _items = items;
        _sheetTexture = sheetTexture;
    }

    /// <inheritdoc/>
    public void AddToWorld(WorldSpriteBatch world)
    {
        long now = Environment.TickCount64;
        using (_items.Read())
        {
            if (!string.Equals(_items.MapId, _mapId, StringComparison.Ordinal))
            {
                return;
            }

            for (int i = 0; i < _items.Count; i++)
            {
                GroundItemInfo item = _items.At(i);
                if (!ItemIcons.TryGetGroundCell(item.ItemId, out int srcX, out int srcY))
                {
                    continue;
                }

                // A gentle bob (0-3 px, a different phase per item) and, on arrival, a hop over the first 300 ms.
                float bob = 1.5f + (1.5f * MathF.Sin((now + (item.Id * 137)) * 0.006f));
                long age = now - item.SeenAtMs;
                float hop = age is >= 0 and < PopMs ? 10f * MathF.Sin(MathF.PI * age / PopMs) : 0f;
                int lift = (int)(bob + hop);
                world.Add(_sheetTexture, srcX, srcY, Icon, Icon, item.X - (Icon / 2), item.Y - 12 - lift, item.Y, item.X, (int)item.Id);
            }

            for (int i = 0; i < GroundItemChannel.FlyCapacity; i++)
            {
                FlyingItem flight = _items.FlightAt(i);
                long flown = now - flight.StartedAtMs;
                if (flight.ItemId == 0 || flown < 0 || flown >= FlightMs || !ItemIcons.TryGetGroundCell(flight.ItemId, out int srcX, out int srcY))
                {
                    continue;
                }

                // Ease in toward the player's chest; drawn in front of them until it arrives.
                float t = (float)flown / FlightMs;
                t *= t;
                int x = (int)(flight.FromX + ((flight.ToX - flight.FromX) * t));
                int y = (int)(flight.FromY + (((flight.ToY - 10) - flight.FromY) * t));
                world.Add(_sheetTexture, srcX, srcY, Icon, Icon, x - (Icon / 2), y - 12, flight.ToY + 2, flight.ToX, 0);
            }
        }
    }
}

/// <summary>Builds the <see cref="GroundItemsDecorator"/> of every map.</summary>
internal sealed class GroundItemsDecoratorProvider : IMapDecoratorProvider
{
    private readonly GroundItemChannel _items;

    /// <summary>Initializes the provider.</summary>
    /// <param name="items">The mirrored items.</param>
    public GroundItemsDecoratorProvider(GroundItemChannel items) => _items = items;

    /// <inheritdoc/>
    public async Task<IMapDecorator?> CreateAsync(MapSpec spec, TileMap map, TextureCache textures, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(textures);

        int sheet = await textures.LoadAsync(ItemIcons.GroundSheetUrl).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return new GroundItemsDecorator(spec.Id, _items, sheet);
    }
}
