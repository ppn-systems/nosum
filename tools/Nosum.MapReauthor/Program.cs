// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;

namespace Nosum.MapReauthor;

/// <summary>
/// Command line.
/// <c>tileset --pack &lt;sheet.png&gt;</c> composes <c>art/own/Maps/own_terrain.png</c> from the Kenney pack's spritesheet.
/// <c>reauthor &lt;Map&gt; [--check]</c> writes <c>art/own/Maps/&lt;Map&gt;.tmx</c> and <c>art/own/reports/&lt;Map&gt;.md</c> from <c>assets/Maps/&lt;Map&gt;.tmx</c>;
/// <c>--check</c> writes nothing and exits 1 when the committed files differ.
/// <c>preview &lt;Map&gt; --out &lt;png&gt; [--placeholder]</c> draws the re-authored (or with <c>--placeholder</c> the original) map.
/// </summary>
internal static class Program
{
    private const string Usage = "usage: Nosum.MapReauthor [--repo <root>] (tileset --pack <sheet.png> | reauthor <Map> [--check] | preview <Map> --out <png> [--placeholder])";

    public static int Main(string[] args)
    {
        string? repo = null;
        string? pack = null;
        string? output = null;
        bool check = false, placeholder = false;
        List<string> words = [];
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--repo" when i + 1 < args.Length:
                    repo = args[++i];
                    break;
                case "--pack" when i + 1 < args.Length:
                    pack = args[++i];
                    break;
                case "--out" when i + 1 < args.Length:
                    output = args[++i];
                    break;
                case "--check":
                    check = true;
                    break;
                case "--placeholder":
                    placeholder = true;
                    break;
                default:
                    words.Add(args[i]);
                    break;
            }
        }

        repo = Path.GetFullPath(repo ?? FindRoot());
        string assets = Path.Combine(repo, "assets", "Maps");
        string own = Path.Combine(repo, "art", "own", "Maps");
        switch (words)
        {
            case ["tileset"] when pack is not null:
                File.WriteAllBytes(Path.Combine(own, OwnTileset.Name + ".png"), TilesetBuilder.Build(Rgba.Read(pack)).ToPng());
                Console.WriteLine($"Wrote art/own/Maps/{OwnTileset.Name}.png ({OwnTileset.TileCount} tiles).");
                return 0;
            case ["reauthor", var map]:
                return Run(repo, map, check, Console.Out);
            case ["preview", var map] when output is not null:
                string tmx = placeholder ? Path.Combine(assets, map + ".tmx") : Path.Combine(own, map + ".tmx");
                File.WriteAllBytes(output, Preview.Render(tmx, [own, assets]).ToPng());
                Console.WriteLine($"Wrote {output}.");
                return 0;
            default:
                Console.Error.WriteLine(Usage);
                return 2;
        }
    }

    /// <summary>Re-authors one map of a checkout.</summary>
    /// <param name="repo">The repository root.</param>
    /// <param name="map">The map name, for example <c>Farm</c>.</param>
    /// <param name="check">Write nothing and fail when the committed files differ.</param>
    /// <param name="log">Where messages go.</param>
    /// <returns>0 on success; 1 when <paramref name="check"/> finds a difference.</returns>
    internal static int Run(string repo, string map, bool check, TextWriter log)
    {
        string source = Path.Combine(repo, "assets", "Maps", map + ".tmx");
        ReauthorResult result = Reauthor.Run(File.ReadAllText(source), map + ".tmx", KindTable.Embedded());
        string tmx = Path.Combine(repo, "art", "own", "Maps", map + ".tmx");
        string report = Path.Combine(repo, "art", "own", "reports", map + ".md");
        if (check)
        {
            bool same = File.Exists(tmx) && File.Exists(report) && File.ReadAllText(tmx) == result.Tmx && File.ReadAllText(report) == result.Report;
            log.WriteLine(same ? $"{map}: up to date." : $"{map}: out of date; run `reauthor {map}`.");
            return same ? 0 : 1;
        }

        _ = Directory.CreateDirectory(Path.GetDirectoryName(tmx)!);
        _ = Directory.CreateDirectory(Path.GetDirectoryName(report)!);
        File.WriteAllText(tmx, result.Tmx);
        File.WriteAllText(report, result.Report);
        log.WriteLine($"{map}: {result.OwnCells} of {result.TerrainCells} ground cells now own art; {result.Kept.Count} placeholder tiles stay.");
        return 0;
    }

    private static string FindRoot()
    {
        string? dir = Directory.GetCurrentDirectory();
        while (dir is not null && !File.Exists(Path.Combine(dir, "AGENTS.md")))
        {
            dir = Path.GetDirectoryName(dir);
        }

        return dir ?? throw new InvalidOperationException("Run from inside the repository, or pass --repo.");
    }
}
