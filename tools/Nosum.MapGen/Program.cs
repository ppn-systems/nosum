// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Nosum.Gameplay.Mines;

namespace Nosum.MapGen;

/// <summary>
/// Command line (docs/adr/ADR-042 D4). Writes under <c>art/own/Maps</c> and reads <c>art/own/source</c>:
/// <c>layout &lt;Name&gt;</c> compiles <c>source/layouts/&lt;Name&gt;.txt</c> to <c>Maps/&lt;Name&gt;.tmx</c>, <c>layout all</c> every layout;
/// <c>mine &lt;floor&gt;</c> generates one mine floor to <c>Maps/Mines/&lt;file&gt;.tmx</c>, <c>mine all</c> the forty.
/// <c>--check</c> writes nothing and exits 1 when a committed file differs from what the tool would write. Exit 2 means a usage or source error.
/// </summary>
internal static class Program
{
    private const string Usage = "usage: Nosum.MapGen [--repo <root>] (layout (<Name> | all) | mine (<floor> | all)) [--check]";

    /// <summary>Runs the tool.</summary>
    /// <param name="args">The command line.</param>
    /// <returns>0 when done, 1 when a check fails, 2 on a usage or source error.</returns>
    public static int Main(string[] args)
    {
        string? repo = null;
        bool check = false;
        List<string> words = [];
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--repo" when i + 1 < args.Length:
                    repo = args[++i];
                    break;
                case "--check":
                    check = true;
                    break;
                default:
                    words.Add(args[i]);
                    break;
            }
        }

        try
        {
            string root = Path.GetFullPath(repo ?? FindRoot());
            string source = Path.Combine(root, "art", "own", "source");
            string tilesets = Path.Combine(source, "tilesets");
            string maps = Path.Combine(root, "art", "own", "Maps");
            switch (words)
            {
                case ["layout", var which] when which == "all" || IsName(which):
                    List<Output> pages = [];
                    foreach (string name in which == "all" ? LayoutNames(Path.Combine(source, "layouts")) : new[] { which })
                    {
                        string layout = Path.Combine(source, "layouts", name + ".txt");
                        if (!File.Exists(layout))
                        {
                            throw new InvalidDataException($"No layout {layout}.");
                        }

                        string tmx = LayoutCompiler.Compile(File.ReadAllText(layout), layout, n => TilesetData.Load(tilesets, n));
                        pages.Add(new Output(Path.Combine(maps, name + ".tmx"), tmx));
                    }

                    return Emit(root, pages, check);

                case ["mine", var which] when which == "all" || int.TryParse(which, out _):
                    TilesetData cave = TilesetData.Load(tilesets, MineGenerator.Tileset);
                    IEnumerable<int> floors = which == "all" ? Enumerable.Range(1, MineRules.MaxFloor) : new[] { FloorOf(which) };
                    List<Output> floorFiles = floors
                        .Select(floor => new Output(Path.Combine(maps, MineLayouts.FileOf(floor)), MineGenerator.Generate(floor, cave)))
                        .ToList();
                    return Emit(root, floorFiles, check);

                default:
                    Console.Error.WriteLine(Usage);
                    return 2;
            }
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or InvalidOperationException or ArgumentException or DirectoryNotFoundException)
        {
            Console.Error.WriteLine(ex.Message);
            return 2;
        }
    }

    private static int Emit(string root, IReadOnlyList<Output> outputs, bool check)
    {
        int status = 0;
        foreach (Output output in outputs)
        {
            string relative = Path.GetRelativePath(root, output.Target);
            if (check)
            {
                if (!File.Exists(output.Target) || File.ReadAllText(output.Target) != output.Text)
                {
                    Console.Error.WriteLine($"differs: {relative}");
                    status = 1;
                }

                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(output.Target)!);
            File.WriteAllText(output.Target, output.Text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            Console.WriteLine($"Wrote {relative}.");
        }

        return status;
    }

    private static string[] LayoutNames(string folder)
    {
        if (!Directory.Exists(folder))
        {
            return Array.Empty<string>();
        }

        return Directory.GetFiles(folder, "*.txt")
            .Select(file => Path.GetFileNameWithoutExtension(file) ?? string.Empty)
            .Where(IsName)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    private static bool IsName(string word) => word.Length > 0 && word.All(c => char.IsAsciiLetterOrDigit(c) || c == '_');

    private static int FloorOf(string word)
    {
        if (!int.TryParse(word, out int floor) || floor < 1 || floor > MineRules.MaxFloor)
        {
            throw new ArgumentException($"A mine floor is a number from 1 to {MineRules.MaxFloor}, not '{word}'.");
        }

        return floor;
    }

    private static string FindRoot()
    {
        DirectoryInfo? dir = new(Directory.GetCurrentDirectory());
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AGENTS.md")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new DirectoryNotFoundException("Repository root (AGENTS.md) not found above the working directory.");
    }

    private sealed record Output(string Target, string Text);
}
