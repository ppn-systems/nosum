// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;
using Nosum.AssetInventory;
using Nosum.UnitTests.Maps;
using Xunit;

namespace Nosum.UnitTests.Tools;

/// <summary>
/// <c>docs/art/inventory.md</c> (docs/adr/ADR-040) lists every placeholder file the game loads. These tests fail when code starts
/// loading a file the report does not list, so the art plan cannot silently fall behind the game.
/// </summary>
public sealed partial class AssetInventoryTests
{
    private static readonly string[] s_sourceFolders = ["engine", "gameplay", "server", "wasm", "contracts"];
    private static readonly string[] s_sourceExtensions = [".cs", ".razor", ".js", ".css", ".html"];
    private static readonly Lazy<Inventory> s_inventory = new(() => Inventory.Build(RepoFiles.Path(), PlaceholderContent.Assets));

    [Fact]
    public void Report_ListsExactlyTheFilesTheCodeLoads()
    {
        if (!PlaceholderContent.Available)
        {
            return;
        }

        string report = File.ReadAllText(RepoFiles.Path("docs", "art", "inventory.md"));
        HashSet<string> listed = [.. ReportRow().Matches(report).Select(m => $"{m.Groups[1].Value} {m.Groups[2].Value}x{m.Groups[3].Value}")];
        HashSet<string> loaded = [.. s_inventory.Value.Listed.Select(f => $"{f.Path} {f.Width}x{f.Height}")];

        listed.Except(loaded).Should().BeEmpty("the report names files the code no longer loads (or at another size); regenerate it: dotnet run --project tools/Nosum.AssetInventory");
        loaded.Except(listed).Should().BeEmpty("the code loads files docs/art/inventory.md does not list; regenerate it: dotnet run --project tools/Nosum.AssetInventory");
    }

    [Fact]
    public void ProjectContentLinks_AreAllListed()
    {
        if (!PlaceholderContent.Available)
        {
            return;
        }

        HashSet<string> loaded = [.. s_inventory.Value.Listed.Select(f => f.Path)];
        List<string> unlisted = [];
        foreach (string project in SourceFiles(".csproj"))
        {
            foreach (Match include in ContentInclude().Matches(File.ReadAllText(project)))
            {
                foreach (string item in include.Groups[1].Value.Split(';', StringSplitOptions.RemoveEmptyEntries))
                {
                    string path = item.Trim().Replace('\\', '/');
                    int marker = path.IndexOf("assets/", StringComparison.Ordinal);
                    if (marker >= 0 && !loaded.Contains(path[(marker + "assets/".Length)..]))
                    {
                        unlisted.Add(Path.GetFileName(project) + ": " + path);
                    }
                }
            }
        }

        unlisted.Should().BeEmpty("a project that ships a placeholder file the report does not list defeats the release gate");
    }

    [Fact]
    public void SourceLiterals_NamingAnAssetsFile_NameAListedOne()
    {
        if (!PlaceholderContent.Available)
        {
            return;
        }

        string assets = PlaceholderContent.Assets;
        HashSet<string> assetNames = new(StringComparer.OrdinalIgnoreCase);
        foreach (string file in Directory.EnumerateFiles(assets, "*", SearchOption.AllDirectories))
        {
            _ = assetNames.Add(Path.GetFileName(file));
        }

        HashSet<string> listedNames = new(s_inventory.Value.Listed.Select(f => Path.GetFileName(f.Path)), StringComparer.OrdinalIgnoreCase);
        List<string> unlisted = [];
        foreach (string extension in s_sourceExtensions)
        {
            foreach (string file in SourceFiles(extension))
            {
                foreach (string line in File.ReadLines(file))
                {
                    string trimmed = line.TrimStart();
                    if (trimmed.StartsWith("//", StringComparison.Ordinal) || trimmed.StartsWith("/*", StringComparison.Ordinal) || trimmed.StartsWith('*'))
                    {
                        continue;
                    }

                    foreach (Match literal in AssetLiteral().Matches(line))
                    {
                        string name = Path.GetFileName(literal.Groups[1].Value.Replace('\\', '/'));
                        if (assetNames.Contains(name) && !listedNames.Contains(name))
                        {
                            unlisted.Add($"{Path.GetRelativePath(RepoFiles.Path(), file)}: {literal.Value}");
                        }
                    }
                }
            }
        }

        unlisted.Should().BeEmpty("code names a file of assets/ that docs/art/inventory.md does not list; add it to the inventory tool (tools/Nosum.AssetInventory)");
    }

