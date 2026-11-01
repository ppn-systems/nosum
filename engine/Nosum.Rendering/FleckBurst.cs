// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Nosum.Rendering;

/// <summary>
/// A pool of short-lived square flecks drawn as world sprites: the feedback of an action (a burst of soil, sprout, water or gold flecks, or a pop
/// that grows as it fades). Each fleck starts at a point, travels a short way and fades out over its life. The pool is fixed: a burst over the
/// capacity overwrites the oldest flecks, and nothing is allocated per frame. Reduced motion (<c>reduced</c> on <see cref="Spawn"/>) keeps the
/// flecks where they start, so they only fade. Flecks are sorted with the world, so a burst is drawn over the tile it marks and under anything lower
/// on the screen (docs/adr/ADR-005).
/// </summary>
public sealed class FleckBurst
{
    /// <summary>How many flecks can be alive at once.</summary>
    public const int Capacity = 96;

    // Eight directions around the point: the step on each axis in quarters of the travel (a diagonal is 3/4 on each axis, close to 1/sqrt 2).
    private static readonly int[] s_dirX = [0, 3, 4, 3, 0, -3, -4, -3];
    private static readonly int[] s_dirY = [-4, -3, 0, 3, 4, 3, 0, -3];

    private readonly Fleck[] _flecks = new Fleck[Capacity];
    private int _next;

    /// <summary>Starts a burst of flecks at a point.</summary>
    /// <param name="x">Centre x, world pixels.</param>
    /// <param name="y">Centre y, world pixels.</param>
    /// <param name="sortY">The depth the flecks sort at, world pixels (the tile's centre, plus one, so they draw over the crop of that tile).</param>
    /// <param name="count">How many flecks to start, at most <see cref="Capacity"/>.</param>
    /// <param name="style">The colour, sizes, life, travel and lift.</param>
    /// <param name="nowMs">The scene clock, milliseconds.</param>
    /// <param name="reduced">Whether reduced motion is on: the flecks fade in place.</param>
    /// <param name="seed">Varies the directions of one burst from the next (the same seed always gives the same burst).</param>
    public void Spawn(int x, int y, int sortY, int count, in FleckStyle style, long nowMs, bool reduced, int seed)
    {
        int n = Math.Min(count, Capacity);
        for (int i = 0; i < n; i++)
        {
            int direction = (seed + (i * 3)) & 7;
            int slot = _next;
            _next = (_next + 1) % Capacity;
            ref Fleck fleck = ref _flecks[slot];
            fleck.X = x;
            fleck.Y = y;
            fleck.SortY = sortY;
            fleck.Rgb = style.Rgb;
            fleck.StartSize = style.StartSize;
            fleck.EndSize = style.EndSize;
            fleck.LifeMs = style.LifeMs;
            fleck.TravelX = reduced ? 0 : (s_dirX[direction] * style.TravelPx) / 4;
            fleck.TravelY = reduced ? 0 : (s_dirY[direction] * style.TravelPx) / 4;
            fleck.LiftPx = reduced ? 0 : style.LiftPx;
            fleck.StartMs = nowMs;
            fleck.Live = true;
        }
    }

    /// <summary>Adds every live fleck to the world batch for this frame, and retires the ones whose life is over.</summary>
    /// <param name="world">Where the flecks are sorted and drawn.</param>
    /// <param name="nowMs">The scene clock, milliseconds.</param>
    public void AddTo(WorldSpriteBatch world, long nowMs)
    {
        ArgumentNullException.ThrowIfNull(world);

        for (int i = 0; i < Capacity; i++)
        {
            ref Fleck fleck = ref _flecks[i];
            if (!fleck.Live)
            {
                continue;
            }

            long elapsed = nowMs - fleck.StartMs;
            if (elapsed < 0)
            {
                elapsed = 0;
            }

            if (elapsed >= fleck.LifeMs)
            {
                fleck.Live = false;
                continue;
            }

            // Progress through the life, in 1/1024 steps, so the maths stays in integers.
            int progress = (int)((elapsed * 1024) / fleck.LifeMs);
            int size = fleck.StartSize + (((fleck.EndSize - fleck.StartSize) * progress) >> 10);
            int alpha = 255 - ((255 * progress) >> 10);
            if (size <= 0 || alpha <= 0)
            {
                continue;
            }

            int x = fleck.X + ((fleck.TravelX * progress) >> 10);
            int y = fleck.Y + ((fleck.TravelY * progress) >> 10) - ((fleck.LiftPx * progress) >> 10);
            world.Add(RenderCommandBuffer.FillTexture, 0, 0, size, size, x - (size / 2), y - (size / 2), fleck.SortY, x, 0, 0, (fleck.Rgb << 8) | alpha);
        }
    }

    /// <summary>Counts the flecks still alive at a clock reading.</summary>
    /// <param name="nowMs">The scene clock, milliseconds.</param>
    /// <returns>The live flecks whose life has not run out.</returns>
    public int CountLive(long nowMs)
    {
        int live = 0;
        for (int i = 0; i < Capacity; i++)
        {
            if (_flecks[i].Live && nowMs - _flecks[i].StartMs < _flecks[i].LifeMs)
            {
                live++;
            }
        }

        return live;
    }

    private struct Fleck
    {
        public int X;
        public int Y;
        public int SortY;
        public int Rgb;
        public int StartSize;
        public int EndSize;
        public int LifeMs;
        public int TravelX;
        public int TravelY;
        public int LiftPx;
        public long StartMs;
        public bool Live;
    }
}

/// <summary>What one kind of fleck looks like. Values are fixed per effect, so a style is a constant.</summary>
/// <param name="Rgb">Colour, <c>0xRRGGBB</c>; the alpha fades from full to none over the life.</param>
/// <param name="StartSize">Side of the square at the start, pixels.</param>
/// <param name="EndSize">Side of the square at the end, pixels (a pop uses a bigger end size than its start).</param>
/// <param name="LifeMs">How long the fleck lives, milliseconds.</param>
/// <param name="TravelPx">How far it travels from its start by the end of its life, pixels, in one of eight directions (diagonals reach a little further).</param>
/// <param name="LiftPx">How far it rises by the end of its life, pixels.</param>
public readonly record struct FleckStyle(int Rgb, int StartSize, int EndSize, int LifeMs, int TravelPx, int LiftPx);
