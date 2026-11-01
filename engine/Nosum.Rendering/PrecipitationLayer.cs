// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Nosum.Rendering;

/// <summary>What falls from the sky on the client (docs/adr/ADR-027 D6).</summary>
public enum Precipitation
{
    /// <summary>Nothing.</summary>
    None = 0,

    /// <summary>Rain: short fast streaks.</summary>
    Rain = 1,

    /// <summary>A storm: denser, faster, slanted rain.</summary>
    Storm = 2,

    /// <summary>Snow: slow drifting flakes.</summary>
    Snow = 3,

    /// <summary>Autumn leaves: slow, swaying, in orange, red and gold (docs/adr/ADR-071 D5).</summary>
    Leaves = 4,
}

/// <summary>
/// A screen-space rain/snow layer: a fixed pool of particles that fall across the view and wrap round, drawn as small rectangles. Purely cosmetic and
/// client-only; positions are in the same unscaled screen units as the command buffer. Everything is preallocated, so a frame allocates nothing.
/// </summary>
public sealed class PrecipitationLayer
{
    /// <summary>Particles in the pool; the densest weather uses all of them.</summary>
    public const int Capacity = 320;

    // Screen area, in unscaled pixels, per particle at full density: 26 tiles across at 16 px is a 416 x 234 view, about 97 000 units.
    private const int AreaPerParticle = 360;

    private readonly float[] _x = new float[Capacity];
    private readonly float[] _y = new float[Capacity];
    private readonly float[] _depth = new float[Capacity];
    private uint _seed = 0x2545F491;
    private int _width = 1;
    private int _height = 1;
    private int _active;
    private bool _seeded;

    /// <summary>Gets how many particles are in use for the current weather and view.</summary>
    public int ActiveCount => _active;

    /// <summary>Moves the particles on and sizes the pool to the view.</summary>
    /// <param name="kind">The weather; <see cref="Precipitation.None"/> empties the sky.</param>
    /// <param name="seconds">Wall time since the previous frame.</param>
    /// <param name="viewWidth">View width, unscaled screen pixels.</param>
    /// <param name="viewHeight">View height, unscaled screen pixels.</param>
    public void Update(Precipitation kind, double seconds, int viewWidth, int viewHeight)
    {
        if (kind == Precipitation.None || viewWidth <= 0 || viewHeight <= 0)
        {
            _active = 0;
            return;
        }

        if (!_seeded || viewWidth != _width || viewHeight != _height)
        {
            _width = viewWidth;
            _height = viewHeight;
            this.Scatter();
        }

        int wanted = Math.Clamp(viewWidth * viewHeight / AreaPerParticle, 24, Capacity);
        _active = kind switch
        {
            Precipitation.Storm => wanted,
            Precipitation.Rain => wanted * 2 / 3,
            Precipitation.Leaves => wanted / 3,
            _ => wanted / 2,
        };

        float dt = (float)Math.Min(seconds, 0.1);
        (float fall, float drift) = kind switch
        {
            Precipitation.Storm => (330f, -90f),
            Precipitation.Rain => (260f, -40f),
            Precipitation.Leaves => (48f, -20f),
            _ => (22f, 0f),
        };

        for (int i = 0; i < _active; i++)
        {
            // Nearer particles (greater depth) move faster, which gives the layer some parallax.
            float speed = 0.6f + (0.4f * _depth[i]);
            float sway = kind switch
            {
                Precipitation.Snow => MathF.Sin((_y[i] * 0.05f) + i) * 14f,
                Precipitation.Leaves => MathF.Sin((_y[i] * 0.03f) + i) * 26f,
                _ => drift,
            };
            _x[i] += sway * speed * dt;
            _y[i] += fall * speed * dt;
            if (_y[i] >= _height)
            {
                _y[i] -= _height + 8;
                _x[i] = this.Next() * _width;
            }

            if (_x[i] < 0)
            {
                _x[i] += _width;
            }
            else if (_x[i] >= _width)
            {
                _x[i] -= _width;
            }
        }
    }

    /// <summary>Records the particles into a frame.</summary>
    /// <param name="kind">The weather the last <see cref="Update"/> was for.</param>
    /// <param name="commands">Where the rectangles are recorded.</param>
    public void Draw(Precipitation kind, RenderCommandBuffer commands)
    {
        ArgumentNullException.ThrowIfNull(commands);

        for (int i = 0; i < _active; i++)
        {
            int x = (int)_x[i];
            int y = (int)_y[i];
            if (kind == Precipitation.Snow)
            {
                // A near flake is two pixels square: a one-pixel flake was lost on the grass and the path (ADR-027 amendment, round 2). A far one stays one pixel.
                bool near = _depth[i] > 0.5f;
                int size = near ? 2 : 1;
                commands.FillRect(x, y, size, size, near ? Rgba.From(0xFF, 0xFF, 0xFF, 0xE0) : Rgba.From(0xDC, 0xE8, 0xFF, 0xB0));
            }
            else if (kind == Precipitation.Leaves)
            {
                commands.FillRect(x, y, 2, 2, LeafColor(i));
            }
            else
            {
                int colour = _depth[i] > 0.5f ? unchecked((int)0xB4CCFFC0) : unchecked((int)0x8CAAE090);
                if (kind == Precipitation.Storm)
                {
                    // A storm's rain leans with the wind (its drift), so each drop is two segments that step a pixel sideways. Upright streaks read as a light shower.
                    commands.FillRect(x, y, 1, 3, colour);
                    if (y + 3 < _height)
                    {
                        commands.FillRect(Math.Max(0, x - 1), y + 3, 1, 3, colour);
                    }
                }
                else
                {
                    commands.FillRect(x, y, 1, 4, colour);
                }


                // A drop that reaches the bottom of the view leaves a short splash.
                if (_y[i] >= _height - 6)
                {
                    commands.FillRect(Math.Max(0, x - 1), _height - 1, 3, 1, Rgba.From(0x90, 0xC8, 0xE8, 0xD0));
                }
            }
        }
    }

    // Autumn colours, orange, red and gold, in turn; packed like the other rectangles, red in the top byte and alpha in the low byte.
    private static int LeafColor(int i) => (i % 3) switch
    {
        0 => unchecked((int)0xD9783AFFU),
        1 => unchecked((int)0xB8432AFFU),
        _ => unchecked((int)0xE0B040FFU),
    };

    private void Scatter()
    {
        for (int i = 0; i < Capacity; i++)
        {
            _x[i] = this.Next() * _width;
            _y[i] = this.Next() * _height;
            _depth[i] = this.Next();
        }

        _seeded = true;
    }

    // xorshift32 mapped to [0, 1): deterministic, allocation-free.
    private float Next()
    {
        uint x = _seed;
        x ^= x << 13;
        x ^= x >> 17;
        x ^= x << 5;
        _seed = x;
        return (x >> 8) / 16777216f;
    }
}
