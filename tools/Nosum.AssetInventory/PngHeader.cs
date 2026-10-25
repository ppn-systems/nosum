// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Buffers.Binary;
using System.IO;

namespace Nosum.AssetInventory;

/// <summary>Reads the pixel size of a PNG file from its header, without decoding it.</summary>
internal static class PngHeader
{
    private static readonly byte[] s_signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>Reads the size of a PNG file.</summary>
    /// <param name="path">Path of the file.</param>
    /// <returns>The width and height in pixels.</returns>
    /// <exception cref="InvalidDataException">The file is not a PNG.</exception>
    public static (int Width, int Height) ReadSize(string path)
    {
        Span<byte> header = stackalloc byte[24];
        using FileStream stream = File.OpenRead(path);
        if (stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false) < header.Length || !header[..8].SequenceEqual(s_signature))
        {
            throw new InvalidDataException($"'{path}' is not a PNG file.");
        }

        return (BinaryPrimitives.ReadInt32BigEndian(header[16..]), BinaryPrimitives.ReadInt32BigEndian(header[20..]));
    }
}
