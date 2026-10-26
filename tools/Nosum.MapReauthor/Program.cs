// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;

namespace Nosum.MapReauthor;

/// <summary>
/// Command line.
/// <c>tileset</c> composes <c>art/own/Maps/own_terrain.png</c> from the PixelLab corner tilesets in <c>art/own/source/pixellab</c>.
/// <c>reauthor &lt;Map&gt; [--check]</c> writes <c>art/own/Maps/&lt;Map&gt;.tmx</c> and <c>art/own/reports/&lt;Map&gt;.md</c> from <c>assets/Maps/&lt;Map&gt;.tmx</c>;
/// <c>reauthor all [--check]</c> does so for every map the world hosts (<c>GameMaps</c>) and then draws the own tilesheets they use under their placeholder names;
/// <c>--check</c> writes nothing and exits 1 when the committed files differ.
/// <c>preview &lt;Map&gt; --out &lt;png&gt; [--placeholder]</c> draws the re-authored (or with <c>--placeholder</c> the original) map.
/// </summary>
internal static class Program
{
    /// <summary>The environment variable that names the placeholder <c>assets/</c> folder.</summary>
    internal const string PlaceholderVariable = "NOSUM_PLACEHOLDER_ROOT";

    private const string Usage = "usage: Nosum.MapReauthor [--repo <root>] (tileset | reauthor (<Map> | all) [--check] | preview <Map> --out <png> [--placeholder])";

    public static int Main(string[] args)
    {
        string? repo = null;
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
        string own = Path.Combine(repo, "art", "own", "Maps");
        try
        {
            switch (words)
            {
                case ["tileset"]:
                    File.WriteAllBytes(Path.Combine(own, OwnTileset.Name + ".png"), TilesetBuilder.Build(Path.Combine(repo, "art", "own", "source", "pixellab")).ToPng());
                    Console.WriteLine($"Wrote art/own/Maps/{OwnTileset.Name}.png ({OwnTileset.TileCount} tiles).");
                    return 0;
                case ["reauthor", "all"]:
                    return RunAll(repo, check, Console.Out);
                case ["reauthor", var map]:
                    return Run(repo, map, check, Console.Out);
                case ["preview", var map] when output is not null:
                    string tmx = placeholder ? Path.Combine(PlaceholderMaps(), map + ".tmx") : Path.Combine(own, map + ".tmx");
                    File.WriteAllBytes(output, Preview.Render(tmx, placeholder ? [PlaceholderMaps()] : [own], map.Contains('/', StringComparison.Ordinal) ? map[..map.LastIndexOf('/')] : string.Empty).ToPng());
                    Console.WriteLine($"Wrote {output}.");
                    return 0;
                default:
                    Console.Error.WriteLine(Usage);
                    return 2;
            }
        }
        catch (InvalidOperationException error)
        {
            Console.Error.WriteLine(error.Message);
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
        string source = Path.Combine(PlaceholderMaps(), map + ".tmx");
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

    /// <summary>Re-authors every hosted map of a checkout, then draws the own tilesheets they use.</summary>
    /// <param name="repo">The repository root.</param>
    /// <param name="check">Write nothing and fail when a committed file differs.</param>
    /// <param name="log">Where messages go.</param>
    /// <returns>0 on success; 1 when <paramref name="check"/> finds a difference.</returns>
    internal static int RunAll(string repo, bool check, TextWriter log)
    {
        string assets = PlaceholderMaps();
        string own = Path.Combine(repo, "art", "own", "Maps");
        KindTable table = KindTable.Embedded();
        SheetBuilder sheets = new();
        int stale = 0;
        foreach (string file in HostedMaps())
        {
            ReauthorResult result = Reauthor.Run(File.ReadAllText(Path.Combine(assets, file)), file, table);
            string name = file[..^4];
            stale += Emit(Path.Combine(own, file), result.Tmx, check, log);
            stale += Emit(Path.Combine(repo, "art", "own", "reports", name + ".md"), result.Report, check, log);
            sheets.Add(result.Tmx, file);
            log.WriteLine($"{name}: {result.OwnCells} of {result.TerrainCells} ground cells from the own terrain tileset.");
        }

        foreach ((string key, Rgba sheet) in sheets.Build(assets))
        {
            stale += EmitSheet(Path.Combine(own, key), sheet, check, log);
        }

        return stale == 0 ? 0 : 1;
    }

    /// <summary>
    /// Gets the placeholder <c>Maps/</c> folder that the re-author reads. The repository no longer holds the placeholder content (ADR-040 D9); the
    /// folder is the <c>assets/Maps</c> of commit <c>64ce88e</c>, named by <c>NOSUM_PLACEHOLDER_ROOT</c> (the <c>assets/</c> folder itself).
    /// </summary>
    /// <returns>The folder.</returns>
    /// <exception cref="InvalidOperationException">The variable is unset or does not name a folder.</exception>
    internal static string PlaceholderMaps()
    {
        string? root = Environment.GetEnvironmentVariable(PlaceholderVariable);
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            throw new InvalidOperationException(
                $"This reads the placeholder maps, which the repository no longer holds. Set {PlaceholderVariable} to the assets/ folder of commit 64ce88e "
                + "(git archive 64ce88e assets | tar -x -C <dir>, then <dir>/assets).");
        }

        return Path.Combine(Path.GetFullPath(root), "Maps");
    }

    /// <summary>
    /// Lists the map files the world hosts, relative to the placeholder <c>Maps/</c> folder, each once. The mine (the entrance cave and the forty
    /// floors) is left out: since ADR-042 P3 it is written by <c>Nosum.MapGen</c>, and re-authoring it would overwrite the generated maps.
    /// </summary>
    /// <returns>The files, sorted.</returns>
    internal static IReadOnlyList<string> HostedMaps()
    {
        SortedSet<string> files = new(StringComparer.Ordinal);
        foreach (Nosum.Gameplay.Maps.MapSpec spec in Nosum.Gameplay.Maps.GameMaps.All)
        {
            if (spec.IsMineFloor || spec.Id == Nosum.Gameplay.Maps.GameMaps.Mine)
            {
                continue;
            }

            _ = files.Add(spec.TmxFile);
        }

        return [.. files];
    }

    private static int Emit(string path, string text, bool check, TextWriter log)
    {
        if (check)
        {
            bool same = File.Exists(path) && File.ReadAllText(path) == text;
            if (!same)
            {
                log.WriteLine($"out of date: {path}");
            }

            return same ? 0 : 1;
        }

        _ = Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return 0;
    }

    private static int EmitSheet(string path, Rgba sheet, bool check, TextWriter log)
    {
        if (check)
        {
            bool same = File.Exists(path) && Rgba.Read(path).Pixels.AsSpan().SequenceEqual(sheet.Pixels);
            if (!same)
            {
                log.WriteLine($"out of date: {path}");
            }

            return same ? 0 : 1;
        }

        _ = Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, sheet.ToPng());
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