    [Fact]
    public void NodeObjectCells_CoverEveryObjectCellNodeSpritesDraws()
    {
        string source = SpriteUses.ReadSource(RepoFiles.Path(), "wasm/Nosum.Wasm/Game/NodeSprites.cs");
        List<int> literals = [];
        foreach (string line in source.Split('\n').Where(l => l.Contains("AddObject(world", StringComparison.Ordinal)))
        {
            literals.AddRange(Number().Matches(line).Select(m => int.Parse(m.Value, System.Globalization.CultureInfo.InvariantCulture)).Where(n => n >= 100));
        }

        literals.Should().NotBeEmpty();
        literals.Should().OnlyContain(n => SpriteUses.NodeObjectCells.Contains(n), "NodeSprites draws an objects-sheet cell the inventory tool does not know (tools/Nosum.AssetInventory/SpriteUses.cs)");
    }

    [Fact]
    public void Fingerprints_RecordEveryListedFileAtItsCurrentSize()
    {
        if (!PlaceholderContent.Available)
        {
            return;
        }

        Fingerprints recorded = Fingerprints.Read(RepoFiles.Path("docs", "art", Fingerprints.FileName));

        recorded.Entries.Keys.Should().BeEquivalentTo(s_inventory.Value.Listed.Select(f => f.Path), "a new placeholder file needs `--write-fingerprints` (docs/adr/ADR-040 D6)");
        foreach (ListedFile file in s_inventory.Value.Listed)
        {
            recorded.Entries[file.Path].Width.Should().Be(file.Width, file.Path);
            recorded.Entries[file.Path].Height.Should().Be(file.Height, file.Path);
        }
    }

    [Fact]
    public void Render_SameInput_SameText()
    {
        if (!PlaceholderContent.Available)
        {
            return;
        }

        ReportWriter.Render(s_inventory.Value).Should().Be(ReportWriter.Render(Inventory.Build(RepoFiles.Path(), PlaceholderContent.Assets)));
    }

    [Fact]
    public void Release_ReplacedSheetOfTheSameSize_PassesAndWrongSizeFails()
    {
        string assets = Directory.CreateTempSubdirectory("nosum-art-").FullName;
        try
        {
            string sheet = Path.Combine(assets, "sheet.png");
            File.WriteAllBytes(sheet, PngWithHeader(32, 16, 1));
            UseLedger ledger = new(assets);
            _ = ledger.Loaded("sheet.png", Package.Items, "test");
            List<ListedFile> listed = [.. ledger.Files.Select(f => new ListedFile(f.Path, f.Width, f.Height))];
            Fingerprints recorded = Fingerprints.Capture(listed, assets);

            Program.Release(listed, recorded, assets, TextWriter.Null).Should().Be(1, "the file still has the placeholder's hash");

            File.WriteAllBytes(sheet, PngWithHeader(32, 16, 2));
            Program.Release(listed, recorded, assets, TextWriter.Null).Should().Be(0, "a different file of the same size is a replacement");

            File.WriteAllBytes(sheet, PngWithHeader(32, 32, 2));
            Program.Release(listed, recorded, assets, TextWriter.Null).Should().Be(2, "a replacement must keep the layout");

            File.Delete(sheet);
            Program.Release(listed, recorded, assets, TextWriter.Null).Should().Be(2, "a missing file cannot be packaged");
        }
        finally
        {
            Directory.Delete(assets, recursive: true);
        }
    }

