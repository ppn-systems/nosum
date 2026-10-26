// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;

namespace Nosum.AssetInventory;

/// <summary>
/// Command line. With no option it writes <c>docs/art/inventory.md</c>. <c>--check</c> writes nothing and exits 1 when the committed
/// report is stale. <c>--write-fingerprints</c> records the placeholder files' hashes once. <c>--release</c> is the release gate:
/// it exits 1 while a placeholder file is still present and 2 when a replacement has the wrong size or a file is missing.
/// </summary>
internal static class Program
{
    private const string ReportPath = "docs/art/inventory.md";

    public static int Main(string[] args)
    {
        string? repo = null;
        string mode = "write";
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--repo" when i + 1 < args.Length:
                    repo = args[++i];
                    break;
                case "--check" or "--release" or "--write-fingerprints":
                    mode = args[i][2..];
                    break;
                default:
                    Console.Error.WriteLine("usage: Nosum.AssetInventory [--repo <root>] [--check | --release | --write-fingerprints]");
                    return 2;
            }
        }

        repo = Path.GetFullPath(repo ?? FindRoot());
        return Run(repo, mode, Console.Out);
    }

    /// <summary>Runs one mode against a checkout.</summary>
    /// <param name="repo">The repository root.</param>
    /// <param name="mode"><c>write</c>, <c>check</c>, <c>release</c> or <c>write-fingerprints</c>.</param>
    /// <param name="log">Where messages go.</param>
    /// <returns>The exit code.</returns>
    internal static int Run(string repo, string mode, TextWriter log)
    {
        Inventory inventory = Inventory.Build(repo);
        string report = Path.Combine(repo, ReportPath.Replace('/', Path.DirectorySeparatorChar));
        string prints = Path.Combine(Path.GetDirectoryName(report)!, Fingerprints.FileName);
        string assets = Path.Combine(repo, "assets");
        switch (mode)
        {
            case "write":
                Directory.CreateDirectory(Path.GetDirectoryName(report)!);
                File.WriteAllText(report, ReportWriter.Render(inventory));
                log.WriteLine($"Wrote {ReportPath}: {inventory.Listed.Count} files.");
                return 0;

            case "write-fingerprints":
                Directory.CreateDirectory(Path.GetDirectoryName(report)!);
                File.WriteAllText(prints, Fingerprints.Capture(inventory.Listed, assets).Render());
                log.WriteLine($"Wrote {Fingerprints.FileName}: {inventory.Listed.Count} files.");
                return 0;

            case "check":
                if (!File.Exists(report) || File.ReadAllText(report) != ReportWriter.Render(inventory))
                {
                    log.WriteLine($"{ReportPath} is stale; run `dotnet run --project tools/Nosum.AssetInventory`.");
                    return 1;
                }

                log.WriteLine($"{ReportPath} is up to date.");
                return 0;

            default:
                return Release(inventory.Listed, Fingerprints.Read(prints), assets, log);
        }
    }

    /// <summary>The release gate: checks every listed file against its recorded fingerprint.</summary>
    /// <param name="files">The listed files.</param>
    /// <param name="prints">The recorded fingerprints.</param>
    /// <param name="assets">The <c>assets/</c> folder.</param>
    /// <param name="log">Where messages go.</param>
    /// <returns>0 when every file is replaced at its size; 1 while one still matches its placeholder hash; 2 when one is unrecorded, missing or the wrong size.</returns>
    internal static int Release(IReadOnlyList<ListedFile> files, Fingerprints prints, string assets, TextWriter log)
    {
        int placeholders = 0;
        int broken = 0;
        foreach (ListedFile file in files)
        {
            if (!prints.Entries.TryGetValue(file.Path, out Fingerprint? print))
            {
                log.WriteLine($"NOT RECORDED  {file.Path}: add it with --write-fingerprints before it can be checked.");
                broken++;
                continue;
            }

            string? own = Fingerprints.OwnFile(file.Path, assets);
            string full = own ?? Path.Combine(assets, file.Path);
            if (!File.Exists(full))
            {
                log.WriteLine($"MISSING       {file.Path}");
                broken++;
                continue;
            }

            if (own is not null && !own.EndsWith(".png", StringComparison.Ordinal))
            {
                continue;
            }

            (int width, int height) = AssetSize.Read(full);
            if (width != print.Width || height != print.Height)
            {
                log.WriteLine($"WRONG SIZE    {file.Path}: {width} x {height}, the layout needs {print.Width} x {print.Height}.");
                broken++;
            }
            else if (string.Equals(Fingerprints.HashOf(full), print.Sha256, StringComparison.Ordinal))
            {
                log.WriteLine($"PLACEHOLDER   {file.Path}");
                placeholders++;
            }
        }

        log.WriteLine($"{files.Count} files listed: {placeholders} placeholder, {broken} broken, {files.Count - placeholders - broken} replaced.");
        return broken > 0 ? 2 : placeholders > 0 ? 1 : 0;
    }

    private static string FindRoot()
    {
        DirectoryInfo? dir = new(Directory.GetCurrentDirectory());
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AGENTS.md")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new DirectoryNotFoundException("Repository root (AGENTS.md) not found above " + Directory.GetCurrentDirectory());
    }
}
