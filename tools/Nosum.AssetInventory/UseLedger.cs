// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;

namespace Nosum.AssetInventory;

/// <summary>One placeholder file the game loads, with the grid cells the code actually draws from it.</summary>
internal sealed class FileEntry
{
    private readonly Dictionary<(int Column, int Row), CellState> _cells = [];
    private readonly SortedSet<string> _users = new(StringComparer.Ordinal);

    /// <summary>Initializes an entry.</summary>
    /// <param name="path">Path under <c>assets/</c> with forward slashes.</param>
    /// <param name="width">Width in pixels.</param>
    /// <param name="height">Height in pixels.</param>
    /// <param name="gridWidth">Width of one cell in pixels.</param>
    /// <param name="gridHeight">Height of one cell in pixels.</param>
    /// <param name="home">The package that first loads the file.</param>
    public FileEntry(string path, int width, int height, int gridWidth, int gridHeight, Package home)
    {
        this.Path = path;
        this.Width = width;
        this.Height = height;
        this.GridWidth = gridWidth;
        this.GridHeight = gridHeight;
        this.Home = home;
    }

    /// <summary>Gets the path under <c>assets/</c>.</summary>
    public string Path { get; }

    /// <summary>Gets the width in pixels.</summary>
    public int Width { get; }

    /// <summary>Gets the height in pixels.</summary>
    public int Height { get; }

    /// <summary>Gets the width of one cell in pixels.</summary>
    public int GridWidth { get; }

    /// <summary>Gets the height of one cell in pixels.</summary>
    public int GridHeight { get; }

    /// <summary>Gets the package that first loads the file; the package of a loaded file with no used cell.</summary>
    public Package Home { get; private set; }

    /// <summary>Gets the number of cell columns.</summary>
    public int Columns => (this.Width + this.GridWidth - 1) / this.GridWidth;

    /// <summary>Gets the number of cell rows.</summary>
    public int Rows => (this.Height + this.GridHeight - 1) / this.GridHeight;

    /// <summary>Gets the number of cells of the whole file.</summary>
    public int TotalCells => this.Columns * this.Rows;

    /// <summary>Gets the number of cells the code draws.</summary>
    public int UsedCells => _cells.Count;

    /// <summary>Gets the used cells that belong to a walk, direction, growth or animation sequence.</summary>
    public int UsedFrames
    {
        get
        {
            int count = 0;
            foreach (CellState state in _cells.Values)
            {
                if (state.Frame)
                {
                    count++;
                }
            }

            return count;
        }
    }

    /// <summary>Gets what loads the file, sorted.</summary>
    public IReadOnlyCollection<string> Users => _users;

    /// <summary>Counts the used cells (and frames) a package owns in this file.</summary>
    /// <param name="package">The package.</param>
    /// <returns>The cells and the frames among them.</returns>
    public (int Cells, int Frames) CountOwnedBy(Package package)
    {
        int cells = 0;
        int frames = 0;
        foreach (CellState state in _cells.Values)
        {
            if (state.Owner == package)
            {
                cells++;
                if (state.Frame)
                {
                    frames++;
                }
            }
        }

        return (cells, frames);
    }

    /// <summary>Notes that a package loads the file.</summary>
    /// <param name="package">The package.</param>
    /// <param name="by">What loads it.</param>
    public void Touch(Package package, string by)
    {
        if (package < this.Home)
        {
            this.Home = package;
        }

        _ = _users.Add(by);
    }

    /// <summary>Marks the cells a pixel rectangle covers as used.</summary>
    /// <param name="package">The package using them.</param>
    /// <param name="x">Left edge in pixels.</param>
    /// <param name="y">Top edge in pixels.</param>
    /// <param name="width">Width in pixels.</param>
    /// <param name="height">Height in pixels.</param>
    /// <param name="frame">Whether the rectangle is one frame of a sequence.</param>
    public void Mark(Package package, int x, int y, int width, int height, bool frame)
    {
        if (width <= 0 || height <= 0 || x >= this.Width || y >= this.Height || x + width <= 0 || y + height <= 0)
        {
            throw new InvalidDataException($"{this.Path}: rectangle {x},{y} {width}x{height} lies outside the {this.Width}x{this.Height} file.");
        }

        int firstColumn = Math.Max(x, 0) / this.GridWidth;
        int lastColumn = Math.Min(x + width - 1, this.Width - 1) / this.GridWidth;
        int firstRow = Math.Max(y, 0) / this.GridHeight;
        int lastRow = Math.Min(y + height - 1, this.Height - 1) / this.GridHeight;
        for (int row = firstRow; row <= lastRow; row++)
        {
            for (int column = firstColumn; column <= lastColumn; column++)
            {
                (int, int) key = (column, row);
                if (_cells.TryGetValue(key, out CellState? state))
                {
                    state.Add(package, frame);
                }
                else
                {
                    _cells[key] = new CellState(package, frame);
                }
            }
        }
    }

