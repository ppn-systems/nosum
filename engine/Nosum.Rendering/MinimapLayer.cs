// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Nosum.Core;

namespace Nosum.Rendering;

/// <summary>
/// The minimap in a HUD corner: the current map downscaled to one cell per tile (a cell is up to <see cref="MaxCell"/> pixels, and the whole map
/// fits in <see cref="MaxWidth"/> by <see cref="MaxHeight"/>), its ground coloured from the traits of the topmost tile, and a few dots for the
/// farmer, the other players, the NPCs and the exits.
/// </summary>
/// <remarks>
/// The ground is cached per map: <see cref="Build"/> runs when the map changes (not per frame), merges equal colours of each row into runs and keeps
/// them in preallocated arrays. Each frame draws the runs and the dots recorded since <see cref="BeginDots"/> into the frame's command buffer, so the
/// minimap costs no interop crossing and allocates nothing per frame. Pixels are unscaled screen pixels, like every draw of the frame.
/// </remarks>
public sealed class MinimapLayer
{
    /// <summary>Widest the minimap box may be, unscaled pixels, when the map fits (larger maps are clamped to one pixel per tile).</summary>
    public const int MaxWidth = 96;

    /// <summary>Tallest the minimap box may be, unscaled pixels.</summary>
    public const int MaxHeight = 64;

    /// <summary>Largest size of one tile in the minimap, unscaled pixels: a room shows its tiles large, a big map small.</summary>
    public const int MaxCell = 4;

    /// <summary>Gap between the minimap and the screen corner, unscaled pixels.</summary>
    public const int Margin = 6;

    /// <summary>Dots one frame may carry; further ones are dropped.</summary>
    public const int MaxDots = 128;

    /// <summary>Markers one map may carry (its NPCs and exits); further ones are dropped.</summary>
    public const int MaxMarkers = 64;

    /// <summary>Side of every dot and marker, unscaled pixels.</summary>
    public const int DotSize = 2;

    private const int Border = 1;

    private readonly int[] _cellColor;
    private readonly int[] _runX;
    private readonly int[] _runY;
    private readonly int[] _runLength;
    private readonly int[] _runColor;
    private readonly int[] _markerX = new int[MaxMarkers];
    private readonly int[] _markerY = new int[MaxMarkers];
    private readonly int[] _markerColor = new int[MaxMarkers];
    private readonly int[] _dotX = new int[MaxDots];
    private readonly int[] _dotY = new int[MaxDots];
    private readonly int[] _dotColor = new int[MaxDots];
    private readonly int _maxCells;
    private int _runCount;
    private int _markerCount;
    private int _dotCount;
    private int _cell = 1;
    private int _mapWidth;
    private int _mapHeight;
    private string? _key;

