// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Nosum.Core;
using Nosum.Gameplay.Calendar;
using Nosum.Gameplay.Maps;
using Nosum.Gameplay.Placeables;
using Nosum.Gameplay.Weather;
using Nosum.Rendering;
using Nosum.Wasm.Socket.Calendar;
using Nosum.Wasm.Socket.Placeable;

namespace Nosum.Wasm.Game;

/// <summary>
/// The sky over the world (docs/adr/ADR-027): the day/night tint with the lamps cut out of it, and the rain or snow. It computes the weather and the time of
/// day from the server calendar with the same shared rules as the server, so everyone sees the same sky. Frame path: no allocation, no await; the ambient
/// pass is one command and the lights at most <see cref="RenderCommandBuffer.MaxLights"/> more.
/// </summary>
internal sealed class SkyLayer
{
    private readonly CalendarChannel? _calendar;
    private readonly PlaceableChannel? _placed;
    private readonly PrecipitationLayer _precipitation = new();
    private GameCalendar? _rules;
    private Precipitation _falling;

    /// <summary>Initializes the layer.</summary>
    /// <param name="calendar">The server calendar; without it (offline) the sky stays plain daylight.</param>
    /// <param name="placed">The placed pieces, whose lamps give light at night.</param>
    public SkyLayer(CalendarChannel? calendar, PlaceableChannel? placed)
    {
        _calendar = calendar;
        _placed = placed;
    }

    /// <summary>Picks the weather for this frame and moves the rain or snow on; call before <see cref="DrawPrecipitation"/>.</summary>
    /// <param name="spec">The map shown.</param>
    /// <param name="frameSeconds">Wall time since the previous frame.</param>
    /// <param name="viewWidth">View width, unscaled screen pixels.</param>
    /// <param name="viewHeight">View height, unscaled screen pixels.</param>
    public void Update(MapSpec spec, double frameSeconds, int viewWidth, int viewHeight)
    {
        GameCalendar? rules = this.Rules();
        _falling = rules is null || spec.Indoors ? Precipitation.None : PrecipitationOf(WeatherRules.At(rules, _calendar!.ServerNowMs()));
        _precipitation.Update(_falling, frameSeconds, viewWidth, viewHeight);
    }

    /// <summary>Records the light sources and the ambient tint over everything drawn so far.</summary>
    /// <param name="commands">Where the commands are recorded.</param>
    /// <param name="spec">The map shown.</param>
    /// <param name="camera">The camera.</param>
    /// <param name="tileSize">Map tile size, pixels.</param>
    public void DrawAmbient(RenderCommandBuffer commands, MapSpec spec, in Camera2D camera, int tileSize)
    {
        GameCalendar? rules = this.Rules();
        if (rules is null)
        {
            return;
        }

        long now = _calendar!.ServerNowMs();
        AmbientTint tint = AmbientLightRules.At(rules.At(now).MinuteOfDay, WeatherRules.At(rules, now), spec.Indoors);
        if (!tint.IsVisible)
        {
            return;
        }

        this.AddLights(commands, spec, camera, tileSize);
        commands.SetAmbient(tint.R, tint.G, tint.B, tint.A);
    }

    /// <summary>Records the rain or snow of this frame.</summary>
    /// <param name="commands">Where the rectangles are recorded.</param>
    public void DrawPrecipitation(RenderCommandBuffer commands) => _precipitation.Draw(_falling, commands);

    internal static Precipitation PrecipitationOf(WeatherKind weather) => weather switch
    {
        WeatherKind.Rain => Precipitation.Rain,
        WeatherKind.Storm => Precipitation.Storm,
        WeatherKind.Snow => Precipitation.Snow,
        _ => Precipitation.None,
    };

    // Lamps among the placed pieces of the map shown, culled to the camera (a lamp's glow reaches its radius beyond its sprite).
    private void AddLights(RenderCommandBuffer commands, MapSpec spec, in Camera2D camera, int tile)
    {
        if (_placed is null)
        {
            return;
        }

        using (_placed.Read())
        {
            if (!string.Equals(_placed.MapId, spec.Id, StringComparison.Ordinal))
            {
                return;
            }

            for (int i = 0; i < _placed.Count; i++)
            {
                PlacedObjectInfo piece = _placed.At(i);
                if (!PlaceableCatalog.TryGet(piece.ItemId, out PlaceableDefinition? definition) || definition.LightRadius <= 0)
                {
                    continue;
                }

                PlaceableView view = definition.View(Math.Min(piece.Rotation, definition.Rotations - 1));
                int centerX = (piece.TileX * tile) + (view.Width / 2);
                int centerY = ((piece.TileY + view.FootprintHeight) * tile) - (view.Height * 3 / 4);
                int radius = definition.LightRadius;
                if (centerX + radius < camera.Left || centerX - radius > camera.Left + camera.Width || centerY + radius < camera.Top || centerY - radius > camera.Top + camera.Height)
                {
                    continue;
                }

                if (!commands.AddLight(centerX - camera.Left, centerY - camera.Top, radius))
                {
                    return;
                }
            }
        }
    }

    // The calendar the server sent, rebuilt only when its numbers change (once, at sign-in); null until it is known.
    private GameCalendar? Rules()
    {
        if (_calendar is not { IsLoaded: true })
        {
            return null;
        }

        if (_rules is null || _rules.EpochMs != _calendar.EpochMs || _rules.DayLengthMs != _calendar.DayLengthMs)
        {
            _rules = new GameCalendar(_calendar.EpochMs, _calendar.DayLengthMs);
        }

        return _rules;
    }
}
