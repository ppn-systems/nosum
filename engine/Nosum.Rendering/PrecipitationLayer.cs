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
            _ => wanted / 2,
        };

        float dt = (float)Math.Min(seconds, 0.1);
        (float fall, float drift) = kind switch
        {
            Precipitation.Storm => (330f, -90f),
            Precipitation.Rain => (260f, -40f),
            _ => (22f, 0f),
        };

        for (int i = 0; i < _active; i++)
        {
            // Nearer particles (greater depth) move faster, which gives the layer some parallax.
            float speed = 0.6f + (0.4f * _depth[i]);
            float sway = kind == Precipitation.Snow ? MathF.Sin((_y[i] * 0.05f) + i) * 14f : drift;
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
                commands.FillRect(x, y, 1, 1, _depth[i] > 0.5f ? unchecked((int)0xFFFFFFE0) : unchecked((int)0xDCE8FFB0));
            }
            else
            {
                commands.FillRect(x, y, 1, kind == Precipitation.Storm ? 5 : 4, _depth[i] > 0.5f ? unchecked((int)0xB4CCFFC0) : unchecked((int)0x8CAAE090));
            }
        }
    }

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
