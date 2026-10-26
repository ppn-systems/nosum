// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using SkiaSharp;

namespace Nosum.MapGen.Sheets;

/// <summary>An RGBA 8888 picture the sheet drawings paint on, and the PNG it is written as.</summary>
internal sealed class Canvas
{
    private static readonly SKImageInfo s_info = new(1, 1, SKColorType.Rgba8888, SKAlphaType.Unpremul);

    /// <summary>Initializes a transparent picture.</summary>
    /// <param name="width">Width in pixels.</param>
    /// <param name="height">Height in pixels.</param>
    public Canvas(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        this.Width = width;
        this.Height = height;
        this.Pixels = new byte[width * height * 4];
    }

    /// <summary>Gets the width in pixels.</summary>
    public int Width { get; }

    /// <summary>Gets the height in pixels.</summary>
    public int Height { get; }

    /// <summary>Gets the pixels, row by row, four bytes (red, green, blue, alpha) each.</summary>
    public byte[] Pixels { get; }

    /// <summary>Paints one pixel opaque; a point outside the picture is ignored.</summary>
    /// <param name="x">Column.</param>
    /// <param name="y">Row.</param>
    /// <param name="color">The colour.</param>
    public void Set(int x, int y, Rgb color)
    {
        if ((uint)x >= (uint)this.Width || (uint)y >= (uint)this.Height)
        {
            return;
        }

        int i = ((y * this.Width) + x) * 4;
        this.Pixels[i] = color.R;
        this.Pixels[i + 1] = color.G;
        this.Pixels[i + 2] = color.B;
        this.Pixels[i + 3] = 255;
    }

    /// <summary>Copies the opaque pixels of another picture, enlarged by a whole factor, over this one.</summary>
    /// <param name="from">The picture to copy.</param>
    /// <param name="left">Where its top-left pixel lands, before enlarging.</param>
    /// <param name="top">Where its top-left pixel lands, before enlarging.</param>
    /// <param name="scale">The enlargement.</param>
    public void Paste(Canvas from, int left, int top, int scale)
    {
        ArgumentNullException.ThrowIfNull(from);

        for (int y = 0; y < from.Height; y++)
        {
            for (int x = 0; x < from.Width; x++)
            {
                int i = ((y * from.Width) + x) * 4;
                if (from.Pixels[i + 3] < 128)
                {
                    continue;
                }

                Rgb color = new(from.Pixels[i], from.Pixels[i + 1], from.Pixels[i + 2]);
                for (int dy = 0; dy < scale; dy++)
                {
                    for (int dx = 0; dx < scale; dx++)
                    {
                        this.Set(left + (x * scale) + dx, top + (y * scale) + dy, color);
                    }
                }
            }
        }
    }

    /// <summary>Encodes the picture as a PNG.</summary>
    /// <returns>The PNG bytes.</returns>
    public byte[] ToPng()
    {
        using SKBitmap bitmap = new(s_info.WithSize(this.Width, this.Height));
        this.Pixels.CopyTo(bitmap.GetPixelSpan());
        using SKImage image = SKImage.FromBitmap(bitmap);
        using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
