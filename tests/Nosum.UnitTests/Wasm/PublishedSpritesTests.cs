// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;
using Nosum.Gameplay.Animals;
using Nosum.Gameplay.Buildings;
using Nosum.UnitTests.Maps;
using Xunit;

namespace Nosum.UnitTests.Wasm;

/// <summary>
/// Every sprite the client loads is in the wasm project's published list (Nosum.Wasm.csproj, the <c>Content</c> items with
/// <c>NosumBase</c>). A sprite that is loaded but not published is a 404 in production: drop.png nearly shipped that way, and the
/// goat, duck, pig and sheep were in assets/ but never published. The client's sprite paths are literals in its sources and names
/// that come from the animal and building catalogues, so the guard checks both.
/// </summary>
public sealed partial class PublishedSpritesTests
{
    private const string Project = "wasm/Nosum.Wasm/Nosum.Wasm.csproj";

    // <Content Include="a.png;b.png" ... NosumBase="assets/sprites"> (the attribute follows the include in the csproj).
    [GeneratedRegex("<Content Include=\"([^\"]+)\"[^>]*NosumBase=\"([^\"]+)\"")]
    private static partial Regex ContentItem();

    // A full served path in a source file, such as "assets/sprites/drop.png" or "assets/farmer/body.png".
    [GeneratedRegex("\"(assets/[^\"]*\\.png)\"")]
    private static partial Regex AssetLiteral();

    // A bare sprite name in a source file, such as the "soil.png" joined to SpritesUrl.
    [GeneratedRegex("\"([A-Za-z0-9_]+\\.png)\"")]
    private static partial Regex BareLiteral();

    /// <summary>The served URL (NosumBase and the file name) of every file the wasm project publishes under assets/.</summary>
    private static HashSet<string> Published()
    {
        string projectDir = RepoFiles.Path("wasm", "Nosum.Wasm");
        HashSet<string> served = new(StringComparer.Ordinal);
        foreach (Match item in ContentItem().Matches(File.ReadAllText(RepoFiles.Path(Project))))
        {
            string folder = item.Groups[2].Value;
            foreach (string include in item.Groups[1].Value.Split(';'))
            {
                // Relative to the csproj, with Windows separators; a wildcard (npcs/*.png) expands to the files it matches.
                string full = Path.GetFullPath(Path.Combine(projectDir, include.Replace('\\', '/')));
                string dir = Path.GetDirectoryName(full)!;
                string pattern = Path.GetFileName(full);
                IEnumerable<string> files = pattern.Contains('*', StringComparison.Ordinal) ? Directory.GetFiles(dir, pattern) : [full];
                foreach (string file in files)
                {
                    served.Add(folder + "/" + Path.GetFileName(file));
                }
            }
        }

        return served;
    }

    /// <summary>Client sources that name sprites: the wasm projects' code and markup (not the tests, the tools or the server).</summary>
    private static IEnumerable<string> ClientSources()
    {
        string wasm = RepoFiles.Path("wasm");
        return Directory.EnumerateFiles(wasm, "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".cs", StringComparison.Ordinal) || f.EndsWith(".razor", StringComparison.Ordinal))
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            && !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.Ordinal));
    }

    [Fact]
    public void ClientSources_EveryAssetsSpritePath_IsPublished()
    {
        HashSet<string> served = Published();

        List<string> missing = [];
        foreach (string file in ClientSources())
        {
            foreach (Match m in AssetLiteral().Matches(File.ReadAllText(file)))
            {
                if (!served.Contains(m.Groups[1].Value))
                {
                    missing.Add(Path.GetFileName(file) + ": " + m.Groups[1].Value);
                }
            }
        }

        string.Join(", ", missing).Should().BeEmpty("every assets/ sprite the client requests must be listed in Nosum.Wasm.csproj");
    }

    [Fact]
    public void ClientSources_EveryBareSpriteName_IsPublishedInSomeFolder()
    {
        HashSet<string> served = Published();

        List<string> missing = [];
        foreach (string file in ClientSources())
        {
            foreach (Match m in BareLiteral().Matches(File.ReadAllText(file)))
            {
                string name = m.Groups[1].Value;
                if (!served.Any(s => s.EndsWith("/" + name, StringComparison.Ordinal)))
                {
                    missing.Add(Path.GetFileName(file) + ": " + name);
                }
            }
        }

        string.Join(", ", missing).Should().BeEmpty("a sprite name joined to a folder URL must be published (drop.png was once missing)");
    }

    [Fact]
    public void Catalogs_EveryAnimalSheet_IsPublished()
    {
        HashSet<string> served = Published();

        List<string> missing = AnimalCatalog.All
            .Select(animal => "assets/sprites/animals/" + animal.Sheet + ".png")
            .Where(url => !served.Contains(url))
            .ToList();

        string.Join(", ", missing).Should().BeEmpty("every animal the catalogue sells needs its sheet published");
    }

    [Fact]
    public void Catalogs_EveryBuildingSheet_IsPublished()
    {
        HashSet<string> served = Published();

        List<string> missing = BuildingCatalog.All
            .Select(building => "assets/sprites/" + building.Sheet + ".png")
            .Where(url => !served.Contains(url))
            .ToList();

        string.Join(", ", missing).Should().BeEmpty("every building the catalogue sells needs its picture published");
    }
}
