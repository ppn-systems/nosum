// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Nosum.AssetInventory;

/// <summary>A file the game loads that is not placeholder content: it needs no replacement.</summary>
/// <param name="Path">Repository-relative path.</param>
/// <param name="What">What it is.</param>
/// <param name="License">Its license.</param>
internal sealed record KeptFile(string Path, string What, string License);

/// <summary>A placeholder file the release gate watches: an image or a map, with its pixel size.</summary>
/// <param name="Path">Path under <c>assets/</c>.</param>
/// <param name="Width">Width in pixels (a map: tiles × tile size).</param>
/// <param name="Height">Height in pixels.</param>
internal sealed record ListedFile(string Path, int Width, int Height);

/// <summary>Everything the game loads from the placeholder content, and how much of it.</summary>
internal sealed class Inventory
{
    private Inventory(IReadOnlyList<FileEntry> files, IReadOnlyList<MapReport> maps, IReadOnlyList<MapFile> mapFiles, IReadOnlyList<KeptFile> kept, int filesInAssets)
    {
        this.Files = files;
        this.Maps = maps;
        this.MapFiles = mapFiles;
        this.Kept = kept;
        this.FilesInAssets = filesInAssets;
    }

    /// <summary>Gets the placeholder files the game loads, in path order.</summary>
    public IReadOnlyList<FileEntry> Files { get; }

    /// <summary>Gets the tiles each map places.</summary>
    public IReadOnlyList<MapReport> Maps { get; }

    /// <summary>Gets every hosted map file.</summary>
    public IReadOnlyList<MapFile> MapFiles { get; }

    /// <summary>Gets every placeholder file the game loads, images and maps, in path order.</summary>
    public IReadOnlyList<ListedFile> Listed
    {
        get
        {
            List<ListedFile> listed = [.. this.Files.Select(f => new ListedFile(f.Path, f.Width, f.Height)), .. this.MapFiles.Select(m => new ListedFile(m.Path, m.Width * m.TileWidth, m.Height * m.TileHeight))];
            listed.Sort((a, b) => string.CompareOrdinal(a.Path, b.Path));
            return listed;
        }
    }

    /// <summary>Gets the loaded files that are not placeholders.</summary>
    public IReadOnlyList<KeptFile> Kept { get; }

    /// <summary>Gets the number of files under <c>assets/</c>, loaded or not.</summary>
    public int FilesInAssets { get; }

    /// <summary>Reads the code and the maps of a checkout.</summary>
    /// <param name="repoRoot">The repository root.</param>
    /// <returns>The inventory.</returns>
    public static Inventory Build(string repoRoot)
    {
        string assets = Path.Combine(repoRoot, "assets");
        UseLedger ledger = new(assets);
        MapCatalog maps = MapUses.Collect(ledger, assets);
        SpriteUses.Collect(ledger, repoRoot);

        // Files the game loads that are not Stardew content. Each is checked to exist, so the list cannot rot silently.
        KeptFile[] kept =
        [
            new("wasm/Nosum.Wasm.Controls/wwwroot/fonts/VT323-Regular.ttf", "UI font (ADR-017)", "SIL OFL 1.1 (wasm/Nosum.Wasm.Controls/wwwroot/fonts/OFL-VT323.txt)"),
        ];
        foreach (KeptFile file in kept)
        {
            if (!File.Exists(Path.Combine(repoRoot, file.Path)))
            {
                throw new FileNotFoundException("A kept file is missing: " + file.Path);
            }
        }

        int inAssets = Directory.GetFiles(assets, "*", SearchOption.AllDirectories).Length;
        return new Inventory(ledger.Files, maps.Reports, maps.Files, kept, inAssets);
    }

    /// <summary>Returns the files a package is responsible for: those it owns a cell of, and the loaded files with no used cell it is first to load.</summary>
    /// <param name="package">The package.</param>
    /// <returns>The files in path order.</returns>
    public List<FileEntry> FilesOf(Package package)
    {
        List<FileEntry> result = [];
        foreach (FileEntry file in this.Files)
        {
            if (file.CountOwnedBy(package).Cells > 0 || (file.UsedCells == 0 && file.Home == package))
            {
                result.Add(file);
            }
        }

        return result;
    }
}
