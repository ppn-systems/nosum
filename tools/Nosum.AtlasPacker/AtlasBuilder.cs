// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using SkiaSharp;

namespace Nosum.AtlasPacker;

/// <summary>One packed category: the atlas pixels and its JSON region map.</summary>
/// <param name="Name">Category name; also the atlas file name without extension.</param>
/// <param name="Image">Atlas pixels, RGBA 8888 unpremultiplied.</param>
/// <param name="Json">JSON region map, LF line endings.</param>
internal sealed record BuiltAtlas(string Name, SKBitmap Image, string Json);

/// <summary>
/// Builds one atlas per category folder of <c>assets/</c>: every PNG in the folder (recursively)
/// becomes a sprite named after its file name.
/// </summary>
internal static partial class AtlasBuilder
{
    /// <summary>Atlas JSON format version.</summary>
    public const int FormatVersion = 1;

    /// <summary>Empty pixels between sprites, so a later WebGL backend can sample without bleeding.</summary>
    public const int Padding = 2;

    private static readonly SKImageInfo s_rgbaInfo = new(1, 1, SKColorType.Rgba8888, SKAlphaType.Unpremul);

    /// <summary>
    /// Packs one category folder.
    /// </summary>
    /// <param name="categoryDirectory">The category folder, e.g. <c>assets/crops</c>.</param>
    /// <returns>The atlas, or <see langword="null"/> when the folder holds no PNG.</returns>
    /// <exception cref="InvalidDataException">A file name is not a valid sprite name, appears twice, or does not decode.</exception>
    public static BuiltAtlas? Build(string categoryDirectory)
    {
        ArgumentNullException.ThrowIfNull(categoryDirectory);

        string category = Path.GetFileName(Path.TrimEndingDirectorySeparator(categoryDirectory));
        string[] files = Directory.GetFiles(categoryDirectory, "*.png", SearchOption.AllDirectories);
        if (files.Length == 0)
        {
            return null;
        }

        Array.Sort(files, StringComparer.Ordinal);
        Dictionary<string, SKBitmap> sprites = new(StringComparer.Ordinal);
        try
        {
            foreach (string file in files)
            {
                string name = Path.GetFileNameWithoutExtension(file);
                if (!SpriteName().IsMatch(name))
                {
                    throw new InvalidDataException($"'{file}': sprite names are lowercase snake_case (a-z, 0-9, '_').");
                }

                if (sprites.ContainsKey(name))
                {
                    throw new InvalidDataException($"'{file}': sprite name '{name}' is already used in '{category}'.");
                }

                sprites.Add(name, Decode(file));
            }

            List<PackItem> items = new(sprites.Count);
            foreach (KeyValuePair<string, SKBitmap> sprite in sprites)
            {
                items.Add(new PackItem(sprite.Key, sprite.Value.Width, sprite.Value.Height));
            }

            PackResult layout = ShelfPacker.Pack(items, Padding);
            SKBitmap? atlas = new(s_rgbaInfo.WithSize(layout.Width, layout.Height));
            try
            {
                atlas.Erase(SKColors.Transparent);
                foreach (Placement placement in layout.Placements)
                {
                    Blit(sprites[placement.Name], atlas, placement.X, placement.Y);
                }

                BuiltAtlas built = new(category, atlas, WriteJson(category, layout));
                atlas = null;
                return built;
            }
            finally
            {
                atlas?.Dispose();
            }
        }
        finally
        {
            foreach (SKBitmap bitmap in sprites.Values)
            {
                bitmap.Dispose();
            }
        }
    }

    /// <summary>
    /// Decodes a PNG into RGBA 8888 unpremultiplied pixels, so copies are exact.
    /// </summary>
    /// <param name="path">PNG file path.</param>
    /// <returns>The decoded bitmap; the caller disposes it.</returns>
    /// <exception cref="InvalidDataException">The file is not a decodable image.</exception>
    public static SKBitmap Decode(string path)
    {
        using SKCodec? codec = SKCodec.Create(path)
            ?? throw new InvalidDataException($"'{path}' is not a decodable image.");
        SKImageInfo info = s_rgbaInfo.WithSize(codec.Info.Width, codec.Info.Height);
        return SKBitmap.Decode(codec, info)
            ?? throw new InvalidDataException($"'{path}' could not be decoded.");
    }

    /// <summary>
    /// Returns whether two RGBA 8888 bitmaps have the same size and identical pixels.
    /// </summary>
    /// <param name="a">First bitmap.</param>
    /// <param name="b">Second bitmap.</param>
    /// <returns><see langword="true"/> when they match pixel for pixel.</returns>
    public static bool SamePixels(SKBitmap a, SKBitmap b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);

        return a.Width == b.Width
            && a.Height == b.Height
            && a.GetPixelSpan().SequenceEqual(b.GetPixelSpan());
    }

    private static void Blit(SKBitmap source, SKBitmap target, int x, int y)
    {
        ReadOnlySpan<byte> from = source.GetPixelSpan();
        Span<byte> to = target.GetPixelSpan();
        int sourceRow = source.RowBytes;
        int targetRow = target.RowBytes;
        int rowLength = source.Width * 4;
        for (int row = 0; row < source.Height; row++)
        {
            from.Slice(row * sourceRow, rowLength).CopyTo(to.Slice(((y + row) * targetRow) + (x * 4), rowLength));
        }
    }

    private static string WriteJson(string category, PackResult layout)
    {
        using MemoryStream buffer = new();
        using (Utf8JsonWriter json = new(buffer, new JsonWriterOptions { Indented = true, NewLine = "\n" }))
        {
            json.WriteStartObject();
            json.WriteNumber("version", FormatVersion);
            json.WriteString("image", category + ".png");
            json.WriteNumber("width", layout.Width);
            json.WriteNumber("height", layout.Height);
            json.WriteStartObject("sprites");
            foreach (Placement placement in layout.Placements)
            {
                json.WriteStartObject(placement.Name);
                json.WriteNumber("x", placement.X);
                json.WriteNumber("y", placement.Y);
                json.WriteNumber("w", placement.Width);
                json.WriteNumber("h", placement.Height);
                json.WriteEndObject();
            }

            json.WriteEndObject();
            json.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray()) + "\n";
    }

    [GeneratedRegex("^[a-z0-9]+(_[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex SpriteName();
}