    /// <summary>Initializes the minimap with room for maps of up to <paramref name="maxCells"/> tiles.</summary>
    /// <param name="maxCells">Most tiles of a map the minimap can show; larger maps are not drawn.</param>
    public MinimapLayer(int maxCells = 16384)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxCells);
        _maxCells = maxCells;
        _cellColor = new int[maxCells];
        _runX = new int[maxCells];
        _runY = new int[maxCells];
        _runLength = new int[maxCells];
        _runColor = new int[maxCells];
    }

    /// <summary>Gets or sets whether the minimap is drawn at all (the M key; hidden by default on a narrow screen).</summary>
    public bool Visible { get; set; }

    /// <summary>Gets the key of the map the runs were built for, or <see langword="null"/> before the first <see cref="Build"/>.</summary>
    public string? Key => _key;

    /// <summary>Gets the size of one tile in the minimap, unscaled pixels.</summary>
    public int Cell => _cell;

    /// <summary>Gets the width of the minimap box, unscaled pixels, including the border.</summary>
    public int BoxWidth => (_mapWidth * _cell) + (Border * 2);

    /// <summary>Gets the height of the minimap box, unscaled pixels, including the border.</summary>
    public int BoxHeight => (_mapHeight * _cell) + (Border * 2);

    /// <summary>Caches the ground of a map. Does nothing when <paramref name="key"/> is the map already built.</summary>
    /// <param name="map">The map.</param>
    /// <param name="key">Identifies the map, for example its id; a new key rebuilds the cache.</param>
    /// <param name="palette">The colours of the ground by tile trait.</param>
    /// <returns><see langword="true"/> when the cache now holds <paramref name="map"/>; <see langword="false"/> when the map is too large to show.</returns>
    public bool Build(TileMap map, string key, in MinimapPalette palette)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(key);
        if (_key == key)
        {
            return true;
        }

        int cells = map.Width * map.Height;
        if (cells > _maxCells || cells == 0)
        {
            _runCount = 0;
            _key = null;
            return false;
        }

        _mapWidth = map.Width;
        _mapHeight = map.Height;
        _cell = Math.Clamp(Math.Min(MaxWidth / map.Width, MaxHeight / map.Height), 1, MaxCell);
        _markerCount = 0;
        for (int y = 0; y < map.Height; y++)
        {
            for (int x = 0; x < map.Width; x++)
            {
                _cellColor[(y * map.Width) + x] = ColourOf(map, x, y, palette);
            }
        }

        _runCount = 0;
        for (int y = 0; y < map.Height; y++)
        {
            int row = y * map.Width;
            int x = 0;
            while (x < map.Width)
            {
                int colour = _cellColor[row + x];
                int start = x;
                while (x < map.Width && _cellColor[row + x] == colour)
                {
                    x++;
                }

                if (colour != 0)
                {
                    _runX[_runCount] = start;
                    _runY[_runCount] = y;
                    _runLength[_runCount] = x - start;
                    _runColor[_runCount] = colour;
                    _runCount++;
                }
            }
        }

        _key = key;
        return true;
    }

    /// <summary>Starts this frame's dots (the players). Call before the <see cref="AddDot"/> calls of the frame.</summary>
    public void BeginDots() => _dotCount = 0;

    /// <summary>
    /// Records a marker that stays on the map until the next <see cref="Build"/> of a new map: an NPC or an exit. Call right after that build,
    /// before drawing. Dropped when <see cref="MaxMarkers"/> are recorded already.
    /// </summary>
    /// <param name="tileX">Tile column.</param>
    /// <param name="tileY">Tile row.</param>
    /// <param name="colour">Colour as <c>0xRRGGBBAA</c>.</param>
    public void AddMarker(double tileX, double tileY, int colour)
    {
        if (_markerCount == MaxMarkers)
        {
            return;
        }

        (_markerX[_markerCount], _markerY[_markerCount]) = this.CellOrigin(tileX, tileY, DotSize);
        _markerColor[_markerCount] = colour;
        _markerCount++;
    }

    /// <summary>Records a dot for this frame at a tile of the map (a player). Dropped when <see cref="MaxDots"/> are recorded already.</summary>
    /// <param name="tileX">Tile column, fractional.</param>
    /// <param name="tileY">Tile row, fractional.</param>
    /// <param name="colour">Colour as <c>0xRRGGBBAA</c>.</param>
    public void AddDot(double tileX, double tileY, int colour)
    {
        if (_dotCount == MaxDots)
        {
            return;
        }

        (_dotX[_dotCount], _dotY[_dotCount]) = this.CellOrigin(tileX, tileY, DotSize);
        _dotColor[_dotCount] = colour;
        _dotCount++;
    }

    // The dot is centred on its tile's cell and kept inside the ground, so a player at the edge still shows.
    private (int X, int Y) CellOrigin(double tileX, double tileY, int size)
    {
        int px = Math.Clamp((int)Math.Floor(tileX * _cell) + (_cell / 2) - (size / 2), 0, Math.Max(0, (_mapWidth * _cell) - size));
        int py = Math.Clamp((int)Math.Floor(tileY * _cell) + (_cell / 2) - (size / 2), 0, Math.Max(0, (_mapHeight * _cell) - size));
        return (px, py);
    }

    /// <summary>
    /// Draws the minimap in the bottom-right corner of the screen, above the hotbar's strip: the box, the cached ground runs and this frame's dots.
    /// Nothing is drawn while <see cref="Visible"/> is off, before a map is built, or when the screen is too small to hold the box.
    /// </summary>
    /// <param name="commands">Where draws are recorded.</param>
    /// <param name="screenWidth">Screen width, unscaled pixels.</param>
    /// <param name="screenHeight">Screen height, unscaled pixels.</param>
    /// <param name="rightInset">Unscaled pixels the HUD takes at the right edge (its system column), kept clear beside the box.</param>
    /// <param name="bottomInset">Unscaled pixels the HUD takes at the bottom (the hotbar), kept clear above the box.</param>
    /// <param name="frameColour">Colour of the box's backing, <c>0xRRGGBBAA</c>.</param>
    /// <param name="borderColour">Colour of the border, <c>0xRRGGBBAA</c>.</param>
    public void Draw(RenderCommandBuffer commands, int screenWidth, int screenHeight, int rightInset, int bottomInset, int frameColour, int borderColour)
    {
        ArgumentNullException.ThrowIfNull(commands);
        if (!this.Visible || _key is null || _runCount == 0 || screenWidth < this.BoxWidth + rightInset + (Margin * 2) || screenHeight < this.BoxHeight + bottomInset + (Margin * 2))
        {
            return;
        }

        int left = screenWidth - rightInset - Margin - this.BoxWidth;
        int top = screenHeight - bottomInset - Margin - this.BoxHeight;
        commands.FillRect(left, top, this.BoxWidth, this.BoxHeight, borderColour);
        commands.FillRect(left + Border, top + Border, this.BoxWidth - (Border * 2), this.BoxHeight - (Border * 2), frameColour);
        int originX = left + Border;
        int originY = top + Border;
        for (int i = 0; i < _runCount; i++)
        {
            commands.FillRect(originX + (_runX[i] * _cell), originY + (_runY[i] * _cell), _runLength[i] * _cell, _cell, _runColor[i]);
        }

        for (int i = 0; i < _markerCount; i++)
        {
            commands.FillRect(originX + _markerX[i], originY + _markerY[i], DotSize, DotSize, _markerColor[i]);
        }

        for (int i = 0; i < _dotCount; i++)
        {
            commands.FillRect(originX + _dotX[i], originY + _dotY[i], DotSize, DotSize, _dotColor[i]);
        }
    }

    private static int ColourOf(TileMap map, int x, int y, in MinimapPalette palette)
    {
        // The topmost layer with a tile decides the ground; an empty stack is the void.
        for (int layer = map.Layers.Count - 1; layer >= 0; layer--)
        {
            int gid = map.Layers[layer].Gids[(y * map.Width) + x];
            if (gid == 0)
            {
                continue;
            }

            TileTraits traits = map.GetTraits(gid);
            if ((traits & TileTraits.Water) != 0)
            {
                return palette.Water;
            }

            if ((traits & TileTraits.Impassable) != 0)
            {
                return palette.Blocked;
            }

            if ((traits & TileTraits.Diggable) != 0)
            {
                return palette.Tilled;
            }

            return palette.Ground;
        }

        return 0;
    }
}

/// <summary>The minimap's ground colours, <c>0xRRGGBBAA</c> each. A void cell (no tile) is never drawn.</summary>
/// <param name="Ground">Walkable ground: grass, paths, floors.</param>
/// <param name="Water">Water.</param>
/// <param name="Blocked">Walls, rocks and other things that stop the farmer.</param>
/// <param name="Tilled">Ground the farmer may dig or till.</param>
public readonly record struct MinimapPalette(int Ground, int Water, int Blocked, int Tilled)
{
    /// <summary>The palette of the game.</summary>
    public static MinimapPalette Default { get; } = new(0x6B9E4BFF, 0x3B7FC4FF, 0x4A4A52FF, unchecked((int)0x9C7A4AFF));
}
