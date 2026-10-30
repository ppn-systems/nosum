// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;
using Nosum.Core;
using Nosum.Gameplay.Maps;
using Nosum.Rendering;
using Nosum.Wasm.ViewModels.Emotes;

namespace Nosum.Wasm.Game;

/// <summary>
/// Draws the emote bubble above each farmer on the map that has one showing (docs/adr/ADR-075 D1): the speech bubble, and the emote's icon centred in it.
/// It reads the farmers of the frame and the bubble store, both allocation-free; the bubble is depth-sorted with its farmer, so it never draws behind them.
/// </summary>
internal sealed class EmoteBubbleDecorator : IMapDecorator
{
    private const int BubbleSize = 24;
    private const int IconSize = 16;
    private const int IconOffset = 4;

    private readonly FarmerFeed _feed;
    private readonly EmoteBubbleStore _bubbles;
    private readonly int _bubbleTexture;
    private readonly int _iconsTexture;
    private long _nowMs;

    /// <summary>Initializes the decorator.</summary>
    /// <param name="feed">The farmers of the frame.</param>
    /// <param name="bubbles">The emote bubbles on screen.</param>
    /// <param name="bubbleTexture">Texture of the speech bubble.</param>
    /// <param name="iconsTexture">Texture of the emote icons, one 16 px icon per emote in a row.</param>
    public EmoteBubbleDecorator(FarmerFeed feed, EmoteBubbleStore bubbles, int bubbleTexture, int iconsTexture)
    {
        _feed = feed;
        _bubbles = bubbles;
        _bubbleTexture = bubbleTexture;
        _iconsTexture = iconsTexture;
    }

    /// <inheritdoc/>
    public void SetClock(in FrameClock clock) => _nowMs = clock.LocalMs;

    /// <inheritdoc/>
    public void AddToWorld(WorldSpriteBatch world)
    {
        for (int i = 0; i < _feed.Count; i++)
        {
            FarmerEntry farmer = _feed.At(i);
            if (!_bubbles.TryGet(farmer.Id, _nowMs, out byte emote))
            {
                continue;
            }

            int x = (int)Math.Round(farmer.X);
            int y = (int)Math.Round(farmer.Y);
            int left = x - (BubbleSize / 2);
            int top = y - FarmerSprite.Height - BubbleSize;
            world.Add(_bubbleTexture, 0, 0, BubbleSize, BubbleSize, left, top, y + 1, x);
            world.Add(_iconsTexture, emote * IconSize, 0, IconSize, IconSize, left + IconOffset, top + IconOffset, y + 1, x);
        }
    }
}

/// <summary>Builds the <see cref="EmoteBubbleDecorator"/> of every map: the bubbles follow the farmers wherever they are.</summary>
internal sealed class EmoteBubbleDecoratorProvider : IMapDecoratorProvider
{
    // The emote art is linked once, in Nosum.Wasm.Controls.csproj, and served under its _content path.
    private const string BubbleUrl = "_content/Nosum.Wasm.Controls/ui/emote_bubble.png";
    private const string IconsUrl = "_content/Nosum.Wasm.Controls/ui/emotes.png";

    private readonly FarmerFeed _feed;
    private readonly EmoteBubbleStore _bubbles;

    /// <summary>Initializes the provider.</summary>
    /// <param name="feed">The farmers of the frame.</param>
    /// <param name="bubbles">The emote bubbles on screen.</param>
    public EmoteBubbleDecoratorProvider(FarmerFeed feed, EmoteBubbleStore bubbles)
    {
        _feed = feed;
        _bubbles = bubbles;
    }

    /// <inheritdoc/>
    public async Task<IMapDecorator?> CreateAsync(MapSpec spec, TileMap map, TextureCache textures, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(textures);

        int bubble = await textures.LoadAsync(BubbleUrl).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        int icons = await textures.LoadAsync(IconsUrl).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return new EmoteBubbleDecorator(_feed, _bubbles, bubble, icons);
    }
}
