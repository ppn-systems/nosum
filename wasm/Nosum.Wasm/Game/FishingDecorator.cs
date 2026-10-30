// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;
using Nosum.Core;
using Nosum.Gameplay.Fishing;
using Nosum.Gameplay.Maps;
using Nosum.Rendering;
using Nosum.Wasm.ViewModels.Fishing;
using Nosum.Wasm.ViewModels.Settings;

namespace Nosum.Wasm.Game;

/// <summary>
/// Draws the line of a cast in the world (docs/adr/ADR-078 polish): the line from the rod tip to the bobber on the water, the bobber bobbing, a
/// splash when the line lands and when a fish bites, the "!" bubble over the farmer, and the reel-in gauge beside them. It reads the fishing view
/// model's frame snapshot and the farmer's position; it decides nothing: the server owns the bite and the catch. Frame path: no allocation, and one
/// <see cref="RenderCommandBuffer"/> draw per dot or sprite, so it costs no crossing of its own.
/// </summary>
internal sealed class FishingDecorator : IMapDecorator
{
    private const int BobberSize = 16;
    private const int BubbleSize = 24;
    private const int GaugeWidth = 40;
    private const int GaugeHeight = 3;
    private const int BobMs = 700;
    private const int SplashMs = 600;
    private const int SplashMaxRadius = 8;
    private const int StillRingRadius = 6;

    // The rod tip sits this many pixels right of the farmer's feet, and this many above the top of the character's hands.
    private const int TipOffsetX = 6;
    private const int TipOffsetY = FarmerSprite.Height * 3 / 4;

    // Colours as 0xRRGGBBAA, drawn through FillRect (the high bit makes these ints negative, hence unchecked).
    private const int LineColour = unchecked((int)0x4A3A2AFF);
    private const int GaugeBorderColour = unchecked((int)0x2B2B2BFF);
    private const int GaugeZoneColour = unchecked((int)0x3E8948FF);
    private const int GaugeMarkerColour = unchecked((int)0xFFFFFFFF);
    private const int SplashColourRgb = 0xE8F6FF;

    // The eight neighbours of a ring, scaled by the radius; the ring is squashed vertically to read as a ripple on the 3/4 view.
    private static readonly int[] s_ringX = [1, 1, 0, -1, -1, -1, 0, 1];
    private static readonly int[] s_ringY = [0, 1, 1, 1, 0, -1, -1, -1];

    private const long NeverMs = -10_000_000;

    private readonly FishingViewModel _fishing;
    private readonly SettingsViewModel _settings;
    private readonly int _bobberTexture;
    private readonly int _bubbleTexture;
    private readonly int _tileSize;

    private FishingFrame _frame;
    private FishingPhase _lastPhase;
    private long _nowMs;
    private long _splashMs = NeverMs;
    private double _farmerX;
    private double _farmerY;

    /// <summary>Initializes the decorator.</summary>
    /// <param name="fishing">The fishing view model, whose frame snapshot this draws.</param>
    /// <param name="settings">The player's settings; reduced motion holds the bobber and the ripple still.</param>
    /// <param name="bobberTexture">Texture of the bobber, one 16 px cell.</param>
    /// <param name="bubbleTexture">Texture of the bite bubble, 24 px.</param>
    /// <param name="tileSize">Tile size of the map, in pixels; the bobber sits on the centre of its tile.</param>
    public FishingDecorator(FishingViewModel fishing, SettingsViewModel settings, int bobberTexture, int bubbleTexture, int tileSize)
    {
        _fishing = fishing;
        _settings = settings;
        _bobberTexture = bobberTexture;
        _bubbleTexture = bubbleTexture;
        _tileSize = tileSize;
    }

    /// <inheritdoc/>
    public void SetClock(in FrameClock clock)
    {
        _nowMs = clock.LocalMs;
        _frame = _fishing.Read();
        if (_frame.Phase != _lastPhase)
        {
            // The ripple runs when the line lands and again when a fish bites: the two moments a player watches the bobber for.
            if (_frame.Phase == FishingPhase.Biting || (_frame.Phase == FishingPhase.Waiting && _lastPhase == FishingPhase.Casting))
            {
                _splashMs = _nowMs;
            }

            _lastPhase = _frame.Phase;
        }
    }

    /// <inheritdoc/>
    public void SetFarmer(double x, double y)
    {
        _farmerX = x;
        _farmerY = y;
    }

    /// <inheritdoc/>
    public void DrawOnGround(RenderCommandBuffer commands, in Camera2D camera, long timeMs)
    {
        if (!this.HasLine)
        {
            return;
        }

        int bobberX = (_frame.CastTileX * _tileSize) + (_tileSize / 2);
        int bobberY = (_frame.CastTileY * _tileSize) + (_tileSize / 2) + this.BobOffset();
        int left = (int)Math.Round(bobberX - (BobberSize / 2d) - camera.Left);
        int top = (int)Math.Round(bobberY - (BobberSize / 2d) - camera.Top);
        commands.Draw(_bobberTexture, 0, 0, BobberSize, BobberSize, left, top);
        this.DrawRipple(commands, camera, bobberX, bobberY);
    }

