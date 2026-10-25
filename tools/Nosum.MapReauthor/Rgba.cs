// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.IO;
using SkiaSharp;

namespace Nosum.MapReauthor;

/// <summary>An RGBA 8888 pixel buffer with PNG reading and writing; all the pixel work of the tool happens on these.</summary>
internal sealed class Rgba
{
    private static readonly SKImageInfo s_info = new(1, 1, SKColorType.Rgba8888, SKAlphaType.Unpremul);

    /// <summary>Initializes a transparent buffer.</summary>
    /// <param name="width">Width in pixels.</param>
    /// <param name="height">Height in pixels.</param>
    public Rgba(int width, int height)
    {
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

    /// <summary>Reads a PNG.</summary>
    /// <param name="path">The file.</param>
    /// <returns>The pixels.</returns>
    /// <exception cref="InvalidDataException">The file does not decode.</exception>
    public static Rgba Read(string path)
    {
        using SKCodec codec = SKCodec.Create(path) ?? throw new InvalidDataException($"'{path}' is not a decodable image.");
        using SKBitmap bitmap = SKBitmap.Decode(codec, s_info.WithSize(codec.Info.Width, codec.Info.Height)) ?? throw new InvalidDataException($"'{path}' could not be decoded.");
        Rgba result = new(bitmap.Width, bitmap.Height);
        bitmap.GetPixelSpan().CopyTo(result.Pixels);
        return result;
    }

    /// <summary>Encodes the buffer as a PNG.</summary>
    /// <returns>The PNG bytes.</returns>
    public byte[] ToPng()
    {
        using SKBitmap bitmap = new(s_info.WithSize(this.Width, this.Height));
        this.Pixels.CopyTo(bitmap.GetPixelSpan());
        using SKImage image = SKImage.FromBitmap(bitmap);
        using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    /// <summary>Copies a square of another buffer, optionally mirrored.</summary>
    /// <param name="from">The source.</param>
    /// <param name="fromX">Source left.</param>
    /// <param name="fromY">Source top.</param>
    /// <param name="size">Side of the square.</param>
    /// <param name="toX">Target left.</param>
    /// <param name="toY">Target top.</param>
    /// <param name="flipX">Mirror left to right.</param>
    /// <param name="flipY">Mirror top to bottom.</param>
    public void Blit(Rgba from, int fromX, int fromY, int size, int toX, int toY, bool flipX = false, bool flipY = false)
    {
        ArgumentNullException.ThrowIfNull(from);

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int sx = fromX + (flipX ? size - 1 - x : x);
                int sy = fromY + (flipY ? size - 1 - y : y);
                Array.Copy(from.Pixels, ((sy * from.Width) + sx) * 4, this.Pixels, (((toY + y) * this.Width) + toX + x) * 4, 4);
            }
        }
    }

    /// <summary>Draws a square of another buffer over this one, honouring its alpha (which must be 0 or 255).</summary>
    /// <param name="from">The source.</param>
    /// <param name="fromX">Source left.</param>
    /// <param name="fromY">Source top.</param>
    /// <param name="size">Side of the square.</param>
    /// <param name="toX">Target left.</param>
    /// <param name="toY">Target top.</param>
    public void Over(Rgba from, int fromX, int fromY, int size, int toX, int toY)
    {
        ArgumentNullException.ThrowIfNull(from);

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int s = (((fromY + y) * from.Width) + fromX + x) * 4;
                if (from.Pixels[s + 3] >= 128)
                {
                    Array.Copy(from.Pixels, s, this.Pixels, (((toY + y) * this.Width) + toX + x) * 4, 4);
                }
            }
        }
    }
}
