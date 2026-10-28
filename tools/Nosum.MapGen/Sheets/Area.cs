// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Nosum.MapGen.Sheets;

/// <summary>
/// A rectangle of a sheet, in its own pixel coordinates (0 at its top-left), that a drawing paints into. A tile is a 16 × 16 area; a multi-tile
/// object is one area over several tiles, and every tile of it is one index of the sheet.
/// </summary>
internal sealed class Area
{
    private readonly Canvas _canvas;
    private readonly int _left;
    private readonly int _top;

    /// <summary>Initializes an area.</summary>
    /// <param name="canvas">The sheet.</param>
    /// <param name="left">Left of the area on the sheet, in pixels.</param>
    /// <param name="top">Top of the area on the sheet, in pixels.</param>
    /// <param name="width">Width in pixels.</param>
    /// <param name="height">Height in pixels.</param>
    public Area(Canvas canvas, int left, int top, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        this._canvas = canvas;
        this._left = left;
        this._top = top;
        this.Width = width;
        this.Height = height;
    }

    /// <summary>Gets the width in pixels.</summary>
    public int Width { get; }

    /// <summary>Gets the height in pixels.</summary>
    public int Height { get; }

    /// <summary>Returns the area of tiles that start at a sheet index: <paramref name="tiles"/> wide and <paramref name="rows"/> high.</summary>
    /// <param name="canvas">The sheet.</param>
    /// <param name="index">The index of the top-left tile.</param>
    /// <param name="tiles">Tiles across.</param>
    /// <param name="rows">Tiles down.</param>
    /// <returns>The area.</returns>
    public static Area Of(Canvas canvas, int index, int tiles = 1, int rows = 1)
        => new(canvas, index % SheetInfo.Columns * SheetInfo.TileSize, index / SheetInfo.Columns * SheetInfo.TileSize, tiles * SheetInfo.TileSize, rows * SheetInfo.TileSize);

    /// <summary>Paints one pixel; points outside the area are ignored.</summary>
    /// <param name="x">Column in the area.</param>
    /// <param name="y">Row in the area.</param>
    /// <param name="color">The colour.</param>
    public void Put(int x, int y, Rgb color)
    {
        if ((uint)x < (uint)this.Width && (uint)y < (uint)this.Height)
        {
            this._canvas.Set(this._left + x, this._top + y, color);
        }
    }

    /// <summary>Paints a filled rectangle.</summary>
    /// <param name="x">Left.</param>
    /// <param name="y">Top.</param>
    /// <param name="width">Width.</param>
    /// <param name="height">Height.</param>
    /// <param name="color">The colour.</param>
    public void Fill(int x, int y, int width, int height, Rgb color)
    {
        for (int yy = y; yy < y + height; yy++)
        {
            for (int xx = x; xx < x + width; xx++)
            {
                this.Put(xx, yy, color);
            }
        }
    }

    /// <summary>Paints the one-pixel outline of a rectangle.</summary>
    /// <param name="x">Left.</param>
    /// <param name="y">Top.</param>
    /// <param name="width">Width.</param>
    /// <param name="height">Height.</param>
    /// <param name="color">The colour.</param>
    public void Frame(int x, int y, int width, int height, Rgb color)
    {
        this.Fill(x, y, width, 1, color);
        this.Fill(x, y + height - 1, width, 1, color);
        this.Fill(x, y, 1, height, color);
        this.Fill(x + width - 1, y, 1, height, color);
    }

    /// <summary>
    /// Paints a shaded, outlined ellipse: light from the top left, the shadow on the bottom right, the outline on its rim.
    /// </summary>
    /// <param name="cx">Centre column.</param>
    /// <param name="cy">Centre row.</param>
    /// <param name="rx">Radius across.</param>
    /// <param name="ry">Radius down.</param>
    /// <param name="tones">The shading.</param>
    public void Blob(double cx, double cy, double rx, double ry, Tones tones)
    {
        for (int y = 0; y < this.Height; y++)
        {
            for (int x = 0; x < this.Width; x++)
            {
                if (!Inside(x, y, cx, cy, rx, ry))
                {
                    continue;
                }

                bool rim = !Inside(x - 1, y, cx, cy, rx, ry) || !Inside(x + 1, y, cx, cy, rx, ry)
                    || !Inside(x, y - 1, cx, cy, rx, ry) || !Inside(x, y + 1, cx, cy, rx, ry);
                double light = ((x - cx) / rx) + ((y - cy) / ry);
                this.Put(x, y, rim ? Palette.Outline : light < -0.45 ? tones.Light : light > 0.55 ? tones.Shade : tones.Base);
            }
        }
    }

    private static bool Inside(int x, int y, double cx, double cy, double rx, double ry)
    {
        double dx = (x - cx) / rx;
        double dy = (y - cy) / ry;
        return (dx * dx) + (dy * dy) <= 1.0;
    }
}