    [Fact]
    public void Release_UnlistedFileUnderAssets_FailsExceptTheReadme()
    {
        string root = Directory.CreateTempSubdirectory("nosum-gate-").FullName;
        try
        {
            string assets = Path.Combine(root, "assets");
            _ = Directory.CreateDirectory(assets);
            File.WriteAllBytes(Path.Combine(assets, "sheet.png"), PngWithHeader(32, 16, 2));
            File.WriteAllText(Path.Combine(assets, "README.md"), "# assets");
            string prints = Path.Combine(root, "placeholders.sha256");
            File.WriteAllText(prints, "0000000000000000000000000000000000000000000000000000000000000000 32x16 sheet.png\n");
            List<ListedFile> listed = [new("sheet.png", 32, 16)];

            Program.Release(listed, Fingerprints.Read(prints), assets, TextWriter.Null).Should().Be(0, "the README and the listed replacement are allowed");

            File.WriteAllBytes(Path.Combine(assets, "stray.png"), PngWithHeader(8, 8, 3));
            using StringWriter log = new();
            Program.Release(listed, Fingerprints.Read(prints), assets, log).Should().Be(2, "assets/ holds a file the inventory does not list");
            log.ToString().Should().Contain("UNLISTED      assets/stray.png");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Release_PlaceholderAtItsAssetsPath_FailsEvenWithAnOwnReplacement()
    {
        string root = Directory.CreateTempSubdirectory("nosum-gate-").FullName;
        try
        {
            string assets = Path.Combine(root, "assets");
            _ = Directory.CreateDirectory(Path.Combine(assets, "Maps"));
            _ = Directory.CreateDirectory(Path.Combine(root, "art", "own", "Maps"));
            File.WriteAllText(Path.Combine(assets, "Maps", "Farm.tmx"), "<map>placeholder</map>");
            File.WriteAllText(Path.Combine(root, "art", "own", "Maps", "Farm.tmx"), "<map>own</map>");
            List<ListedFile> listed = [new("Maps/Farm.tmx", 16, 16)];
            Fingerprints recorded = Fingerprints.Capture(listed, assets);

            using StringWriter log = new();
            Program.Release(listed, recorded, assets, log).Should().Be(1, "the stale placeholder is still in assets/, whatever its replacement says");
            log.ToString().Should().Contain("PLACEHOLDER   Maps/Farm.tmx");

            File.Delete(Path.Combine(assets, "Maps", "Farm.tmx"));
            Program.Release(listed, recorded, assets, TextWriter.Null).Should().Be(0, "without the placeholder file, the own replacement is what the game loads");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Mark_RectangleOutsideTheFile_Throws()
    {
        string assets = Directory.CreateTempSubdirectory("nosum-art-").FullName;
        try
        {
            File.WriteAllBytes(Path.Combine(assets, "sheet.png"), PngWithHeader(32, 16, 1));
            UseLedger ledger = new(assets);

            Action outside = () => ledger.Use("sheet.png", Package.Items, "test", 32, 0, 16, 16);
            Action inside = () => ledger.Use("sheet.png", Package.Items, "test", 16, 0, 16, 16);

            outside.Should().Throw<InvalidDataException>();
            inside.Should().NotThrow();
            ledger.Files.Single().UsedCells.Should().Be(1);
        }
        finally
        {
            Directory.Delete(assets, recursive: true);
        }
    }

    // A PNG signature and IHDR are all the tool reads; the trailing byte makes two files of one size differ.
    private static byte[] PngWithHeader(int width, int height, byte marker)
    {
        byte[] bytes = new byte[25];
        byte[] signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, 0x49, 0x48, 0x44, 0x52];
        signature.CopyTo(bytes, 0);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(16), width);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(20), height);
        bytes[24] = marker;
        return bytes;
    }

    private static IEnumerable<string> SourceFiles(string extension)
    {
        foreach (string folder in s_sourceFolders)
        {
            foreach (string file in Directory.EnumerateFiles(RepoFiles.Path(folder), "*" + extension, SearchOption.AllDirectories))
            {
                string relative = file.Replace('\\', '/');
                if (!relative.Contains("/obj/", StringComparison.Ordinal) && !relative.Contains("/bin/", StringComparison.Ordinal) && !relative.Contains("/_framework/", StringComparison.Ordinal))
                {
                    yield return file;
                }
            }
        }
    }

    [GeneratedRegex(@"^\| `([^`]+)` \| (\d+) × (\d+) \|", RegexOptions.Multiline)]
    private static partial Regex ReportRow();

    [GeneratedRegex("""<Content Include="([^"]+)"[^>]*Link=""")]
    private static partial Regex ContentInclude();

    [GeneratedRegex("""["']([\w ./\\\-]+\.(?:png|tmx|ttf|woff2?|ogg|mp3|wav|jpg|gif))["']""", RegexOptions.IgnoreCase)]
    private static partial Regex AssetLiteral();

    [GeneratedRegex(@"\d+")]
    private static partial Regex Number();
}
