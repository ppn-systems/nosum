// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Nosum.AssetInventory;

/// <summary>
/// Command line. With no option it writes <c>docs/art/inventory.md</c>. <c>--check</c> writes nothing and exits 1 when the committed
/// report is stale. <c>--write-fingerprints</c> records the placeholder files' hashes once. <c>--release</c> is the release gate: it reads
/// only the committed list (<c>docs/art/placeholders.sha256</c>) and the files of the repository, so it needs no placeholder content.
/// <c>--stardew-scan</c> lists every file whose hash is a Stardew Valley fingerprint (<c>docs/art/stardew-fingerprints.sha256</c>, docs/adr/ADR-042 D5).
/// The three other modes read the placeholder content, which the repository no longer holds (ADR-040 D9): point
/// <c>NOSUM_PLACEHOLDER_ROOT</c> at an <c>assets/</c> folder of the commit that still had it (<c>64ce88e</c>).
/// </summary>
internal static class Program
{
    /// <summary>The environment variable that names the placeholder <c>assets/</c> folder.</summary>
    internal const string PlaceholderVariable = "NOSUM_PLACEHOLDER_ROOT";

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
                case "--check" or "--release" or "--write-fingerprints" or "--stardew-scan":
                    mode = args[i][2..];
                    break;
                default:
                    Console.Error.WriteLine("usage: Nosum.AssetInventory [--repo <root>] [--check | --release | --write-fingerprints | --stardew-scan]");
                    return 2;
            }
        }

        repo = Path.GetFullPath(repo ?? FindRoot());
        return Run(repo, mode, Console.Out);
    }

    /// <summary>Gets the placeholder <c>assets/</c> folder named by <c>NOSUM_PLACEHOLDER_ROOT</c>.</summary>
    /// <returns>The folder, or <see langword="null"/> when the variable is unset or the folder does not exist.</returns>
    internal static string? PlaceholderAssets()
    {
        string? root = Environment.GetEnvironmentVariable(PlaceholderVariable);
        return string.IsNullOrWhiteSpace(root) || !Directory.Exists(root) ? null : Path.GetFullPath(root);
    }

    /// <summary>Runs one mode against a checkout.</summary>
    /// <param name="repo">The repository root.</param>
    /// <param name="mode"><c>write</c>, <c>check</c>, <c>release</c> or <c>write-fingerprints</c>.</param>
    /// <param name="log">Where messages go.</param>
    /// <returns>The exit code.</returns>
    internal static int Run(string repo, string mode, TextWriter log)
    {
        string report = Path.Combine(repo, ReportPath.Replace('/', Path.DirectorySeparatorChar));
        string prints = Path.Combine(Path.GetDirectoryName(report)!, Fingerprints.FileName);
        string assets = Path.Combine(repo, "assets");
        if (mode == "release")
        {
            return ReleaseGate(prints, assets, log);
        }

        if (mode == "stardew-scan")
        {
            return StardewScanMode(repo, log);
        }

        string? placeholder = PlaceholderAssets();
        if (placeholder is null)
        {
            log.WriteLine($"`--{mode}` reads the placeholder content, which the repository no longer holds. Set {PlaceholderVariable} to the assets/ folder of commit 64ce88e "
                + "(git archive 64ce88e assets | tar -x -C <dir>, then <dir>/assets).");
            return 2;
        }

        Inventory inventory = Inventory.Build(repo, placeholder);
        switch (mode)
        {
            case "write":
                Directory.CreateDirectory(Path.GetDirectoryName(report)!);
                File.WriteAllText(report, ReportWriter.Render(inventory));
                log.WriteLine($"Wrote {ReportPath}: {inventory.Listed.Count} files.");
                return 0;

            case "write-fingerprints":
                Directory.CreateDirectory(Path.GetDirectoryName(report)!);
                File.WriteAllText(prints, Fingerprints.Capture(inventory.Listed, placeholder).Render());
                log.WriteLine($"Wrote {Fingerprints.FileName}: {inventory.Listed.Count} files.");
                return 0;

            default:
                if (!File.Exists(report) || File.ReadAllText(report) != ReportWriter.Render(inventory))
                {
                    log.WriteLine($"{ReportPath} is stale; run `dotnet run --project tools/Nosum.AssetInventory` with {PlaceholderVariable} set.");
                    return 1;
                }

                log.WriteLine($"{ReportPath} is up to date.");
                return 0;
        }
    }

    /// <summary>
    /// The Stardew scan (docs/adr/ADR-042 D5): lists every file of the repository whose hash is a Stardew Valley fingerprint.
    /// It reads no placeholder content, only <c>docs/art/stardew-fingerprints.sha256</c>. It is opt-in and does not gate the
    /// release yet, because the maps still carry Stardew's layouts.
    /// </summary>
    /// <param name="repo">The repository root.</param>
    /// <param name="log">Where messages go.</param>
    /// <returns>0 when no file matches, 1 when files match, 2 when the list is missing or malformed.</returns>
    internal static int StardewScanMode(string repo, TextWriter log)
    {
        string list = Path.Combine(repo, "docs", "art", StardewScan.FileName);
        if (!File.Exists(list))
        {
            log.WriteLine($"MISSING       docs/art/{StardewScan.FileName}: the scan has no list of the Stardew fingerprints.");
            return 2;
        }

        List<string> hits;
        try
        {
            hits = StardewScan.Scan(repo, StardewScan.ReadFingerprints(list));
        }
        catch (InvalidDataException error)
        {
            log.WriteLine($"MALFORMED     {error.Message}");
            return 2;
        }

        foreach (string path in hits)
        {
            log.WriteLine($"STARDEW       {path}: its hash is a Stardew Valley fingerprint (docs/adr/ADR-042 D5).");
        }

        log.WriteLine(hits.Count == 0 ? "No file of the repository is Stardew Valley content." : $"{hits.Count} file(s) are Stardew Valley content.");
        return hits.Count == 0 ? 0 : 1;
    }

    /// <summary>The release gate's input: the committed list of the placeholder files, checked against the repository.</summary>
    /// <param name="prints">The committed fingerprint file.</param>
    /// <param name="assets">The <c>assets/</c> folder of the repository.</param>
    /// <param name="log">Where messages go.</param>
    /// <returns>The exit code of <see cref="Release"/>, or 2 when the list is missing.</returns>
    internal static int ReleaseGate(string prints, string assets, TextWriter log)
    {
        if (!File.Exists(prints))
        {
            log.WriteLine($"MISSING       docs/art/{Fingerprints.FileName}: the release gate has no list of the placeholder files.");
            return 2;
        }

        Fingerprints recorded = Fingerprints.Read(prints);
        List<ListedFile> listed = [.. recorded.Entries.Select(e => new ListedFile(e.Key, e.Value.Width, e.Value.Height))];
        return Release(listed, recorded, assets, log);
    }

    /// <summary>
    /// The release gate: every listed file must be replaced, and <c>assets/</c> must hold nothing else but the listed files and its README.
    /// A listed file is still a placeholder when the file at its <c>assets/</c> path, or its own replacement, matches the recorded hash.
    /// </summary>
    /// <param name="files">The listed files.</param>
    /// <param name="prints">The recorded fingerprints.</param>
    /// <param name="assets">The <c>assets/</c> folder.</param>
    /// <param name="log">Where messages go.</param>
    /// <returns>0 when every file is replaced at its size and nothing is unlisted; 1 while one still matches its placeholder hash; 2 when one is unrecorded, missing, the wrong size, or <c>assets/</c> holds an unlisted file.</returns>
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

            string at = Path.Combine(assets, file.Path);
            if (File.Exists(at) && string.Equals(Fingerprints.HashOf(at), print.Sha256, StringComparison.Ordinal))
            {
                log.WriteLine($"PLACEHOLDER   {file.Path}");
                placeholders++;
                continue;
            }

            string? own = Fingerprints.OwnFile(file.Path, assets);
            string full = own ?? at;
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
        }

        int unlisted = 0;
        if (Directory.Exists(assets))
        {
            HashSet<string> listedPaths = [.. files.Select(f => f.Path)];
            foreach (string file in Directory.EnumerateFiles(assets, "*", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal))
            {
                string relative = Path.GetRelativePath(assets, file).Replace(Path.DirectorySeparatorChar, '/');
                if (relative != "README.md" && !listedPaths.Contains(relative))
                {
                    log.WriteLine($"UNLISTED      assets/{relative}: only the listed files and assets/README.md may stay (docs/art/inventory.md).");
                    unlisted++;
                }
            }
        }

        log.WriteLine($"{files.Count} files listed: {placeholders} placeholder, {broken} broken, {files.Count - placeholders - broken} replaced.");
        if (unlisted > 0)
        {
            log.WriteLine($"{unlisted} file(s) under assets/ are not listed.");
        }

        return broken > 0 || unlisted > 0 ? 2 : placeholders > 0 ? 1 : 0;
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
