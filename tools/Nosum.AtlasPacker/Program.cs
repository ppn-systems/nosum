// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.IO;
using SkiaSharp;

namespace Nosum.AtlasPacker;

/// <summary>
/// Command line: packs every category folder of <c>--assets</c> into
/// <c>&lt;out&gt;/&lt;category&gt;.png</c> + <c>.json</c>.
/// </summary>
/// <remarks>
/// <c>--check</c> writes nothing and exits 1 when a committed atlas differs from a fresh pack.
/// <c>--assets</c> and <c>--out</c> are required: <c>assets/</c> currently holds third-party
/// placeholder sheets that are used as shipped, not packed (docs/adr/ADR-007).
/// </remarks>
internal static class Program
{
    public static int Main(string[] args)
    {
        bool check = false;
        string? assets = null;
        string? output = null;
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--check":
                    check = true;
                    break;
                case "--assets" when i + 1 < args.Length:
                    assets = args[++i];
                    break;
                case "--out" when i + 1 < args.Length:
                    output = args[++i];
                    break;
                default:
                    Console.Error.WriteLine("usage: Nosum.AtlasPacker [--check] [--assets <dir>] [--out <dir>]");
                    return 2;
            }
        }

        if (assets is null || output is null)
        {
            Console.Error.WriteLine("usage: Nosum.AtlasPacker [--check] --assets <dir> --out <dir>");
            return 2;
        }

        return Run(assets, output, check, Console.Out);
    }

    /// <summary>
    /// Packs (or checks) every category folder.
    /// </summary>
    /// <param name="assetsDirectory">Folder holding one sub-folder per category.</param>
    /// <param name="outputDirectory">Folder the atlases are written to.</param>
    /// <param name="check">When true, write nothing and report stale atlases instead.</param>
    /// <param name="log">Where progress is written.</param>
    /// <returns>0 on success; 1 when <paramref name="check"/> finds a stale or missing atlas.</returns>
    internal static int Run(string assetsDirectory, string outputDirectory, bool check, TextWriter log)
    {
        ArgumentNullException.ThrowIfNull(assetsDirectory);
        ArgumentNullException.ThrowIfNull(outputDirectory);
        ArgumentNullException.ThrowIfNull(log);

        string[] categories = Directory.GetDirectories(assetsDirectory);
        Array.Sort(categories, StringComparer.Ordinal);

        int stale = 0;
        foreach (string category in categories)
        {
            BuiltAtlas? atlas = AtlasBuilder.Build(category);
            if (atlas is null)
            {
                continue;
            }

            using (atlas.Image)
            {
                string pngPath = Path.Combine(outputDirectory, atlas.Name + ".png");
                string jsonPath = Path.Combine(outputDirectory, atlas.Name + ".json");
                if (check)
                {
                    if (!IsCurrent(atlas, pngPath, jsonPath))
                    {
                        log.WriteLine($"stale: {atlas.Name} (run Nosum.AtlasPacker and commit the result)");
                        stale++;
                    }

                    continue;
                }

                Directory.CreateDirectory(outputDirectory);
                using (FileStream png = File.Create(pngPath))
                {
                    atlas.Image.Encode(png, SKEncodedImageFormat.Png, 100);
                }

                File.WriteAllText(jsonPath, atlas.Json);
                log.WriteLine($"packed: {atlas.Name} ({atlas.Image.Width}x{atlas.Image.Height})");
            }
        }

        return stale == 0 ? 0 : 1;
    }

    private static bool IsCurrent(BuiltAtlas atlas, string pngPath, string jsonPath)
    {
        if (!File.Exists(pngPath) || !File.Exists(jsonPath))
        {
            return false;
        }

        if (!string.Equals(File.ReadAllText(jsonPath), atlas.Json, StringComparison.Ordinal))
        {
            return false;
        }

        using SKBitmap committed = AtlasBuilder.Decode(pngPath);
        return AtlasBuilder.SamePixels(committed, atlas.Image);
    }
}