    /// <inheritdoc/>
    public void DrawInFront(RenderCommandBuffer commands, in Camera2D camera)
    {
        if (!this.HasLine)
        {
            return;
        }

        int tipX = (int)Math.Round(_farmerX + TipOffsetX);
        int tipY = (int)Math.Round(_farmerY - TipOffsetY);
        int bobberX = (_frame.CastTileX * _tileSize) + (_tileSize / 2);
        int bobberY = (_frame.CastTileY * _tileSize) + (_tileSize / 2) + this.BobOffset();
        this.DrawLine(commands, camera, tipX, tipY, bobberX, bobberY);

        if (_frame.Phase == FishingPhase.Biting)
        {
            int bubbleLeft = (int)Math.Round(_farmerX - (BubbleSize / 2d) - camera.Left);
            int bubbleTop = (int)Math.Round(_farmerY - FarmerSprite.Height - BubbleSize - 2 - camera.Top);
            commands.Draw(_bubbleTexture, 0, 0, BubbleSize, BubbleSize, bubbleLeft, bubbleTop);
            this.DrawGauge(commands, camera);
        }
    }

    // A line is out while the cast waits, bites or is being reeled in; the bobber sits on the water the whole time.
    private bool HasLine => _frame.Phase is FishingPhase.Waiting or FishingPhase.Biting or FishingPhase.Reeling;

    // The bobber bobs by one pixel every BobMs, and not at all for a player who asked for reduced motion.
    private int BobOffset() => !_settings.ReducesMotion && (_nowMs / BobMs) % 2 == 1 ? 1 : 0;

    // A 1 px line from the rod tip to the bobber, sagging a little in the middle as a line does in water. One draw per pixel: the line is short
    // (the reach is three tiles).
    private void DrawLine(RenderCommandBuffer commands, in Camera2D camera, int tipX, int tipY, int bobberX, int bobberY)
    {
        int dx = bobberX - tipX;
        int dy = bobberY - tipY;
        int steps = Math.Max(Math.Abs(dx), Math.Abs(dy));
        if (steps == 0)
        {
            return;
        }

        double sag = Math.Min(10d, steps / 6d);
        double controlX = (tipX + bobberX) / 2d;
        double controlY = ((tipY + bobberY) / 2d) + sag;
        for (int i = 0; i <= steps; i++)
        {
            double t = (double)i / steps;
            double u = 1 - t;
            double x = (u * u * tipX) + (2 * u * t * controlX) + (t * t * bobberX);
            double y = (u * u * tipY) + (2 * u * t * controlY) + (t * t * bobberY);
            commands.FillRect((int)Math.Round(x - camera.Left), (int)Math.Round(y - camera.Top), 1, 1, LineColour);
        }
    }

    // The splash: a ring of pixels around the bobber that grows and fades in SplashMs. Reduced motion keeps one still ring for the same time.
    private void DrawRipple(RenderCommandBuffer commands, in Camera2D camera, int centreX, int centreY)
    {
        long age = _nowMs - _splashMs;
        if (age < 0 || age >= SplashMs)
        {
            return;
        }

        bool still = _settings.ReducesMotion;
        int radius = still ? StillRingRadius : 2 + (int)(age * (SplashMaxRadius - 2) / SplashMs);
        int alpha = still ? 160 : 255 - (int)(age * 255 / SplashMs);
        int colour = (SplashColourRgb << 8) | alpha;
        for (int i = 0; i < s_ringX.Length; i++)
        {
            double x = centreX + (s_ringX[i] * radius) - camera.Left;
            double y = centreY + (s_ringY[i] * radius * 2 / 3) - camera.Top;
            commands.FillRect((int)Math.Round(x), (int)Math.Round(y), 2, 2, colour);
        }
    }

    // The reel-in gauge beside the farmer. The track is the press window of this cast: every press inside it reels the fish in (the server judges
    // the press, not the bar), so the whole track is the target zone. The marker is the time left, and it moves toward the zone's end as the
    // window closes.
    private void DrawGauge(RenderCommandBuffer commands, in Camera2D camera)
    {
        if (_frame.WindowMs <= 0)
        {
            return;
        }

        int left = (int)Math.Round(_farmerX - (GaugeWidth / 2d) - camera.Left);
        int top = (int)Math.Round(_farmerY + 6 - camera.Top);
        double remaining = Math.Clamp((double)_frame.BarMs / _frame.WindowMs, 0d, 1d);
        commands.FillRect(left - 1, top - 1, GaugeWidth + 2, GaugeHeight + 2, GaugeBorderColour);
        commands.FillRect(left, top, GaugeWidth, GaugeHeight, GaugeZoneColour);
        int marker = left + (int)Math.Round((GaugeWidth - 1) * remaining);
        commands.FillRect(marker, top - 1, 2, GaugeHeight + 2, GaugeMarkerColour);
    }
}

/// <summary>Builds the <see cref="FishingDecorator"/> of the maps that have water to cast into.</summary>
internal sealed class FishingDecoratorProvider : IMapDecoratorProvider
{
    // The fishing art is linked once, in Nosum.Wasm.Controls.csproj, and served under its _content path.
    private const string BobberUrl = "_content/Nosum.Wasm.Controls/ui/fishing_bobber.png";
    private const string BiteUrl = "_content/Nosum.Wasm.Controls/ui/fish_bite.png";

    private readonly FishingViewModel _fishing;
    private readonly SettingsViewModel _settings;

    /// <summary>Initializes the provider.</summary>
    /// <param name="fishing">The fishing view model.</param>
    /// <param name="settings">The player's settings.</param>
    public FishingDecoratorProvider(FishingViewModel fishing, SettingsViewModel settings)
    {
        _fishing = fishing;
        _settings = settings;
    }

    /// <inheritdoc/>
    public async Task<IMapDecorator?> CreateAsync(MapSpec spec, TileMap map, TextureCache textures, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(textures);
        if (FishingWaters.Of(spec.Id) == FishingWater.None)
        {
            return null;
        }

        int bobber = await textures.LoadAsync(BobberUrl).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        int bite = await textures.LoadAsync(BiteUrl).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return new FishingDecorator(_fishing, _settings, bobber, bite, map.TileWidth);
    }
}