    private sealed class CellState
    {
        public CellState(Package owner, bool frame)
        {
            this.Owner = owner;
            this.Frame = frame;
        }

        public Package Owner { get; private set; }

        public bool Frame { get; private set; }

        public void Add(Package package, bool frame)
        {
            if (package < this.Owner)
            {
                this.Owner = package;
            }

            this.Frame |= frame;
        }
    }
}

/// <summary>Collects which cells of which placeholder files the game draws.</summary>
internal sealed class UseLedger
{
    private readonly string _assetsRoot;
    private readonly Dictionary<string, FileEntry> _files = new(StringComparer.Ordinal);

    /// <summary>Initializes an empty ledger.</summary>
    /// <param name="assetsRoot">The <c>assets/</c> folder.</param>
    public UseLedger(string assetsRoot)
    {
        _assetsRoot = assetsRoot;
    }

    /// <summary>Gets the files in path order.</summary>
    public IReadOnlyList<FileEntry> Files
    {
        get
        {
            List<FileEntry> files = [.. _files.Values];
            files.Sort((a, b) => string.CompareOrdinal(a.Path, b.Path));
            return files;
        }
    }

    /// <summary>Notes that the game loads a file, whether or not it draws any cell of it.</summary>
    /// <param name="path">Path under <c>assets/</c>, forward slashes.</param>
    /// <param name="package">The package responsible for the file.</param>
    /// <param name="by">What loads it.</param>
    /// <returns>The entry.</returns>
    public FileEntry Loaded(string path, Package package, string by)
    {
        if (!_files.TryGetValue(path, out FileEntry? entry))
        {
            string full = System.IO.Path.Combine(_assetsRoot, path);
            if (!File.Exists(full))
            {
                throw new FileNotFoundException($"The game loads 'assets/{path}' ({by}) but the file does not exist.", full);
            }

            (int width, int height) = PngHeader.ReadSize(full);
            (int gridWidth, int gridHeight) = GridOf(path, width, height);
            entry = new FileEntry(path, width, height, gridWidth, gridHeight, package);
            _files.Add(path, entry);
        }

        entry.Touch(package, by);
        return entry;
    }

    /// <summary>Notes that the game draws a rectangle of a file.</summary>
    /// <param name="path">Path under <c>assets/</c>, forward slashes.</param>
    /// <param name="package">The package using it.</param>
    /// <param name="by">What draws it.</param>
    /// <param name="x">Left edge in pixels.</param>
    /// <param name="y">Top edge in pixels.</param>
    /// <param name="width">Width in pixels.</param>
    /// <param name="height">Height in pixels.</param>
    /// <param name="frame">Whether the rectangle is one frame of a walk, direction, growth or animation sequence.</param>
    public void Use(string path, Package package, string by, int x, int y, int width, int height, bool frame = false)
        => this.Loaded(path, package, by).Mark(package, x, y, width, height, frame);

    /// <summary>
    /// The grid a sheet is addressed on: the size of one frame or tile cell the replacement must keep. Anything not listed is a
    /// 16 × 16 px tile sheet (docs/adr/ADR-007 D1).
    /// </summary>
    private static (int Width, int Height) GridOf(string path, int width, int height) => path switch
    {
        "farmer/body.png" or "farmer/pants.png" or "farmer/hair.png" => (16, 32),
        "farmer/shirts.png" => (8, 8),
        "farmer/hats.png" => (20, 20),
        "sprites/crops.png" or "sprites/craftables.png" => (16, 32),
        "sprites/animals/cow.png" => (32, 32),
        "ui/start_background.png" => (width, height),
        _ => (16, 16),
    };
}
