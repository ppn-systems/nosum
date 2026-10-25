// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Nosum.AssetInventory;

/// <summary>
/// The placeholder files as they were taken from Stardew Valley: their SHA-256 and pixel size. A file is still a placeholder while
/// its hash matches; a replacement must keep the recorded size (docs/adr/ADR-040 D6).
/// </summary>
internal sealed class Fingerprints
{
    /// <summary>The file name under <c>docs/art/</c>.</summary>
    public const string FileName = "placeholders.sha256";

    private Fingerprints(SortedDictionary<string, Fingerprint> entries)
    {
        this.Entries = entries;
    }

    /// <summary>Gets the recorded files by path under <c>assets/</c>.</summary>
    public SortedDictionary<string, Fingerprint> Entries { get; }

    /// <summary>Records the files of an inventory as they are on disk now.</summary>
    /// <param name="files">The listed files.</param>
    /// <param name="assetsRoot">The <c>assets/</c> folder.</param>
    /// <returns>The fingerprints.</returns>
    public static Fingerprints Capture(IEnumerable<ListedFile> files, string assetsRoot)
    {
        SortedDictionary<string, Fingerprint> entries = new(StringComparer.Ordinal);
        foreach (ListedFile file in files)
        {
            entries.Add(file.Path, new Fingerprint(HashOf(Path.Combine(assetsRoot, file.Path)), file.Width, file.Height));
        }

        return new Fingerprints(entries);
    }

    /// <summary>Reads a fingerprint file.</summary>
    /// <param name="path">The file.</param>
    /// <returns>The fingerprints.</returns>
    public static Fingerprints Read(string path)
    {
        SortedDictionary<string, Fingerprint> entries = new(StringComparer.Ordinal);
        foreach (string line in File.ReadAllLines(path))
        {
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            string[] parts = line.Split(' ', 3);
            string[] size = parts[1].Split('x');
            entries.Add(parts[2], new Fingerprint(parts[0], int.Parse(size[0], CultureInfo.InvariantCulture), int.Parse(size[1], CultureInfo.InvariantCulture)));
        }

        return new Fingerprints(entries);
    }

    /// <summary>Renders the file content: a comment header, then one line per file.</summary>
    /// <returns>The text, LF line endings.</returns>
    public string Render()
    {
        StringBuilder text = new();
        _ = text.Append("# SHA-256, pixel size (a map: tiles x tile size) and path (under assets/) of every placeholder file the game loads, as unpacked from Stardew Valley.\n");
        _ = text.Append("# A file whose hash still matches is a placeholder. Written once by `dotnet run --project tools/Nosum.AssetInventory -- --write-fingerprints`; docs/adr/ADR-040.\n");
        foreach ((string path, Fingerprint print) in this.Entries)
        {
            _ = text.Append(print.Sha256).Append(' ').Append(print.Width.ToString(CultureInfo.InvariantCulture)).Append('x').Append(print.Height.ToString(CultureInfo.InvariantCulture)).Append(' ').Append(path).Append('\n');
        }

        return text.ToString();
    }

    /// <summary>Classifies a listed file against its recorded fingerprint.</summary>
    /// <param name="path">The file's path under <c>assets/</c>.</param>
    /// <param name="assetsRoot">The <c>assets/</c> folder.</param>
    /// <returns>The state: still the placeholder, replaced at the recorded size, missing or resized, or never recorded.</returns>
    public ArtState StateOf(string path, string assetsRoot)
    {
        if (!this.Entries.TryGetValue(path, out Fingerprint? print))
        {
            return ArtState.Unrecorded;
        }

        string full = Path.Combine(assetsRoot, path);
        if (!File.Exists(full))
        {
            return ArtState.Broken;
        }

        (int width, int height) = AssetSize.Read(full);
        return width != print.Width || height != print.Height ? ArtState.Broken
            : string.Equals(HashOf(full), print.Sha256, StringComparison.Ordinal) ? ArtState.Placeholder : ArtState.Replaced;
    }

    /// <summary>Computes the SHA-256 of a file.</summary>
    /// <param name="path">The file.</param>
    /// <returns>Lower-case hex.</returns>
    public static string HashOf(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }
}

/// <summary>Where a listed file stands against the placeholder fingerprints (docs/adr/ADR-040 D6).</summary>
internal enum ArtState
{
    /// <summary>The file still has the placeholder's hash.</summary>
    Placeholder,

    /// <summary>The file differs from the placeholder and keeps its pixel size.</summary>
    Replaced,

    /// <summary>The file is missing or no longer has the recorded size.</summary>
    Broken,

    /// <summary>No fingerprint is recorded for the file.</summary>
    Unrecorded,
}

/// <summary>The recorded identity of one placeholder file.</summary>
/// <param name="Sha256">Lower-case hex hash.</param>
/// <param name="Width">Pixel width.</param>
/// <param name="Height">Pixel height.</param>
internal sealed record Fingerprint(string Sha256, int Width, int Height);
